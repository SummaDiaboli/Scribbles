# Scribbles

A Samsung Notes-style annotator for Windows PCs, laptops and tablets. Write on PDFs, slides and blank notebooks with a pen, mouse or touch, then choose what happens to your handwriting: keep it as ink, convert it to editable text, or keep the ink and make it **copyable** in the exported PDF.

Built natively in C# / WPF for low pen latency and offline Windows handwriting recognition. Every dependency is free and open source.

## Download

Grab `Scribbles-win-Setup.exe` from the [Releases page](https://github.com/SummaDiaboli/Scribbles/releases). Per-user install, no admin rights needed; the app updates itself in the background (File → Restart to apply update when one lands).

Requires Windows 10 (build 19041) or later. Works with a mouse; a pen gives you pressure-sensitive ink, and a touchscreen adds finger scrolling and pinch-zoom. The installer fetches the .NET 8 Desktop Runtime automatically if it's missing.

![Home grid with document thumbnails](docs/screenshots/home.png)
![Viewer with pen toolbar and page sidebar](docs/screenshots/viewer.png)

## Features

**Inking**
- Pen draws, finger scrolls and pinch-zooms, palm rejection keeps resting hands from marking the page
- Ballpoint (fixed width), Fountain (pressure-sensitive, needs a pen) and Highlighter
- Five thicknesses (XS–XL, 1–8 pt) plus any custom size from 1–20 pt
- Eraser, lasso select (move, resize, recolor, delete) and per-page undo/redo
- Full color picker (HSV field, hue rail, hex input) with a saved palette
- Toolbar that can be dragged between the top and bottom of the window
- Double-click selects the word under the cursor

**Handwriting recognition**
1. **Keep as ink** (default)
2. **Convert to text**: replaces the ink with an editable text box sized to match
3. **Keep ink + make copyable**: the ink stays visible, and an invisible text layer goes underneath it (the same trick searchable scanned PDFs use), so selecting the handwriting in any PDF viewer copies real text

Recognition uses Windows' built-in `InkRecognizerContainer`. It works offline with whatever handwriting language packs are installed.

**Reading and navigation**
- Find in document (`Ctrl+F`): live match count, next/previous, hits highlighted on every page
- Bookmark outline panel for jumping through long documents
- Select and copy native PDF text, browser-style; click a line with the TextEdit tool to edit it in place

**Documents**
- The PDF is the save file. Ink is stored as standard `/Ink` annotations that show up in Edge and Acrobat, and the full note model sits in the PDF, so reopening a saved file keeps every stroke editable
- PDFs changed by another tool still open: their ink annotations come back as editable strokes; PDFs too broken to save into still open with ink kept in a sidecar file next to them
- **Export flattened copy** burns everything in for sharing
- Blank notebooks that grow page by page
- Slide import through PowerPoint (if installed) or LibreOffice headless
- Autosave with crash recovery (File → Recover autosave lists every snapshot)

**Shell**
- Home grid of thumbnails for notebooks and recent files with search, sort and kind filter (open, rename, delete to Recycle Bin)
- Page sidebar with live ink thumbnails, a hover magnifier, and current-page highlight that follows your scroll
- "Open with" support: right-click any PDF in Explorer (Scribbles never takes over the default)
- Light, dark or system theme
- Settings for theme, default pen, autosave interval, export defaults and update channel (stable/beta), persisted with window size and position

## Building from source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Optional: a Windows handwriting language pack (for recognition), and PowerPoint or LibreOffice (for slide import).

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

Official releases are cut by pushing a `v*` tag: CI tests, publishes, packs with Velopack and uploads the installer, portable build and update packages to the Releases page.

## Keyboard and input

| Action | Input |
| --- | --- |
| Draw | Pen |
| Scroll / zoom | One finger / two-finger pinch, or `Ctrl` + mouse wheel |
| Open / save | `Ctrl+O` / `Ctrl+S` |
| Find in document | `Ctrl+F` (`Enter` next, `Shift+Enter` previous) |
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
                    themes, recognition, rendering and auto-update services
tests/
  Scribbles.Unit.Tests/         Fast tests on Core (incl. FsCheck property tests)
  Scribbles.Integration.Tests/  Real PDF round-trips, export, golden images
  Scribbles.E2E.Tests/          Headless + FlaUI tests driving the real app
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
- [PdfPig](https://github.com/UglyToad/PdfPig) for text extraction, page sizes and outlines
- [Velopack](https://github.com/velopack/velopack) for the installer and auto-updates
- xUnit, Shouldly, FsCheck, Verify, coverlet and [FlaUI](https://github.com/FlaUI/FlaUI) for tests

## Roadmap

- Code-signed installer (releases are currently unsigned, so SmartScreen warns on first install)
- Beta update channel builds alongside stable
- Microsoft Store listing as an alternative to Velopack distribution
