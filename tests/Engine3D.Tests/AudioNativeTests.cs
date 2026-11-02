using Engine3D.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class AudioNativeTests
{
    internal const string NoDeviceScenario = "audio-no-device";
    internal const string NativeScenario = "audio-native";
    internal const string ResumeRaceScenario = "audio-resume-race";

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void NoDeviceStillValidatesFilesAndStrictModeFailsInFreshProcess()
    {
        var result = TestProcess.Run(new Dictionary<string, string?> { ["ALSOFT_DRIVERS"] = "unavailable" }, NoDeviceScenario);
        Assert.AreEqual(0, result.ExitCode, result.Output);
        Assert.Contains("PASS audio no-device", result.Output);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [NativeAudioTest]
    public void NativeBackendSupportsContextsStereoLimitsAndCleanupWithoutAudibleOutput()
    {
        // OpenAL Soft null driver — настоящий native backend без физического вывода.
        // Это не NoDevice и не доказательство слышимости.
        var result = TestProcess.Run(new Dictionary<string, string?> { ["ALSOFT_DRIVERS"] = "null" }, NativeScenario);
        Assert.AreEqual(0, result.ExitCode, result.Output);
        Assert.Contains("PASS audio native", result.Output);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [NativeAudioTest]
    public void NativeResumeDoesNotReplayEffectThatFinishesInsidePause()
    {
        var result = TestProcess.Run(new Dictionary<string, string?> { ["ALSOFT_DRIVERS"] = "null" }, ResumeRaceScenario);
        Assert.AreEqual(0, result.ExitCode, result.Output);
        Assert.Contains("PASS audio resume race", result.Output);
    }

    internal static int Run(string scenario)
    {
        try
        {
            if (scenario == NoDeviceScenario) NoDevice();
            else if (scenario == ResumeRaceScenario) ResumeRace();
            else Native();
            Console.WriteLine(scenario == NoDeviceScenario ? "PASS audio no-device"
                : scenario == ResumeRaceScenario ? "PASS audio resume race" : "PASS audio native");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void NoDevice()
    {
        for (int repeat = 0; repeat < 2; repeat++)
        {
            using var device = AudioDevice.Create(new AudioOptions());
            Assert.IsTrue(device.IsSilent);
            Assert.IsFalse(string.IsNullOrWhiteSpace(device.Diagnostic));
            using var broken = new AudioFile([0, 1, 2]);
            Assert.ThrowsExactly<InvalidDataException>(() => device.LoadWave(broken.Path));
            using var file = new AudioFile();
            using var clip = device.LoadWave(file.Path);
            using var voice = device.Play(clip, loop: true);
            device.MasterVolume = 0f;
            voice.Pause(); voice.Resume(); voice.Stop();
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => device.Play(clip, volume: float.PositiveInfinity));
            Assert.ThrowsExactly<InvalidOperationException>(() => AudioDevice.Create(new AudioOptions { AllowSilentFallback = false }));
        }
    }

    private static void Native()
    {
        using var monoFile = new AudioFile();
        using var stereoFile = new AudioFile(AudioFile.Wave(channels: 2));
        for (int repeat = 0; repeat < 3; repeat++)
        {
            using var first = AudioDevice.Create(new AudioOptions { AllowSilentFallback = false });
            using var second = AudioDevice.Create(new AudioOptions { AllowSilentFallback = false });
            Assert.IsFalse(first.IsSilent);
            Assert.IsFalse(second.IsSilent);
            using var mono = first.LoadWave(monoFile.Path);
            using var stereo = first.LoadWave(stereoFile.Path);
            using var other = second.LoadWave(stereoFile.Path);
            using var otherVoice = second.Play(other, true);
            var voices = Enumerable.Range(0, 32).Select(i => first.Play(i % 2 == 0 ? mono : stereo, true, 0f)).ToArray();
            Assert.ThrowsExactly<InvalidOperationException>(() => first.Play(mono, true));
            Assert.ThrowsExactly<ArgumentException>(() => second.Play(mono));
            first.MasterVolume = 0.3f;
            second.MasterVolume = 0.7f;
            voices[0].Pause(); otherVoice.Pause();
            voices[0].Resume(); otherVoice.Resume();
            voices[0].Stop();
            using var replacement = first.Play(stereo, true);
            voices[0].Stop(); voices[0].Pause(); voices[0].Resume();
            mono.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(voices[0].Stop);
            stereo.Dispose();
            otherVoice.Stop();
            first.StopAll();
            second.StopAll();
            first.Dispose(); first.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(replacement.Pause);
        }
        ConcurrentOwners(monoFile.Path);
    }

    private static void ConcurrentOwners(string path)
    {
        // Разные устройства могут иметь разных owner threads. Глобальный ALC
        // current context не должен перенаправить AL-вызов в чужое устройство.
        using var start = new ManualResetEventSlim();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        Thread[] threads = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
        {
            start.Wait();
            try
            {
                for (int i = 0; i < 40; i++)
                {
                    using var device = AudioDevice.Create(new AudioOptions { AllowSilentFallback = false });
                    using var clip = device.LoadWave(path);
                    using var voice = device.Play(clip, true, 0f);
                    device.MasterVolume = 0.3f;
                    voice.Pause(); voice.Resume(); voice.Stop();
                }
            }
            catch (Exception exception) { errors.Enqueue(exception); }
        }) { IsBackground = true }).ToArray();
        foreach (var thread in threads) thread.Start();
        start.Set();
        foreach (var thread in threads) Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Native owner thread timeout");
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
    }

    private static void ResumeRace()
    {
        var backend = new CompletingPauseBackend(OpenAlBackend.Open());
        using var device = new AudioDevice(backend);
        using var file = new AudioFile(AudioFile.Wave(rate: 8000, samples: new byte[8000])); // 0.5 seconds
        using var clip = device.LoadWave(file.Path);
        using var effect = device.Play(clip, volume: 0f);
        backend.CompleteOnNextPause = true;
        effect.Pause();
        Assert.IsTrue(backend.PauseCalled, "Pause must reach the backend after Finished returned false");
        Assert.IsTrue(backend.IsFinished(backend.LastSource));
        effect.Resume();
        Assert.IsTrue(backend.IsFinished(backend.LastSource), "Resume must not restart the naturally finished effect");
        effect.Resume();
        Assert.IsTrue(backend.IsFinished(backend.LastSource));

        using var loop = device.Play(clip, loop: true, volume: 0f);
        // Play уже убрал завершённый effect; его handle остаётся безопасным.
        effect.Stop(); effect.Pause(); effect.Resume();
        loop.Pause();
        loop.Resume();
        Assert.IsFalse(backend.IsFinished(backend.LastSource));
        loop.Stop();
        // Native Resume must also leave an explicitly stopped source stopped.
        backend.Resume(backend.LastSource);
        Assert.IsTrue(backend.IsFinished(backend.LastSource));
    }

    private sealed class CompletingPauseBackend(IAudioBackend inner) : IAudioBackend
    {
        internal bool CompleteOnNextPause;
        internal bool PauseCalled;
        internal int LastSource;
        public int LoadBuffer(WaveData wave) => inner.LoadBuffer(wave);
        public void DeleteBuffer(int buffer) => inner.DeleteBuffer(buffer);
        public int Play(int buffer, bool loop, float volume) => LastSource = inner.Play(buffer, loop, volume);
        public bool IsFinished(int source) => inner.IsFinished(source);
        public void Pause(int source)
        {
            PauseCalled = true;
            if (CompleteOnNextPause)
            {
                CompleteOnNextPause = false;
                // Моделируем вытеснение owner thread после проверки Finished. Ждём
                // реального естественного окончания; source не останавливаем сами.
                Assert.IsTrue(SpinWait.SpinUntil(() => inner.IsFinished(source), TimeSpan.FromSeconds(5)),
                    "The one-shot must finish before the native pause call");
            }
            inner.Pause(source);
        }
        public void Resume(int source) => inner.Resume(source);
        public void Stop(int source) => inner.Stop(source);
        public void DeleteSource(int source) => inner.DeleteSource(source);
        public void SetMasterVolume(float volume) => inner.SetMasterVolume(volume);
        public void Dispose() => inner.Dispose();
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NativeAudioTestAttribute : ConditionBaseAttribute
{
    public NativeAudioTestAttribute() : base(ConditionMode.Include) =>
        IgnoreMessage = "Set ENGINE3D_AUDIO_TESTS=1; requires the OpenAL Soft native library (no audible output).";
    public override bool IsConditionMet => Environment.GetEnvironmentVariable("ENGINE3D_AUDIO_TESTS") == "1";
    public override string GroupName => nameof(NativeAudioTestAttribute);
}
