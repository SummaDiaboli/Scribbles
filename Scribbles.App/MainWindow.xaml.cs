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
    private readonly HandwritingRecognizer _recognizer = new();
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
        _vm.RequestCopyText += (_, _) => CopyTextSelection();
        _vm.RequestOpen += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf" };
            if (dlg.ShowDialog() == true)
                OpenSafely(dlg.FileName);
        };
        DataContext = _vm;
        _vm.Status = $"Scribbles ({BuildStamp()}) — open a PDF to start writing. Pen draws, touch scrolls.";
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
            ToggleTextOverlays();
            UpdateUndoRedoState();
            RefreshRecentMenu();
            CheckForAutosave();
        };
        PageList.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            // Containers materialize asynchronously (and recycle while scrolling).
            _session.SyncContainers();
            ToggleTextOverlays();
        };
        PageScroll.ScrollChanged += (_, _) => ToggleTextOverlays();

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
        ToggleTextOverlays();
        ClearAllTextHighlights();
        _textSelections.Clear();
        if (_pen.Tool == PenTool.TextSelect)
        {
            int total = _vm.Pages.Sum(p => p.Words.Count);
            _vm.Status = total == 0
                ? "No selectable text in this document (scanned images?)."
                : $"~{total:N0} selectable word(s) — drag over text to select, Ctrl+C to copy.";
        }
        else if (_pen.Tool == PenTool.TextEdit)
        {
            _vm.Status = "Click a line of text to edit it — Enter commits, Esc cancels.";
        }
    }

    private void ToggleTextOverlays()
    {
        bool textMode = _pen.Tool is PenTool.TextSelect or PenTool.TextEdit;
        foreach (var overlay in CanvasExplorer.FindOverlays(PageScroll))
            overlay.Visibility = textMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearAllTextHighlights()
    {
        foreach (var overlay in CanvasExplorer.FindOverlays(PageScroll))
            overlay.Children.Clear();
    }

    // ---------- native text selection (browser-like: select, then copy) ----------

    private Point? _marqueeStartDip;

    /// <summary>Reading-order lines cached for the in-progress drag.</summary>
    private IReadOnlyList<IReadOnlyList<PdfWord>>? _readingLines;

    /// <summary>Current text selection per page (highlighted until cleared).</summary>
    private readonly Dictionary<int, SelectedText> _textSelections = new();

    private static Brush AccentBrush(double opacity)
    {
        var accent = (Color)Application.Current.FindResource("AppAccentColor");
        return new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), accent.R, accent.G, accent.B));
    }

    private static SelectionRect MarqueeToPoints(PageViewModel page, Point startDip, Point endDip) => new(
        Math.Min(startDip.X, endDip.X) / page.DipPerPtX,
        Math.Min(startDip.Y, endDip.Y) / page.DipPerPtY,
        Math.Abs(endDip.X - startDip.X) / page.DipPerPtX,
        Math.Abs(endDip.Y - startDip.Y) / page.DipPerPtY);

    private static void PaintWordHighlights(Canvas overlay, PageViewModel page, SelectedText selected)
    {
        var highlight = AccentBrush(0.35);
        foreach (var word in selected.Words)
        {
            var rect = new System.Windows.Shapes.Rectangle
            {
                Fill = highlight,
                Width = word.Width * page.DipPerPtX,
                Height = word.Height * page.DipPerPtY,
            };
            Canvas.SetLeft(rect, word.X * page.DipPerPtX);
            Canvas.SetTop(rect, word.Y * page.DipPerPtY);
            overlay.Children.Add(rect);
        }
    }

    private SelectedText SelectUnderMarquee(PageViewModel page, Point startDip, Point endDip) =>
        TextSelector.Select(page.Words, MarqueeToPoints(page, startDip, endDip));

    /// <summary>
    /// Browser anchor-range, snapping to the nearest word so a press
    /// slightly off the glyphs still anchors; marquee fallback only when
    /// text is truly far from both ends of the drag.
    /// </summary>
    private SelectedText SelectBetween(PageViewModel page, Point startDip, Point endDip)
    {
        double ax = startDip.X / page.DipPerPtX, ay = startDip.Y / page.DipPerPtY;
        double fx = endDip.X / page.DipPerPtX, fy = endDip.Y / page.DipPerPtY;
        var anchor = TextSelector.WordAt(page.Words, ax, ay)
            ?? TextSelector.WordNearest(page.Words, ax, ay);
        var focus = TextSelector.WordAt(page.Words, fx, fy)
            ?? TextSelector.WordNearest(page.Words, fx, fy);
        if (anchor is not null && focus is not null)
        {
            var lines = _readingLines ?? TextSelector.OrderReading(page.Words);
            return TextSelector.Range(lines, anchor, focus);
        }
        return SelectUnderMarquee(page, startDip, endDip);
    }

    // ---------- recognition: convert / copyable ----------

    private List<(int Page, InkCanvas Ink, Stroke Stroke)> SelectedInkStrokes()
    {
        var selected = new List<(int, InkCanvas, Stroke)>();
        foreach (var ink in CanvasExplorer.Find(PageScroll))
        {
            int page = CanvasExplorer.GetPageNumber(ink);
            foreach (var stroke in ink.GetSelectedStrokes().Cast<Stroke>())
                selected.Add((page, ink, stroke));
        }
        return selected;
    }

    private PageViewModel? PageVm(int page) => _vm.Pages.FirstOrDefault(p => p.Number == page);

    private List<Core.InkStroke> ToModels(List<(int Page, InkCanvas Ink, Stroke Stroke)> selected)
    {
        var models = new List<Core.InkStroke>();
        foreach (var (page, _, stroke) in selected)
        {
            var vm = PageVm(page);
            models.Add(_converter.ToModel(stroke,
                page, vm?.DipPerPtX ?? InkUnits.DipsPerPoint, vm?.DipPerPtY ?? InkUnits.DipsPerPoint));
        }
        return models;
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedInkStrokes();
        if (selected.Count == 0)
        {
            _vm.Status = "Lasso some handwriting first, then Convert.";
            return;
        }
        _vm.Status = "Recognizing handwriting…";
        string? text = null;
        try
        {
            text = await _recognizer.RecognizeAsync(ToModels(selected));
        }
        catch (Exception ex)
        {
            _vm.Status = $"Recognition failed: {ex.Message}";
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            _vm.Status = _recognizer.IsAvailable
                ? "No text recognized — try larger, clearer writing."
                : "No handwriting recognizer installed (Windows Settings → Language → Handwriting).";
            return;
        }

        foreach (var group in selected.GroupBy(t => t.Page))
        {
            var vm = PageVm(group.Key);
            if (vm is null)
                continue;
            _history.Push(group.Key, CanvasStrokes);
            var models = group.Select(t =>
            {
                var v = PageVm(t.Page);
                return _converter.ToModel(t.Stroke, t.Page,
                    v?.DipPerPtX ?? InkUnits.DipsPerPoint, v?.DipPerPtY ?? InkUnits.DipsPerPoint);
            }).ToList();
            foreach (var stroke in group.Select(t => t.Stroke).ToList())
            {
                group.First().Ink.Strokes.Remove(stroke);
                _converter.Forget(stroke);
            }
            double minX = models.Min(m => m.Points.Min(p => p.X));
            double minY = models.Min(m => m.Points.Min(p => p.Y));
            double maxX = models.Max(m => m.Points.Max(p => p.X));
            double maxY = models.Max(m => m.Points.Max(p => p.Y));
            var box = new TextBoxItem
            {
                Id = Guid.NewGuid().ToString("N"),
                PageNumber = group.Key,
                X = minX,
                Y = minY,
                Width = Math.Max(60, maxX - minX),
                Height = Math.Max(20, maxY - minY),
                Text = text,
                FontSizePt = Math.Clamp(maxY - minY, 8, 48),
                ColorArgb = 0xFF000000,
                Kind = TextBoxKind.Converted,
            };
            DocumentSession.SetDisplay(box, vm.DipPerPtX, vm.DipPerPtY);
            vm.TextBoxes.Add(box);
        }
        _session.MarkDirty();
        _vm.Status = "Converted to text — edit freely.";
    }

    private async void Copyable_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedInkStrokes();
        if (selected.Count == 0)
        {
            _vm.Status = "Lasso some handwriting first, then Make copyable.";
            return;
        }
        _vm.Status = "Recognizing handwriting…";
        string? text = null;
        try
        {
            text = await _recognizer.RecognizeAsync(ToModels(selected));
        }
        catch (Exception ex)
        {
            _vm.Status = $"Recognition failed: {ex.Message}";
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            _vm.Status = _recognizer.IsAvailable
                ? "No text recognized — try larger, clearer writing."
                : "No handwriting recognizer installed (Windows Settings → Language → Handwriting).";
            return;
        }
        _session.MarkCopyable(
            selected.Select(t => (t.Page, t.Stroke, _converter.GetId(t.Stroke))), text);
        _vm.Status = $"Marked {selected.Count} stroke(s) copyable — embeds on export.";
    }

    // ---------- native text editing (dedicated tool + double-click) ----------

    private TextBox? _nativeEditor;
    private int _nativePage;
    private SelectionRect _nativeBounds = new(0, 0, 0, 0);
    private string _nativeOldText = "";

    private void TryStartNativeEdit(Canvas overlay, PageViewModel page)
    {
        DiscardNativeEditor(commit: false);
        if (!_textSelections.TryGetValue(page.Number, out var selected) || selected.Words.Count == 0)
        {
            _vm.Status = "Select native text first (drag), then double-click to edit it.";
            return;
        }
        double minX = selected.Words.Min(w => w.X);
        double minY = selected.Words.Min(w => w.Y);
        double maxX = selected.Words.Max(w => w.X + w.Width);
        double maxY = selected.Words.Max(w => w.Y + w.Height);
        ShowEditor(overlay, page,
            selected.Text.Replace("\n", " "),
            new SelectionRect(minX, minY, maxX - minX, maxY - minY),
            caretOffset: null); // select-all
    }

    /// <summary>Click a line in the TextEdit tool: caret lands where clicked.</summary>
    private void TryStartLineEdit(Canvas overlay, PageViewModel page, Point clickDip)
    {
        DiscardNativeEditor(commit: false);
        if (page.Words.Count == 0)
        {
            _vm.Status = "No editable text on this page (scanned image?).";
            return;
        }
        double xPt = clickDip.X / page.DipPerPtX;
        double yPt = clickDip.Y / page.DipPerPtY;
        var word = TextSelector.WordAt(page.Words, xPt, yPt)
            ?? TextSelector.WordNearest(page.Words, xPt, yPt);
        if (word is null)
        {
            _vm.Status = "Click directly on a line of text to edit it.";
            return;
        }
        var lines = TextSelector.OrderReading(page.Words);
        var line = lines.FirstOrDefault(l => l.Contains(word))
            ?? new[] { word }.ToList();
        double minX = line.Min(w => w.X);
        double minY = line.Min(w => w.Y);
        double maxX = line.Max(w => w.X + w.Width);
        double maxY = line.Max(w => w.Y + w.Height);
        string text = string.Join(" ", line.Select(w => w.Text));
        // Remember as the page selection so copy still works after editing focus.
        _textSelections[page.Number] = new SelectedText(line.ToList(), text);
        overlay.Children.Clear();
        PaintWordHighlights(overlay, page, _textSelections[page.Number]);
        ShowEditor(overlay, page, text,
            new SelectionRect(minX, minY, maxX - minX, maxY - minY),
            caretOffset: TextCaret.OffsetForX(line.ToList(), xPt));
    }

    private void ShowEditor(
        Canvas overlay, PageViewModel page, string text, SelectionRect boundsPt, int? caretOffset)
    {
        var (minX, minY, w, h) = (boundsPt.X, boundsPt.Y, boundsPt.Width, boundsPt.Height);
        _nativeBounds = boundsPt;
        _nativePage = page.Number;
        _nativeOldText = text;

        double lineHeight = Math.Max(8, h);
        var editor = new TextBox
        {
            Text = text,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            FontSize = Math.Clamp(lineHeight * page.DipPerPtY, 10, 72),
            Width = Math.Max(60, w * page.DipPerPtX + 20),
            Height = h * page.DipPerPtY + 12,
            Background = (Brush)Application.Current.FindResource("AppSurface"),
            Foreground = (Brush)Application.Current.FindResource("AppForeground"),
            BorderBrush = (Brush)Application.Current.FindResource("AppAccent"),
            BorderThickness = new Thickness(2),
        };
        Canvas.SetLeft(editor, minX * page.DipPerPtX - 4);
        Canvas.SetTop(editor, minY * page.DipPerPtY - 4);
        editor.KeyDown += NativeEditor_KeyDown;
        editor.LostFocus += NativeEditor_LostFocus;
        overlay.Children.Add(editor);
        _nativeEditor = editor;
        editor.Focus();
        if (caretOffset is int caret)
            editor.CaretIndex = Math.Clamp(caret, 0, editor.Text.Length);
        else
            editor.SelectAll();
        _vm.Status = "Editing native text — Enter to commit, Esc to cancel.";
    }

    private void NativeEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DiscardNativeEditor(commit: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DiscardNativeEditor(commit: false);
            e.Handled = true;
        }
    }

    private void NativeEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        // Commit on focus loss (clicking elsewhere accepts the edit).
        if (_nativeEditor is not null)
            DiscardNativeEditor(commit: true);
    }

    private void DiscardNativeEditor(bool commit)
    {
        var editor = _nativeEditor;
        _nativeEditor = null;
        if (editor is null)
            return;
        string text = editor.Text;
        if (editor.Parent is Canvas overlay)
            overlay.Children.Remove(editor);
        if (!commit || string.IsNullOrWhiteSpace(text))
        {
            if (!commit)
                _vm.Status = "Edit cancelled.";
            return;
        }
        var vm = _vm.Pages.FirstOrDefault(p => p.Number == _nativePage);
        if (vm is null)
            return;

        // 1. True edit: rewrite the original text layer when possible.
        if (_session.CurrentBytes is not null
            && TrueTextEdit.TryReplaceLine(
                _session.CurrentBytes, _nativePage, _nativeOldText, text.Trim(), out byte[] updated))
        {
            _session.ReplaceBaseBytes(updated);
            _session.RefreshPage(_vm, _nativePage);
            _textSelections.Remove(_nativePage);
            foreach (var ov in CanvasExplorer.FindOverlays(PageScroll))
            {
                if ((ov.DataContext as PageViewModel)?.Number == _nativePage)
                    ov.Children.Clear();
            }
            _vm.Status = "Edited in the original text layer.";
            return;
        }

        // 2. Fallback: cover the original with an overlay box (baked on export).
        var (x, y, w, h) = (_nativeBounds.X, _nativeBounds.Y, _nativeBounds.Width, _nativeBounds.Height);
        var box = new TextBoxItem
        {
            Id = Guid.NewGuid().ToString("N"),
            PageNumber = _nativePage,
            X = x,
            Y = y,
            Width = Math.Max(40, w),
            Height = Math.Max(16, h),
            Text = text.Trim(),
            FontSizePt = Math.Clamp(h / Math.Max(1, text.Trim().Split('\n').Length) * 0.7, 8, 48),
            ColorArgb = 0xFF000000,
            Kind = TextBoxKind.EditedNative,
        };
        DocumentSession.SetDisplay(box, vm.DipPerPtX, vm.DipPerPtY);
        vm.TextBoxes.Add(box);
        _textSelections.Remove(_nativePage);
        foreach (var ov in CanvasExplorer.FindOverlays(PageScroll))
        {
            if ((ov.DataContext as PageViewModel)?.Number == _nativePage)
                ov.Children.Clear();
        }
        _session.MarkDirty();
        _vm.Status = "Native text edited — baked into the PDF on export.";
    }

    private void TextOverlay_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Canvas overlay || overlay.DataContext is not PageViewModel page)
            return;
        // Native editing parked (2026-10-08): selection + copy only for now.
        // if (_pen.Tool == PenTool.TextEdit)
        // {
        //     _marqueeStartDip = e.GetPosition(overlay);
        //     overlay.CaptureMouse();
        //     overlay.Focus();
        //     e.Handled = true;
        //     return;
        // }
        // if (e.ClickCount == 2)
        // {
        //     TryStartNativeEdit(overlay, page);
        //     e.Handled = true;
        //     return;
        // }
        // Fresh drag: clear this page's selection. The anchor is the word under
        // the cursor (browser behavior — no selection box).
        _textSelections.Remove(page.Number);
        overlay.Children.Clear();
        _marqueeStartDip = e.GetPosition(overlay);
        _readingLines = TextSelector.OrderReading(page.Words);
        overlay.CaptureMouse();
        // Take keyboard focus so Ctrl+C reaches the text selection instead of
        // whatever toolbar box was focused before (e.g. the zoom level).
        overlay.Focus();
        e.Handled = true;
    }

    private void TextOverlay_Move(object sender, MouseEventArgs e)
    {
        if (_pen.Tool != PenTool.TextSelect)
            return; // edit tool selects nothing on drag
        if (_marqueeStartDip is null
            || sender is not Canvas overlay || overlay.DataContext is not PageViewModel page)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        // Live highlight while dragging, browser-style (no box visual).
        var selected = SelectBetween(page, _marqueeStartDip.Value, e.GetPosition(overlay));
        _textSelections[page.Number] = selected;
        overlay.Children.Clear();
        PaintWordHighlights(overlay, page, selected);
    }

    private void TextOverlay_Up(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Canvas overlay)
            return;
        overlay.ReleaseMouseCapture();
        if (_marqueeStartDip is null || overlay.DataContext is not PageViewModel page)
            return;
        var end = e.GetPosition(overlay);
        var start = _marqueeStartDip.Value;
        _marqueeStartDip = null;
        _readingLines = null;

        // Edit tool parked (2026-10-08).
        // if (_pen.Tool == PenTool.TextEdit)
        // {
        //     if (Math.Abs(end.X - start.X) < 5 && Math.Abs(end.Y - start.Y) < 5
        //         && overlay.DataContext is PageViewModel editPage)
        //         TryStartLineEdit(overlay, editPage, end);
        //     return;
        // }

        // Treat a plain click as "clear selection".
        if (Math.Abs(end.X - start.X) < 3 && Math.Abs(end.Y - start.Y) < 3)
        {
            _textSelections.Remove(page.Number);
            overlay.Children.Clear();
            _vm.Status = "Selection cleared. Drag over text to select.";
            return;
        }

        if (page.Words.Count == 0)
        {
            _vm.Status = "No selectable text on this page (scanned image?).";
            return;
        }

        var selected = SelectBetween(page, start, end);
        _textSelections[page.Number] = selected;
        overlay.Children.Clear();
        PaintWordHighlights(overlay, page, selected);

        _vm.Status = selected.Words.Count == 0
            ? "No text under the cursor."
            : $"Selected {selected.Words.Count} word(s) — Ctrl+C to copy.";
        e.Handled = true;
    }

    private void CopyTextSelection()
    {
        if (_pen.Tool is not (PenTool.TextSelect or PenTool.TextEdit))
            return; // let TextBoxes etc. handle Ctrl+C themselves
        var selected = _textSelections.Values.FirstOrDefault(s => s.Words.Count > 0);
        if (selected is null)
        {
            _vm.Status = "Nothing to copy — drag over text to select first.";
            return;
        }
        try
        {
            Clipboard.SetText(selected.Text);
            _vm.Status = $"Copied {selected.Words.Count} word(s) to the clipboard.";
        }
        catch (Exception ex)
        {
            _vm.Status = $"Copy failed: {ex.Message}";
        }
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
        // Text modes draw nothing: the overlay handles mouse selection/editing.
        var kind = InputClassification.Classify(e.StylusDevice.TabletDevice.Type.ToString());
        if (_pen.Tool is PenTool.TextSelect or PenTool.TextEdit
            || (InputClassification.ShouldScroll(kind) && kind != InputKind.Mouse))
        {
            ink.EditingMode = InkCanvasEditingMode.None; // let ScrollViewer pan
            if (_pen.Tool is PenTool.TextSelect or PenTool.TextEdit)
                ink.Focus(); // keep Ctrl+C on the selection, not the toolbar
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

    private void TextBoxEdited(object sender, TextChangedEventArgs e) => _session.MarkDirty();

    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveSafely(saveAs: true);

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_session.CurrentBytes is null)
        {
            _vm.Status = "Nothing to export — open a PDF first.";
            return;
        }
        var dlg = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = "export.pdf" };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            _session.ExportFlatCopy(_vm, dlg.FileName);
        }
        catch (Exception ex)
        {
            _vm.Status = $"Export failed: {ex.Message}";
        }
    }

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
            _textSelections.Clear();
            _session.OpenDocument(path, _vm);
            _settings = _settings with { RecentFiles = Core.RecentFiles.Add(_settings.RecentFiles, path) };
            PersistSettings();
            RefreshRecentMenu();
            // Containers materialize async: re-apply overlay visibility after layout.
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(ToggleTextOverlays));
        }
        catch (Exception ex)
        {
            _vm.Status = $"Could not open PDF: {ex.Message}";
        }
    }

    private void RecentMenu_Opened(object sender, RoutedEventArgs e) => RefreshRecentMenu();

    /// <summary>
    /// Populated eagerly (not only when opened): a childless menu item never
    /// opens its submenu, which made Recent look like a dead button.
    /// </summary>
    private void RefreshRecentMenu()
    {
        var files = Core.RecentFiles.PruneMissing(_settings.RecentFiles);
        if (_settings.RecentFiles.Length != files.Length)
        {
            _settings = _settings with { RecentFiles = files };
            PersistSettings();
        }
        RecentMenuItem.Items.Clear();
        if (files.Length == 0)
        {
            RecentMenuItem.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }
        foreach (string path in files)
        {
            var item = new MenuItem { Header = path };
            item.Click += (_, _) => OpenSafely(path);
            RecentMenuItem.Items.Add(item);
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

    // ---------- pinch zoom (two-finger touch) ----------

    private readonly Dictionary<int, Point> _touches = new();
    private double _pinchStartDist;
    private double _pinchStartZoom;

    private static double TouchDistance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void PageScroll_TouchDown(object sender, TouchEventArgs e)
    {
        _touches[e.TouchDevice.Id] = e.GetTouchPoint(PageScroll).Position;
        if (_touches.Count == 2)
        {
            var points = _touches.Values.ToArray();
            _pinchStartDist = TouchDistance(points[0], points[1]);
            _pinchStartZoom = _vm.Zoom;
        }
    }

    private void PageScroll_TouchMove(object sender, TouchEventArgs e)
    {
        if (!_touches.ContainsKey(e.TouchDevice.Id))
            return;
        _touches[e.TouchDevice.Id] = e.GetTouchPoint(PageScroll).Position;
        if (_touches.Count == 2)
        {
            var points = _touches.Values.ToArray();
            _vm.Pinch(_pinchStartDist, TouchDistance(points[0], points[1]));
        }
    }

    private void PageScroll_TouchUp(object sender, TouchEventArgs e)
    {
        _touches.Remove(e.TouchDevice.Id);
    }

    // ---------- misc ----------

    /// <summary>Exe write time, so a stale build is identifiable from the status bar.</summary>
    private static string BuildStamp()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
                return "dev";
            return File.GetLastWriteTime(path).ToString("dd-MMM HH:mm");
        }
        catch
        {
            return "dev";
        }
    }

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
