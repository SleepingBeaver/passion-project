using System;

public static class ItemIdentity
{
    public static bool Matches(ItemData first, ItemData second)
    {
        if (ReferenceEquals(first, second))
            return first != null;

        if (first == null || second == null)
            return false;

        return Matches(first.itemId, second.itemId);
    }

    public static bool Matches(ItemData item, string itemId)
    {
        return item != null && Matches(item.itemId, itemId);
    }

    public static bool Matches(string firstId, string secondId)
    {
        return !string.IsNullOrWhiteSpace(firstId) &&
               !string.IsNullOrWhiteSpace(secondId) &&
               string.Equals(firstId.Trim(), secondId.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
