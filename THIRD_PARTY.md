# Сторонние зависимости и ассеты

Сторонние модели, текстуры и шрифты в репозиторий не добавлены. NuGet-пакеты ниже подключены в экспериментальных проектах [`spikes/`](spikes/) (см. [описание spike](docs/spikes.md)); в репозиторий попадают только ссылки на пакеты, сами пакеты скачивает NuGet. В `tests/Engine3D.Tests` добавлены пакеты MSTest для будущих CPU-тестов основного solution.

Код и документация этого репозитория распространяются по [MIT License](LICENSE). Это не переносит MIT автоматически на сторонние ассеты: для каждой внешней библиотеки, модели, текстуры или шрифта нужно сохранить исходную лицензию и проверить совместимость с учебным использованием и публичным GitHub-репозиторием.

При добавлении каждого стороннего материала заполнить таблицу **в том же PR**:

| Материал / версия | Автор и источник | Лицензия и ссылка на ее текст | Где используется | Кто проверил |
|---|---|---|---|---|
| OpenTK 4.9.4 | The Open Toolkit, [github.com/opentk/opentk](https://github.com/opentk/opentk) | MIT, [licenses.nuget.org/MIT](https://licenses.nuget.org/MIT) | `spikes/OpenTkSpike`: окно, контекст, OpenGL | @VladimirKhmelev (по nuspec пакета) |
| GLFW 3.4 (`OpenTK.redist.glfw` 3.4.0.44, транзитивно через OpenTK) | Marcus Geelnard, Camilla Löwy, [glfw.org](https://www.glfw.org/) | zlib/libpng, `COPYING.md` в пакете; [glfw.org/license](https://www.glfw.org/license.html) | `spikes/OpenTkSpike`: нативная библиотека окна | @VladimirKhmelev (по `COPYING.md` пакета) |
| StbImageSharp 2.30.16 | StbSharp, [github.com/StbSharp/StbImageSharp](https://github.com/StbSharp/StbImageSharp) | Unlicense OR MIT, [licenses.nuget.org](https://licenses.nuget.org/Unlicense%20OR%20MIT) | `spikes/OpenTkSpike`: декодирование PNG | @VladimirKhmelev (по nuspec пакета) |
| StbImageWriteSharp 1.16.7 | StbSharp, [github.com/StbSharp/StbImageWriteSharp](https://github.com/StbSharp/StbImageWriteSharp) | Public Domain — указано только в README репозитория; в пакете и репозитории нет файла лицензии и SPDX-выражения | `spikes/OpenTkSpike`: сохранение скриншотов в PNG (отладочная функция) | @VladimirKhmelev (по README) |
| xUnit 2.9.3, xunit.runner.visualstudio 3.1.4 | .NET Foundation, [xunit.net](https://xunit.net/) | Apache-2.0, [licenses.nuget.org/Apache-2.0](https://licenses.nuget.org/Apache-2.0) | `spikes/OpenTkSpike.Tests` | @VladimirKhmelev (по nuspec пакетов) |
| Microsoft.NET.Test.Sdk 17.14.1 | Microsoft, [github.com/microsoft/vstest](https://github.com/microsoft/vstest) | MIT, [licenses.nuget.org/MIT](https://licenses.nuget.org/MIT) | `spikes/OpenTkSpike.Tests` | @VladimirKhmelev (по nuspec пакета) |
| Microsoft.NET.Test.Sdk 18.0.1 | Microsoft, [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.0.1) | MIT, [текст](https://github.com/microsoft/vstest/blob/v18.0.1/LICENSE) | `tests/Engine3D.Tests`: запуск через `dotnet test` | @Nickovlev (метаданные NuGet и лицензия репозитория) |
| MSTest.TestAdapter 4.0.2 | Microsoft, [NuGet](https://www.nuget.org/packages/MSTest.TestAdapter/4.0.2) | MIT, [текст](https://github.com/microsoft/testfx/blob/v4.0.2/LICENSE) | `tests/Engine3D.Tests`: обнаружение и запуск MSTest | @Nickovlev (метаданные NuGet и лицензия репозитория) |
| MSTest.TestFramework 4.0.2 | Microsoft, [NuGet](https://www.nuget.org/packages/MSTest.TestFramework/4.0.2) | MIT, [текст](https://github.com/microsoft/testfx/blob/v4.0.2/LICENSE) | `tests/Engine3D.Tests`: тесты Core | @Nickovlev (метаданные NuGet и лицензия репозитория) |

**Собственные ассеты команды:** контрольная текстура [`spikes/OpenTkSpike/Assets/control.png`](spikes/OpenTkSpike/Assets/control.png) создана командой скриптом [`generate_control_texture.py`](spikes/OpenTkSpike/Assets/generate_control_texture.py) (без шрифтов и чужих изображений) и распространяется вместе с репозиторием по MIT. Скриншоты в [`docs/spikes/`](docs/spikes/) сняты с этой сцены.

Собственная контрольная текстура для проверки UV должна быть явно помечена как созданная командой. Наличие чужого ассета не делает его автоматически совместимым с лицензией репозитория.
