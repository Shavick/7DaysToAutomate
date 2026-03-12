using System;
using System.Collections.Generic;

public class FluidGraphData
{
    public Guid FluidGraphId;
    public readonly HashSet<Vector3i> PipePositions = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> PumpEndpoints = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> StorageEndpoints = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> IntakeEndpoints = new HashSet<Vector3i>();

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
        return $"FluidGraphId={FluidGraphId} Fluid={fluid} Pipes={PipePositions.Count} Pumps={PumpEndpoints.Count} Storage={StorageEndpoints.Count} Intakes={IntakeEndpoints.Count}";
    }
}


