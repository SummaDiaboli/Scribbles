using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Scribbles.App.Services;
using Scribbles.Core;
using Scribbles.Pdf;

namespace Scribbles.App.ViewModels;

public sealed partial class PageViewModel : ObservableObject
{
    public int Number { get; init; }

    [ObservableProperty]
    private double _widthPt;

    [ObservableProperty]
    private double _heightPt;

    [ObservableProperty]
    private ImageSource _image = null!;

    /// <summary>Native PDF text words for the text-select tool (empty for scans).</summary>
    [ObservableProperty]
    private IReadOnlyList<PdfWord> _words = Array.Empty<PdfWord>();

    /// <summary>Editable text boxes (converted handwriting, native edits).</summary>
    public ObservableCollection<TextBoxItem> TextBoxes { get; } = new();

    /// <summary>Composited thumbnail (base + ink); null until rendered.</summary>
    [ObservableProperty]
    private ImageSource? _thumbnail;

    /// <summary>True for the page the main scroll is currently on.</summary>
    [ObservableProperty]
    private bool _isCurrentPage;

    /// <summary>Fixed base size in DIPs (render pixels / 2). Zoom is a pure visual scale.</summary>
    [ObservableProperty]
    private double _baseWidth;

    [ObservableProperty]
    private double _baseHeight;

    /// <summary>
    /// True display scale: the raster is fit-to-dimensions, not exact-DPI, so
    /// 1pt is NOT always 96/72 dips. All ink/text mapping must use these.
    /// </summary>
    public double DipPerPtX => WidthPt > 0 ? BaseWidth / WidthPt : InkUnits.DipsPerPoint;

    public double DipPerPtY => HeightPt > 0 ? BaseHeight / HeightPt : InkUnits.DipsPerPoint;

    [ObservableProperty]
    private double _displayScale = 1.0;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ThemeService _themes;

    public ObservableCollection<PageViewModel> Pages { get; } = new();

    [ObservableProperty]
    private string _status = "Open a PDF to start writing. Pen draws, touch scrolls.";

    [ObservableProperty]
    private double _zoom = 1.0;

    public string ZoomLabel
    {
        get => $"{Zoom * 100:0}%";
        set
        {
            if (ZoomLevels.TryParseLabel(value, out double zoom))
                Zoom = zoom;
            else
                OnPropertyChanged(); // reset text to current zoom
        }
    }

    public IReadOnlyList<string> ZoomPresets { get; } =
        ZoomLevels.Presets.Select(p => $"{p * 100:0}%").ToList();

    [ObservableProperty]
    private AppTheme _theme;

    public MainViewModel(ThemeService themes)
    {
        _themes = themes;
        Theme = themes.Current;
    }

    [RelayCommand]
    private void OpenPdf() => RequestOpen?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised when Open is requested. Handled by the view (round-trip rebuild).</summary>
    public event EventHandler? RequestOpen;

    /// <summary>
    /// Sizes + words parse on a pool thread (PdfPig is free-threaded);
    /// page list populates back on the UI thread.
    /// </summary>
    public async Task LoadPdfAsync(string path, byte[] bytes)
    {
        try
        {
            Status = $"Opening {System.IO.Path.GetFileName(path)}…";
            // Sizes + words in one PdfPig open (fast); rasters fill in lazily.
            var content = await Task.Run(() => Pdf.PdfPageContentReader.Read(bytes)).ConfigureAwait(true);
            var sizes = content.Sizes;
            var words = content.Words;
            Pages.Clear();
            for (int i = 0; i < sizes.Count; i++)
            {
                var (baseW, baseH) = RenderDims.NominalBaseDips(sizes[i].Width, sizes[i].Height);
                Pages.Add(new PageViewModel
                {
                    Number = i + 1,
                    WidthPt = sizes[i].Width,
                    HeightPt = sizes[i].Height,
                    Image = null!,
                    BaseWidth = baseW,
                    BaseHeight = baseH,
                    DisplayScale = Zoom,
                    Words = i < words.Count ? words[i] : new List<PdfWord>(),
                });
            }
            Status = $"{Pages.Count} page(s). Rendering… Pen draws, finger scrolls/pinch-zooms.";
        }
        catch (Exception ex)
        {
            Status = $"Could not open PDF: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ZoomIn() => Zoom = ZoomLevels.Next(Zoom, up: true);

    [RelayCommand]
    private void ZoomOut() => Zoom = ZoomLevels.Next(Zoom, up: false);

    /// <summary>Pinch-zoom anchored at the gesture start.</summary>
    public void Pinch(double startDistance, double currentDistance) =>
        Zoom = ZoomLevels.ApplyPinch(Zoom, startDistance, currentDistance);

    [RelayCommand]
    private void SetTheme(string name)
    {
        if (Enum.TryParse<AppTheme>(name, out var theme))
        {
            Theme = theme;
            _themes.Apply(theme);
        }
    }

    /// <summary>Raised when Save is requested (Ctrl+S / toolbar). Handled by the view.</summary>
    public event EventHandler? RequestSave;

    [RelayCommand]
    private void Save() => RequestSave?.Invoke(this, EventArgs.Empty);

    public event EventHandler? RequestUndo;
    public event EventHandler? RequestRedo;
    public event EventHandler? RequestDeleteSelection;

    [RelayCommand]
    private void Undo() => RequestUndo?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Redo() => RequestRedo?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void DeleteSelection() => RequestDeleteSelection?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised for explicit text copy (Ctrl+C / menu). Handled by the view.</summary>
    public event EventHandler? RequestCopyText;

    [RelayCommand]
    private void CopyText() => RequestCopyText?.Invoke(this, EventArgs.Empty);

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomLabel));
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        foreach (var p in Pages)
            p.DisplayScale = Zoom;
    }
}
