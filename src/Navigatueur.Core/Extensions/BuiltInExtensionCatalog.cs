using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Navigatueur.Core.Extensions;

/// <summary>One extension shipped as part of Navigatueur itself.</summary>
public sealed class BuiltInExtension
{
    /// <summary>Stable identifier (lower-case, dashes) — never shown.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The extension version the package contains (manifest.json "version").</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>File name the package is shipped under next to the app (Extensions\): a .zip, or a .crx when that's what upstream publishes.</summary>
    public string Package { get; set; } = string.Empty;

    /// <summary>The package as its authors published it (an https URL), which the release build — or a development build lacking it — downloads.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>SHA-256 of the package, lower-case hex: nothing is installed unless it matches.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Folder (a plain name) under %LocalAppData%\Navigatueur\Extensions the extension is unpacked to and loaded from.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Fragments of the name the engine reports for this extension (localized names vary), used to recognize a copy that's already installed.</summary>
    public List<string> Matches { get; set; } = new();

    /// <summary>Where its source code and license live (the package is redistributed unmodified).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Set instead of Package/Url/Sha256/Version for an extension whose license
    /// doesn't allow Navigatueur to redistribute it (BetterTTV): it's then
    /// installed on the user's machine straight from the Chrome Web Store, and
    /// trusted through its CRX signature (see CrxPackage), not a checksum.
    /// </summary>
    public string WebStoreId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsFromWebStore => !string.IsNullOrEmpty(WebStoreId);

    /// <summary>True when an engine-reported extension name is this extension.</summary>
    public bool Recognizes(string? engineName) =>
        !string.IsNullOrWhiteSpace(engineName) &&
        Matches.Any(fragment => engineName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the packaged version is newer than <paramref name="installedVersion"/> (an unreadable version never counts as older).</summary>
    public bool IsNewerThan(string? installedVersion) =>
        System.Version.TryParse(Version, out var packaged) &&
        System.Version.TryParse(installedVersion, out var installed) &&
        packaged > installed;
}

/// <summary>
/// The extensions that are part of Navigatueur's core: shipped with the app,
/// installed automatically, kept at the version Navigatueur was released with.
/// Read from Resources\Extensions\builtin-extensions.json by the app and by
/// packaging\windows\build-release.ps1, so both always agree.
/// </summary>
public sealed class BuiltInExtensionCatalog
{
    private static readonly Regex HexSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex PlainName = new(@"^[A-Za-z0-9][A-Za-z0-9 ._-]*$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static BuiltInExtensionCatalog Empty { get; } = new();

    public List<BuiltInExtension> Extensions { get; set; } = new();

    [JsonIgnore]
    public bool IsEmpty => Extensions.Count == 0;

    public static BuiltInExtensionCatalog Parse(string json) =>
        JsonSerializer.Deserialize<BuiltInExtensionCatalog>(json, JsonOptions) ?? Empty;

    /// <summary>The valid catalog, or <see cref="Empty"/> when anything is wrong — a broken catalog installs nothing rather than something unexpected.</summary>
    public static BuiltInExtensionCatalog ParseOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            var catalog = Parse(json);
            return catalog.Validate().Count == 0 ? catalog : Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public BuiltInExtension? FindByEngineName(string? engineName) => Extensions.FirstOrDefault(e => e.Recognizes(engineName));

    /// <summary>Everything wrong with the catalog, in plain words; empty when it's usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var extension in Extensions)
        {
            var label = string.IsNullOrWhiteSpace(extension.Id) ? "(no id)" : extension.Id;
            if (!Regex.IsMatch(extension.Id, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            {
                problems.Add($"{label}: id must be lower-case words separated by dashes");
            }

            if (string.IsNullOrWhiteSpace(extension.Name))
            {
                problems.Add($"{label}: missing name");
            }

            if (extension.IsFromWebStore)
            {
                if (!Regex.IsMatch(extension.WebStoreId, "^[a-p]{32}$"))
                {
                    problems.Add($"{label}: webStoreId must be a 32-letter Chrome extension id (a to p)");
                }

                if (extension.Package.Length > 0 || extension.Url.Length > 0 || extension.Sha256.Length > 0 || extension.Version.Length > 0)
                {
                    problems.Add($"{label}: a Web Store extension has no package, url, sha256 or version (it isn't redistributed)");
                }
            }
            else
            {
                if (!System.Version.TryParse(extension.Version, out _))
                {
                    problems.Add($"{label}: version '{extension.Version}' isn't a dotted number");
                }

                if (!PlainName.IsMatch(extension.Package) || extension.Package.Contains("..") ||
                    !(extension.Package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || extension.Package.EndsWith(".crx", StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{label}: package must be a plain .zip or .crx file name");
                }

                if (!Uri.TryCreate(extension.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                {
                    problems.Add($"{label}: url must be an https URL");
                }

                if (!HexSha256.IsMatch(extension.Sha256))
                {
                    problems.Add($"{label}: sha256 must be 64 lower-case hex digits");
                }
            }

            if (!PlainName.IsMatch(extension.Folder) || extension.Folder.Contains("..") || extension.Folder.EndsWith('.') || extension.Folder.EndsWith(' '))
            {
                problems.Add($"{label}: folder must be a plain folder name");
            }

            if (extension.Matches.Count == 0 || extension.Matches.Any(string.IsNullOrWhiteSpace))
            {
                problems.Add($"{label}: needs at least one name fragment in matches");
            }
        }

        AddDuplicates(problems, Extensions.Select(e => e.Id), "id");
        AddDuplicates(problems, Extensions.Select(e => e.Folder), "folder");
        AddDuplicates(problems, Extensions.Where(e => !e.IsFromWebStore).Select(e => e.Package), "package");
        return problems;
    }

    private static void AddDuplicates(List<string> problems, IEnumerable<string> values, string what)
    {
        foreach (var duplicate in values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"duplicate {what} '{duplicate.Key}'");
        }
    }
}
