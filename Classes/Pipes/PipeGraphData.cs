using System;
using System.Collections.Generic;

public class PipeGraphData
{
    public Guid PipeGraphId;
    public readonly HashSet<Vector3i> PipePositions = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> StorageEndpoints = new HashSet<Vector3i>();
    public readonly Dictionary<Vector3i, ItemStack[]> UnloadedStorageSnapshots =
        new Dictionary<Vector3i, ItemStack[]>();

    public PipeGraphData()
    {
        PipeGraphId = Guid.NewGuid();
    }

    public PipeGraphData(Guid pipeGraphId)
    {
        PipeGraphId = pipeGraphId == Guid.Empty ? Guid.NewGuid() : pipeGraphId;
    }

    public bool ContainsPipe(Vector3i pos)
    {
        return PipePositions.Contains(pos);
    }

    public bool ContainsStorageEndpoint(Vector3i pos)
    {
        return StorageEndpoints.Contains(pos);
    }

    public void AddPipe(Vector3i pos)
    {
        PipePositions.Add(pos);
    }

    public void AddStorageEndpoint(Vector3i pos)
    {
        StorageEndpoints.Add(pos);
    }

    public void SetStorageSnapshot(Vector3i pos, ItemStack[] slots)
    {
        if (slots == null)
        {
            UnloadedStorageSnapshots.Remove(pos);
            return;
        }

        UnloadedStorageSnapshots[pos] = CloneSlots(slots);
    }

    public bool TryGetStorageSnapshot(Vector3i pos, out ItemStack[] slots)
    {
        slots = null;

        if (!UnloadedStorageSnapshots.TryGetValue(pos, out ItemStack[] existing) || existing == null)
            return false;

        slots = CloneSlots(existing);
        return true;
    }

    public void RemoveStorageSnapshot(Vector3i pos)
    {
        UnloadedStorageSnapshots.Remove(pos);
    }

    public void PruneStorageSnapshotsToEndpoints()
    {
        if (UnloadedStorageSnapshots.Count == 0)
            return;

        List<Vector3i> toRemove = null;

        foreach (var kvp in UnloadedStorageSnapshots)
        {
            if (StorageEndpoints.Contains(kvp.Key))
                continue;

            if (toRemove == null)
                toRemove = new List<Vector3i>();

            toRemove.Add(kvp.Key);
        }

        if (toRemove == null)
            return;

        for (int i = 0; i < toRemove.Count; i++)
            UnloadedStorageSnapshots.Remove(toRemove[i]);
    }

    public void Clear()
    {
        PipePositions.Clear();
        StorageEndpoints.Clear();
        UnloadedStorageSnapshots.Clear();
    }

    public override string ToString()
    {
        return $"PipeGraphId={PipeGraphId} Pipes={PipePositions.Count} StorageEndpoints={StorageEndpoints.Count} Snapshots={UnloadedStorageSnapshots.Count}";
    }

    private static ItemStack[] CloneSlots(ItemStack[] source)
    {
        ItemStack[] clone = new ItemStack[source.Length];

        for (int i = 0; i < source.Length; i++)
        {
            ItemStack stack = source[i];
            if (stack.IsEmpty() || stack.count <= 0 || stack.itemValue == null)
            {
                clone[i] = ItemStack.Empty;
                continue;
            }

            clone[i] = new ItemStack(stack.itemValue.Clone(), stack.count);
        }

        return clone;
    }
}
