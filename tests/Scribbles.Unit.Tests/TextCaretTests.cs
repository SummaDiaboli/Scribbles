using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>
/// TDD RED: caret offset within a line for click-to-edit.
/// Must fail before TextCaret lands.
/// </summary>
public sealed class TextCaretTests
{
    private static IReadOnlyList<PdfWord> Line() => new[]
    {
        new PdfWord("Hello", 50, 100, 40, 14),
        new PdfWord("world", 95, 100, 42, 14),
    };

    [Fact]
    public void Offset_AtLineStart_IsZero()
    {
        TextCaret.OffsetForX(Line(), 50).ShouldBe(0);
    }

    [Fact]
    public void Offset_MidFirstWord_IsMiddle()
    {
        TextCaret.OffsetForX(Line(), 70).ShouldBe(2);
    }

    [Fact]
    public void Offset_InGap_SnapsToNextWord()
    {
        TextCaret.OffsetForX(Line(), 92).ShouldBe(6);
    }

    [Fact]
    public void Offset_PastEnd_IsFullLength()
    {
        TextCaret.OffsetForX(Line(), 500).ShouldBe("Hello world".Length);
    }

    [Fact]
    public void Offset_BeforeStart_IsZero()
    {
        TextCaret.OffsetForX(Line(), 0).ShouldBe(0);
    }
}
