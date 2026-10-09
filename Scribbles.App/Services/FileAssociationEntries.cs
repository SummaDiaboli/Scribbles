using System.IO;
using Microsoft.Win32;

namespace Scribbles.App.Services;

/// <summary>
/// Open-With registration (HKCU, per-user, never the default handler).
/// The entry shapes are pure and unit-tested; only Apply/Remove touch the
/// registry, and every call is exception-proof.
/// </summary>
public static class FileAssociationEntries
{
    public const string ProgId = "Scribbles.PDF";
    public const string Extension = ".pdf";

    /// <summary>HKCU-relative key → (value name, value). Empty name = default value.</summary>
    public static IReadOnlyDictionary<string, (string ValueName, string Value)> Build(string exePath)
    {
        string command = $"\"{exePath}\" \"%1\"";
        string appKey = $"Software\\Classes\\Applications\\{Path.GetFileName(exePath)}";
        return new Dictionary<string, (string, string)>
        {
            [$"Software\\Classes\\{ProgId}"] = ("", "Scribbles PDF Document"),
            [$"Software\\Classes\\{ProgId}\\shell\\open\\command"] = ("", command),
            [$"Software\\Classes\\{Extension}\\OpenWithProgids"] = (ProgId, ""),
            [$"{appKey}\\shell\\open\\command"] = ("", command),
            [$"{appKey}\\SupportedTypes"] = (Extension, ""),
        };
    }

    public static void Apply(string exePath)
    {
        try
        {
            foreach (var (key, (name, value)) in Build(exePath))
                Registry.CurrentUser.CreateSubKey(key)?.SetValue(name, value);
        }
        catch
        {
            // Association is a nicety; never break startup.
        }
    }

    public static void Remove(string exePath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($"Software\\Classes\\{ProgId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(
                $"Software\\Classes\\Applications\\{Path.GetFileName(exePath)}", throwOnMissingSubKey: false);
            Registry.CurrentUser.OpenSubKey($"Software\\Classes\\{Extension}\\OpenWithProgids", writable: true)
                ?.DeleteValue(ProgId, throwOnMissingValue: false);
        }
        catch
        {
            // Uninstall cleanup must stay fast and silent.
        }
    }
}
