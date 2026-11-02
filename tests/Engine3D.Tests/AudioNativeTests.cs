using Engine3D.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class AudioNativeTests
{
    internal const string NoDeviceScenario = "audio-no-device";
    internal const string NativeScenario = "audio-native";

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

    internal static int Run(string scenario)
    {
        try
        {
            if (scenario == NoDeviceScenario) NoDevice();
            else Native();
            Console.WriteLine(scenario == NoDeviceScenario ? "PASS audio no-device" : "PASS audio native");
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
            Assert.ThrowsExactly<ObjectDisposedException>(voices[0].Pause);
            mono.Dispose();
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
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NativeAudioTestAttribute : ConditionBaseAttribute
{
    public NativeAudioTestAttribute() : base(ConditionMode.Include) =>
        IgnoreMessage = "Set ENGINE3D_AUDIO_TESTS=1; requires the OpenAL Soft native library (no audible output).";
    public override bool IsConditionMet => Environment.GetEnvironmentVariable("ENGINE3D_AUDIO_TESTS") == "1";
    public override string GroupName => nameof(NativeAudioTestAttribute);
}
