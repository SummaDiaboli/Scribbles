using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Scribbles.App.Services;
using Scribbles.Core;

namespace Scribbles.App.ViewModels;

public sealed partial class PageViewModel : ObservableObject
{
    public int Number { get; init; }
    public double WidthPt { get; init; }
    public double HeightPt { get; init; }
    public ImageSource Image { get; init; } = null!;

    /// <summary>Fixed base size in DIPs (render pixels / 2). Zoom is a pure visual scale.</summary>
    [ObservableProperty]
    private double _baseWidth;

    [ObservableProperty]
    private double _baseHeight;

    [ObservableProperty]
    private double _displayScale = 1.0;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DocnetPdfRenderer _renderer = new();
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
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (double.TryParse(digits, out double pct) && pct is >= 25 and <= 400)
                Zoom = pct / 100.0;
            else
                OnPropertyChanged(); // reset text to current zoom
        }
    }

    public IReadOnlyList<string> ZoomPresets { get; } =
        new[] { "50%", "75%", "100%", "125%", "150%", "200%", "300%" };

    private static readonly double[] PresetValues = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0 };

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

    public void LoadPdf(string path)
    {
        try
        {
            Status = $"Rendering {System.IO.Path.GetFileName(path)}…";
            var rendered = _renderer.Render(path);
            Pages.Clear();
            foreach (var p in rendered)
            {
                Pages.Add(new PageViewModel
                {
                    Number = p.Number,
                    WidthPt = p.WidthPt,
                    HeightPt = p.HeightPt,
                    Image = p.Image,
                    BaseWidth = p.Image.Width / 2.0,
                    BaseHeight = p.Image.Height / 2.0,
                    DisplayScale = Zoom,
                });
            }
            Status = $"{rendered.Count} page(s). Pen draws, finger scrolls/pinch-zooms.";
        }
        catch (Exception ex)
        {
            Status = $"Could not open PDF: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ZoomIn() => Zoom = NextPreset(Zoom, forward: true);

    [RelayCommand]
    private void ZoomOut() => Zoom = NextPreset(Zoom, forward: false);

    private static double NextPreset(double current, bool forward)
    {
        if (forward)
        {
            foreach (var p in PresetValues)
                if (p > current + 1e-9)
                    return p;
            return Math.Min(current * 1.25, 4.0);
        }
        for (int i = PresetValues.Length - 1; i >= 0; i--)
            if (PresetValues[i] < current - 1e-9)
                return PresetValues[i];
        return Math.Max(current / 1.25, 0.25);
    }

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
