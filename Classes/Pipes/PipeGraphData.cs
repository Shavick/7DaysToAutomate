using System;
using System.Collections.Generic;

public class PipeGraphData
{
    public Guid PipeGraphId;
    public readonly HashSet<Vector3i> PipePositions = new HashSet<Vector3i>();
    public readonly HashSet<Vector3i> StorageEndpoints = new HashSet<Vector3i>();

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

    public void Clear()
    {
        PipePositions.Clear();
        StorageEndpoints.Clear();
    }

    public override string ToString()
    {
        return $"PipeGraphId={PipeGraphId} Pipes={PipePositions.Count} StorageEndpoints={StorageEndpoints.Count}";
    }
}