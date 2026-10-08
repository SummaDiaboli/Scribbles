using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Scribbles.App.Services;
using Scribbles.App.ViewModels;
using Scribbles.Core;
using Scribbles.Pdf;

namespace Scribbles.App;

/// <summary>
/// Viewer shell glue: wires the pen toolbar, document session and autosave.
/// Pen draws; finger scrolls/pinch-zooms; palm rejected via TabletDevice.Type.
/// (WinUI 3 InkCanvas is still experimental-only, so WPF InkCanvas is used.)
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly PenToolbarController _pen = new();
    private readonly InkModelConverter _converter = new();
    private readonly StrokeHistory _history;
    private readonly DocumentSession _session;
    private readonly AutosaveService _autosave = new();
    private readonly DispatcherTimer _autosaveTimer;
    private AppSettings _settings = App.Settings;

    public MainWindow()
    {
        InitializeComponent();
        _history = new StrokeHistory(_converter);
        _session = new DocumentSession(
            new ScribblesPdfStore(), _converter, _history, _pen, () => CanvasExplorer.Find(PageScroll));
        _session.StateChanged += (_, _) => UpdateUndoRedoState();
        _vm = new MainViewModel(App.Themes);
        _vm.RequestSave += (_, _) => SaveSafely(saveAs: false);
        _vm.RequestUndo += (_, _) => _session.Undo(TargetPages());
        _vm.RequestRedo += (_, _) => _session.Redo(TargetPages());
        _vm.RequestDeleteSelection += (_, _) => _session.DeleteSelection();
        _vm.RequestOpen += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf" };
            if (dlg.ShowDialog() == true)
                OpenSafely(dlg.FileName);
        };
        DataContext = _vm;
        App.Themes.ThemeChanged += (_, theme) =>
        {
            _settings = _settings with { Theme = theme };
            PersistSettings();
        };

        ApplySettingsToPen();
        RestorePlacement();

        _autosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.AutosaveSeconds, 10, 300)),
        };
        _autosaveTimer.Tick += (_, _) => { if (_session.IsDirty) SaveWorkingCopy(); };
        _autosaveTimer.Start();

        Closing += (_, _) =>
        {
            if (_session.IsDirty)
                SaveWorkingCopy();
            SavePlacementAndSettings();
        };

        Loaded += (_, _) =>
        {
            _session.HookCanvases();
            _session.ApplyToolToAll();
            SyncPenControls();
            RebuildPalette();
            UpdateUndoRedoState();
            CheckForAutosave();
        };

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
            Loaded += (_, _) => OpenSafely(args[1]);
    }

    // ---------- settings + placement ----------

    private void ApplySettingsToPen()
    {
        var (a, r, g, b) = InkUnits.FromArgb(_settings.PenColorArgb);
        _pen.SetColor(Color.FromArgb(a, r, g, b));
        _pen.SetSize(_settings.PenSizeDip);
        _pen.SetPenFamily(_settings.PenType);
    }

    private void RestorePlacement()
    {
        if (_settings.Window is null)
            return;
        var screen = new ScreenBounds(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var placed = WindowPlacement.ClampToVisible(_settings.Window, screen);
        Left = placed.Left;
        Top = placed.Top;
        Width = placed.Width;
        Height = placed.Height;
        WindowState = placed.Maximized ? WindowState.Maximized : WindowState.Normal;
    }

    private void SavePlacementAndSettings()
    {
        var bounds = WindowState == WindowState.Minimized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _settings = _settings with
        {
            Theme = App.Themes.Current,
            Window = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                WindowState == WindowState.Maximized),
        };
        PersistSettings();
    }

    private void PersistSettings()
    {
        App.Settings = _settings;
        App.SaveSettings();
    }

    private void ApplySettingsResult(AppSettings result)
    {
        _settings = result;
        App.Themes.Apply(result.Theme);
        _autosaveTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(result.AutosaveSeconds, 10, 300));
        ApplySettingsToPen();
        SyncPenControls();
        RebuildPalette();
        _session.ApplyToolToAll();
        PersistSettings();
    }

    // ---------- palette + pen controls ----------

    private void SyncPenControls()
    {
        foreach (var item in FamilyBox.Items.OfType<ComboBoxItem>())
            item.IsSelected = item.Tag as string == _settings.PenType.ToString();
        foreach (var item in SizeBox.Items.OfType<ComboBoxItem>())
        {
            if (double.TryParse(item.Tag?.ToString(), out double size)
                && Math.Abs(size - _settings.PenSizeDip) < 0.01)
                item.IsSelected = true;
        }
    }

    private void RebuildPalette()
    {
        PalettePanel.Children.Clear();
        var accent = (Brush)Application.Current.FindResource("AppAccent");
        foreach (string hex in _settings.Palette)
        {
            if (!ColorPalette.TryNormalize(hex, out string? normalized) || normalized is null)
                continue;
            uint argb = Convert.ToUInt32(normalized.TrimStart('#'), 16);
            var (a, r, g, b) = ((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            var color = Color.FromArgb(a, r, g, b);
            var swatch = new Button
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush(color),
                ToolTip = normalized,
            };
            if (argb == InkUnits.ToArgb(color.A, color.R, color.G, color.B)
                && argb == InkUnits.ToArgb(_pen.InkColor.A, _pen.InkColor.R, _pen.InkColor.G, _pen.InkColor.B))
            {
                swatch.BorderBrush = accent;
                swatch.ToolTip += " (selected)";
            }
            swatch.Click += (_, _) => PickColor(color, argb);
            PalettePanel.Children.Add(swatch);
        }
    }

    private void Picker_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ColorPickerDialog(_pen.InkColor, _settings.Palette) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _pen.SetColor(dlg.Selected);
            var (a, r, g, b) = (dlg.Selected.A, dlg.Selected.R, dlg.Selected.G, dlg.Selected.B);
            _settings = _settings with
            {
                PenColorArgb = InkUnits.ToArgb(a, r, g, b),
                Palette = dlg.Palette,
            };
            PersistSettings();
            RebuildPalette();
            _session.ApplyToolToAll();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() == true)
            ApplySettingsResult(dlg.Result);
    }

    private void FamilyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FamilyBox.SelectedItem is ComboBoxItem item
            && Enum.TryParse<PenKind>(item.Tag?.ToString(), out var family))
        {
            _pen.SetPenFamily(family);
            _settings = _settings with { PenType = family };
            PersistSettings();
            if (PageScroll is not null)
                _session.ApplyToolToAll();
        }
    }

    // ---------- pen toolbar ----------

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        _pen.SetTool((sender as FrameworkElement)?.Tag as string);
        _session.ApplyToolToAll();
    }

    private void PickColor(Color color, uint argb)
    {
        _pen.SetColor(color);
        _settings = _settings with { PenColorArgb = argb };
        PersistSettings();
        RebuildPalette(); // refresh the selected ring
        if (_pen.RecolorSelection(CanvasExplorer.Find(PageScroll), page => _history.Push(page, CanvasStrokes)))
            _session.MarkDirty();
        _session.ApplyToolToAll();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        // Legacy fixed swatches (replaced by the settings-backed palette). Kept
        // wired in case any XAML still references it.
        if ((sender as FrameworkElement)?.Tag is string hex
            && ColorPalette.TryNormalize(hex, out string? normalized)
            && normalized is not null)
        {
            uint argb = Convert.ToUInt32(normalized.TrimStart('#'), 16);
            var (a, r, g, b) = ((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            PickColor(Color.FromArgb(a, r, g, b), argb);
        }
    }

    private void SizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SizeBox.SelectedItem is ComboBoxItem item
            && double.TryParse(item.Tag?.ToString(), out double size))
            ApplyPenSize(size);
        else if (!string.IsNullOrWhiteSpace(SizeBox.Text)
            && double.TryParse(SizeBox.Text, out double custom)
            && custom is >= 1 and <= 20)
            ApplyPenSize(custom);
    }

    private void ApplyPenSize(double size)
    {
        _pen.SetSize(size);
        _settings = _settings with { PenSizeDip = size };
        PersistSettings();
        if (PageScroll is not null)
            _session.ApplyToolToAll();
    }

    private StrokeCollection? CanvasStrokes(int page) => _session.CanvasForPage(page)?.Strokes;

    // ---------- edit actions ----------

    private void Undo_Click(object sender, RoutedEventArgs e) => _session.Undo(TargetPages());

    private void Redo_Click(object sender, RoutedEventArgs e) => _session.Redo(TargetPages());

    /// <summary>Undo/Redo are only available when there is something to undo/redo.</summary>
    private void UpdateUndoRedoState()
    {
        bool canUndo = _session.CanUndoAny();
        bool canRedo = _session.CanRedoAny();
        UndoButton.IsEnabled = canUndo;
        RedoButton.IsEnabled = canRedo;
        UndoMenuItem.IsEnabled = canUndo;
        RedoMenuItem.IsEnabled = canRedo;
    }

    private void DeleteSelection_Click(object sender, RoutedEventArgs e) => _session.DeleteSelection();

    private List<int> TargetPages()
    {
        var focused = new List<int>();
        foreach (var ink in CanvasExplorer.Find(PageScroll))
        {
            if (ink.IsFocused || ink.IsKeyboardFocusWithin)
                focused.Add(CanvasExplorer.GetPageNumber(ink));
        }
        return focused.Count > 0 ? focused : _history.Pages.ToList();
    }

    // ---------- input split ----------

    private void PageInk_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        if (sender is not InkCanvas ink)
            return;
        // Palm rejection + pen/touch split: touch always scrolls, pen uses the active tool.
        var kind = InputClassification.Classify(e.StylusDevice.TabletDevice.Type.ToString());
        if (InputClassification.ShouldScroll(kind) && kind != InputKind.Mouse)
        {
            ink.EditingMode = InkCanvasEditingMode.None; // let ScrollViewer pan
            return;
        }
        _pen.ApplyTo(ink);
    }

    private void PageInk_PreviewTouchDown(object sender, TouchEventArgs e)
    {
        // Touch must scroll, never draw.
        if (sender is InkCanvas ink)
            ink.EditingMode = InkCanvasEditingMode.None;
    }

    // ---------- open / save ----------

    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveSafely(saveAs: true);

    private void SaveSafely(bool saveAs)
    {
        try
        {
            _session.Save(_vm, saveAs ? null : _session.CurrentPath, () =>
            {
                var dlg = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf" };
                return dlg.ShowDialog() == true ? dlg.FileName : null;
            });
        }
        catch (Exception ex)
        {
            _vm.Status = $"Save failed: {ex.Message}";
        }
    }

    private void OpenSafely(string path)
    {
        try
        {
            _session.OpenDocument(path, _vm);
        }
        catch (Exception ex)
        {
            _vm.Status = $"Could not open PDF: {ex.Message}";
        }
    }

    // ---------- autosave ----------

    private void SaveWorkingCopy()
    {
        try
        {
            _session.SaveWorkingCopy(_vm, _autosave);
        }
        catch { /* autosave must never interrupt writing */ }
    }

    private void CheckForAutosave()
    {
        var latest = _autosave.LatestInfoPath();
        if (latest is not null)
            _vm.Status = $"Autosave found ({new FileInfo(latest).LastWriteTime:g}). Use File → Recover autosave to restore it. Pen draws, touch scrolls.";
    }

    private void Recover_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var found = _autosave.ReadLatest();
            if (found is null)
            {
                _vm.Status = "No autosaves found.";
                return;
            }
            OpenSafely(found.Value.PdfPath);
            if (!string.IsNullOrEmpty(found.Value.SourcePath) && File.Exists(found.Value.SourcePath))
            {
                _session.RetargetCurrentPath(found.Value.SourcePath); // keep saving to the real file
                _vm.Status += " (recovered from autosave)";
            }
        }
        catch (Exception ex)
        {
            _vm.Status = $"Recover failed: {ex.Message}";
        }
    }

    // ---------- misc ----------

    private void ToolBar_Loaded(object sender, RoutedEventArgs e)
    {
        // A standalone ToolBar has no overflow needs — collapse any overflow chrome.
        if (sender is ToolBar bar
            && bar.Template.FindName("OverflowGrid", bar) is FrameworkElement overflow)
            overflow.Visibility = Visibility.Collapsed;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;
        if (e.Delta > 0)
            _vm.ZoomInCommand.Execute(null);
        else
            _vm.ZoomOutCommand.Execute(null);
        e.Handled = true;
    }
}
