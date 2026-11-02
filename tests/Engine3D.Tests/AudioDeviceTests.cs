using Engine3D.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class AudioDeviceTests
{
    [TestMethod]
    public void MasterGainImmediatelyMultipliesExistingVoicesAndMuteDoesNotCreateSources()
    {
        var backend = new RecordingAudioBackend();
        using var device = new AudioDevice(backend);
        using var file = new AudioFile();
        using var clip = device.LoadWave(file.Path);
        using var first = device.Play(clip, loop: true, volume: 0.7f);
        using var second = device.Play(clip, volume: 0.2f);
        Assert.HasCount(2, backend.Sources);
        device.MasterVolume = 0.25f;
        Assert.AreEqual(0.175f, backend.Master * backend.Sources.Values.First(), 1e-6f);
        device.MasterVolume = 0f;
        Assert.AreEqual(0f, backend.Master);
        device.MasterVolume = 1f;
        Assert.HasCount(2, backend.Sources);
        Assert.AreEqual(1f, device.MasterVolume);
        Assert.IsFalse(device.IsSilent);
        Assert.IsNull(device.Diagnostic);
    }

    [TestMethod]
    public void PauseResumeAndStopAreIdempotentAndDoNotRestartStoppedVoice()
    {
        var backend = new RecordingAudioBackend();
        using var device = new AudioDevice(backend);
        using var file = new AudioFile();
        using var clip = device.LoadWave(file.Path);
        using var voice = device.Play(clip, loop: true);
        voice.Resume();
        voice.Pause(); voice.Pause();
        voice.Resume(); voice.Resume();
        voice.Stop(); voice.Stop(); voice.Resume(); voice.Pause();
        Assert.AreEqual(1, backend.Calls.Count(call => call == "play"));
        Assert.AreEqual(1, backend.Calls.Count(call => call == "pause"));
        Assert.AreEqual(1, backend.Calls.Count(call => call == "resume"));
        Assert.AreEqual(1, backend.Calls.Count(call => call == "stop"));
    }

    [TestMethod]
    public void FinishedVoiceIsReclaimedBeforeOldestActiveNonLoop()
    {
        var backend = new RecordingAudioBackend();
        using var device = new AudioDevice(backend);
        using var file = new AudioFile();
        using var clip = device.LoadWave(file.Path);
        var voices = Enumerable.Range(0, 32).Select(_ => device.Play(clip)).ToArray();
        int finishedSource = backend.Sources.Keys.ElementAt(15);
        backend.Completed.Add(finishedSource);
        using var replacement = device.Play(clip);
        Assert.HasCount(32, backend.Sources);
        Assert.IsFalse(backend.Sources.ContainsKey(finishedSource));
        Assert.ThrowsExactly<ObjectDisposedException>(voices[15].Pause);
        voices[0].Pause();
        using var next = device.Play(clip);
        Assert.ThrowsExactly<ObjectDisposedException>(voices[0].Resume);
        Assert.HasCount(32, backend.Sources);
    }

    [TestMethod]
    public void ActiveLoopsAreProtectedAndStoppedLoopCanBeReclaimed()
    {
        var backend = new RecordingAudioBackend();
        using var device = new AudioDevice(backend);
        using var file = new AudioFile();
        using var clip = device.LoadWave(file.Path);
        var voices = Enumerable.Range(0, 32).Select(_ => device.Play(clip, loop: true)).ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => device.Play(clip));
        Assert.HasCount(32, backend.Sources);
        voices[7].Stop();
        using var replacement = device.Play(clip);
        Assert.ThrowsExactly<ObjectDisposedException>(voices[7].Pause);
        voices[0].Pause();
    }

    [TestMethod]
    public void ForeignDisposedAndInvalidArgumentsFailBeforeAnyBackendMutation()
    {
        var backend = new RecordingAudioBackend();
        using var device = new AudioDevice(backend);
        using var other = new AudioDevice(new RecordingAudioBackend());
        using var file = new AudioFile();
        using var clip = device.LoadWave(file.Path);
        using var foreign = other.LoadWave(file.Path);
        int calls = backend.Calls.Count;
        Assert.ThrowsExactly<ArgumentException>(() => device.Play(foreign));
        Assert.ThrowsExactly<ArgumentNullException>(() => device.Play(null!));
        foreach (float value in new[] { -1f, 1.01f, float.NaN, float.PositiveInfinity })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => device.Play(clip, volume: value));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => device.MasterVolume = value);
        }
        Assert.HasCount(calls, backend.Calls);
        clip.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => device.Play(clip));
    }

    [TestMethod]
    public void WrongThreadIsRejectedBeforeBackendIncludingDisposal()
    {
        var backend = new RecordingAudioBackend();
        using var device = new AudioDevice(backend);
        using var file = new AudioFile();
        using var clip = device.LoadWave(file.Path);
        using var voice = device.Play(clip);
        int calls = backend.Calls.Count;
        Action[] operations = [() => device.MasterVolume = 0.5f, () => device.LoadWave(file.Path),
            () => device.Play(clip), device.StopAll, voice.Pause, voice.Resume, voice.Stop,
            voice.Dispose, clip.Dispose, device.Dispose, () => _ = device.IsSilent, () => _ = device.Diagnostic];
        foreach (var operation in operations)
        {
            Exception? error = null;
            var thread = new Thread(() => { try { operation(); } catch (Exception exception) { error = exception; } });
            thread.Start(); thread.Join();
            Assert.IsInstanceOfType<InvalidOperationException>(error);
        }
        Assert.HasCount(calls, backend.Calls);
    }

    [TestMethod]
    public void ClipDisposeStopsItsVoicesAndDeviceDisposeReleasesSourcesBeforeBuffers()
    {
        var backend = new RecordingAudioBackend();
        var device = new AudioDevice(backend);
        using var file = new AudioFile();
        var clip = device.LoadWave(file.Path);
        var other = device.LoadWave(file.Path);
        var voices = new[] { device.Play(clip), device.Play(clip, true), device.Play(other) };
        clip.Dispose(); clip.Dispose();
        Assert.HasCount(1, backend.Sources);
        Assert.HasCount(1, backend.Buffers);
        Assert.ThrowsExactly<ObjectDisposedException>(voices[0].Pause);
        device.StopAll(); device.StopAll();
        backend.Calls.Clear();
        device.Dispose(); device.Dispose();
        Assert.IsEmpty(backend.Sources);
        Assert.IsEmpty(backend.Buffers);
        Assert.IsLessThan(backend.Calls.IndexOf("delete-buffer"), backend.Calls.IndexOf("delete-source"));
        Assert.AreEqual("dispose", backend.Calls[^1]);
        voices[2].Dispose(); other.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => device.MasterVolume = 1f);
        Assert.ThrowsExactly<ObjectDisposedException>(() => device.Play(other));
        Assert.ThrowsExactly<ObjectDisposedException>(device.StopAll);
        Assert.ThrowsExactly<ObjectDisposedException>(voices[2].Stop);
    }

    [TestMethod]
    public void CleanupContinuesAfterFailureAndDisposeStaysIdempotent()
    {
        var backend = new RecordingAudioBackend();
        var device = new AudioDevice(backend);
        using var file = new AudioFile();
        var clip = device.LoadWave(file.Path);
        var voice = device.Play(clip);
        backend.FailOn = "stop";
        Assert.ThrowsExactly<InvalidOperationException>(device.Dispose);
        Assert.Contains("delete-source", backend.Calls);
        Assert.Contains("delete-buffer", backend.Calls);
        Assert.AreEqual("dispose", backend.Calls[^1]);
        Assert.IsEmpty(backend.Sources);
        Assert.IsEmpty(backend.Buffers);
        int calls = backend.Calls.Count;
        device.Dispose(); clip.Dispose(); voice.Dispose();
        Assert.HasCount(calls, backend.Calls);
    }

    [TestMethod]
    public void StartupFallbackDoesNotHideArgumentOrWaveErrors()
    {
        using var silent = AudioDevice.Create(new AudioOptions(), () => throw new DllNotFoundException("OpenAL missing"));
        Assert.IsTrue(silent.IsSilent);
        StringAssert.Contains(silent.Diagnostic!, "OpenAL missing");
        Assert.ThrowsExactly<InvalidOperationException>(() => AudioDevice.Create(new AudioOptions { AllowSilentFallback = false },
            () => throw new InvalidOperationException("NoDevice")));
        Assert.ThrowsExactly<ArgumentNullException>(() => AudioDevice.Create(null!));
        using var invalid = new AudioFile([1, 2, 3]);
        Assert.ThrowsExactly<InvalidDataException>(() => silent.LoadWave(invalid.Path));
        using var file = new AudioFile();
        using var clip = silent.LoadWave(file.Path);
        using var voice = silent.Play(clip, true);
        voice.Pause(); voice.Resume(); silent.StopAll();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => silent.Play(clip, volume: float.NaN));
    }

    [TestMethod]
    public void SilentBackendPreservesPauseDurationLoopAndNaturalFinishWithoutSleeping()
    {
        double now = 0d;
        var backend = new SilentAudioBackend(() => now);
        int buffer = backend.LoadBuffer(new WaveData(new byte[48000], 24000, 1)); // 1 second
        int oneShot = backend.Play(buffer, false, 1f);
        int loop = backend.Play(buffer, true, 1f);
        now = 0.4d; backend.Pause(oneShot);
        now = 100d;
        Assert.IsFalse(backend.IsFinished(oneShot));
        Assert.IsFalse(backend.IsFinished(loop));
        backend.Resume(oneShot);
        now = 100.59d; Assert.IsFalse(backend.IsFinished(oneShot));
        now = 100.61d; Assert.IsTrue(backend.IsFinished(oneShot));
        backend.Stop(loop); Assert.IsTrue(backend.IsFinished(loop));
        backend.Dispose(); backend.Dispose();
    }
}
