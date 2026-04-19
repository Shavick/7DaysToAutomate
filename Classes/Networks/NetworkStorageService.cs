using System;
using System.Collections.Generic;
using System.Threading;

public static class NetworkStorageService
{
    private const int TileEntitySnapshotMaxAttempts = 4;
    private const ulong RuntimeCacheMaxAgeTicks = 20UL;

    private static readonly Dictionary<Guid, int> revisionByNetworkId = new Dictionary<Guid, int>();
    private static readonly Dictionary<Guid, NetworkStorageSnapshot> cachedSnapshotByNetworkId = new Dictionary<Guid, NetworkStorageSnapshot>();
    private static readonly Dictionary<Guid, NetworkRuntimeCache> runtimeCacheByNetworkId = new Dictionary<Guid, NetworkRuntimeCache>();

    private sealed class MutableContainerState
    {
        public NetworkStorageContainerRef Ref;
        public TEFeatureStorage Storage;
        public ItemStack[] Slots;
        public bool IsModified;
        public int RuntimeIndex;
    }

    private sealed class WithdrawCandidate
    {
        public MutableContainerState Container;
        public int SlotIndex;
        public int Count;
    }

    private sealed class SlotRef
    {
        public MutableContainerState Container;
        public int SlotIndex;
    }

    private sealed class NetworkRuntimeCache
    {
        public Guid NetworkId;
        public ulong BuiltAtWorldTime;
        public readonly List<MutableContainerState> Containers = new List<MutableContainerState>();
        public readonly Dictionary<string, List<SlotRef>> SlotRefsByItemName = new Dictionary<string, List<SlotRef>>(StringComparer.Ordinal);
        public readonly HashSet<int> ContainersWithEmptySlots = new HashSet<int>();
    }

    public static bool TryBuildStorageSnapshot(WorldBase world, Guid networkId, out NetworkStorageSnapshot snapshot)
    {
        snapshot = null;

        if (world == null || world.IsRemote() || networkId == Guid.Empty)
            return false;

        NetworkRuntimeCache runtimeCache = GetOrBuildRuntimeCache(world, networkId, false);
        if (runtimeCache == null)
            return false;

        List<NetworkStorageEntry> entries = BuildPhysicalEntriesFromRuntime(runtimeCache);
        int revision = NextRevision(networkId);

        snapshot = NetworkStorageSnapshot.Build(networkId, revision, world.GetWorldTime(), entries);
        cachedSnapshotByNetworkId[networkId] = snapshot;
        return true;
    }

    public static bool TryGetCachedSnapshot(Guid networkId, out NetworkStorageSnapshot snapshot)
    {
        snapshot = null;
        if (networkId == Guid.Empty)
            return false;

        return cachedSnapshotByNetworkId.TryGetValue(networkId, out snapshot) && snapshot != null;
    }

    public static void InvalidateNetwork(Guid networkId)
    {
        if (networkId == Guid.Empty)
            return;

        cachedSnapshotByNetworkId.Remove(networkId);
        runtimeCacheByNetworkId.Remove(networkId);
    }

    public static void InvalidateAll()
    {
        cachedSnapshotByNetworkId.Clear();
        revisionByNetworkId.Clear();
        runtimeCacheByNetworkId.Clear();
    }

    public static bool TryWithdraw(WorldBase world, Guid networkId, ItemValue requestedItemValue, int requestedCount, out int withdrawnCount)
    {
        withdrawnCount = 0;

        if (world == null || world.IsRemote() || networkId == Guid.Empty || requestedItemValue == null || requestedCount <= 0)
            return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            bool forceRefresh = attempt > 0;
            NetworkRuntimeCache runtimeCache = GetOrBuildRuntimeCache(world, networkId, forceRefresh);
            if (runtimeCache == null)
                return false;

            int moved = TryWithdrawFromRuntime(runtimeCache, requestedItemValue, requestedCount);
            if (moved <= 0)
                continue;

            withdrawnCount = moved;
            PersistModifiedContainers(runtimeCache.Containers);
            cachedSnapshotByNetworkId.Remove(networkId);
            TryBuildStorageSnapshot(world, networkId, out _);
            return true;
        }

        return false;
    }

    public static bool TryDeposit(WorldBase world, Guid networkId, ItemStack toDeposit, out int depositedCount)
    {
        depositedCount = 0;

        if (world == null || world.IsRemote() || networkId == Guid.Empty || !NetworkItemIdentity.IsValidStack(toDeposit))
            return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            bool forceRefresh = attempt > 0;
            NetworkRuntimeCache runtimeCache = GetOrBuildRuntimeCache(world, networkId, forceRefresh);
            if (runtimeCache == null)
                return false;

            int moved = TryDepositToRuntime(runtimeCache, toDeposit);
            if (moved <= 0)
                continue;

            depositedCount = moved;
            PersistModifiedContainers(runtimeCache.Containers);
            cachedSnapshotByNetworkId.Remove(networkId);
            TryBuildStorageSnapshot(world, networkId, out _);
            return true;
        }

        return false;
    }

    public static bool TryResolveItemValueByDisplayKey(WorldBase world, Guid networkId, string displayKey, out ItemValue itemValue)
    {
        itemValue = null;

        if (world == null || world.IsRemote() || networkId == Guid.Empty || string.IsNullOrEmpty(displayKey))
            return false;

        if (!TryBuildStorageSnapshot(world, networkId, out NetworkStorageSnapshot snapshot) || snapshot == null)
            return false;

        for (int i = 0; i < snapshot.DisplayStacks.Count; i++)
        {
            NetworkDisplayStack displayStack = snapshot.DisplayStacks[i];
            if (displayStack == null || !NetworkItemIdentity.IsValidStack(displayStack.DisplayStack))
                continue;

            string key = NetworkItemIdentity.GetDisplayKey(displayStack.DisplayStack);
            if (!string.Equals(key, displayKey, StringComparison.Ordinal))
                continue;

            itemValue = displayStack.DisplayStack.itemValue.Clone();
            return itemValue != null && itemValue.ItemClass != null;
        }

        return false;
    }

    private static int TryWithdrawFromRuntime(NetworkRuntimeCache runtimeCache, ItemValue requestedItemValue, int requestedCount)
    {
        if (runtimeCache == null || requestedItemValue == null || requestedItemValue.ItemClass == null || requestedCount <= 0)
            return 0;

        string itemName = requestedItemValue.ItemClass.GetItemName() ?? string.Empty;
        if (string.IsNullOrEmpty(itemName))
            return 0;

        if (!runtimeCache.SlotRefsByItemName.TryGetValue(itemName, out List<SlotRef> slotRefs) || slotRefs == null || slotRefs.Count == 0)
            return 0;

        List<WithdrawCandidate> candidates = BuildWithdrawCandidates(runtimeCache, slotRefs, requestedItemValue);
        if (candidates.Count == 0)
            return 0;

        candidates.Sort(CompareWithdrawCandidates);

        int remaining = requestedCount;
        int withdrawnCount = 0;

        for (int i = 0; i < candidates.Count && remaining > 0; i++)
        {
            WithdrawCandidate candidate = candidates[i];
            if (candidate == null || candidate.Container == null)
                continue;

            MutableContainerState container = candidate.Container;
            ItemStack oldStack = container.Slots[candidate.SlotIndex];

            if (!NetworkItemIdentity.IsValidStack(oldStack))
                continue;

            if (!NetworkItemIdentity.AreSameForNetworkStacking(oldStack.itemValue, requestedItemValue))
                continue;

            int remove = Math.Min(oldStack.count, remaining);
            if (remove <= 0)
                continue;

            ItemStack newStack = oldStack;
            newStack.count -= remove;

            if (newStack.count <= 0)
                container.Slots[candidate.SlotIndex] = ItemStack.Empty;
            else
                container.Slots[candidate.SlotIndex] = newStack;

            container.IsModified = true;
            withdrawnCount += remove;
            remaining -= remove;

            UpdateRuntimeCacheAfterSlotMutation(runtimeCache, container, candidate.SlotIndex, oldStack, container.Slots[candidate.SlotIndex]);
        }

        return withdrawnCount;
    }

    private static int TryDepositToRuntime(NetworkRuntimeCache runtimeCache, ItemStack toDeposit)
    {
        if (runtimeCache == null || !NetworkItemIdentity.IsValidStack(toDeposit))
            return 0;

        ItemValue itemValue = toDeposit.itemValue.Clone();
        string itemName = itemValue.ItemClass.GetItemName() ?? string.Empty;
        if (string.IsNullOrEmpty(itemName))
            return 0;

        int remaining = toDeposit.count;
        int depositedCount = 0;

        HashSet<int> matchingContainers = GetContainersContainingExactItem(runtimeCache, itemName, itemValue);

        for (int i = 0; i < runtimeCache.Containers.Count && remaining > 0; i++)
        {
            MutableContainerState container = runtimeCache.Containers[i];
            if (container == null)
                continue;

            if (!matchingContainers.Contains(container.RuntimeIndex))
                continue;

            int moved = DepositIntoContainer(runtimeCache, container, itemValue, remaining, true);
            if (moved <= 0)
                continue;

            remaining -= moved;
            depositedCount += moved;
        }

        for (int i = 0; i < runtimeCache.Containers.Count && remaining > 0; i++)
        {
            MutableContainerState container = runtimeCache.Containers[i];
            if (container == null)
                continue;

            if (matchingContainers.Contains(container.RuntimeIndex))
                continue;

            if (!runtimeCache.ContainersWithEmptySlots.Contains(container.RuntimeIndex))
                continue;

            int moved = DepositIntoContainer(runtimeCache, container, itemValue, remaining, true);
            if (moved <= 0)
                continue;

            remaining -= moved;
            depositedCount += moved;
        }

        return depositedCount;
    }

    private static HashSet<int> GetContainersContainingExactItem(NetworkRuntimeCache runtimeCache, string itemName, ItemValue itemValue)
    {
        var result = new HashSet<int>();

        if (runtimeCache == null || string.IsNullOrEmpty(itemName) || itemValue == null)
            return result;

        if (!runtimeCache.SlotRefsByItemName.TryGetValue(itemName, out List<SlotRef> refs) || refs == null)
            return result;

        for (int i = 0; i < refs.Count; i++)
        {
            SlotRef slotRef = refs[i];
            if (slotRef == null || slotRef.Container == null)
                continue;

            if (slotRef.SlotIndex < 0 || slotRef.SlotIndex >= slotRef.Container.Slots.Length)
                continue;

            ItemStack stack = slotRef.Container.Slots[slotRef.SlotIndex];
            if (!NetworkItemIdentity.IsValidStack(stack))
                continue;

            if (!NetworkItemIdentity.AreSameForNetworkStacking(stack.itemValue, itemValue))
                continue;

            result.Add(slotRef.Container.RuntimeIndex);
        }

        return result;
    }

    private static List<WithdrawCandidate> BuildWithdrawCandidates(NetworkRuntimeCache runtimeCache, List<SlotRef> slotRefs, ItemValue requestedItemValue)
    {
        var candidates = new List<WithdrawCandidate>();

        if (runtimeCache == null || slotRefs == null || requestedItemValue == null)
            return candidates;

        for (int i = 0; i < slotRefs.Count; i++)
        {
            SlotRef slotRef = slotRefs[i];
            if (slotRef == null || slotRef.Container == null)
                continue;

            if (slotRef.SlotIndex < 0 || slotRef.SlotIndex >= slotRef.Container.Slots.Length)
                continue;

            ItemStack stack = slotRef.Container.Slots[slotRef.SlotIndex];
            if (!NetworkItemIdentity.IsValidStack(stack))
                continue;

            if (!NetworkItemIdentity.AreSameForNetworkStacking(stack.itemValue, requestedItemValue))
                continue;

            candidates.Add(new WithdrawCandidate
            {
                Container = slotRef.Container,
                SlotIndex = slotRef.SlotIndex,
                Count = stack.count
            });
        }

        return candidates;
    }

    private static int CompareWithdrawCandidates(WithdrawCandidate a, WithdrawCandidate b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        int priorityCmp = b.Container.Ref.StoragePriority.CompareTo(a.Container.Ref.StoragePriority);
        if (priorityCmp != 0) return priorityCmp;

        int stackCmp = a.Count.CompareTo(b.Count);
        if (stackCmp != 0) return stackCmp;

        int listCmp = a.Container.Ref.StorageListIndex.CompareTo(b.Container.Ref.StorageListIndex);
        if (listCmp != 0) return listCmp;

        return a.SlotIndex.CompareTo(b.SlotIndex);
    }

    private static int CompareContainersForPriorityAndList(MutableContainerState a, MutableContainerState b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        int priorityCmp = b.Ref.StoragePriority.CompareTo(a.Ref.StoragePriority);
        if (priorityCmp != 0) return priorityCmp;

        return a.Ref.StorageListIndex.CompareTo(b.Ref.StorageListIndex);
    }

    private static int DepositIntoContainer(NetworkRuntimeCache runtimeCache, MutableContainerState container, ItemValue itemValue, int amount, bool allowEmptySlots)
    {
        if (runtimeCache == null || container == null || container.Slots == null || itemValue == null || amount <= 0 || itemValue.ItemClass == null)
            return 0;

        int moved = 0;
        int remaining = amount;
        int maxStack = GetMaxStack(itemValue);

        for (int i = 0; i < container.Slots.Length && remaining > 0; i++)
        {
            ItemStack oldStack = container.Slots[i];
            if (!NetworkItemIdentity.IsValidStack(oldStack))
                continue;

            if (!NetworkItemIdentity.AreSameForNetworkStacking(oldStack.itemValue, itemValue))
                continue;

            int space = maxStack - oldStack.count;
            if (space <= 0)
                continue;

            int delta = Math.Min(space, remaining);
            ItemStack newStack = oldStack;
            newStack.count += delta;
            container.Slots[i] = newStack;

            remaining -= delta;
            moved += delta;
            container.IsModified = true;

            UpdateRuntimeCacheAfterSlotMutation(runtimeCache, container, i, oldStack, newStack);
        }

        if (!allowEmptySlots || remaining <= 0)
            return moved;

        for (int i = 0; i < container.Slots.Length && remaining > 0; i++)
        {
            ItemStack oldStack = container.Slots[i];
            if (NetworkItemIdentity.IsValidStack(oldStack))
                continue;

            int delta = Math.Min(maxStack, remaining);
            ItemStack newStack = new ItemStack(itemValue.Clone(), delta);
            container.Slots[i] = newStack;

            remaining -= delta;
            moved += delta;
            container.IsModified = true;

            UpdateRuntimeCacheAfterSlotMutation(runtimeCache, container, i, oldStack, newStack);
        }

        return moved;
    }

    private static void UpdateRuntimeCacheAfterSlotMutation(NetworkRuntimeCache runtimeCache, MutableContainerState container, int slotIndex, ItemStack oldStack, ItemStack newStack)
    {
        if (runtimeCache == null || container == null || slotIndex < 0)
            return;

        string oldName = NetworkItemIdentity.GetStableDisplayName(oldStack);
        string newName = NetworkItemIdentity.GetStableDisplayName(newStack);

        if (!string.IsNullOrEmpty(oldName) && !string.Equals(oldName, newName, StringComparison.Ordinal))
            RemoveSlotRef(runtimeCache, oldName, container, slotIndex);

        if (!string.IsNullOrEmpty(newName))
            EnsureSlotRef(runtimeCache, newName, container, slotIndex);

        RecomputeContainerEmptyFlag(runtimeCache, container);
    }

    private static void EnsureSlotRef(NetworkRuntimeCache runtimeCache, string itemName, MutableContainerState container, int slotIndex)
    {
        if (runtimeCache == null || string.IsNullOrEmpty(itemName) || container == null || slotIndex < 0)
            return;

        if (!runtimeCache.SlotRefsByItemName.TryGetValue(itemName, out List<SlotRef> refs) || refs == null)
        {
            refs = new List<SlotRef>();
            runtimeCache.SlotRefsByItemName[itemName] = refs;
        }

        for (int i = 0; i < refs.Count; i++)
        {
            SlotRef sr = refs[i];
            if (sr == null)
                continue;

            if (sr.Container == container && sr.SlotIndex == slotIndex)
                return;
        }

        refs.Add(new SlotRef { Container = container, SlotIndex = slotIndex });
    }

    private static void RemoveSlotRef(NetworkRuntimeCache runtimeCache, string itemName, MutableContainerState container, int slotIndex)
    {
        if (runtimeCache == null || string.IsNullOrEmpty(itemName) || container == null || slotIndex < 0)
            return;

        if (!runtimeCache.SlotRefsByItemName.TryGetValue(itemName, out List<SlotRef> refs) || refs == null)
            return;

        for (int i = refs.Count - 1; i >= 0; i--)
        {
            SlotRef sr = refs[i];
            if (sr == null)
            {
                refs.RemoveAt(i);
                continue;
            }

            if (sr.Container == container && sr.SlotIndex == slotIndex)
                refs.RemoveAt(i);
        }

        if (refs.Count == 0)
            runtimeCache.SlotRefsByItemName.Remove(itemName);
    }

    private static void RecomputeContainerEmptyFlag(NetworkRuntimeCache runtimeCache, MutableContainerState container)
    {
        if (runtimeCache == null || container == null || container.Slots == null)
            return;

        bool hasEmpty = false;
        for (int i = 0; i < container.Slots.Length; i++)
        {
            if (!NetworkItemIdentity.IsValidStack(container.Slots[i]))
            {
                hasEmpty = true;
                break;
            }
        }

        if (hasEmpty)
            runtimeCache.ContainersWithEmptySlots.Add(container.RuntimeIndex);
        else
            runtimeCache.ContainersWithEmptySlots.Remove(container.RuntimeIndex);
    }

    private static NetworkRuntimeCache GetOrBuildRuntimeCache(WorldBase world, Guid networkId, bool forceRefresh)
    {
        if (world == null || world.IsRemote() || networkId == Guid.Empty)
            return null;

        ulong now = world.GetWorldTime();

        if (!forceRefresh &&
            runtimeCacheByNetworkId.TryGetValue(networkId, out NetworkRuntimeCache existing) &&
            existing != null &&
            now >= existing.BuiltAtWorldTime &&
            (now - existing.BuiltAtWorldTime) <= RuntimeCacheMaxAgeTicks)
        {
            return existing;
        }

        NetworkRuntimeCache rebuilt = BuildRuntimeCache(world, networkId, now);
        if (rebuilt != null)
            runtimeCacheByNetworkId[networkId] = rebuilt;

        return rebuilt;
    }

    private static NetworkRuntimeCache BuildRuntimeCache(WorldBase world, Guid networkId, ulong builtAtWorldTime)
    {
        if (world == null || world.IsRemote() || networkId == Guid.Empty)
            return null;

        var cache = new NetworkRuntimeCache
        {
            NetworkId = networkId,
            BuiltAtWorldTime = builtAtWorldTime
        };

        List<NetworkStorageContainerRef> refs = CollectStorageContainersForNetwork(world, networkId);
        if (refs == null || refs.Count == 0)
            return cache;

        for (int i = 0; i < refs.Count; i++)
        {
            NetworkStorageContainerRef containerRef = refs[i];
            if (containerRef == null)
                continue;

            if (!SafeWorldRead.TryGetTileEntity(world, 0, containerRef.StoragePos, out TileEntity te) || !(te is TileEntityComposite comp))
                continue;

            TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
            if (storage == null || storage.items == null)
                continue;

            cache.Containers.Add(new MutableContainerState
            {
                Ref = containerRef,
                Storage = storage,
                Slots = storage.items,
                IsModified = false,
                RuntimeIndex = -1
            });
        }

        cache.Containers.Sort(CompareContainersForPriorityAndList);

        for (int i = 0; i < cache.Containers.Count; i++)
        {
            MutableContainerState container = cache.Containers[i];
            container.RuntimeIndex = i;

            bool hasEmpty = false;

            for (int slot = 0; slot < container.Slots.Length; slot++)
            {
                ItemStack stack = container.Slots[slot];
                if (!NetworkItemIdentity.IsValidStack(stack))
                {
                    hasEmpty = true;
                    continue;
                }

                string itemName = NetworkItemIdentity.GetStableDisplayName(stack);
                if (string.IsNullOrEmpty(itemName))
                    continue;

                if (!cache.SlotRefsByItemName.TryGetValue(itemName, out List<SlotRef> refsByName) || refsByName == null)
                {
                    refsByName = new List<SlotRef>();
                    cache.SlotRefsByItemName[itemName] = refsByName;
                }

                refsByName.Add(new SlotRef
                {
                    Container = container,
                    SlotIndex = slot
                });
            }

            if (hasEmpty)
                cache.ContainersWithEmptySlots.Add(i);
        }

        return cache;
    }

    private static int NextRevision(Guid networkId)
    {
        if (!revisionByNetworkId.TryGetValue(networkId, out int rev))
            rev = 0;

        rev++;
        revisionByNetworkId[networkId] = rev;
        return rev;
    }

    private static List<NetworkStorageEntry> BuildPhysicalEntriesFromRuntime(NetworkRuntimeCache runtimeCache)
    {
        var results = new List<NetworkStorageEntry>();

        if (runtimeCache == null || runtimeCache.Containers.Count == 0)
            return results;

        for (int c = 0; c < runtimeCache.Containers.Count; c++)
        {
            MutableContainerState container = runtimeCache.Containers[c];
            if (container == null || container.Slots == null)
                continue;

            for (int slot = 0; slot < container.Slots.Length; slot++)
            {
                ItemStack stack = container.Slots[slot];
                if (!NetworkItemIdentity.IsValidStack(stack))
                    continue;

                results.Add(new NetworkStorageEntry(
                    container.Ref.StoragePos,
                    slot,
                    container.Ref.StoragePriority,
                    container.Ref.StorageListIndex,
                    new ItemStack(stack.itemValue.Clone(), stack.count)));
            }
        }

        return results;
    }

    private static List<NetworkStorageContainerRef> CollectStorageContainersForNetwork(WorldBase world, Guid networkId)
    {
        var results = new List<NetworkStorageContainerRef>();
        List<Guid> graphIds = CollectGraphIdsForNetwork(world, networkId);

        var seenStorage = new HashSet<Vector3i>();
        int listIndex = 0;

        for (int g = 0; g < graphIds.Count; g++)
        {
            Guid graphId = graphIds[g];
            if (!PipeGraphManager.TryGetStorageEndpoints(graphId, out List<Vector3i> endpoints) || endpoints == null)
                continue;

            for (int i = 0; i < endpoints.Count; i++)
            {
                Vector3i storagePos = endpoints[i];
                if (storagePos == Vector3i.zero)
                    continue;

                if (!seenStorage.Add(storagePos))
                    continue;

                results.Add(new NetworkStorageContainerRef(graphId, storagePos, 0, listIndex));
                listIndex++;
            }
        }

        return results;
    }

    private static List<Guid> CollectGraphIdsForNetwork(WorldBase world, Guid networkId)
    {
        var graphIds = new HashSet<Guid>();

        foreach (Chunk chunk in SafeWorldRead.GetChunkArraySnapshot(world))
        {
            if (chunk == null)
                continue;

            List<TileEntity> tileEntities = SnapshotTileEntities(chunk);
            for (int i = 0; i < tileEntities.Count; i++)
            {
                if (!(tileEntities[i] is TileEntityItemPipe pipe))
                    continue;

                if (pipe.NetworkId != networkId)
                    continue;

                if (pipe.PipeGraphId == Guid.Empty)
                    continue;

                graphIds.Add(pipe.PipeGraphId);
            }
        }

        List<Guid> ordered = new List<Guid>(graphIds);
        ordered.Sort(CompareGuidOrdinal);
        return ordered;
    }

    private static void PersistModifiedContainers(List<MutableContainerState> containers)
    {
        if (containers == null || containers.Count == 0)
            return;

        for (int i = 0; i < containers.Count; i++)
        {
            MutableContainerState container = containers[i];
            if (container == null || !container.IsModified || container.Storage == null)
                continue;

            container.Storage.SetModified();
            container.IsModified = false;
        }
    }

    private static int GetMaxStack(ItemValue itemValue)
    {
        if (itemValue?.ItemClass == null)
            return 1;

        int maxStack = itemValue.ItemClass.Stacknumber.Value;
        return maxStack > 0 ? maxStack : 1;
    }

    private static int CompareGuidOrdinal(Guid a, Guid b)
    {
        return string.CompareOrdinal(a.ToString(), b.ToString());
    }

    private static List<TileEntity> SnapshotTileEntities(Chunk chunk)
    {
        for (int attempt = 1; attempt <= TileEntitySnapshotMaxAttempts; attempt++)
        {
            try
            {
                return new List<TileEntity>(chunk.GetTileEntities().list);
            }
            catch (Exception)
            {
                if (attempt == TileEntitySnapshotMaxAttempts)
                    break;

                Thread.Yield();
            }
        }

        return new List<TileEntity>();
    }
}
