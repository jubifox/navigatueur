using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Navigatueur.Core.Bookmarks;

namespace Navigatueur.App.Services;

/// <summary>
/// The user's favorites, kept in their own file next to the history rather
/// than in settings.json — they're user data that should survive a settings
/// reset, and they'd otherwise be rewritten on every session autosave.
/// Newest first.
/// </summary>
public sealed class BookmarkService
{
    private static readonly string BookmarksFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "bookmarks.json");

    public ObservableCollection<Bookmark> Bookmarks { get; } = new();

    public BookmarkService()
    {
        Load();
    }

    /// <summary>Raised after any add/remove, for star buttons and the new tab page to refresh.</summary>
    public event EventHandler? Changed;

    public bool IsBookmarked(string? url) =>
        !string.IsNullOrEmpty(url) && BookmarkRules.IndexOf(Bookmarks, url) >= 0;

    /// <summary>Adds the page, or removes it if already there. Returns true when it ends up bookmarked.</summary>
    public bool Toggle(string url, string title)
    {
        if (!BookmarkRules.CanBookmark(url))
        {
            return false;
        }

        var index = BookmarkRules.IndexOf(Bookmarks, url);
        if (index >= 0)
        {
            Bookmarks.RemoveAt(index);
            Save();
            return false;
        }

        Bookmarks.Insert(0, new Bookmark
        {
            Url = url,
            Title = string.IsNullOrWhiteSpace(title) ? url : title.Trim(),
            AddedAt = DateTimeOffset.Now,
        });
        Save();
        return true;
    }

    public void Remove(string url)
    {
        var index = BookmarkRules.IndexOf(Bookmarks, url);
        if (index < 0)
        {
            return;
        }

        Bookmarks.RemoveAt(index);
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(BookmarksFilePath))
            {
                return;
            }

            var items = JsonSerializer.Deserialize<List<Bookmark>>(File.ReadAllText(BookmarksFilePath));
            foreach (var item in items ?? new List<Bookmark>())
            {
                if (!string.IsNullOrWhiteSpace(item.Url))
                {
                    Bookmarks.Add(item);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable file: start empty rather than crash. It's left in place, not overwritten, until the next change.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BookmarksFilePath)!);
            File.WriteAllText(BookmarksFilePath, JsonSerializer.Serialize(Bookmarks, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: the change still applies for this session.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
