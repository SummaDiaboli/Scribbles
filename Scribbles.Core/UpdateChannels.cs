namespace Scribbles.Core;

/// <summary>Update channels (Velopack). Unknown values fall back to stable.</summary>
public static class UpdateChannels
{
    public const string Stable = "stable";
    public const string Beta = "beta";

    public static string Normalize(string? channel) =>
        string.Equals(channel?.Trim(), Beta, StringComparison.OrdinalIgnoreCase) ? Beta : Stable;
}
