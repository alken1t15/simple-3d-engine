# Логический размер окна на Wayland

`WindowBounds` запрашивает глобальную позицию окна. Wayland её не предоставляет;
поэтому использование этого свойства только ради Width/Height оставляло GLFW error,
который прерывал Run. Это воспроизводилось в OverlaySample на `7d097269`.

`Engine.ClientSize` читает только логическую клиентскую область, без позиции.
OverlaySample/InputSample и GPU-проверки используют его для layout/диагностики.
Поведение WindowBounds для клиентов, сохраняющих позицию на X11, не изменено.

После сборки solution на Linux:

```sh
bash scripts/check_hidpi_wayland.sh
```

Нужны Weston, Xvfb/xauth и Mesa. Скрипт создаёт отдельный X11-дисплей и nested
Wayland compositor с output scale=2. GLFW и Engine работают как native Wayland client.
Тест требует framebuffer/client=2 по обеим осям и проверяет фактические пиксели overlay
внутри/снаружи логического прямоугольника. Он не подменяет renderer viewport.
Логи остаются в `artifacts/hidpi`, compositor завершается при любом результате.

Это автоматическая проверка настоящего Wayland buffer scale; она не является
ручным вводом физической мышью, тестом физического HiDPI-монитора или измерением FPS.
X11 native input/overlay сценарии запускаются отдельно по существующим инструкциям.
