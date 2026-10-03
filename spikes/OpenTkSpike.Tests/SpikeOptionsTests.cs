using OpenTK.Mathematics;

namespace OpenTkSpike.Tests;

public class SpikeOptionsTests
{
    [Fact]
    public void Defaults_match_interactive_mode()
    {
        var options = SpikeOptions.Parse([]);

        Assert.Equal(new SpikeOptions(0, 1, null, true, CullMode.Off, true, null, 0, true, null), options);
    }

    [Fact]
    public void Parses_all_options()
    {
        var options = SpikeOptions.Parse(
        [
            "--frames", "30", "--cycles", "5", "--screenshot", "shot.png", "--depth", "off",
            "--cull", "front", "--texture", "off", "--angle", "0.7", "--fail-at-frame", "5",
            "--focus", "off", "--position", "40,60",
        ]);

        Assert.Equal(new SpikeOptions(30, 5, "shot.png", false, CullMode.Front, false, 0.7f, 5, false, new Vector2i(40, 60)), options);
    }

    [Theory]
    [InlineData("--unknown", "1")]
    [InlineData("--frames", "0")]
    [InlineData("--cycles", "-1")]
    [InlineData("--depth", "yes")]
[InlineData("--cull", "back")]
[InlineData("--focus", "yes")]
    [InlineData("--position", "40")]
    [InlineData("--position", "a,b")]
    [InlineData("--position", "40,60,1")]
    [InlineData("--frames")]
    public void Rejects_invalid_arguments(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => SpikeOptions.Parse(args));
    }

    [Fact]
    public void Screenshot_requires_frame_limit()
    {
        Assert.Throws<ArgumentException>(() => SpikeOptions.Parse(["--screenshot", "shot.png"]));
    }
}
