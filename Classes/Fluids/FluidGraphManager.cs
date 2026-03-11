using System;
using System.Collections.Generic;

public static class FluidGraphManager
{
    private static readonly Dictionary<Guid, FluidGraphData> graphsById = new Dictionary<Guid, FluidGraphData>();
    private static readonly HashSet<Vector3i> dirtyPipePositions = new HashSet<Vector3i>();

    public static ulong LastRebuildWorldTime { get; private set; } = 0UL;

    private static readonly Vector3i[] NeighborOffsets =
    {
        Vector3i.forward,
        Vector3i.back,
        Vector3i.left,
        Vector3i.right,
        Vector3i.up,
        Vector3i.down
    };

    public static IReadOnlyDictionary<Guid, FluidGraphData> GetAllGraphs()
    {
        return graphsById;
    }

    public static bool TryGetGraph(Guid fluidGraphId, out FluidGraphData graph)
    {
        return graphsById.TryGetValue(fluidGraphId, out graph);
    }

    public static bool TryEnsureGraphForPipe(WorldBase world, int clrIdx, Vector3i pipePos, out FluidGraphData graph)
    {
        graph = null;
        if (world == null)
            return false;

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity te) || !(te is TileEntityLiquidPipe pipe))
            return false;

        if (pipe.FluidGraphId != Guid.Empty && graphsById.TryGetValue(pipe.FluidGraphId, out graph) && graph != null)
            return true;

        if (world.IsRemote())
            return false;

        MarkPipeDirty(pipePos);
        ProcessDirtyGraphs(world, 1024);

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out te) || !(te is TileEntityLiquidPipe refreshed))
            return false;

        if (refreshed.FluidGraphId == Guid.Empty)
            return false;

        return graphsById.TryGetValue(refreshed.FluidGraphId, out graph) && graph != null;
    }

    public static void ClearAll()
    {
        graphsById.Clear();
        dirtyPipePositions.Clear();
        LastRebuildWorldTime = 0UL;
    }

    public static void MarkPipeDirty(Vector3i pipePos)
    {
        dirtyPipePositions.Add(pipePos);
    }

    public static void ProcessDirtyGraphs(WorldBase world, int maxGraphsToProcess = 8)
    {
        if (world == null || world.IsRemote())
            return;

        if (dirtyPipePositions.Count == 0)
            return;

        int processed = 0;
        List<Vector3i> batch = new List<Vector3i>(dirtyPipePositions);

        for (int i = 0; i < batch.Count && processed < maxGraphsToProcess; i++)
        {
            Vector3i seedPos = batch[i];
            if (!dirtyPipePositions.Contains(seedPos))
                continue;

            RebuildGraphFromSeed(world, 0, seedPos);
            processed++;
        }

        if (processed > 0)
            LastRebuildWorldTime = world.GetWorldTime();
    }

    private static void RebuildGraphFromSeed(WorldBase world, int clrIdx, Vector3i seedPos)
    {
        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, seedPos, out TileEntity seedEntity) || !(seedEntity is TileEntityLiquidPipe))
        {
            dirtyPipePositions.Remove(seedPos);
            return;
        }

        if (!SafeWorldRead.TryGetBlock(world, clrIdx, seedPos, out BlockValue seedValue) || !(seedValue.Block is LiquidPipeBlock))
        {
            dirtyPipePositions.Remove(seedPos);
            return;
        }

        HashSet<Vector3i> connectedPipes = CollectConnectedPipeRegion(world, clrIdx, seedPos);
        if (connectedPipes.Count == 0)
        {
            dirtyPipePositions.Remove(seedPos);
            return;
        }

        HashSet<Guid> oldGraphIds = new HashSet<Guid>();

        foreach (Vector3i pipePos in connectedPipes)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity oldPipeEntity) || !(oldPipeEntity is TileEntityLiquidPipe oldPipeTe))
                continue;

            if (oldPipeTe.FluidGraphId != Guid.Empty)
                oldGraphIds.Add(oldPipeTe.FluidGraphId);
        }

        string retainedFluidType = string.Empty;
        foreach (Guid oldGraphId in oldGraphIds)
        {
            if (!graphsById.TryGetValue(oldGraphId, out FluidGraphData oldGraph) || oldGraph == null)
                continue;

            if (string.IsNullOrEmpty(retainedFluidType) && !string.IsNullOrEmpty(oldGraph.FluidType))
                retainedFluidType = oldGraph.FluidType;

            graphsById.Remove(oldGraphId);
        }

        FluidGraphData newGraph = new FluidGraphData();
        newGraph.FluidType = retainedFluidType;

        foreach (Vector3i pipePos in connectedPipes)
        {
            newGraph.AddPipe(pipePos);

            if (SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity newPipeEntity) && newPipeEntity is TileEntityLiquidPipe newPipeTe)
            {
                newPipeTe.SetFluidGraphId(newGraph.FluidGraphId);
                newPipeTe.setModified();
            }

            dirtyPipePositions.Remove(pipePos);
        }

        PopulateGraphEndpoints(world, clrIdx, newGraph);

        graphsById[newGraph.FluidGraphId] = newGraph;
    }

    private static HashSet<Vector3i> CollectConnectedPipeRegion(WorldBase world, int clrIdx, Vector3i seedPos)
    {
        HashSet<Vector3i> visited = new HashSet<Vector3i>();
        List<Vector3i> open = new List<Vector3i> { seedPos };
        int index = 0;

        while (index < open.Count)
        {
            Vector3i current = open[index++];
            if (!visited.Add(current))
                continue;

            if (!SafeWorldRead.TryGetBlock(world, clrIdx, current, out BlockValue currentValue))
                continue;

            List<Vector3i> neighbors = LiquidPipeBlock.GetConnectedPipeNeighbors(world, clrIdx, current, currentValue);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector3i neighborPos = neighbors[i];
                if (!visited.Contains(neighborPos))
                    open.Add(neighborPos);
            }
        }

        return visited;
    }

    private static void PopulateGraphEndpoints(WorldBase world, int clrIdx, FluidGraphData graph)
    {
        if (graph == null)
            return;

        foreach (Vector3i pipePos in graph.PipePositions)
        {
            foreach (Vector3i offset in NeighborOffsets)
            {
                Vector3i neighborPos = pipePos + offset;
                if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out TileEntity te))
                    continue;

                if (te is TileEntityFluidPump)
                {
                    graph.AddPumpEndpoint(neighborPos);
                    continue;
                }

                if (te is TileEntityFluidStorage)
                    graph.AddStorageEndpoint(neighborPos);
            }
        }
    }
}

