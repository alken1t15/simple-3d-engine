# Отдельный аудиосервис Engine3D.Audio

Расширение для [игровой задачи #18](https://github.com/alken1t15/simple-platformer/issues/18),
по [App/engine v1 игры](https://github.com/alken1t15/simple-platformer/blob/main/docs/app-engine-contract-v1.md#audio).
Сервис реализован в отдельном проекте net10.0, без зависимости от Engine3D.Core,
Scene, окна и OpenGL. Публичный API не раскрывает OpenTK-типы и нативные handles.
`Engine.Create` не инициализирует звук: устройство явно создаёт и освобождает клиент.

## Использование

```csharp
using Engine3D.Audio;

using var audio = AudioDevice.Create(new AudioOptions { AllowSilentFallback = true });
if (audio.IsSilent) Console.Error.WriteLine(audio.Diagnostic);
audio.MasterVolume = 0.7f;
using var clip = audio.LoadWave("tone.wav");
using var voice = audio.Play(clip, loop: true, volume: 0.5f);
voice.Pause();
voice.Resume();
audio.MasterVolume = 0f; // mute; клиент хранит предпочитаемую громкость отдельно
audio.StopAll();
```

В приложении устройство создаётся на основном потоке до показа меню и живёт
до завершения процесса. Оно не пересоздаётся при Next/Restart. Пути к игровым
ресурсам клиент строит от AppContext.BaseDirectory; audio API принимает путь
к файлу и не знает структуры игры, монет, уровней или событий.
Пример выше демонстрирует вызовы: для прослушивания нужен цикл клиента или
[пошаговый sample](../samples/AudioSample/Program.cs), чтобы процесс не завершился сразу.

## Публичный API

```csharp
public sealed class AudioOptions
{
    public bool AllowSilentFallback { get; init; } = true;
}
public sealed class AudioDevice : IDisposable
{
    public static AudioDevice Create(AudioOptions options);
    public bool IsSilent { get; }
    public string? Diagnostic { get; }
    public float MasterVolume { get; set; }
    public AudioClip LoadWave(string path);
    public AudioVoice Play(AudioClip clip, bool loop = false, float volume = 1f);
    public void StopAll();
    public void Dispose();
}
public sealed class AudioClip : IDisposable { public void Dispose(); }
public sealed class AudioVoice : IDisposable
{
    public void Pause();
    public void Resume();
    public void Stop();
    public void Dispose();
}
```

MasterVolume и volume конечны и лежат в [0,1]; ошибка — ArgumentOutOfRangeException.
Итоговый gain — их произведение. MasterVolume действует на уже играющие и
приостановленные voices; mute не создаёт и не удаляет источники.
Повторный Play того же клипа создаёт отдельную voice. Pause/Resume повторяемы;
Resume продолжает только паузу и не перезапускает закончившуюся/остановленную voice.
Для нового воспроизведения нужен Play. Stop повторяем до Dispose.
OpenAL backend перед возобновлением проверяет фактическое состояние Paused:
если one-shot естественно закончился между проверкой Finished и нативным Pause,
последующий Resume сохраняет его остановленным и не воспроизводит эффект повторно.

После успешного Play устройство удерживает не больше 32 voices. Каждый Play проверяет
завершение non-loop voices и освобождает все закончившиеся/явно остановленные voices;
они не накапливаются до заполнения лимита. Приостановленные voices и loop без явного
Stop не опрашиваются на естественное завершение. Если после этого лимит заполнен,
выбирается самая старая non-loop. Если все 32 — активные либо приостановленные loop, Play выдаёт
InvalidOperationException. Вытесненная voice освобождается: Pause/Resume/Stop
на старом объекте дают ObjectDisposedException, повторный Dispose безопасен.
Автоматически освобождённые завершённые voices имеют те же правила доступа.

Новый native source создаётся до освобождения старых: если backend.Play не удался,
существующие voices сохраняются. Для замены временно допустим 33-й source;
backend запрашивает соответствующий резерв, публичный лимит остаётся 32.
При ошибках освобождения обрабатываются остальные выбранные voices и новая voice,
затем Play сообщает InvalidOperationException со всеми причинами. Отката уже выполненного
cleanup нет; гарантии сохранения старых voices относятся к ошибке создания нового source.
Завершившийся эффект без следующего Play сохраняет source до явного Dispose,
освобождения клипа или устройства; фонового потока очистки нет.

### Пауза приложения и потеря фокуса

В v1 нет PauseAll/ResumeAll. Клиент (игровой App/AudioCoordinator) хранит handles
voices, которые нужно приостанавливать, вызывает Pause при потере фокуса и Resume
только для приостановленных им voices после возвращения фокуса. Он также прекращает
создание новых эффектов на паузе. MasterVolume=0 является mute и не заменяет паузу:
позиция воспроизведения при mute продолжает меняться.
Resume не создаёт вторую loop voice. Клиент учитывает автоматическое освобождение
завершённых/вытесненных voices: их методы дают ObjectDisposedException,
такие handles удаляются из клиентского реестра. Игровая интеграция относится к
[задаче игры #21](https://github.com/alken1t15/simple-platformer/issues/21).

## Поток и владение

Все публичные операции устройства/клипов/voices выполняются на потоке AudioDevice.Create;
другой поток получает InvalidOperationException до backend-вызова. Clip принадлежит
своему устройству: чужой clip — ArgumentException, освобождённый — ObjectDisposedException.
AudioDevice владеет загруженными клипами и созданными voices; клиент может освободить
их раньше, а при Device.Dispose оставшиеся ресурсы освобождаются автоматически.

Разные устройства могут иметь разных владельцев. Переключение общего OpenAL
current context и нативные операции сериализуются; прежний контекст восстанавливается
после вызова. Это не разрешает обращаться к одному устройству с чужого потока.

Clip.Dispose останавливает и удаляет связанные voices, затем buffer. Device.Dispose
обрабатывает все voices, затем clips/buffers, context и device; ошибка одного
освобождения не отменяет попытки остальных и сообщается через InvalidOperationException
с причиной. Повторный Dispose безопасен. После освобождения остальные операции,
включая свойства устройства и Stop voice, дают ObjectDisposedException.

## Поддерживаемый WAV и ошибки

RIFF/WAVE, формат PCM (tag 1), signed integer16 little-endian, mono/stereo,
8–48 kHz. BlockAlign и ByteRate должны соответствовать каналам/частоте/разрядности.
Поддержаны неизвестные chunks с нечётным padding, перестановка fmt/data,
PCM fmt длиной 16 и WAVEFORMATEX с согласованным cbSize. Требуются ровно один fmt,
один непустой data, согласованные границы RIFF/файла и целые sample frames.
Максимальная длина data ограничена int.MaxValue байт (предел размера buffer API).
Отдельные кодеки, float WAV, extensible tag, RF64 и streaming не добавлены.
В v1 размер RIFF должен точно совпадать с длиной файла, обязательный chunk padding
должен присутствовать. WAV с хвостом после RIFF и WAVE_FORMAT_EXTENSIBLE, даже с
PCM16 subformat, отклоняются как неподдерживаемые. Для игровых ресурсов нужен
экспорт PCM с format tag 1; расширение совместимости остаётся последующей доработкой.

Повреждённый/неподдерживаемый WAV — InvalidDataException с path. Отсутствующий
файл — FileNotFoundException; ошибки доступа/чтения сохраняют стандартные
исключения .NET. Ошибка нативной загрузки buffer — InvalidOperationException с path
и причиной. Неверный файл полностью проверяется до выделения нативного buffer.

## Linux backend и silent fallback

Backend — OpenAL через OpenTK.Audio.OpenAL 4.9.4. На проверенном Linux установлена
системная OpenAL Soft (`libopenal.so.1`, пакет `libopenal1`); binary в репозиторий
не включён. В Debian/Ubuntu/Mint при её отсутствии установка выполняется отдельно:

```sh
sudo apt-get install libopenal1
```

Источники/лицензии — [THIRD_PARTY](../THIRD_PARTY.md), исходный эксперимент —
[OpenAL spike](../spikes/OpenAlSpike/README.md).

Если библиотека, устройство или контекст недоступны и AllowSilentFallback=true,
Create возвращает IsSilent=true и содержательную Diagnostic. Play/volume/Stop
остаются доступны, но ошибки WAV, аргументов и состояния не скрываются.
AllowSilentFallback=false даёт InvalidOperationException с причиной.
Silent backend сохраняет завершение one-shot и pause/loop по монотонным часам;
deterministic clock подменяется только внутренними тестами.
Сбой Play/загрузки уже созданного устройства не превращается в молчаливый fallback.

### Известные ограничения работающего backend

`IsSilent` и `Diagnostic` фиксируют результат создания устройства. v1 не проверяет
ALC_CONNECTED и не сообщает через эти свойства об отключении устройства после Create;
автоматического восстановления после смены USB/аудиосервера нет. При таком отключении
звук может остановиться при IsSilent=false и Diagnostic=null. Неожиданно остановленный
loop не считается штатно завершённым и не вытесняется как finished; Resume не перезапускает
его без предшествующей Pause. Обнаружение отключения и восстановление требуют доработки.

Ошибка восстановления предыдущего контекста в ContextScope.Dispose пока может
скрыть исходное исключение AL-операции. Если сама Pause/Resume/Stop прошла, а восстановление
контекста не удалось, локальные флаги voice могут не соответствовать native state.
После такой ошибки клиент не должен считать операцию отменённой или устройство исправным;
дальнейшее воспроизведение не гарантируется, ресурсы следует освободить. Сохранение обеих
причин и согласование состояния при таком сбое оставлены последующей доработкой.

Глобальная сериализация защищает устройства этой библиотеки, но не координирует
сторонний код с прямыми OpenAL-вызовами. Совместное использование process-global context
с таким кодом не гарантируется. В v1 сохранены lock и переключения контекста;
ALC_EXT_thread_local_context пока не используется.

Согласно [документации OpenAL Soft](https://github.com/kcat/openal-soft/blob/master/docs/env-vars.txt),
установка ALSOFT_DRIVERS применяется к диагностическому процессу до загрузки
OpenAL. `unavailable` принудительно даёт NoDevice; `null` запускает настоящий OpenAL
без физического вывода. Null driver не является silent fallback и не подтверждает
слышимость. Команды и фактические результаты — [проверки аудио](audio-testing.md).
