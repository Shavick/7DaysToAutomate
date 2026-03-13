using System;
using System.Collections.Generic;

public class FluidGraphData
{
    public sealed class PumpSnapshot
    {
        public bool PumpEnabled;
        public int OutputCapMgPerTick;
    }

    public Guid FluidGraphId;
    public readonly HashSet<Vector3i> PipePositions = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> PumpEndpoints = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> StorageEndpoints = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> IntakeEndpoints = new HashSet<Vector3i>();
    public readonly Dictionary<Vector3i, PumpSnapshot> UnloadedPumpSnapshots =
        new Dictionary<Vector3i, PumpSnapshot>();

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
        return $"FluidGraphId={FluidGraphId} Fluid={fluid} Pipes={PipePositions.Count} Pumps={PumpEndpoints.Count} PumpSnapshots={UnloadedPumpSnapshots.Count} Storage={StorageEndpoints.Count} Intakes={IntakeEndpoints.Count}";
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
}


