namespace Navigatueur.Core.Tabs;

/// <summary>What's needed to bring a closed tab back where it was.</summary>
public sealed record ClosedTabRecord(string Url, string Title, int Index, Guid? GroupId, bool IsPinned);

/// <summary>
/// Most-recently-closed-first stack behind Ctrl+Shift+T. Bounded, so a long
/// session of opening and closing tabs doesn't grow it forever.
/// </summary>
public sealed class ClosedTabHistory
{
    private readonly LinkedList<ClosedTabRecord> _records = new();

    public ClosedTabHistory(int capacity = 25)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count => _records.Count;

    public void Push(ClosedTabRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Url))
        {
            return;
        }

        _records.AddFirst(record);
        while (_records.Count > Capacity)
        {
            _records.RemoveLast();
        }
    }

    public bool TryPop(out ClosedTabRecord? record)
    {
        if (_records.First is null)
        {
            record = null;
            return false;
        }

        record = _records.First.Value;
        _records.RemoveFirst();
        return true;
    }

    public void Clear() => _records.Clear();
}
