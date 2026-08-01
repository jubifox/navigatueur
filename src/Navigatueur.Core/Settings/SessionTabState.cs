namespace Navigatueur.Core.Settings;

/// <summary>
/// Persisted snapshot of one open tab, saved on shutdown and reopened on the
/// next launch. Only the last known address is kept — restored tabs start
/// suspended (except the previously active one), so reopening many tabs
/// doesn't spin up many WebView2 processes at once.
/// </summary>
public sealed class SessionTabState
{
    public string Url { get; set; } = string.Empty;

    public Guid? GroupId { get; set; }

    public bool IsPinned { get; set; }
}
