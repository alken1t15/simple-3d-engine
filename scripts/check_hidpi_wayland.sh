#!/usr/bin/env bash
# Требует weston, Xvfb/xauth, Mesa и .NET 10; запускает отдельный native Wayland compositor.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ${1:-} != --inside-xvfb ]]; then
    exec xvfb-run -a -s '-screen 0 2400x1600x24' bash "$0" --inside-xvfb
fi
runtime=$(mktemp -d /tmp/engine-hidpi.XXXXXX)
chmod 700 "$runtime"
export XDG_RUNTIME_DIR="$runtime" WAYLAND_DISPLAY=engine-hidpi XDG_SESSION_TYPE=wayland
export LIBGL_ALWAYS_SOFTWARE=true ENGINE3D_GRAPHICS_TESTS=1 ENGINE_EXPECT_HIDPI=1
mkdir -p artifacts/hidpi
weston --backend=x11-backend.so --use-pixman --width=1920 --height=1280 --scale=2 \
    --socket=engine-hidpi --no-config --idle-time=0 --log="$runtime/weston.log" >"$runtime/output.log" 2>&1 &
compositor=$!
cleanup() {
    kill "$compositor" 2>/dev/null || true
    wait "$compositor" 2>/dev/null || true
    cp "$runtime/weston.log" artifacts/hidpi/weston.log
    rm -rf -- "$runtime"
}
trap cleanup EXIT
for n in $(seq 1 100); do [[ -S "$runtime/engine-hidpi" ]] && break; sleep 0.1; done
test -S "$runtime/engine-hidpi"
dotnet test Engine3D.sln -c Release --no-build --filter 'FullyQualifiedName~ClientSizeTests' \
    --logger 'console;verbosity=normal' | tee artifacts/hidpi/tests.log
