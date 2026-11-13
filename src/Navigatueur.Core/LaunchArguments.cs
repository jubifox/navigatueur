namespace Navigatueur.Core;

/// <summary>
/// What Windows hands Navigatueur when it is the default browser (or chosen
/// in "Ouvrir avec") — a link ("https://…") or a file ("C:\…\page.html") —
/// turned into the addresses to open. Anything else on the command line is
/// ignored: arguments are never searched for, so a stray word can't turn
/// into a search request.
/// </summary>
public static class LaunchArguments
{
    public const int MaxUrls = 20;

    private static readonly string[] WebSchemes = { "http", "https", "file", "ftp" };

    public static IReadOnlyList<string> ParseUrls(IEnumerable<string>? args, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var urls = new List<string>();
        if (args is null)
        {
            return urls;
        }

        foreach (var raw in args)
        {
            var arg = raw?.Trim().Trim('"');
            if (string.IsNullOrEmpty(arg) || arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (ToUrl(arg, fileExists) is { } url)
            {
                urls.Add(url);
                if (urls.Count == MaxUrls)
                {
                    break;
                }
            }
        }

        return urls;
    }

    private static string? ToUrl(string arg, Func<string, bool> fileExists)
    {
        if (InternalPages.FromDisplayUrl(arg) is { } internalUrl)
        {
            return internalUrl;
        }

        // A full Windows path ("C:\…", "\\server\…"): a file to show.
        if (Path.IsPathFullyQualified(arg))
        {
            return fileExists(arg) ? new Uri(arg).AbsoluteUri : null;
        }

        if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) &&
            WebSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            return arg;
        }

        return null;
    }
}
