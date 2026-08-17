using Blight.Blare.Core.Updates;

namespace Blare.Core.Tests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V0.1.0", 0, 1, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1.2.3.0", 1, 2, 3)]
    [InlineData(" v2.0.1 ", 2, 0, 1)]
    public void ParsesTheTagFormsWeActuallySee(string text, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("1.2.3-beta.1", 1, 2, 3)]
    [InlineData("1.2.3+build7", 1, 2, 3)]
    public void PreReleaseAndBuildSuffixesAreIgnored(string text, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nightly")]
    [InlineData("v")]
    [InlineData("1")]
    public void GarbageIsRejectedRatherThanGuessed(string? text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    [Fact]
    public void NewerVersionsCompareGreater()
    {
        Assert.True(new ReleaseVersion(1, 0, 0) > new ReleaseVersion(0, 9, 9));
        Assert.True(new ReleaseVersion(1, 2, 0) > new ReleaseVersion(1, 1, 9));
        Assert.True(new ReleaseVersion(1, 1, 2) > new ReleaseVersion(1, 1, 1));
    }

    [Fact]
    public void MinorIsNotComparedAsText()
    {
        // The bug this guards: "1.10" sorts before "1.9" as a string.
        Assert.True(new ReleaseVersion(1, 10, 0) > new ReleaseVersion(1, 9, 0));
    }

    [Fact]
    public void FourPartAssemblyVersionMatchesTheEquivalentTag()
    {
        // An updater that treats these as different would offer an endless
        // "update" to the version already installed.
        ReleaseVersion.TryParse("1.2.3.0", out var assembly);
        ReleaseVersion.TryParse("v1.2.3", out var tag);

        Assert.Equal(tag, assembly);
        Assert.False(tag > assembly);
    }

    [Fact]
    public void SameVersionIsNotAnUpdate()
    {
        Assert.False(new ReleaseVersion(1, 0, 0) > new ReleaseVersion(1, 0, 0));
    }

    [Fact]
    public void OlderReleaseIsNeverOfferedAsAnUpdate()
    {
        Assert.False(new ReleaseVersion(0, 9, 0) > new ReleaseVersion(1, 0, 0));
    }
}
