namespace WestMarch.Web.Services;

/// <summary>
/// Extension point for future DM-screen tools (initiative tracker, monster HP/condition
/// tracking, dice log, …). Register an implementation in DI and it appears as an extra
/// tab on every session's DM screen — no changes to the screen itself.
///
///     builder.Services.AddSingleton&lt;IDmScreenTool, InitiativeTrackerTool&gt;();
///
/// The component receives the session id as a parameter named "SessionId".
/// No tools ship in this phase; the seam is deliberately in place.
/// </summary>
public interface IDmScreenTool
{
    /// <summary>Tab label, e.g. "Initiative".</summary>
    string Name { get; }

    /// <summary>The Blazor component rendered inside the tab (via DynamicComponent).</summary>
    Type ComponentType { get; }
}
