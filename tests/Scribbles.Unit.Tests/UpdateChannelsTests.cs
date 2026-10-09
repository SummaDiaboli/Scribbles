using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class UpdateChannelsTests
{
    [Fact]
    public void Normalize_Maps()
    {
        UpdateChannels.Normalize("beta").ShouldBe(UpdateChannels.Beta);
        UpdateChannels.Normalize("Beta").ShouldBe(UpdateChannels.Beta);
        UpdateChannels.Normalize("stable").ShouldBe(UpdateChannels.Stable);
        UpdateChannels.Normalize(null).ShouldBe(UpdateChannels.Stable);
        UpdateChannels.Normalize("nightly").ShouldBe(UpdateChannels.Stable);
    }

    [Fact]
    public void OldSettingsFile_WithoutChannel_LoadsStable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "scribbles-channel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """{"theme":0,"penColorArgb":4278190080}""");
        try
        {
            SettingsService.Load(path).UpdateChannel.ShouldBe(UpdateChannels.Stable);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BetaChannel_RoundTrips()
    {
        string dir = Path.Combine(Path.GetTempPath(), "scribbles-channel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "settings.json");
        try
        {
            SettingsService.Save(path, AppSettings.Default with { UpdateChannel = UpdateChannels.Beta });
            SettingsService.Load(path).UpdateChannel.ShouldBe(UpdateChannels.Beta);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
