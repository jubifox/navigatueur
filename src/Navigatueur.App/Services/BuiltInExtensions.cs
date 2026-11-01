using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Navigatueur.Core.Extensions;

namespace Navigatueur.App.Services;

/// <summary>
/// The disk side of the extensions that are part of Navigatueur's core (see
/// BuiltInExtensionCatalog): where their packages come from, how they're
/// unpacked, and how an installed copy is brought up to date.
///
/// Each one lives in %LocalAppData%\Navigatueur\Extensions\{Folder} — the
/// folders earlier versions already installed the recommended extensions to.
/// The engine identifies an unpacked extension by its folder path, so keeping
/// the same folder (and the same manifest sub-folder inside it) keeps the same
/// extension: its settings and data survive installs and updates.
/// </summary>
public static class BuiltInExtensions
{
    public static readonly string ExtensionsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "Extensions");

    /// <summary>Packages shipped with the app by the release build (absent in development builds).</summary>
    public static readonly string BundledPackagesFolder = Path.Combine(AppContext.BaseDirectory, "Extensions");

    private const long MaxWebStorePackageSize = 64L * 1024 * 1024;

    private static readonly string CatalogPath =Path.Combine(AppContext.BaseDirectory, "Resources", "Extensions", "builtin-extensions.json");

    private static readonly Lazy<BuiltInExtensionCatalog> LazyCatalog = new(() =>
    {
        try
        {
            return BuiltInExtensionCatalog.ParseOrEmpty(File.Exists(CatalogPath) ? File.ReadAllText(CatalogPath) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BuiltInExtensionCatalog.Empty;
        }
    });

    public static BuiltInExtensionCatalog Catalog => LazyCatalog.Value;

    public static string FolderOf(BuiltInExtension extension) => Path.Combine(ExtensionsDirectory, extension.Folder);

    /// <summary>
    /// Startup pass, run before the web engine starts (it loads extension files
    /// when it starts): replaces Navigatueur's own copies that are older than
    /// the packages shipped with this version. Never touches anything outside
    /// <see cref="ExtensionsDirectory"/>, never downloads, and skips any folder
    /// still in use or whose layout would change the extension's identity.
    /// </summary>
    public static void UpgradeInstalledCopies()
    {
        if (Catalog.IsEmpty || !Directory.Exists(ExtensionsDirectory))
        {
            return;
        }

        RemoveLeftovers();

        foreach (var extension in Catalog.Extensions)
        {
            try
            {
                var folder = FolderOf(extension);
                if (FindManifestDirectory(folder) is not { } manifestDirectory ||
                    !extension.IsNewerThan(ReadManifestVersion(manifestDirectory)) ||
                    BundledPackage(extension) is not { } package)
                {
                    continue;
                }

                var keepLayout = Path.GetRelativePath(folder, manifestDirectory);
                ReplaceFolder(extension, package, keepLayout);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                CrashLog.Write($"Mise à jour de l'extension intégrée « {extension.Name} »", ex);
            }
        }
    }

    /// <summary>
    /// Unpacks <paramref name="extension"/> into its folder (replacing any copy
    /// already there) from the package shipped with the app, or downloaded when
    /// there's none — from the Chrome Web Store for an extension Navigatueur
    /// may not redistribute — and returns the folder holding its manifest.json.
    /// Throws if no genuine package can be obtained (checksum or signature).
    /// </summary>
    public static async Task<string> InstallAsync(BuiltInExtension extension, string? engineVersion)
    {
        var package = BundledPackage(extension);
        string? downloaded = null;
        try
        {
            if (package is null)
            {
                downloaded = extension.IsFromWebStore
                    ? await DownloadFromWebStoreAsync(extension, engineVersion)
                    : await DownloadAsync(extension);
                package = downloaded;
            }

            return await Task.Run(() => ReplaceFolder(extension, package, keepLayout: null))
                ?? throw new InvalidDataException("le paquet ne contient pas de manifest.json");
        }
        finally
        {
            if (downloaded is not null)
            {
                TryDelete(downloaded);
            }
        }
    }

    /// <summary>The folder holding manifest.json: the root, or one level down (some archives wrap everything in a folder).</summary>
    public static string? FindManifestDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        if (File.Exists(Path.Combine(root, "manifest.json")))
        {
            return root;
        }

        return Directory.GetDirectories(root).FirstOrDefault(dir => File.Exists(Path.Combine(dir, "manifest.json")));
    }

    public static string? ReadManifestVersion(string manifestDirectory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(manifestDirectory, "manifest.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return document.RootElement.TryGetProperty("version", out var version) ? version.GetString() : null;
    }

    // --------------------------------------------------------------- inner

    private static string? BundledPackage(BuiltInExtension extension)
    {
        if (extension.IsFromWebStore)
        {
            return null;
        }

        var path = Path.Combine(BundledPackagesFolder, extension.Package);
        return File.Exists(path) && HasExpectedHash(path, extension) ? path : null;
    }

    private static bool HasExpectedHash(string path, BuiltInExtension extension)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(extension.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> DownloadAsync(BuiltInExtension extension)
    {
        var target = Path.Combine(Path.GetTempPath(), $"navigatueur-{extension.Id}-{Guid.NewGuid():N}{Path.GetExtension(extension.Package)}");
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Navigatueur");
            using var response = await http.GetAsync(extension.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(target);
            await response.Content.CopyToAsync(file);
        }

        if (!HasExpectedHash(target, extension))
        {
            TryDelete(target);
            throw new InvalidDataException("le paquet téléchargé ne correspond pas à l'empreinte attendue");
        }

        return target;
    }

    /// <summary>
    /// Fetches the extension from the Chrome Web Store's own update service (the
    /// one Chromium browsers install from) and keeps the zip inside only once
    /// its CRX signature proves it's that extension's, untampered.
    /// </summary>
    private static async Task<string> DownloadFromWebStoreAsync(BuiltInExtension extension, string? engineVersion)
    {
        // The store serves the newest version compatible with the engine it's told about.
        var version = engineVersion?.Split(' ')[0];
        var prodVersion = System.Version.TryParse(version, out var parsed) ? parsed.ToString() : "130.0.0.0";
        var url = "https://clients2.google.com/service/update2/crx?response=redirect&acceptformat=crx3" +
                  $"&prodversion={prodVersion}&x=id%3D{extension.WebStoreId}%26uc";

        byte[] crx;
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90), MaxResponseContentBufferSize = MaxWebStorePackageSize })
        {
            using var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            crx = await response.Content.ReadAsByteArrayAsync();
        }

        var archive = CrxPackage.ExtractVerifiedArchive(crx, extension.WebStoreId);
        var target = Path.Combine(Path.GetTempPath(), $"navigatueur-{extension.Id}-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(target, archive);
        return target;
    }

    /// <summary>
    /// Unpacks <paramref name="package"/> next to the extension's folder, then
    /// swaps it in. With <paramref name="keepLayout"/>, gives up unless the new
    /// manifest.json sits at that same relative place (else the engine would
    /// see a different extension). Returns the new manifest folder, or null
    /// if nothing was replaced.
    /// </summary>
    private static string? ReplaceFolder(BuiltInExtension extension, string package, string? keepLayout)
    {
        var folder = FolderOf(extension);
        var staging = $"{folder}.new-{Guid.NewGuid():N}";
        var backup = $"{folder}.old-{Guid.NewGuid():N}";

        Directory.CreateDirectory(ExtensionsDirectory);
        try
        {
            // A .crx is a header in front of the zip. Extraction rejects entries
            // that would land outside the target folder ("zip slip").
            using (var archive = new ZipArchive(new MemoryStream(CrxPackage.ArchiveOf(File.ReadAllBytes(package)).ToArray()), ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(staging);
            }

            if (FindManifestDirectory(staging) is not { } stagedManifest ||
                (keepLayout is not null && !string.Equals(Path.GetRelativePath(staging, stagedManifest), keepLayout, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var hadOldCopy = Directory.Exists(folder);
            if (hadOldCopy)
            {
                // Fails while the engine has the folder open: then the old copy simply stays.
                Directory.Move(folder, backup);
            }

            try
            {
                Directory.Move(staging, folder);
            }
            catch
            {
                if (hadOldCopy)
                {
                    Directory.Move(backup, folder);
                }

                throw;
            }

            return Path.Combine(folder, Path.GetRelativePath(staging, stagedManifest));
        }
        catch (IOException) when (keepLayout is not null)
        {
            return null; // in use: try again at the next start
        }
        finally
        {
            TryDeleteDirectory(staging);
            TryDeleteDirectory(backup);
        }
    }

    /// <summary>Staging/backup folders left behind if Navigatueur was closed in the middle of a swap.</summary>
    private static void RemoveLeftovers()
    {
        foreach (var extension in Catalog.Extensions)
        {
            foreach (var pattern in new[] { $"{extension.Folder}.new-*", $"{extension.Folder}.old-*" })
            {
                foreach (var leftover in Directory.EnumerateDirectories(ExtensionsDirectory, pattern))
                {
                    TryDeleteDirectory(leftover);
                }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
