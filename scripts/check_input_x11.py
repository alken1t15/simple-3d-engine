#!/usr/bin/env python3
"""Нативный X11/XTest-прогон публичного InputSample. Запускать на отдельном тестовом дисплее.

Требует собранный samples/InputSample, DISPLAY, libX11 и libXtst; Python-пакеты не нужны.
Синтезирует ввод и переключает фокус; это не ручная проверка устройства/WM/HiDPI.
"""

import ctypes as c
import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", choices=["all", "pointer-exit", "focus-rearm"], default="all",
                        help="Регрессии ревью; базовые сценарии выполняются всегда.")
    scenario = parser.parse_args().scenario
    if not sys.platform.startswith("linux") or not os.environ.get("DISPLAY"):
        raise RuntimeError("Нужны Linux и X11 DISPLAY (например, xvfb-run).")
    x = c.CDLL("libX11.so.6")
    xt = c.CDLL("libXtst.so.6")
    pointer, window = c.c_void_p, c.c_ulong

    def bind(lib, name, result, *args):
        fn = getattr(lib, name)
        fn.restype, fn.argtypes = result, args
        return fn

    bind(x, "XOpenDisplay", pointer, c.c_char_p)
    bind(x, "XCloseDisplay", c.c_int, pointer)
    bind(x, "XDefaultRootWindow", window, pointer)
    bind(x, "XQueryTree", c.c_int, pointer, window, c.POINTER(window), c.POINTER(window), c.POINTER(c.POINTER(window)), c.POINTER(c.c_uint))
    bind(x, "XFetchName", c.c_int, pointer, window, c.POINTER(pointer))
    bind(x, "XFree", c.c_int, pointer)
    bind(x, "XSetInputFocus", c.c_int, pointer, window, c.c_int, c.c_ulong)
    bind(x, "XGetInputFocus", c.c_int, pointer, c.POINTER(window), c.POINTER(c.c_int))
    bind(x, "XMapRaised", c.c_int, pointer, window)
    bind(x, "XCreateSimpleWindow", window, pointer, window, c.c_int, c.c_int,
         c.c_uint, c.c_uint, c.c_uint, c.c_ulong, c.c_ulong)
    bind(x, "XDestroyWindow", c.c_int, pointer, window)
    bind(x, "XStoreName", c.c_int, pointer, window, c.c_char_p)
    bind(x, "XResizeWindow", c.c_int, pointer, window, c.c_uint, c.c_uint)
    bind(x, "XWarpPointer", c.c_int, pointer, window, window, c.c_int, c.c_int, c.c_uint, c.c_uint, c.c_int, c.c_int)
    bind(x, "XQueryPointer", c.c_int, pointer, window, c.POINTER(window), c.POINTER(window),
         c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_uint))
    bind(x, "XStringToKeysym", c.c_ulong, c.c_char_p)
    bind(x, "XKeysymToKeycode", c.c_uint, pointer, c.c_ulong)
    bind(x, "XFlush", c.c_int, pointer)
    bind(x, "XSync", c.c_int, pointer, c.c_int)
    class XError(c.Structure):
        _fields_ = [("type", c.c_int), ("display", pointer), ("resource", window),
                    ("serial", c.c_ulong), ("code", c.c_ubyte), ("request", c.c_ubyte), ("minor", c.c_ubyte)]

    errors = []
    error_callback = c.CFUNCTYPE(c.c_int, pointer, c.POINTER(XError))

    @error_callback
    def on_error(_, event):
        errors.append((event.contents.code, event.contents.request))
        return 0

    bind(x, "XSetErrorHandler", pointer, error_callback)(on_error)
    bind(xt, "XTestFakeKeyEvent", c.c_int, pointer, c.c_uint, c.c_int, c.c_ulong)
    bind(xt, "XTestFakeButtonEvent", c.c_int, pointer, c.c_uint, c.c_int, c.c_ulong)
    bind(xt, "XTestFakeRelativeMotionEvent", c.c_int, pointer, c.c_int, c.c_int, c.c_ulong)
    display = x.XOpenDisplay(None)
    if not display:
        raise RuntimeError("Не удалось открыть DISPLAY.")
    root = x.XDefaultRootWindow(display)
    old_focus, revert = window(), c.c_int()
    x.XGetInputFocus(display, c.byref(old_focus), c.byref(revert))
    rr, child = window(), window()
    rx, ry, wx, wy, mask = c.c_int(), c.c_int(), c.c_int(), c.c_int(), c.c_uint()
    restore_pointer = x.XQueryPointer(display, root, c.byref(rr), c.byref(child), c.byref(rx), c.byref(ry), c.byref(wx), c.byref(wy), c.byref(mask))
    process = None
    focus_sink = None
    held_keys, held_buttons = set(), set()
    messages = queue.Queue()

    def read_lines():
        for line in process.stdout:
            try:
                messages.put(json.loads(line))
            except json.JSONDecodeError:
                messages.put({"kind": "error", "text": line.strip()})

    def receive(timeout):
        try:
            value = messages.get(timeout=timeout)
        except queue.Empty:
            raise AssertionError("Нет ожидаемого snapshot; InputSample завершился или завис.") from None
        if value.get("kind") == "error":
            raise AssertionError(value["text"])
        return value

    def until(predicate, label):
        deadline = time.monotonic() + 4
        last = None
        while time.monotonic() < deadline:
            try:
                last = receive(max(0.01, deadline - time.monotonic()))
            except AssertionError as error:
                raise AssertionError(f"{label}: {error}; последний snapshot {last}") from error
            if predicate(last):
                return last
        raise AssertionError(f"{label}: последний snapshot {last}")

    def collect(seconds=0.08):
        result = []
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            try:
                item = messages.get(timeout=max(0.001, deadline - time.monotonic()))
            except queue.Empty:
                break
            if item.get("kind") == "error":
                raise AssertionError(item["text"])
            result.append(item)
        return [item for item in result if item.get("kind") == "frame"]

    def focus(target):
        # WM может ещё отображать/переподчинять окно после MapNotify.
        for _ in range(20):
            errors.clear()
            x.XSetInputFocus(display, target, 1, 0)
            x.XSync(display, False)
            if not errors:
                return
            time.sleep(0.05)
        raise AssertionError(f"Не удалось установить фокус: X11 {errors}")

    def key(symbol, down):
        code = x.XKeysymToKeycode(display, x.XStringToKeysym(symbol.encode()))
        assert code, symbol
        xt.XTestFakeKeyEvent(display, code, down, 0)
        (held_keys.add if down else held_keys.discard)(code)
        x.XFlush(display)

    def button(number, down):
        xt.XTestFakeButtonEvent(display, number, down, 0)
        (held_buttons.add if down else held_buttons.discard)(number)
        x.XFlush(display)

    def find_window(parent, title):
        name = pointer()
        if x.XFetchName(display, parent, c.byref(name)) and name.value:
            try:
                if c.string_at(name).decode(errors="replace") == title:
                    return parent
            finally:
                x.XFree(name)
        parent_return, root_return, count = window(), window(), c.c_uint()
        children = c.POINTER(window)()
        if not x.XQueryTree(display, parent, c.byref(root_return), c.byref(parent_return), c.byref(children), c.byref(count)):
            return None
        try:
            for i in range(count.value):
                found = find_window(children[i], title)
                if found:
                    return found
        finally:
            if children:
                x.XFree(children)
        return None

    try:
        assembly = Path(__file__).resolve().parents[1] / "samples/InputSample/bin/Release/net10.0/InputSample.dll"
        env = dict(os.environ, XDG_SESSION_TYPE="x11")
        env.pop("WAYLAND_DISPLAY", None)
        process = subprocess.Popen(["dotnet", str(assembly), "--trace"], stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, text=True, env=env)
        threading.Thread(target=read_lines, daemon=True).start()
        ready = until(lambda f: f.get("kind") == "ready", "startup")
        until(lambda f: f.get("kind") == "frame", "first visible frame")
        collect(0.2)
        native = find_window(root, ready["title"])
        assert native, "Окно InputSample не найдено через X11."
        x.XMapRaised(display, native)
        focus(native)
        until(lambda f: f.get("focused") is True, "focus")
        collect()

        for symbol, name in [("a", "A"), ("d", "D"), ("Left", "Left"), ("Right", "Right"),
                             ("space", "Space"), ("r", "R"), ("w", "W"), ("s", "S"),
                             ("Shift_L", "LeftShift"), ("Shift_R", "RightShift"), ("Up", "Up"),
                             ("Down", "Down"), ("Tab", "Tab")]:
            key(symbol, True)
            until(lambda f: name in f.get("pressed", []) and name in f.get("keys", []), name + " down")
            frames = collect(0.8 if name == "W" else 0.08)  # удержать W дольше стандартной задержки autorepeat
            assert all(name not in f["pressed"] for f in frames), name + " repeated edge"
            key(symbol, False)
            until(lambda f: name in f.get("released", []) and name not in f.get("keys", []), name + " up")
        print("PASS: клавиши, оба Shift, фронты и удержания")

        def move_to(px, py, expected_delta=None):
            x.XWarpPointer(display, 0, native, 0, 0, 0, 0, px, py)
            x.XFlush(display)
            # Опрос backend уже может видеть позицию до следующего CursorPosCallback.
            # Проверка delta должна дождаться самого события, а не первого совпадения Position.
            return until(lambda f: abs(f.get("x", -1) - px) < 1 and abs(f.get("y", -1) - py) < 1
                         and (expected_delta is None or (abs(f.get("dx", 0) - expected_delta[0]) < 1
                              and abs(f.get("dy", 0) - expected_delta[1]) < 1)), "pointer position/delta")

        move_to(100, 100)  # первый delta после focus подавлен
        collect()
        movement = move_to(130, 120, expected_delta=(30, 20))
        assert abs(movement["dx"] - 30) < 1 and abs(movement["dy"] - 20) < 1, movement
        assert all(f["dx"] == 0 and f["dy"] == 0 for f in collect()), "повтор mouse delta"
        for number, name in [(1, "Left"), (3, "Right"), (2, "Middle")]:
            button(number, True)
            until(lambda f: name in f.get("mousePressed", []) and name in f.get("buttons", []), name + " down")
            button(number, False)
            until(lambda f: name in f.get("mouseReleased", []) and name not in f.get("buttons", []), name + " up")
        print("PASS: client position, delta один раз, три кнопки мыши")

        if scenario in ("all", "pointer-exit"):
            move_to(130, 120)
            collect()
            x.XWarpPointer(display, 0, native, 0, 0, 0, 0, 1100, 900)
            x.XSync(display, False)
            actual_x, actual_y, unused_x, unused_y = c.c_int(), c.c_int(), c.c_int(), c.c_int()
            root_return, child_return, buttons_return = window(), window(), c.c_uint()
            assert x.XQueryPointer(display, native, c.byref(root_return), c.byref(child_return),
                                   c.byref(unused_x), c.byref(unused_y), c.byref(actual_x), c.byref(actual_y), c.byref(buttons_return))
            assert actual_x.value >= 800 or actual_y.value >= 600, "Для выхода указателя нужен дисплей больше окна."
            outside = until(lambda f: f.get("focused") is True and not f.get("inside", True)
                            and abs(f.get("x", -1) - actual_x.value) < 1 and abs(f.get("y", -1) - actual_y.value) < 1,
                            f"pointer outside without focus loss: XQueryPointer=({actual_x.value},{actual_y.value})")
            assert not outside["mousePressed"], outside
            assert all(f["focused"] and not f["inside"] for f in collect()), "Курсор снаружи снова стал inside."
            entered = move_to(130, 120)
            assert entered["focused"] and entered["inside"], entered
            print("PASS: выход/повторный вход указателя без потери keyboard focus")

        key("Return", True)
        until(lambda f: f.get("mode") == "Captured" and "Enter" in f.get("pressed", []), "capture")
        key("Return", False)
        collect()
        xt.XTestFakeRelativeMotionEvent(display, 12, -7, 0)
        x.XFlush(display)
        collect()
        xt.XTestFakeRelativeMotionEvent(display, 18, 9, 0)
        x.XFlush(display)
        until(lambda f: f.get("dx", 0) > 0 and f.get("dy", 0) > 0, "captured movement")
        if scenario in ("all", "pointer-exit"):
            # Один большой XTest шаг обрезается краем физического экрана. Малые шаги
            # между updates дают GLFW перецентровать курсор и накапливают virtual position.
            virtual = None
            for _ in range(16):
                xt.XTestFakeRelativeMotionEvent(display, 100, 80, 0)
                x.XFlush(display)
                samples = collect(0.06)
                virtual = next((f for f in samples if f["mode"] == "Captured" and
                                (f["x"] >= f["width"] or f["y"] >= f["height"])), None)
                if virtual:
                    break
            assert virtual, f"Captured потерял virtual position: {samples[-1] if samples else 'нет кадров'}"
            assert not virtual["inside"], virtual
            print("PASS: Captured сохраняет неограниченную виртуальную позицию")
        key("Escape", True)
        until(lambda f: f.get("mode") == "Normal" and "Escape" in f.get("pressed", []), "release")
        key("Escape", False)
        collect()

        key("Return", True)
        until(lambda f: f.get("mode") == "Captured", "capture again")
        key("Return", False)
        key("w", True)
        button(1, True)
        until(lambda f: "W" in f.get("keys", []) and "Left" in f.get("buttons", []), "held before focus loss")
        # Отдельное пустое окно только тестового драйвера: WM может сразу вернуть фокус с root.
        # Оно не получает и не маршрутизирует игровой ввод, Engine по-прежнему создаёт одно окно.
        focus_sink = x.XCreateSimpleWindow(display, root, 0, 0, 80, 60, 0, 0, 0)
        x.XStoreName(display, focus_sink, b"Engine input test focus sink")
        x.XMapRaised(display, focus_sink)
        focus(focus_sink)
        lost = until(lambda f: f.get("focused") is False, "focus loss")
        assert lost["mode"] == "Normal" and not lost["keys"] and not lost["buttons"], lost
        assert not lost["pressed"] and not lost["mousePressed"] and lost["dx"] == lost["dy"] == 0, lost
        assert not lost["released"] and not lost["mouseReleased"], lost
        focus(native)
        until(lambda f: f.get("focused") is True, "focus gain")
        frames = collect(1.2 if scenario in ("all", "focus-rearm") else 0.08)
        invalid = [f for f in frames if f["mode"] != "Normal" or "W" in f["keys"]
                   or "W" in f["pressed"] or f["buttons"]]
        assert not invalid, f"Автоповтор после focus gain без нового нажатия: {invalid[0] if invalid else ''}"
        key("w", False)
        button(1, False)
        collect()
        key("w", True)
        button(1, True)
        until(lambda f: "W" in f.get("keys", []) and "Left" in f.get("buttons", []), "fresh press after focus")
        key("w", False)
        button(1, False)
        print("PASS: capture/release, focus loss/gain, release/new press")

        if scenario in ("all", "focus-rearm"):
            collect()
            key("a", True)
            until(lambda f: "A" in f.get("keys", []), "old key before focus loss")
            focus(focus_sink)
            until(lambda f: f.get("focused") is False, "focus loss for release outside")
            key("a", False)  # отпускание не доставляется окну Engine
            key("w", True)   # новое физическое удержание началось в другом окне
            collect()
            focus(native)
            until(lambda f: f.get("focused") is True, "focus gain with different physical keys")
            frames = collect(1.2)
            invalid = [f for f in frames if "W" in f["keys"] or "W" in f["pressed"] or "A" in f["keys"]]
            assert not invalid, f"Некорректный rearm после изменения удержаний снаружи: {invalid[0] if invalid else ''}"
            key("a", True)
            until(lambda f: "A" in f.get("pressed", []), "released outside key accepts first new press")
            key("a", False)
            key("w", False)
            until(lambda f: not f.get("keys", []), "release physically held outside key")
            key("w", True)
            until(lambda f: "W" in f.get("pressed", []), "fresh press for key held outside")
            key("w", False)
            print("PASS: отпускание вне окна и удержание, начатое без фокуса")

        x.XResizeWindow(display, native, 640, 480)
        x.XFlush(display)
        resized = until(lambda f: f.get("width") == 640 and f.get("height") == 480, "resize")
        position = move_to(200, 150)
        assert position["inside"], position
        print(f"PASS: resize и logical position; framebuffer={resized['framebufferWidth']}x{resized['framebufferHeight']}")
        key("Escape", True)
        key("Escape", False)
        process.wait(timeout=5)
        assert process.returncode == 0, process.returncode
        print("PASS: завершение; ручные Alt-Tab/HiDPI проверяются отдельно")
    finally:
        for code in held_keys:
            xt.XTestFakeKeyEvent(display, code, False, 0)
        for number in held_buttons:
            xt.XTestFakeButtonEvent(display, number, False, 0)
        x.XFlush(display)
        if process and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        if focus_sink:
            x.XDestroyWindow(display, focus_sink)
        if old_focus.value:
            x.XSetInputFocus(display, old_focus, revert.value, 0)
        if restore_pointer:
            x.XWarpPointer(display, 0, root, 0, 0, 0, 0, rx.value, ry.value)
        x.XFlush(display)
        x.XCloseDisplay(display)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
