using System.IO;
using System.Windows.Media.Imaging;

namespace Scribbles.App.Services;

/// <summary>
/// Disk cache of page rasters (PNG): reopening a document reuses them instead
/// of re-rendering every page. Keyed by document hash + page under a settings
/// version (render settings change → bump). Corrupt entries fail soft to null
/// (re-render) — the cache is allowed to be lossy. Thread-safe for the single
/// render worker; frozen bitmaps cross threads.
/// </summary>
public sealed class PageRasterCache
{
    public const string SettingsVersion = "v1-2160px-192dpi";

    public string Directory { get; }
    public int MaxFiles { get; }
    public long MaxBytes { get; }

    public PageRasterCache(
        string? directory = null, int maxFiles = 300, long maxBytes = 512L * 1024 * 1024)
    {
        Directory = directory ?? DefaultDirectory;
        MaxFiles = maxFiles;
        MaxBytes = maxBytes;
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Scribbles", "render-cache");

    public static string KeyFor(string docKey, int pageNumber1) =>
        $"{SettingsVersion}|{docKey}|p{pageNumber1}";

    private string PathFor(string key)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        string name = Convert.ToHexString(sha.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes(key)));
        return Path.Combine(Directory, name + ".png");
    }

    public BitmapSource? TryGet(string docKey, int pageNumber1)
    {
        string path = PathFor(KeyFor(docKey, pageNumber1));
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(
                stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    public void Put(string docKey, int pageNumber1, BitmapSource image)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            File.WriteAllBytes(PathFor(KeyFor(docKey, pageNumber1)), ms.ToArray());
            Prune();
        }
        catch
        {
            // Cache writes must never break rendering.
        }
    }

    internal void Prune()
    {
        try
        {
            var files = new DirectoryInfo(Directory).GetFiles("*.png")
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
            long total = files.Sum(f => f.Length);
            int remaining = files.Count;
            foreach (var file in files)
            {
                if (remaining <= MaxFiles && total <= MaxBytes)
                    break;
                total -= file.Length;
                remaining--;
                file.Delete();
            }
        }
        catch
        {
            // Pruning is hygiene, never load-bearing.
        }
    }
}
