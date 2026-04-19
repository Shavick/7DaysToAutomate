public sealed class NetworkStorageEntry
{
    public Vector3i StoragePos;
    public int SlotIndex;
    public int StoragePriority;
    public int StorageListIndex;
    public ItemStack Stack;

    public NetworkStorageEntry(
        Vector3i storagePos,
        int slotIndex,
        int storagePriority,
        int storageListIndex,
        ItemStack stack)
    {
        StoragePos = storagePos;
        SlotIndex = slotIndex;
        StoragePriority = storagePriority;
        StorageListIndex = storageListIndex;
        Stack = stack;
    }

    public int Count
    {
        get { return NetworkItemIdentity.IsValidStack(Stack) ? Stack.count : 0; }
    }

    public bool IsValid
    {
        get { return NetworkItemIdentity.IsValidStack(Stack); }
    }
}
