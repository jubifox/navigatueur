using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Navigatueur.App.Services;

public sealed record HistoryEntry(string Url, string Title, DateTimeOffset VisitedAt);

/// <summary>Simple local browsing history — never recorded for private-browsing tabs.</summary>
public partial class HistoryService : ObservableObject
{
    private const int MaxEntries = 2000;

    private static readonly string HistoryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "history.json");

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public HistoryService()
    {
        Load();
    }

    public void Record(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("navigatueur.", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Entries.Insert(0, new HistoryEntry(url, string.IsNullOrWhiteSpace(title) ? url : title, DateTimeOffset.Now));
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        Save();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(HistoryFilePath))
            {
                return;
            }

            var json = File.ReadAllText(HistoryFilePath);
            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            if (entries is null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupted or unreadable history file — start fresh rather than crash.
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(HistoryFilePath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(HistoryFilePath, JsonSerializer.Serialize(Entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: history just won't persist this time.
        }
    }
}
