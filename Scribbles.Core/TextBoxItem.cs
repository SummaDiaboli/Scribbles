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

    [ObservableProperty]
    private double _fontSizePt = 12;

    [ObservableProperty]
    private uint _colorArgb = 0xFF000000;
    public TextBoxKind Kind { get; set; } = TextBoxKind.Converted;

    [ObservableProperty]
    private string _text = "";

    /// <summary>Deep copy (snapshots must not alias live items).</summary>
    public TextBoxItem Clone() => new()
    {
        Id = Id,
        PageNumber = PageNumber,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        FontSizePt = FontSizePt,
        ColorArgb = ColorArgb,
        Kind = Kind,
        Text = Text,
        DisplayX = DisplayX,
        DisplayY = DisplayY,
        DisplayWidth = DisplayWidth,
        DisplayHeight = DisplayHeight,
        DisplayFontSize = DisplayFontSize,
    };

    /// <summary>Display coordinates in DIPs (set by the viewer from page factors, never persisted).</summary>
    [JsonIgnore]
    public double DisplayX { get; set; }

    [JsonIgnore]
    public double DisplayY { get; set; }

    [JsonIgnore]
    public double DisplayWidth { get; set; }

    [JsonIgnore]
    public double DisplayHeight { get; set; }

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private double _displayFontSize = 12;
}
