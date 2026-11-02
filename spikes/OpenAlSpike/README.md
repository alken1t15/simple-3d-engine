# OpenAL spike для игровой задачи #18

Изолированная проверка кандидата OpenTK.Audio.OpenAL 4.9.4 + системной OpenAL Soft.
Не зависит от библиотек движка, не входит в Engine3D.sln. Библиотека Engine3D.Audio
не использует spike как зависимость. [Задача игры](https://github.com/alken1t15/simple-platformer/issues/18).

```sh
dotnet build spikes/OpenAlSpike -c Release
dotnet run --project spikes/OpenAlSpike -c Release --no-build -- --probe
dotnet run --project spikes/OpenAlSpike -c Release --no-build -- --audible
ALSOFT_DRIVERS=unavailable dotnet run --project spikes/OpenAlSpike -c Release --no-build -- --expect-no-device
```

`--probe` открывает устройство/context и выделяет 32 источника без воспроизведения.
`--audible` играет собственные синусоидальные тоны: 8 эффектов + loop, пауза,
resume с master 0.25, mute, возврат master 1. Всего около 6 с. Завершение освобождает
sources, buffers, context и device. `--expect-no-device` проверяет отказ открытия
устройства в отдельном процессе; OpenAL Soft читает окружение до инициализации.

Проверено на Linux Mint 22.2 x64, .NET SDK 10.0.112, libopenal1 1.23.1:
`--probe` и `--expect-no-device` — PASS. Нативное состояние pause/gain/cleanup
в `--audible` — PASS; пользователь подтвердил слышимость гудков. Полный ручной
прогон готового публичного API выполнен отдельно — [протокол](../../docs/audio-testing.md).
Песочница блокировала соединение с Pulse/PipeWire; открытие реального устройства
и воспроизведение проверялись вне неё. Это ограничение среды запуска, не NoDevice стенда.

Тоны создаются программно, сторонних звуковых файлов нет. Источники и лицензии
пакета и системной библиотеки — [THIRD_PARTY](../../THIRD_PARTY.md).
