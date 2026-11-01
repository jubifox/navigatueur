namespace Navigatueur.Core.Tabs;

/// <summary>Index math for drag-and-drop reordering within one list.</summary>
public static class ListReorder
{
    /// <summary>
    /// The destination index to pass to ObservableCollection.Move (which removes
    /// the item, then inserts it) so that the item at <paramref name="from"/>
    /// lands just before — or with <paramref name="after"/>, just after — the
    /// item currently at <paramref name="target"/>. Null when nothing would move.
    /// </summary>
    public static int? MoveDestination(int from, int target, bool after, int count)
    {
        if (from < 0 || from >= count || target < 0 || target >= count || from == target)
        {
            return null;
        }

        var targetAfterRemoval = target > from ? target - 1 : target;
        var destination = targetAfterRemoval + (after ? 1 : 0);
        return destination == from ? null : destination;
    }
}
