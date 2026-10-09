namespace Scribbles.App.Services;

/// <summary>
/// Process-wide gate for native PDFium calls (Docnet).
///
/// C# mutex equivalents, for the record:
/// <list type="bullet">
/// <item><c>lock (obj)</c> (Monitor) — sync sections only; cannot cross <c>await</c>.</item>
/// <item><c>SemaphoreSlim(1, 1)</c> — the async mutex: <c>await WaitAsync()</c>,
/// release in <c>finally</c>. Used here.</item>
/// <item><c>Mutex</c> — cross-process; overkill inside one app.</item>
/// </list>
/// PDFium renders run on several threads (lazy worker, home thumbnails, text
/// refresh); the gate serializes only the native calls while managed parsing
/// (PdfPig/PdfSharp) stays fully parallel.
/// </summary>
public static class PdfiumGate
{
    public static readonly SemaphoreSlim Instance = new(1, 1);
}
