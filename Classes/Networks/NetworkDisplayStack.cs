using System.Collections.Generic;

public sealed class NetworkDisplayStack
{
    public ItemStack DisplayStack;
    public int TotalCount;

    public readonly List<NetworkStorageEntry> StorageEntries = new List<NetworkStorageEntry>();

    public NetworkDisplayStack(ItemStack displayStack)
    {
        DisplayStack = displayStack;
        TotalCount = 0;
    }

    public bool CanMerge(NetworkStorageEntry entry)
    {
        if (entry == null || !entry.IsValid || !NetworkItemIdentity.IsValidStack(DisplayStack))
            return false;

        return NetworkItemIdentity.AreSameForNetworkStacking(DisplayStack, entry.Stack);
    }

    public void AddSource(NetworkStorageEntry entry)
    {
        if (entry == null || !entry.IsValid)
            return;

        if (!NetworkItemIdentity.IsValidStack(DisplayStack))
            DisplayStack = new ItemStack(entry.Stack.itemValue.Clone(), 1);

        StorageEntries.Add(entry);
        TotalCount += entry.Count;
    }
}
