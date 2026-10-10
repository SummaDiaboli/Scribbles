# Scribbles

A Samsung Notes-style annotator for Windows PCs, laptops and tablets. Write on PDFs, slides and blank notebooks with a pen, mouse or touch, then choose what happens to your handwriting: keep it as ink, convert it to editable text, or keep the ink and make it **copyable** in the exported PDF.

Built natively in C# / WPF for low pen latency and offline Windows handwriting recognition. Every dependency is free and open source.

## Features

**Inking**
- Pen draws, finger scrolls and pinch-zooms, palm rejection keeps resting hands from marking the page
- Pressure-sensitive pens: Ballpoint, Fountain (pressure-shaped nib) and Highlighter
- Eraser, lasso select (move, resize, recolor, delete) and per-page undo/redo
- Full color picker (HSV field, hue rail, hex input) with a saved palette and adjustable thickness (1–20 pt)
- Toolbar that can be dragged between the top and bottom of the window

**Handwriting recognition** (on a lasso selection)
1. **Keep as ink** (default)
2. **Convert to text**: replaces the ink with an editable text box sized to match
3. **Keep ink + make copyable**: the ink stays visible, and an invisible text layer goes underneath it (the same trick searchable scanned PDFs use), so selecting the handwriting in any PDF viewer copies real text

Recognition uses Windows' built-in `InkRecognizerContainer`. It works offline with whatever handwriting language packs are installed.

**Documents**
- The PDF is the save file. Ink is stored as standard `/Ink` annotations that show up in Edge and Acrobat, and the full note model sits in the PDF's `/PieceInfo` dictionary, so reopening a saved file keeps every stroke editable
- PDFs changed by another tool still open: their ink annotations come back as editable strokes
- **Export flattened copy** burns everything in for sharing
- Select and copy native PDF text, browser-style
- Blank notebooks that grow page by page
- Slide import through PowerPoint (if installed) or LibreOffice headless
- Autosave with crash recovery

**Shell**
- Home grid of thumbnails for notebooks and recent files (open, rename, delete to Recycle Bin)
- Page sidebar with live ink thumbnails and a hover magnifier
- Light, dark or system theme
- Settings for theme, default pen, autosave interval and export defaults, persisted with window size and position

## Requirements

- Windows 10 (build 19041) or later. Works with a mouse; a pen gives you pressure-sensitive ink, and a touchscreen adds finger scrolling and pinch-zoom
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: a Windows handwriting language pack (for recognition), and PowerPoint or LibreOffice (for slide import)

## Getting started

```powershell
git clone https://github.com/SummaDiaboli/Scribbles.git
cd Scribbles
dotnet run --project Scribbles.App
```

The app opens at the home grid. You can also pass a PDF path as an argument to open it directly.

To build a release:

```powershell
dotnet publish Scribbles.App/Scribbles.App.csproj -c Release
```

## Keyboard and input

| Action | Input |
| --- | --- |
| Draw | Pen |
| Scroll / zoom | One finger / two-finger pinch, or `Ctrl` + mouse wheel |
| Undo / redo | `Ctrl+Z` / `Ctrl+Y` |
| Delete selection | `Del` |
| Copy selected text | `Ctrl+C` |

## Project layout

```
Scribbles.Core/     Pure logic: note model, serialization, coordinate transforms,
                    stroke ops, undo/redo, settings, export planning
Scribbles.Pdf/      PDF read/write: /Ink annotations, PieceInfo store, text
                    extraction, flattened export
Scribbles.App/      WPF shell: home grid, page viewer, pen toolbar, dialogs,
                    themes, recognition and rendering services
tests/
  Scribbles.Unit.Tests/         Fast tests on Core (incl. FsCheck property tests)
  Scribbles.Integration.Tests/  Real PDF round-trips, export, golden images
  Scribbles.E2E.Tests/          FlaUI tests driving the real app
  fixtures/                     Sample PDFs and golden images
```

## Testing

```powershell
dotnet test tests/Scribbles.Unit.Tests
dotnet test tests/Scribbles.Integration.Tests
dotnet test tests/Scribbles.E2E.Tests
```

CI (GitHub Actions on `windows-latest`) runs unit and integration tests on every push, and fails the build if line coverage on `Scribbles.Core` drops below **90%**. E2E tests also run on PRs to `main` and on `v*` tags.

Interactive E2E tests (real mouse and keyboard) only run when `SCRIBBLES_INTERACTIVE_E2E=1` is set. Don't set it on your dev machine: those tests take over the desktop.

## Built with

- [WPF](https://github.com/dotnet/wpf) on .NET 8 with [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [Docnet.Core](https://github.com/GowenGit/docnet) (PDFium) for page rendering
- [PDFsharp](https://github.com/empira/PDFsharp) for writing PDFs
- [PdfPig](https://github.com/UglyToad/PdfPig) for text extraction and page sizes
- xUnit, Shouldly, FsCheck, Verify, coverlet and [FlaUI](https://github.com/FlaUI/FlaUI) for tests

## Roadmap

- Whole-page convert / make-copyable (currently per selection)
- Auto-recognize all ink at export when "make everything copyable" is on
- In-place editing of native PDF text (currently parked)
- Installer and auto-updates through Velopack and GitHub Releases
