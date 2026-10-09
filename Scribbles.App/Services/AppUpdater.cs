using Velopack;
using Velopack.Sources;
using Scribbles.Core;

namespace Scribbles.App.Services;

/// <summary>
/// Velopack auto-update: check + download in the background, apply only when
/// the user restarts (File → Restart to apply update). Dev builds (not
/// installed) and offline machines silently skip — no popups, no throws.
/// </summary>
public sealed class AppUpdater
{
    public const string RepoUrl = "https://github.com/SummaDiaboli/Scribbles";

    /// <summary>True only in a real Velopack install (never dev builds).</summary>
    public static bool IsInstalled(string channel)
    {
        try
        {
            var (source, options) = ForChannel(channel);
            return new UpdateManager(source, options).IsInstalled;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Stable tracks the default channel; beta opts into prereleases and downgrades.</summary>
    public static (IUpdateSource Source, UpdateOptions Options) ForChannel(string channel)
    {
        if (UpdateChannels.Normalize(channel) == UpdateChannels.Beta)
        {
            return (new GithubSource(RepoUrl, null, true),
                new UpdateOptions { ExplicitChannel = UpdateChannels.Beta, AllowVersionDowngrade = true });
        }
        return (new GithubSource(RepoUrl, null, false), new UpdateOptions());
    }

    public async Task<UpdateInfo?> CheckAsync(string channel)
    {
        try
        {
            var (source, options) = ForChannel(channel);
            var mgr = new UpdateManager(source, options);
            return await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
        }
        catch
        {
            // Not installed (dev builds), offline, or feed unreachable.
            return null;
        }
    }

    public async Task<bool> DownloadAsync(UpdateInfo update, string channel)
    {
        try
        {
            var (source, options) = ForChannel(channel);
            var mgr = new UpdateManager(source, options);
            await mgr.DownloadUpdatesAsync(update).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyAndRestart(UpdateInfo update, string channel)
    {
        var (source, options) = ForChannel(channel);
        var mgr = new UpdateManager(source, options);
        mgr.ApplyUpdatesAndRestart(update);
    }
}
