using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;

namespace Scribbles.App.Services;

/// <summary>
/// Lazy page rasterizer: one persistent PDFium reader per open document,
/// single background worker, stale generations skipped, newest request
/// first (LIFO) so the visible page never waits behind scrolled-past ones.
/// Callbacks fire on the worker thread — marshal to the UI thread at the
/// call site.
/// </summary>
public sealed class PageRenderQueue : IDisposable
{
    private readonly object _gate = new();
    private readonly ConcurrentStack<RenderRequest> _stack = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly HashSet<int> _pending = new();
    private readonly Thread _worker;
    private bool _disposed;

    private byte[] _bytes = Array.Empty<byte>();
    private int _generation;
    private string? _docKey;
    private Docnet.Core.Readers.IDocReader? _reader;
    private readonly PageRasterCache _cache = new();

    private sealed record RenderRequest(int Generation, int PageNumber1, Action<int, BitmapSource> Done);

    public PageRenderQueue()
    {
        _worker = new Thread(Work) { IsBackground = true, Name = "PageRender" };
        _worker.Start();
    }

    public int Generation
    {
        get { lock (_gate) { return _generation; } }
    }

    public void Open(byte[] pdfBytes)
    {
        lock (_gate)
        {
            _reader?.Dispose();
            _reader = null;
            _bytes = pdfBytes;
            _docKey = null; // recomputed lazily on the worker (hashing is not UI work)
            _generation++;
            _pending.Clear();
            while (_stack.TryPop(out _)) { }
        }
    }

    public void Request(int pageNumber1, Action<int, BitmapSource> done)
    {
        int gen;
        lock (_gate)
        {
            gen = _generation;
            if (!_pending.Add(pageNumber1))
                return; // already queued
        }
        _stack.Push(new RenderRequest(gen, pageNumber1, done));
        _signal.Release();
    }

    private void Work()
    {
        while (true)
        {
            try
            {
                _signal.Wait();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (_disposed)
                return;
            if (!_stack.TryPop(out var request))
                continue;
            lock (_gate)
            {
                _pending.Remove(request.PageNumber1);
            }
            try
            {
                Render(request);
            }
            catch { /* a page may fail; others still render */ }
        }
    }

    private void Render(RenderRequest request)
    {
        Docnet.Core.Readers.IDocReader? reader;
        string? docKey;
        lock (_gate)
        {
            if (request.Generation != _generation || _bytes.Length == 0)
                return;
            _reader ??= DocLib.Instance.GetDocReader(_bytes, new PageDimensions(1080 * 2, 1080 * 2));
            reader = _reader;
            // Hash once per document, on the worker — never on the UI thread.
            _docKey ??= Scribbles.Core.NoteSerializer.Sha256Hex(_bytes);
            docKey = _docKey;
        }
        if (reader is null)
            return;
        int index0 = request.PageNumber1 - 1;
        if (index0 < 0 || index0 >= reader.GetPageCount())
            return;
        // Disk cache first: reopening a document reuses rasters.
        if (docKey is not null && _cache.TryGet(docKey, request.PageNumber1) is { } cached)
        {
            lock (_gate)
            {
                if (request.Generation != _generation)
                    return;
            }
            request.Done(request.PageNumber1, cached);
            return;
        }
        // Native calls stay serialized process-wide (home thumbs, text refresh
        // share PDFium). Sync wait is correct: this is the worker thread.
        PdfiumGate.Instance.Wait();
        byte[] raw;
        int w, h;
        try
        {
            using var pageReader = reader.GetPageReader(index0);
            raw = pageReader.GetImage();
            w = pageReader.GetPageWidth();
            h = pageReader.GetPageHeight();
        }
        finally
        {
            PdfiumGate.Instance.Release();
        }
        if (w <= 0 || h <= 0)
            return;
        var bitmap = BitmapSource.Create(w, h, 192, 192,
            System.Windows.Media.PixelFormats.Bgra32, null, raw, w * 4);
        bitmap.Freeze();

        lock (_gate)
        {
            if (request.Generation != _generation)
                return; // document switched mid-render: drop it
        }
        if (docKey is not null)
            _cache.Put(docKey, request.PageNumber1, bitmap);
        request.Done(request.PageNumber1, bitmap);
    }

    public void Dispose()
    {
        _disposed = true;
        _signal.Release();
        lock (_gate)
        {
            _reader?.Dispose();
            _reader = null;
        }
    }
}
