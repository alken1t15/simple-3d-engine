#!/usr/bin/env python3
"""Нативная проверка OverlaySample через X11/XTest, без Python-пакетов.

Предпочтителен отдельный тестовый DISPLAY. Сохраняет/восстанавливает focus и pointer.
Проверяет pointer marker, очистку, resize, Expose/Refresh и штатный выход.
--window-manager дополнительно проверяет minimize/restore (нужен настоящий WM).
Это синтетическая проверка; реальный HiDPI и ручная читаемость проверяются отдельно.
"""
import argparse
import ctypes as c
import json
import os
from pathlib import Path
import queue
import statistics
import struct
import subprocess
import threading
import time
import zlib


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--window-manager", action="store_true")
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--no-vsync", action="store_true", help="Передать sample --no-vsync для сравнения задержки.")
    parser.add_argument("--latency-check", action="store_true", help="Измерить X11 warp-to-front-buffer задержку marker; это не motion-to-photon.")
    args = parser.parse_args()
    x, xt = c.CDLL("libX11.so.6"), c.CDLL("libXtst.so.6")
    ptr, win = c.c_void_p, c.c_ulong

    def bind(lib, name, result, *parameters):
        fn = getattr(lib, name)
        fn.restype, fn.argtypes = result, parameters
        return fn

    bind(x, "XOpenDisplay", ptr, c.c_char_p)
    bind(x, "XCloseDisplay", c.c_int, ptr)
    bind(x, "XDefaultRootWindow", win, ptr)
    bind(x, "XGetInputFocus", c.c_int, ptr, c.POINTER(win), c.POINTER(c.c_int))
    bind(x, "XSetInputFocus", c.c_int, ptr, win, c.c_int, c.c_ulong)
    bind(x, "XQueryPointer", c.c_int, ptr, win, c.POINTER(win), c.POINTER(win),
         c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_uint))
    bind(x, "XWarpPointer", c.c_int, ptr, win, win, c.c_int, c.c_int, c.c_uint, c.c_uint, c.c_int, c.c_int)
    bind(x, "XQueryTree", c.c_int, ptr, win, c.POINTER(win), c.POINTER(win), c.POINTER(c.POINTER(win)), c.POINTER(c.c_uint))
    bind(x, "XFetchName", c.c_int, ptr, win, c.POINTER(ptr))
    bind(x, "XFree", c.c_int, ptr)
    bind(x, "XResizeWindow", c.c_int, ptr, win, c.c_uint, c.c_uint)
    bind(x, "XMapRaised", c.c_int, ptr, win)
    bind(x, "XIconifyWindow", c.c_int, ptr, win, c.c_int)
    bind(x, "XDefaultScreen", c.c_int, ptr)
    bind(x, "XInternAtom", c.c_ulong, ptr, c.c_char_p, c.c_int)
    bind(x, "XSendEvent", c.c_int, ptr, win, c.c_int, c.c_long, ptr)
    bind(x, "XClearArea", c.c_int, ptr, win, c.c_int, c.c_int, c.c_uint, c.c_uint, c.c_int)
    bind(x, "XFlush", c.c_int, ptr)
    bind(x, "XSync", c.c_int, ptr, c.c_int)
    bind(x, "XStringToKeysym", c.c_ulong, c.c_char_p)
    bind(x, "XKeysymToKeycode", c.c_uint, ptr, c.c_ulong)
    bind(xt, "XTestFakeKeyEvent", c.c_int, ptr, c.c_uint, c.c_int, c.c_ulong)
    bind(x, "XGetImage", ptr, ptr, win, c.c_int, c.c_int, c.c_uint, c.c_uint, c.c_ulong, c.c_int)
    bind(x, "XGetPixel", c.c_ulong, ptr, c.c_int, c.c_int)
    bind(x, "XDestroyImage", c.c_int, ptr)
    # Xlib по умолчанию завершает процесс на BadWindow раньше finally. Сохраняем
    # native-ошибки, чтобы успеть закрыть свой sample и восстановить pointer/focus.
    class XError(c.Structure):
        _fields_ = [("type", c.c_int), ("display", ptr), ("resource", win), ("serial", c.c_ulong),
                    ("code", c.c_ubyte), ("request", c.c_ubyte), ("minor", c.c_ubyte)]

    errors = []
    error_callback = c.CFUNCTYPE(c.c_int, ptr, c.POINTER(XError))

    @error_callback
    def on_error(_, event):
        errors.append((event.contents.code, event.contents.request, event.contents.resource))
        return 0

    bind(x, "XSetErrorHandler", ptr, error_callback)(on_error)
    display = x.XOpenDisplay(None)
    if not display:
        raise RuntimeError("Нужен доступный X11 DISPLAY.")
    root = x.XDefaultRootWindow(display)
    old_focus, revert = win(), c.c_int()
    x.XGetInputFocus(display, c.byref(old_focus), c.byref(revert))
    rr, child = win(), win()
    rx, ry, wx, wy, mask = c.c_int(), c.c_int(), c.c_int(), c.c_int(), c.c_uint()
    pointer_saved = x.XQueryPointer(display, root, c.byref(rr), c.byref(child), c.byref(rx), c.byref(ry),
                                   c.byref(wx), c.byref(wy), c.byref(mask))
    process = None
    messages = queue.Queue()
    held = set()

    def reader():
        for line in process.stdout:
            try:
                messages.put(json.loads(line))
            except json.JSONDecodeError:
                messages.put(dict(kind="error", text=line.strip()))

    def until(predicate, label):
        deadline = time.monotonic() + 6
        last = None
        while time.monotonic() < deadline:
            try:
                last = messages.get(timeout=max(0.01, deadline - time.monotonic()))
            except queue.Empty:
                break
            if last.get("kind") == "error":
                raise AssertionError(last["text"])
            if predicate(last):
                return last
        raise AssertionError(f"{label}: last={last}, exit={process.poll()}")

    def find(parent, title):
        name = ptr()
        if x.XFetchName(display, parent, c.byref(name)) and name.value:
            try:
                if c.string_at(name).decode(errors="replace") == title:
                    return parent
            finally:
                x.XFree(name)
        a, b, count = win(), win(), c.c_uint()
        children = c.POINTER(win)()
        if x.XQueryTree(display, parent, c.byref(a), c.byref(b), c.byref(children), c.byref(count)):
            try:
                for i in range(count.value):
                    result = find(children[i], title)
                    if result:
                        return result
            finally:
                if children:
                    x.XFree(children)
        return None

    def key(symbol):
        code = x.XKeysymToKeycode(display, x.XStringToKeysym(symbol.encode()))
        assert code, symbol
        held.add(code)
        xt.XTestFakeKeyEvent(display, code, True, 0)
        x.XFlush(display)
        time.sleep(0.06)
        xt.XTestFakeKeyEvent(display, code, False, 0)
        held.remove(code)
        x.XFlush(display)

    def activate(native):
        # WM restore через EWMH: одного XMapRaised недостаточно для снятия IconicState в Cinnamon.
        class ClientMessage(c.Structure):
            _fields_ = [("type", c.c_int), ("serial", c.c_ulong), ("send_event", c.c_int),
                        ("display", ptr), ("window", win), ("message_type", c.c_ulong),
                        ("format", c.c_int), ("data", c.c_long * 5)]
        message = ClientMessage(type=33, display=display, window=native,
                                message_type=x.XInternAtom(display, b"_NET_ACTIVE_WINDOW", False), format=32)
        message.data[0] = 1
        event = (c.c_long * 24)()
        c.memmove(event, c.byref(message), c.sizeof(message))
        x.XSendEvent(display, root, False, (1 << 19) | (1 << 20), c.byref(event))
        x.XFlush(display)

    def pixel(native, px, py):
        image = x.XGetImage(display, native, px, py, 1, 1, c.c_ulong(-1).value, 2)
        assert image, "XGetImage failed"
        try:
            value = x.XGetPixel(image, 0, 0)
            return ((value >> 16) & 255, (value >> 8) & 255, value & 255)
        finally:
            x.XDestroyImage(image)

    def color(actual, expected, label):
        assert all(abs(a - b) <= 3 for a, b in zip(actual, expected)), (label, actual, expected)

    def screenshot(native, width, height, filename):
        if args.output_dir is None:
            return
        image = x.XGetImage(display, native, 0, 0, width, height, c.c_ulong(-1).value, 2)
        assert image
        try:
            rows = bytearray()
            for yy in range(height):
                rows.append(0)
                for xx in range(width):
                    value = x.XGetPixel(image, xx, yy)
                    rows.extend(((value >> 16) & 255, (value >> 8) & 255, value & 255))
            def chunk(kind, data):
                return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
            png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            png += chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")
            args.output_dir.mkdir(parents=True, exist_ok=True)
            (args.output_dir / filename).write_bytes(png)
        finally:
            x.XDestroyImage(image)

    try:
        assembly = Path(__file__).resolve().parents[1] / "samples/OverlaySample/bin/Release/net10.0/OverlaySample.dll"
        env = dict(os.environ, XDG_SESSION_TYPE="x11")
        env.pop("WAYLAND_DISPLAY", None)
        sample_args = ["dotnet", str(assembly), "--trace", "--still"]
        if args.no_vsync:
            sample_args.append("--no-vsync")
        process = subprocess.Popen(sample_args,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, env=env)
        threading.Thread(target=reader, daemon=True).start()
        ready = until(lambda f: f.get("kind") == "ready", "startup")
        first = until(lambda f: f.get("rendered", 0) >= 3, "first render")
        native = find(root, ready["title"])
        assert native, "Sample window not found"
        x.XMapRaised(display, native)
        x.XSetInputFocus(display, native, 1, 0)
        x.XWarpPointer(display, 0, native, 0, 0, 0, 0, 500, 440)
        x.XFlush(display)
        record = until(lambda f: f.get("focused") and f.get("inside") and abs(f["x"] - 500) < 1
                       and abs(f["y"] - 440) < 1, "logical pointer")
        assert record["mode"] == "Normal"
        # Этот драйвер проверяет X11 front buffer; на HiDPI нужна ручная проверка плюс GPU-тест scale=2.
        assert record["width"] == record["framebufferWidth"] and record["height"] == record["framebufferHeight"], \
            "Native X11 pixel driver expects scale=1; use documented manual HiDPI checks."
        time.sleep(0.1)
        color(pixel(native, 500, 440), (255, 38, 191), "pointer marker")
        color(pixel(native, 30, 26), (51, 217, 230), "top accent")
        screenshot(native, record["width"], record["height"], "overlay-sample-linux.png")
        print("PASS: same-window overlay, logical pointer marker, Normal cursor")

        if args.latency_check:
            durations = []
            for i in range(24):
                target_x, target_y = (580 if i % 2 == 0 else 500), 440
                start = time.perf_counter()
                x.XWarpPointer(display, 0, native, 0, 0, 0, 0, target_x, target_y)
                x.XSync(display, False)
                if errors:
                    raise AssertionError(f"X11 marker warp failed: {errors}; sample exit={process.poll()}")
                deadline = start + 1
                while True:
                    actual = pixel(native, target_x, target_y)
                    if all(abs(a - b) <= 3 for a, b in zip(actual, (255, 38, 191))):
                        durations.append((time.perf_counter() - start) * 1000)
                        break
                    if time.perf_counter() >= deadline:
                        raise AssertionError(f"Marker did not reach {(target_x, target_y)} within 1s; pixel={actual}")
                    time.sleep(0.001)
                time.sleep(0.02)
            report = dict(measurement="X11 warp-to-front-buffer; not motion-to-photon", vsync=not args.no_vsync,
                          samples=len(durations), medianMs=statistics.median(durations),
                          p95Ms=statistics.quantiles(durations, n=20, method="inclusive")[18],
                          minMs=min(durations), maxMs=max(durations))
            print("LATENCY: " + json.dumps(report))
            if args.output_dir:
                args.output_dir.mkdir(parents=True, exist_ok=True)
                filename = "latency-no-vsync.json" if args.no_vsync else "latency-vsync.json"
                (args.output_dir / filename).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

        key("space")
        until(lambda f: f.get("visible") is False, "clear overlay")
        time.sleep(0.1)
        assert pixel(native, 30, 26) != (51, 217, 230), "Overlay was not cleared"
        key("space")
        until(lambda f: f.get("visible") is True, "restore overlay")
        time.sleep(0.1)
        color(pixel(native, 30, 26), (51, 217, 230), "restored overlay")
        print("PASS: SetOverlay(null), return to overlay")

        x.XResizeWindow(display, native, 640, 480)
        x.XFlush(display)
        record = until(lambda f: f.get("width") == 640 and f.get("height") == 480, "resize")
        time.sleep(0.15)
        color(pixel(native, 30, 26), (51, 217, 230), "resized top")
        color(pixel(native, 300, 475), (51, 217, 230), "clipped bottom stripe")
        screenshot(native, 640, 480, "overlay-resize-linux.png")
        print("PASS: resize 640x480, clipping, preserved overlay")

        while not messages.empty():
            messages.get_nowait()
        record = until(lambda f: f.get("kind") == "frame", "fresh snapshot before Expose")
        baseline = record["rendered"] - record["frame"]
        x.XClearArea(display, native, 24, 24, 100, 4, True)
        x.XFlush(display)
        until(lambda f: f.get("kind") == "frame" and f["rendered"] - f["frame"] > baseline, "Expose refresh without update")
        time.sleep(0.1)
        color(pixel(native, 30, 26), (51, 217, 230), "Refresh includes last overlay")
        print("PASS: Expose/Refresh renders last overlay without extra onUpdate")

        if args.window_manager:
            x.XIconifyWindow(display, native, x.XDefaultScreen(display))
            x.XFlush(display)
            time.sleep(0.2)
            while not messages.empty():
                messages.get_nowait()
            before = until(lambda f: f.get("kind") == "frame", "minimized snapshot")
            time.sleep(0.15)
            # Опустошить очередь и сравнить два снимка с интервалом, не старые queued frames.
            while not messages.empty():
                messages.get_nowait()
            after = until(lambda f: f.get("kind") == "frame", "minimized stable snapshot")
            assert before["rendered"] == after["rendered"], ("minimized window kept rendering", before, after)
            x.XMapRaised(display, native)
            activate(native)
            until(lambda f: f.get("rendered", 0) > after["rendered"], "restore rendering")
            time.sleep(0.1)
            color(pixel(native, 30, 26), (51, 217, 230), "restored from minimized")
            print("PASS: WM minimize skips render, restore preserves overlay")
        else:
            print("SKIP: WM minimize/restore (use --window-manager with a window manager)")
        x.XSetInputFocus(display, native, 1, 0)
        x.XFlush(display)
        until(lambda f: f.get("focused"), "focus before exit")
        key("Escape")
        assert process.wait(timeout=5) == 0
        print(f"PASS: clean exit; client={first['width']}x{first['height']}, framebuffer={first['framebufferWidth']}x{first['framebufferHeight']}")
        print("SKIP: physical HiDPI and manual readability (not established by synthetic X11 input)")
    finally:
        for code in held:
            xt.XTestFakeKeyEvent(display, code, False, 0)
        if process and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        x.XSetInputFocus(display, old_focus, revert, 0)
        if pointer_saved:
            x.XWarpPointer(display, 0, root, 0, 0, 0, 0, rx, ry)
        x.XSync(display, False)
        x.XCloseDisplay(display)


if __name__ == "__main__":
    main()
