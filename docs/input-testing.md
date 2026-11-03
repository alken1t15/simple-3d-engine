# Проверка расширения ввода Ball Adventure #16

Задача: [simple-platformer/issues/16](https://github.com/alken1t15/simple-platformer/issues/16).
Изменения находятся в движке, game submodule не редактируется. Upstream PR и PR точного
game gitlink создаются отдельно; до их принятия зависимая интеграция игры не завершена.
Новых NuGet-пакетов, ассетов или скопированного стороннего кода здесь нет.
Системные библиотеки X11/XTest для тестового скрипта учтены в [THIRD_PARTY](../THIRD_PARTY.md).

## Автоматические проверки

```sh
dotnet restore Engine3D.sln
dotnet build Engine3D.sln -c Release --no-restore
dotnet test Engine3D.sln -c Release --no-build
dotnet test Engine3D.sln -c Release --no-build --environment ENGINE3D_GRAPHICS_TESTS=1
dotnet build samples/InputSample -c Release
dotnet run --project samples/InputSample -c Release --no-build -- --help
dotnet run --project samples/InputSample -c Release --no-build -- --smoke
git diff --check
```

CPU: фронты клавиш/кнопок, auto-repeat, быстрый press/release, иммутабельность,
старые численные значения InputKey, суммирование delta, mode/focus reset и release/new press.
Lifecycle с настоящим окном: owner thread, Input только в update, CursorMode до Run/в update,
запрет после Run/Dispose, освобождение после исключения callback, существующие графические регрессии.

Автоматический сквозной Linux-тест использует Python stdlib, libX11 и libXtst.
Он запускает InputSample, посылает XTest-события и проверяет JSON-снимки публичного API:
все 15 клавиш, три кнопки, position/delta, capture/release, focus loss/gain с удержаниями,
повторное нажатие и resize. Регрессии ревью дополнительно проверяют выход/вход указателя
без focus loss, виртуальную позицию Captured за client bounds, удержание W 1.2 с после
focus gain (дольше обычной задержки autorepeat), отпускание вне окна и удержание,
начавшееся без фокуса. Скрипт меняет системный фокус/курсор, поэтому предпочтителен
отдельный тестовый дисплей:

```sh
xvfb-run --auto-servernum --server-args='-screen 0 1280x1024x24' python scripts/check_input_x11.py
```

Для независимого воспроизведения двух замечаний: `--scenario pointer-exit` или
`--scenario focus-rearm` (базовые сценарии остаются включены). Без аргументов и в CI
выполняются обе регрессии. На исходном HEAD `5dfdd9e` сценарии воспроизводят старую
позицию (130,120) при физической (1100,900) и ложный W pressed после focus gain.

На уже подготовленном X11-дисплее можно вызвать `python scripts/check_input_x11.py`.
В конце скрипт отпускает тестовые кнопки, завершает свой процесс и восстанавливает фокус/курсор.
Нужен собранный `samples/InputSample` в Release; сборка не запускается скрыто из теста.
Wayland-native этот тест не проверяет: он использует X11, при наличии compositor — XWayland.
Для focus loss драйвер создаёт маленькое пустое X11-окно-приёмник фокуса, которое
удаляется в finally. Оно существует только в тесте; Engine и InputSample используют одно окно.

Первоначальный локальный прогон HEAD `5dfdd9e`, 03.11.2026: Release build без предупреждений; CPU — 442 PASS,
32 графических/аудиотеста пропущены без их opt-in. С `ENGINE3D_GRAPHICS_TESTS=1` —
472 PASS, 2 аудиотеста пропущены. InputSample build/help/smoke — PASS, три кадра,
финальный CursorMode.Normal. Сквозной X11/XTest-прогон — PASS для клавиш, pointer,
capture/focus/rearm и resize 640×480; framebuffer также 640×480, то есть реальный
HiDPI этим прогоном не подтверждён. Проверены локальные ссылки и `git diff --check`.
Этот первоначальный тест не покрывал найденные ревьюером exit/rearm сценарии выше.
CI исходного SHA позднее прошёл на GitHub; ручной Alt-Tab, физическая мышь, перенос между DPI-мониторами
и Wayland-native остаются непроведёнными проверками.

После исправлений ревью: restore и Release build — PASS без предупреждений;
CPU — 444 PASS/32 SKIP; с графическим opt-in — 474 PASS/2 audio SKIP.
InputSample пересобран; bounded smoke — три кадра и CursorMode.Normal.
Расширенный X11-прогон прошёл обе воспроизводящие регрессии, virtual position в Captured
и сценарий отпускания/нового удержания вне окна. Backend rearm X11 теперь использует
физический keymap; остальные платформы этим прогоном не проверены.

## Ручной Linux-прогон

```sh
dotnet run --project samples/InputSample -c Release --no-build
```

Проверять на реальном целевом окружении, фиксируя OS/window system, масштаб дисплея,
commit, наблюдаемый результат и PASS/FAIL/SKIP:

1. Открыть окно: курсор свободен. WASD/стрелки двигают куб, оба Shift ускоряют,
   Space/Left/Right/Middle меняют цвет, R сбрасывает. Удержание не повторяет pressed.
2. Enter включает capture: курсор скрыт, мышь вращает куб, длинное движение не ограничено
   краем экрана. Enter освобождает; Escape в capture освобождает, следующий Escape закрывает.
3. Alt-Tab с зажатой W/Space/Shift/кнопкой: курсор освобождён, input очищен. Вернуться,
   продолжая удержание: нет самопроизвольного движения/capture. Отпустить и нажать снова:
   ввод работает. Для нового capture требуется Enter.
4. После focus/mode change нет скачка вращения. При изменении частоты кадров одинаковое
   суммарное движение мыши даёт одинаковый поворот; delta не умножается на dt.
5. Resize, затем перенос окна между мониторами с разным DPI: позиция совпадает с client
   coordinates. В терминале сравнить client и framebuffer; при реальном масштабе 2×
   проверить, что позиция указателя не удвоилась. Отсутствие второго масштаба — SKIP.
6. `--smoke`: три кадра, автоматический выход, без capture. Закрытие окна во время capture
   возвращает курсор. Повторный запуск работает.

Автоматический focus switch не равен ручному Alt-Tab через конкретный WM. Smoke и CPU-тесты
не подтверждают реальный HiDPI, работу физической мыши, внешний вид на всех GPU или FPS.
