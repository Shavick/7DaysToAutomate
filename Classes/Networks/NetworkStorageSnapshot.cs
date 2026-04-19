using System;
using System.Collections.Generic;

public sealed class NetworkStorageSnapshot
{
    public Guid NetworkId;
    public int Revision;
    public ulong BuildAtWorldTime;

    public readonly List<NetworkDisplayStack> DisplayStacks = new List<NetworkDisplayStack>();

    public NetworkStorageSnapshot(Guid networkId, int revision, ulong buildAtWorldTime)
    {
        NetworkId = networkId;
        Revision = revision;
        BuildAtWorldTime = buildAtWorldTime;
    }

    public static NetworkStorageSnapshot Build(Guid networkId, int revision, ulong buildAtWorldTime, List<NetworkStorageEntry> physicalEntries)
    {
        var snapshot = new NetworkStorageSnapshot(networkId, revision, buildAtWorldTime);

        if (physicalEntries == null || physicalEntries.Count == 0)
            return snapshot;

        for (int i = 0; i < physicalEntries.Count; i++)
        {
            NetworkStorageEntry entry = physicalEntries[i];

            if (entry == null || !entry.IsValid)
                continue;

            NetworkDisplayStack target = null;

            for (int d = 0; d < snapshot.DisplayStacks.Count; d++)
            {
                if (snapshot.DisplayStacks[d].CanMerge(entry))
                {
                    target = snapshot.DisplayStacks[d];
                    break;
                }
            }

            if (target == null)
            {
                ItemStack display = new ItemStack(entry.Stack.itemValue.Clone(), 1);
                target = new NetworkDisplayStack(display);
                snapshot.DisplayStacks.Add(target);
            }

            target.AddSource(entry);
        }

        snapshot.DisplayStacks.Sort(CompareDisplayStacksByName);
        return snapshot;
    }

    private static int CompareDisplayStacksByName(NetworkDisplayStack a, NetworkDisplayStack b)
    {
        string aName = a == null ? string.Empty : NetworkItemIdentity.GetStableDisplayName(a.DisplayStack);
        string bName = b == null ? string.Empty : NetworkItemIdentity.GetStableDisplayName(b.DisplayStack);
        return string.CompareOrdinal(aName, bName);
    }
}
