using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Scribbles.Core;

/// <summary>Where an editable text box came from.</summary>
public enum TextBoxKind
{
    /// <summary>Converted from handwriting via recognition.</summary>
    Converted,
    /// <summary>User-edited native PDF text (covers the original on export).</summary>
    EditedNative,
}

/// <summary>
/// Editable text box on a page (points, y-DOWN top-left). Mutable Text so
/// WPF two-way binding works; JSON-serializable.
/// </summary>
public sealed partial class TextBoxItem : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int PageNumber { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double FontSizePt { get; set; } = 12;
    public uint ColorArgb { get; set; } = 0xFF000000;
    public TextBoxKind Kind { get; set; } = TextBoxKind.Converted;

    [ObservableProperty]
    private string _text = "";

    /// <summary>Display coordinates in DIPs (set by the viewer from page factors, never persisted).</summary>
    [JsonIgnore]
    public double DisplayX { get; set; }

    [JsonIgnore]
    public double DisplayY { get; set; }

    [JsonIgnore]
    public double DisplayWidth { get; set; }

    [JsonIgnore]
    public double DisplayHeight { get; set; }

    [JsonIgnore]
    public double DisplayFontSize { get; set; } = 12;
}
