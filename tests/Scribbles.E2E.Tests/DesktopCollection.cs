namespace Scribbles.E2E.Tests;

/// <summary>
/// Tests that drive the real desktop (launch the app, raise native dialogs,
/// move the mouse) share one serial collection: run in parallel they kill each
/// other's app instances (KillStale) and fight over focus.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopCollection
{
    public const string Name = "Desktop";
}
