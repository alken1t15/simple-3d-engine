#!/usr/bin/env bash
# Автоматическая проверка окна spike: повторные запуски, обработка ошибки OpenGL, culling и контрольные кадры.
# Запуск из корня репозитория: bash spikes/OpenTkSpike/smoke-test.sh [папка для результатов]
# Нужен графический дисплей; в CI скрипт запускается под xvfb-run с программным OpenGL (Mesa llvmpipe).
set -euo pipefail

out="${1:-artifacts/spike-smoke}"
mkdir -p "$out"

dotnet build spikes/OpenTkSpike --configuration Release --nologo --verbosity quiet
run() { dotnet run --project spikes/OpenTkSpike --configuration Release --no-build -- "$@"; }

echo "== 1. 10 циклов: открыть окно, показать 30 кадров, закрыть"
run --frames 30 --cycles 10 | tee "$out/cycles.log"
grep -q "Window closed without exceptions." "$out/cycles.log"
test "$(grep -c "GPU resources released." "$out/cycles.log")" -eq 10

echo "== 2. Ошибка OpenGL на кадре 5: должна быть обнаружена на этом кадре, ресурсы освобождены"
if run --frames 30 --fail-at-frame 5 > "$out/fail.log" 2>&1; then
    echo "FAIL: the injected OpenGL error was not detected." >&2
    exit 1
fi
grep "OpenGL errors while rendering frame 5: InvalidEnum" "$out/fail.log"
grep "GPU resources released." "$out/fail.log"

echo "== 3. Culling: задние грани не видны (on ≈ off), неверный обход ломает кадр (front ≠ off)"
for mode in off on front; do
    run --frames 3 --angle 0.7 --cull "$mode" --screenshot "$out/cull-$mode.png" > /dev/null
done
run --compare "$out/cull-on.png" "$out/cull-off.png" --max-diff-percent 0.05
run --compare "$out/cull-front.png" "$out/cull-off.png" --min-diff-percent 1

echo "== 4. Контрольные кадры для просмотра"
run --frames 3 --angle 0.7 --screenshot "$out/textured.png" > /dev/null
run --frames 3 --angle 0.7 --texture off --screenshot "$out/colored.png" > /dev/null
run --frames 3 --angle 0.7 --depth off --screenshot "$out/depth-off.png" > /dev/null

echo "Smoke test passed. Results: $out"
