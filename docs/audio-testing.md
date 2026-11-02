# Проверки аудиосервиса Linux

Задача: [simple-platformer #18](https://github.com/alken1t15/simple-platformer/issues/18).
Проверки не требуют окна/OpenGL и не подтверждают готовность звука в игре:
игровые WAV и AudioCoordinator относятся к #21, обновление game gitlink — к интеграции TL.

## Автоматические проверки

Из корня репозитория движка:

```sh
dotnet restore Engine3D.sln
dotnet build Engine3D.sln -c Release --no-restore
dotnet test Engine3D.sln -c Release --no-build
dotnet test Engine3D.sln -c Release --no-build --environment ENGINE3D_AUDIO_TESTS=1 --filter 'FullyQualifiedName~Audio|FullyQualifiedName~WaveData'
```

Обычный CPU-прогон использует Recording/Silent backend для WAV, gain, ownership,
потоков, eviction, cleanup/error и повторного Dispose. NoDevice проверяется
отдельным Linux-процессом с ALSOFT_DRIVERS=unavailable; при отсутствии самой
библиотеки сохраняются тот же fallback и проверки файлов. Настоящий OpenAL
проверяется при ENGINE3D_AUDIO_TESTS=1: дочерний процесс использует null driver,
две независимые сессии, mono/stereo, 32 loop, pause/resume, StopAll и повторный cleanup.
Отдельный сценарий одновременно создаёт устройства на двух разных потоках-владельцах:
сериализация нативных вызовов защищает общий current context.
Это проверка нативных ресурсов/API без физического звука.

Отдельная native-регрессия откладывает фактический Pause до естественного окончания
one-shot после managed-проверки Finished. Проверяется, что Resume не запускает
завершившийся source заново; loop Pause/Resume и запрет перезапуска stopped source
проверяются в том же процессе. Ожидание ограничено таймаутом и не останавливает
source искусственно. Как и остальные native-тесты, сценарий использует null driver.

CI сохраняет CPU job без требования аудиоустройства и дополнительно запускает
audio-native-tests с системным libopenal1 и null driver. Sample и spike собираются
отдельно; они не являются зависимостью библиотек. Артефакты TRX — artifacts/audio-tests.

## Публичный клиент без вывода звука

```sh
dotnet build samples/AudioSample -c Release
dotnet run --project samples/AudioSample -c Release --no-build -- --probe
ALSOFT_DRIVERS=unavailable dotnet run --project samples/AudioSample -c Release --no-build -- --expect-silent
```

`--probe` требует реальное устройство, но создаёт только voices с volume=0.
`--expect-silent` требует fallback, проверяет загрузку собственного WAV и операции
без устройства. Отказ открытия не подменяется успешным прослушиванием.

## Ручное прослушивание

```sh
dotnet run --project samples/AudioSample -c Release --no-build -- --interactive
```

Клиент использует только публичный Engine3D.Audio и собственные генерируемые PCM WAV
во временной папке. После штатного выхода клипы/voices/device освобождаются, папка
удаляется. Тоны не являются комплектом игровых ресурсов. Включите звук ноутбука;
для перехода между этапами нажимайте Enter. Enter не записывает автоматический PASS.

Проверка пользователем 02.11.2026: Linux Mint 22.2 x64, .NET SDK 10.0.112,
OpenAL Soft 1.23.1 (`libopenal1 1:1.23.1-4build1`), реальный вывод ноутбука.
Пользователь после запуска --interactive подтвердил: «Все этапы сработали».

| Этап | Ожидается | Факт |
|---|---|---|
| Фон | Слышен повторяющийся низкий тон | PASS, подтверждено пользователем |
| Эффекты | 8 коротких эффектов поверх продолжающегося фона | PASS, подтверждено пользователем |
| Pause | Фон замолкает; после окончания эффектов тишина | PASS, подтверждено пользователем |
| Resume | Фон продолжается без второго loop | PASS, подтверждено пользователем |
| Master 0.25 | Уже звучащий фон становится тише | PASS, подтверждено пользователем |
| Mute / возврат 1 | Тишина, затем исходная громкость | PASS, подтверждено пользователем |
| StopAll | Тишина и штатное завершение | PASS, подтверждено пользователем |

Дополнительно --probe и --expect-silent прошли в текущей реализации; native/CPU
результаты фиксируются вместе с точным SHA итогового PR, поскольку на момент
локальной проверки изменения ещё не закоммичены.

## Ограничения

Проверен Linux-стенд, не слышимость macOS/Windows и не игровые события.
В песочнице доступ к Pulse/PipeWire блокировался EPERM; реальное открытие устройства
проверялось вне песочницы либо в терминале пользователя. Наличие сокета или успешный
null backend само по себе не считалось audible PASS. Ручной протокол не является
замером latency/производительности; такие числа не фиксировались.
Исходный graphics renderer не изменён; графические проверки не включались специально.
