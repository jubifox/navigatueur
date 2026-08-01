using System.Collections.Generic;

namespace Navigatueur.Core.Settings;

/// <summary>
/// A named group explicitly saved by the user (as opposed to <see cref="SessionGroupState"/>,
/// which just carries whatever was open across a restart) so it can be
/// reopened on demand later, independent of the current session.
/// </summary>
public sealed class SavedTabGroup
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public List<string> Urls { get; set; } = new();
}
