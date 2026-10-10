using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Scribbles.App.Services;
using Scribbles.Core;
using Scribbles.Pdf;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Scribbles.App;

public sealed partial class HomeCard : ObservableObject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public HomeDocumentKind Kind { get; set; } = HomeDocumentKind.Pdf;
    public DateTime ModifiedUtc { get; set; }

    [ObservableProperty]
    private ImageSource? _thumbnail;
}

/// <summary>Home grid: notebooks + recent documents with thumbnails.</summary>
public partial class HomeWindow : Window
{
    private readonly ObservableCollection<HomeCard> _cards = new();

    public HomeWindow()
    {
        InitializeComponent();
        Title = $"Scribbles {MainWindow.AppVersion()}";
        DocumentList.ItemsSource = _cards;
        Loaded += (_, _) => _ = RefreshAsync();
        // Pick up newly opened documents when returning from the viewer.
        Activated += (_, _) => _ = RefreshAsync();
    }

    private bool _refreshing;

    /// <summary>Merges notebooks + recents, notebooks first, deduped by path.</summary>
    internal static List<(HomeDocument Doc, string Subtitle)> MergeEntries(
        IEnumerable<(HomeDocument Doc, string Subtitle)> notebooks,
        IEnumerable<(HomeDocument Doc, string Subtitle)> recents)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<(HomeDocument Doc, string Subtitle)>();
        foreach (var entry in notebooks.Concat(recents))
        {
            if (seen.Add(entry.Doc.Path))
                merged.Add(entry);
        }
        return merged;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            await RefreshCoreAsync();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshCoreAsync()
    {
        var notebooks = NotebookFiles.List()
            .Select(doc => (doc, $"Notebook · {doc.ModifiedUtc:g}"));
        var recents = new List<(HomeDocument, string)>();
        foreach (string path in App.Settings.RecentFiles)
        {
            if (!File.Exists(path))
                continue;
            string full = Path.GetFullPath(path);
            var modified = File.GetLastWriteTimeUtc(full);
            recents.Add((new HomeDocument(Path.GetFileName(full), full, HomeDocumentKind.Pdf, modified),
                $"PDF · {modified:g}"));
        }
        // Reuse card objects by path so thumbnails survive refreshes
        // (returning from the viewer doesn't re-render everything).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fresh = new List<HomeCard>();
        foreach (var (doc, subtitle) in MergeEntries(notebooks, recents))
        {
            seen.Add(doc.Path);
            if (!_cardCache.TryGetValue(doc.Path, out var card))
            {
                card = new HomeCard();
                _cardCache[doc.Path] = card;
            }
            card.Name = doc.Name;
            card.Path = doc.Path;
            card.Subtitle = subtitle;
            card.Kind = doc.Kind;
            card.ModifiedUtc = doc.ModifiedUtc;
            fresh.Add(card);
        }
        foreach (string stale in _cardCache.Keys.Where(k => !seen.Contains(k)).ToList())
            _cardCache.Remove(stale);
        _allCards.Clear();
        foreach (var card in fresh)
            _allCards.Add(card);
        ApplyListing();

        var renderer = new DocnetPdfRenderer();
        await Task.WhenAll(_allCards
            .Where(c => c.Thumbnail is null && _loading.Add(c.Path))
            .Select(async card =>
            {
                try
                {
                    await LoadCardThumbAsync(renderer, card).ConfigureAwait(false);
                }
                finally
                {
                    _loading.Remove(card.Path);
                }
            }));
    }

    private readonly ObservableCollection<HomeCard> _allCards = new();
    private readonly Dictionary<string, HomeCard> _cardCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
    private HomeSort _sort = HomeSort.Newest;
    private HomeKindFilter _kind = HomeKindFilter.All;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyListing();

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _sort = (SortBox.SelectedIndex == 1) ? HomeSort.Name : HomeSort.Newest;
        ApplyListing();
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _kind = KindBox.SelectedIndex switch { 1 => HomeKindFilter.Notebooks, 2 => HomeKindFilter.Pdfs, _ => HomeKindFilter.All };
        ApplyListing();
    }

    /// <summary>Sorts + filters the cached cards into the visible list.</summary>
    private void ApplyListing()
    {
        // Combo events fire during InitializeComponent before peers exist.
        if (StatusText is null || SearchBox is null)
            return;
        var docs = _allCards.Select(c => new HomeDocument(c.Name, c.Path, c.Kind, c.ModifiedUtc));
        var wanted = HomeListing.Apply(docs, _sort, _kind, SearchBox.Text);
        _cards.Clear();
        foreach (var doc in wanted)
        {
            if (_cardCache.TryGetValue(doc.Path, out var card))
                _cards.Add(card);
        }
        StatusText.Text = _cards.Count == _allCards.Count
            ? $"{_cards.Count} document(s). Click a card to open."
            : $"{_cards.Count} of {_allCards.Count} document(s).";
        if (_allCards.Count == 0)
            StatusText.Text = "No documents yet — create a notebook or open a PDF.";
        if (App.PendingUpdate is not null)
            StatusText.Text += $" Update {App.PendingUpdate.TargetFullRelease.Version} downloaded — open a document, then File → Restart to apply update.";
    }

    private async Task LoadCardThumbAsync(DocnetPdfRenderer renderer, HomeCard card)
    {
        byte[]? bytes = null;
        try
        {
            bytes = await File.ReadAllBytesAsync(card.Path).ConfigureAwait(false);
        }
        catch { /* placeholder */ }
        if (bytes is null)
            return;
        // Managed + native work overlap per card; cards overlap each other.
        var dataTask = Task.Run(() => LoadThumbData(bytes));
        var rasterTask = renderer.RenderFirstPageAsync(bytes);
        await Task.WhenAll(dataTask, rasterTask).ConfigureAwait(false);
        var (size, strokes) = await dataTask.ConfigureAwait(false);
        var thumb = await rasterTask.ConfigureAwait(false);
        _ = Dispatcher.BeginInvoke(() => card.Thumbnail = thumb is null
            ? null
            : InkThumbnailComposer.Compose(thumb, size.W, size.H, strokes));
    }

    private static ((double W, double H) Size, IReadOnlyList<InkStroke> Strokes) LoadThumbData(byte[] bytes)
    {
        (double W, double H) size = (612, 792);
        IReadOnlyList<InkStroke> strokes = Array.Empty<InkStroke>();
        try
        {
            var pageSize = PdfPageSizes.Read(bytes)[0];
            size = (pageSize.Width, pageSize.Height);
            var loaded = new ScribblesPdfStore().Load(bytes);
            var first = loaded.Model?.Pages.FirstOrDefault(p => p.Number == 1);
            if (first is not null)
            {
                strokes = first.Strokes;
                size = (first.WidthPt, first.HeightPt);
            }
            else
            {
                strokes = loaded.FallbackInk.Where(s => s.PageNumber == 1).ToList();
            }
        }
        catch { /* base thumbnail only */ }
        return (size, strokes);
    }

    private void OpenDocument(string path)
    {
        try
        {
            var viewer = new MainWindow();
            viewer.Show();
            viewer.Open(path);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open: {ex.Message}";
        }
    }

    private void Card_Open(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HomeCard card)
            OpenDocument(card.Path);
    }

    private void CardMenu_Open(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HomeCard card)
            OpenDocument(card.Path);
    }

    private async void CardMenu_Rename(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HomeCard card)
            return;
        string? dir = Path.GetDirectoryName(card.Path);
        if (dir is null)
            return;
        var dlg = new RenameDialog(Path.GetFileNameWithoutExtension(card.Name))
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || dlg.NewName is null)
            return;
        string target = Path.Combine(dir, dlg.NewName + ".pdf");
        try
        {
            if (string.Equals(target, card.Path, StringComparison.Ordinal))
                return; // unchanged
            bool sameFile = string.Equals(target, card.Path, StringComparison.OrdinalIgnoreCase);
            if (!sameFile && File.Exists(target))
            {
                StatusText.Text = $"Rename failed: {dlg.NewName}.pdf already exists.";
                return;
            }
            File.Move(card.Path, target, overwrite: false);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Rename failed: {ex.Message}";
            return;
        }
        App.Settings = App.Settings with
        {
            RecentFiles = RecentFiles.Rename(App.Settings.RecentFiles, card.Path, target),
        };
        App.SaveSettings();
        StatusText.Text = $"Renamed to {Path.GetFileName(target)}.";
        await RefreshAsync();
    }

    private async void CardMenu_Delete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HomeCard card)
            return;
        try
        {
            // Recycle bin, not permanent deletion.
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                card.Path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Delete failed: {ex.Message}";
            return;
        }
        App.Settings = App.Settings with
        {
            RecentFiles = RecentFiles.Remove(App.Settings.RecentFiles, card.Path),
        };
        App.SaveSettings();
        StatusText.Text = $"Deleted {card.Name} (recycle bin).";
        await RefreshAsync();
    }

    private void NewNotebook_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = new NotebookService().CreateNotebook();
            _ = RefreshAsync();
            OpenDocument(path);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not create notebook: {ex.Message}";
        }
    }

    private void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf" };
        if (dlg.ShowDialog() == true)
        {
            _ = RefreshAsync();
            OpenDocument(dlg.FileName);
        }
    }

    private void ImportSlides_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Presentations (*.pptx;*.ppt;*.odp)|*.pptx;*.ppt;*.odp",
        };
        if (dlg.ShowDialog() != true)
            return;
        string? pdf = null;
        try
        {
            pdf = SlideImporter.TryImport(dlg.FileName,
                Path.Combine(Path.GetTempPath(), "Scribbles", "slides"));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Slide import failed: {ex.Message}";
            return;
        }
        if (pdf is null)
        {
            StatusText.Text = "Slide import needs PowerPoint or LibreOffice — pick a PDF instead.";
            OpenPdf_Click(sender, e);
            return;
        }
        OpenDocument(pdf);
    }
}
