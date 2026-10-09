using Scribbles.App.Services;
using Shouldly;

namespace Scribbles.E2E.Tests;

/// <summary>Open-With registration shapes: present in Open With, never default.</summary>
public sealed class FileAssociationEntriesTests
{
    [Fact]
    public void Build_RegistersProgId_WithQuotedCommand()
    {
        var entries = FileAssociationEntries.Build(@"C:\Program Files\Scribbles\current\Scribbles.App.exe");
        entries[@"Software\Classes\Scribbles.PDF\shell\open\command"]
            .ShouldBe(("", "\"C:\\Program Files\\Scribbles\\current\\Scribbles.App.exe\" \"%1\""));
    }

    [Fact]
    public void Build_ListsProgId_InOpenWithProgids_NotAsDefault()
    {
        var entries = FileAssociationEntries.Build(@"C:\app\Scribbles.App.exe");
        entries[@"Software\Classes\.pdf\OpenWithProgids"]
            .ShouldBe(("Scribbles.PDF", ""));
        entries.Keys.ShouldNotContain(k => k.Contains("UserChoice"),
            "registration must never take over the default handler");
    }

    [Fact]
    public void Build_ApplicationsKey_MatchesProgIdCommand()
    {
        var entries = FileAssociationEntries.Build(@"C:\app\Scribbles.App.exe");
        entries[@"Software\Classes\Applications\Scribbles.App.exe\shell\open\command"]
            .ShouldBe(entries[@"Software\Classes\Scribbles.PDF\shell\open\command"]);
        entries[@"Software\Classes\Applications\Scribbles.App.exe\SupportedTypes"]
            .ShouldBe((".pdf", ""));
    }
}
