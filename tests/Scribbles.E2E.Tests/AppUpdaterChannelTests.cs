using Scribbles.App.Services;
using Scribbles.Core;
using Shouldly;
using Velopack.Sources;

namespace Scribbles.E2E.Tests;

/// <summary>Update channel mapping: stable tracks the default channel,
/// beta opts into the beta channel + prereleases + downgrades.</summary>
public sealed class AppUpdaterChannelTests
{
    [Fact]
    public void StableChannel_UsesDefaults()
    {
        var (source, options) = AppUpdater.ForChannel(UpdateChannels.Stable);
        source.ShouldBeOfType<GithubSource>();
        options.ExplicitChannel.ShouldBeNull();
        options.AllowVersionDowngrade.ShouldBeFalse();
    }

    [Fact]
    public void BetaChannel_OptsIn()
    {
        var (source, options) = AppUpdater.ForChannel(UpdateChannels.Beta);
        source.ShouldBeOfType<GithubSource>();
        options.ExplicitChannel.ShouldBe(UpdateChannels.Beta);
        options.AllowVersionDowngrade.ShouldBeTrue();
    }

    [Fact]
    public void UnknownChannel_FallsBackToStable()
    {
        var (_, options) = AppUpdater.ForChannel("nightly");
        options.ExplicitChannel.ShouldBeNull();
        options.AllowVersionDowngrade.ShouldBeFalse();
    }
}
