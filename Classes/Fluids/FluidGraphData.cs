using System;
using System.Collections.Generic;

public class FluidGraphData
{
    public sealed class PumpSnapshot
    {
        public bool PumpEnabled;
        public int OutputCapMgPerTick;
    }

    public sealed class StorageSnapshot
    {
        public string FluidType = string.Empty;
        public int FluidAmountMg;
        public int CapacityMg;
        public int InputCapMgPerTick;
        public int OutputCapMgPerTick;
        public int AcceptedThisTickMg;
        public ulong LastInputBudgetWorldTime;
    }

    public Guid FluidGraphId;
    public readonly HashSet<Vector3i> PipePositions = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> PumpEndpoints = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> StorageEndpoints = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> IntakeEndpoints = new HashSet<Vector3i>();
    public readonly Dictionary<Vector3i, PumpSnapshot> UnloadedPumpSnapshots =
        new Dictionary<Vector3i, PumpSnapshot>();
    public readonly Dictionary<Vector3i, StorageSnapshot> UnloadedStorageSnapshots =
        new Dictionary<Vector3i, StorageSnapshot>();

    // Empty string means unassigned fluid type.
    public string FluidType = string.Empty;

    public string LastBlockedReason = string.Empty;
    public int LastBlockedCount = 0;

    public FluidGraphData()
    {
        FluidGraphId = Guid.NewGuid();
    }

    public void AddPipe(Vector3i pos)
    {
        PipePositions.Add(pos);
    }

    public void AddPumpEndpoint(Vector3i pos)
    {
        PumpEndpoints.Add(pos);
    }

    public bool ContainsPumpEndpoint(Vector3i pos)
    {
        return PumpEndpoints.Contains(pos);
    }

    public void SetPumpSnapshot(Vector3i pos, PumpSnapshot snapshot)
    {
        if (snapshot == null)
        {
            UnloadedPumpSnapshots.Remove(pos);
            return;
        }

        UnloadedPumpSnapshots[pos] = ClonePumpSnapshot(snapshot);
    }

    public bool TryGetPumpSnapshot(Vector3i pos, out PumpSnapshot snapshot)
    {
        snapshot = null;

        if (!UnloadedPumpSnapshots.TryGetValue(pos, out PumpSnapshot existing) || existing == null)
            return false;

        snapshot = ClonePumpSnapshot(existing);
        return true;
    }

    public void RemovePumpSnapshot(Vector3i pos)
    {
        UnloadedPumpSnapshots.Remove(pos);
    }

    public void PrunePumpSnapshotsToEndpoints()
    {
        if (UnloadedPumpSnapshots.Count == 0)
            return;

        List<Vector3i> toRemove = null;
        foreach (var kvp in UnloadedPumpSnapshots)
        {
            if (PumpEndpoints.Contains(kvp.Key))
                continue;

            if (toRemove == null)
                toRemove = new List<Vector3i>();

            toRemove.Add(kvp.Key);
        }

        if (toRemove == null)
            return;

        for (int i = 0; i < toRemove.Count; i++)
            UnloadedPumpSnapshots.Remove(toRemove[i]);
    }

    public void AddStorageEndpoint(Vector3i pos)
    {
        StorageEndpoints.Add(pos);
    }

    public bool ContainsStorageEndpoint(Vector3i pos)
    {
        return StorageEndpoints.Contains(pos);
    }

    public void SetStorageSnapshot(Vector3i pos, StorageSnapshot snapshot)
    {
        if (snapshot == null)
        {
            UnloadedStorageSnapshots.Remove(pos);
            return;
        }

        UnloadedStorageSnapshots[pos] = CloneStorageSnapshot(snapshot);
    }

    public bool TryGetStorageSnapshot(Vector3i pos, out StorageSnapshot snapshot)
    {
        snapshot = null;

        if (!UnloadedStorageSnapshots.TryGetValue(pos, out StorageSnapshot existing) || existing == null)
            return false;

        snapshot = CloneStorageSnapshot(existing);
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

    public void AddIntakeEndpoint(Vector3i pos)
    {
        IntakeEndpoints.Add(pos);
    }

    public void RecordBlocked(string reason)
    {
        LastBlockedReason = reason ?? string.Empty;
        LastBlockedCount++;
    }

    public bool ContainsPipe(Vector3i pos)
    {
        return PipePositions.Contains(pos);
    }

    public override string ToString()
    {
        string fluid = string.IsNullOrEmpty(FluidType) ? "Unassigned" : FluidType;
        return $"FluidGraphId={FluidGraphId} Fluid={fluid} Pipes={PipePositions.Count} Pumps={PumpEndpoints.Count} PumpSnapshots={UnloadedPumpSnapshots.Count} Storage={StorageEndpoints.Count} StorageSnapshots={UnloadedStorageSnapshots.Count} Intakes={IntakeEndpoints.Count}";
    }

    private static PumpSnapshot ClonePumpSnapshot(PumpSnapshot source)
    {
        if (source == null)
            return null;

        return new PumpSnapshot
        {
            PumpEnabled = source.PumpEnabled,
            OutputCapMgPerTick = source.OutputCapMgPerTick
        };
    }

    private static StorageSnapshot CloneStorageSnapshot(StorageSnapshot source)
    {
        if (source == null)
            return null;

        return new StorageSnapshot
        {
            FluidType = source.FluidType ?? string.Empty,
            FluidAmountMg = source.FluidAmountMg,
            CapacityMg = source.CapacityMg,
            InputCapMgPerTick = source.InputCapMgPerTick,
            OutputCapMgPerTick = source.OutputCapMgPerTick,
            AcceptedThisTickMg = source.AcceptedThisTickMg,
            LastInputBudgetWorldTime = source.LastInputBudgetWorldTime
        };
    }
}


