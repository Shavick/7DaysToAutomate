using System;
using System.Collections.Generic;

public static class PipeGraphManager
{
    private static readonly Dictionary<Guid, PipeGraphData> graphsById = new Dictionary<Guid, PipeGraphData>();
    private static readonly HashSet<Vector3i> dirtyPipePositions = new HashSet<Vector3i>();

    public static void ClearAll()
    {
        graphsById.Clear();
        dirtyPipePositions.Clear();
        Log.Out("[PipeGraphManager] ClearAll()");
    }

    public static void MarkPipeDirty(Vector3i pipePos)
    {
        dirtyPipePositions.Add(pipePos);
    }

    public static bool TryFindRoute(
        WorldBase world,
        int clrIdx,
        Guid pipeGraphId,
        Vector3i sourceMachinePos,
        Vector3i targetStoragePos,
        out List<Vector3i> route)
    {
        route = new List<Vector3i>();

        if (world == null || pipeGraphId == Guid.Empty)
            return false;

        if (!graphsById.TryGetValue(pipeGraphId, out PipeGraphData graph) || graph == null)
            return false;

        if (graph.PipePositions.Count == 0)
            return false;

        HashSet<Vector3i> startPipes = GetGraphPipesAdjacentTo(world, clrIdx, graph, sourceMachinePos);
        HashSet<Vector3i> endPipes = GetGraphPipesAdjacentTo(world, clrIdx, graph, targetStoragePos);

        if (startPipes.Count == 0 || endPipes.Count == 0)
            return false;

        return TryFindRouteBetweenPipeSets(world, clrIdx, graph, startPipes, endPipes, out route);
    }

    private static HashSet<Vector3i> GetGraphPipesAdjacentTo(
        WorldBase world,
        int clrIdx,
        PipeGraphData graph,
        Vector3i blockPos)
    {
        HashSet<Vector3i> results = new HashSet<Vector3i>();

        if (world == null || graph == null)
            return results;

        foreach (Vector3i neighborPos in GetNeighborPositions(blockPos))
        {
            if (!graph.PipePositions.Contains(neighborPos))
                continue;

            if (!(world.GetTileEntity(clrIdx, neighborPos) is TileEntityItemPipe))
                continue;

            results.Add(neighborPos);
        }

        return results;
    }

    private static bool TryFindRouteBetweenPipeSets(
        WorldBase world,
        int clrIdx,
        PipeGraphData graph,
        HashSet<Vector3i> startPipes,
        HashSet<Vector3i> endPipes,
        out List<Vector3i> route)
    {
        route = new List<Vector3i>();

        if (world == null || graph == null || startPipes == null || endPipes == null)
            return false;

        Dictionary<Vector3i, Vector3i> cameFrom = new Dictionary<Vector3i, Vector3i>();
        HashSet<Vector3i> visited = new HashSet<Vector3i>();
        List<Vector3i> open = new List<Vector3i>();
        int index = 0;

        foreach (Vector3i start in startPipes)
        {
            open.Add(start);
            visited.Add(start);
        }

        Vector3i foundEnd = Vector3i.zero;
        bool found = false;

        while (index < open.Count)
        {
            Vector3i current = open[index++];

            if (endPipes.Contains(current))
            {
                foundEnd = current;
                found = true;
                break;
            }

            BlockValue currentValue = world.GetBlock(clrIdx, current);
            List<Vector3i> neighbors = ItemPipeBlock.GetConnectedPipeNeighbors(world, clrIdx, current, currentValue);

            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector3i neighborPos = neighbors[i];

                if (!graph.PipePositions.Contains(neighborPos))
                    continue;

                if (!visited.Add(neighborPos))
                    continue;

                cameFrom[neighborPos] = current;
                open.Add(neighborPos);
            }
        }

        if (!found)
            return false;

        List<Vector3i> reverse = new List<Vector3i>();
        Vector3i step = foundEnd;
        reverse.Add(step);

        while (cameFrom.TryGetValue(step, out Vector3i prev))
        {
            reverse.Add(prev);
            step = prev;
        }

        reverse.Reverse();
        route = reverse;
        return route.Count > 0;
    }

    public static void RebuildAllGraphs(WorldBase world)
    {
        if (world == null)
        {
            Log.Out("[PipeGraphManager] RebuildAllGraphs ABORT — world is null");
            return;
        }

        Log.Out("[PipeGraphManager] RebuildAllGraphs BEGIN");

        ClearAll();

        List<Vector3i> pipePositions = new List<Vector3i>();

        foreach (Chunk chunk in world.ChunkClusters[0].GetChunkArray())
        {
            if (chunk == null)
                continue;

            List<TileEntity> snapshot = new List<TileEntity>(chunk.GetTileEntities().list);

            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] is TileEntityItemPipe pipe)
                    pipePositions.Add(pipe.ToWorldPos());
            }
        }

        Log.Out($"[PipeGraphManager] RebuildAllGraphs — found {pipePositions.Count} pipes");

        for (int i = 0; i < pipePositions.Count; i++)
        {
            Vector3i pipePos = pipePositions[i];

            if (world.GetTileEntity(0, pipePos) is TileEntityItemPipe pipe)
            {
                pipe.MarkNetworkDirty();
                MarkPipeDirty(pipePos);
            }
        }

        while (HasDirtyPipes())
            ProcessDirtyGraphs(world, int.MaxValue);

        Log.Out($"[PipeGraphManager] RebuildAllGraphs END — graphs={GetGraphCount()} dirty={GetDirtyPipeCount()}");
    }

    public static bool TryGetStorageEndpoints(Guid pipeGraphId, out List<Vector3i> storageEndpoints)
    {
        storageEndpoints = null;

        if (pipeGraphId == Guid.Empty)
            return false;

        if (!graphsById.TryGetValue(pipeGraphId, out PipeGraphData graph) || graph == null)
            return false;

        if (graph.StorageEndpoints == null || graph.StorageEndpoints.Count == 0)
            return false;

        storageEndpoints = new List<Vector3i>(graph.StorageEndpoints);
        return true;
    }

    public static bool TryGetStorageEndpointsForPipe(WorldBase world, int clrIdx, Vector3i pipePos, out List<Vector3i> storageEndpoints)
    {
        storageEndpoints = null;

        var pipeTe = world.GetTileEntity(clrIdx, pipePos) as TileEntityItemPipe;
        if (pipeTe == null || pipeTe.PipeGraphId == Guid.Empty)
            return false;

        return TryGetStorageEndpoints(pipeTe.PipeGraphId, out storageEndpoints);
    }

    public static void MarkPipesDirty(IEnumerable<Vector3i> pipePositions)
    {
        if (pipePositions == null)
            return;

        foreach (Vector3i pos in pipePositions)
            dirtyPipePositions.Add(pos);
    }

    public static bool HasDirtyPipes()
    {
        return dirtyPipePositions.Count > 0;
    }

    public static int GetDirtyPipeCount()
    {
        return dirtyPipePositions.Count;
    }

    public static int GetGraphCount()
    {
        return graphsById.Count;
    }

    public static bool TryGetGraph(Guid pipeGraphId, out PipeGraphData graph)
    {
        return graphsById.TryGetValue(pipeGraphId, out graph);
    }

    public static PipeGraphData CreateGraph()
    {
        PipeGraphData graph = new PipeGraphData();
        graphsById[graph.PipeGraphId] = graph;
        return graph;
    }

    public static void RemoveGraph(Guid pipeGraphId)
    {
        if (pipeGraphId == Guid.Empty)
            return;

        graphsById.Remove(pipeGraphId);
    }

    public static void RegisterGraph(PipeGraphData graph)
    {
        if (graph == null || graph.PipeGraphId == Guid.Empty)
            return;

        graphsById[graph.PipeGraphId] = graph;
    }

    public static IReadOnlyDictionary<Guid, PipeGraphData> GetAllGraphs()
    {
        return graphsById;
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
    }

    private static void RebuildGraphFromSeed(WorldBase world, int clrIdx, Vector3i seedPos)
    {
        if (!(world.GetTileEntity(clrIdx, seedPos) is TileEntityItemPipe))
        {
            dirtyPipePositions.Remove(seedPos);
            return;
        }

        BlockValue seedValue = world.GetBlock(clrIdx, seedPos);
        if (!(seedValue.Block is ItemPipeBlock))
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
            if (!(world.GetTileEntity(clrIdx, pipePos) is TileEntityItemPipe pipeTe))
                continue;

            if (pipeTe.PipeGraphId != Guid.Empty)
                oldGraphIds.Add(pipeTe.PipeGraphId);
        }

        foreach (Guid oldGraphId in oldGraphIds)
            graphsById.Remove(oldGraphId);

        PipeGraphData newGraph = new PipeGraphData();

        foreach (Vector3i pipePos in connectedPipes)
        {
            newGraph.AddPipe(pipePos);

            if (world.GetTileEntity(clrIdx, pipePos) is TileEntityItemPipe pipeTe)
            {
                pipeTe.SetPipeGraphId(newGraph.PipeGraphId);
                pipeTe.setModified();
            }

            dirtyPipePositions.Remove(pipePos);
        }

        CollectStorageEndpoints(world, clrIdx, newGraph);

        graphsById[newGraph.PipeGraphId] = newGraph;

        Log.Out($"[PipeGraphManager] Rebuilt graph {newGraph.PipeGraphId} Pipes={newGraph.PipePositions.Count} StorageEndpoints={newGraph.StorageEndpoints.Count}");
    }

    private static HashSet<Vector3i> CollectConnectedPipeRegion(WorldBase world, int clrIdx, Vector3i seedPos)
    {
        HashSet<Vector3i> visited = new HashSet<Vector3i>();
        List<Vector3i> open = new List<Vector3i>();
        int index = 0;

        open.Add(seedPos);
        visited.Add(seedPos);

        while (index < open.Count)
        {
            Vector3i current = open[index++];
            BlockValue currentValue = world.GetBlock(clrIdx, current);

            List<Vector3i> neighbors = ItemPipeBlock.GetConnectedPipeNeighbors(world, clrIdx, current, currentValue);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector3i neighborPos = neighbors[i];
                if (visited.Add(neighborPos))
                    open.Add(neighborPos);
            }
        }

        return visited;
    }

    private static void CollectStorageEndpoints(WorldBase world, int clrIdx, PipeGraphData graph)
    {
        foreach (Vector3i pipePos in graph.PipePositions)
        {
            BlockValue pipeValue = world.GetBlock(clrIdx, pipePos);

            foreach (Vector3i neighborPos in GetNeighborPositions(pipePos))
            {
                if (!IsStorageConnectedNeighbor(world, clrIdx, pipePos, pipeValue, neighborPos))
                    continue;

                graph.AddStorageEndpoint(neighborPos);
                Log.Out($"[PipeGraph] Added storage endpoint {neighborPos} to graph {graph.PipeGraphId} count={graph.StorageEndpoints.Count}");
            }
        }
        Log.Out($"[PipeGraph] Graph {graph.PipeGraphId} final storage endpoint count={graph.StorageEndpoints.Count}");
    }

    private static bool IsStorageConnectedNeighbor(
        WorldBase world,
        int clrIdx,
        Vector3i pipePos,
        BlockValue pipeValue,
        Vector3i neighborPos)
    {
        TileEntity te = world.GetTileEntity(clrIdx, neighborPos);
        Log.Out($"[PipeGraph] Storage check at {neighborPos} block={world.GetBlock(neighborPos).Block.GetBlockName()} te={(te == null ? "null" : te.GetType().Name)}");

        if (te is TileEntityItemPipe)
            return false;

        if (te is TileEntityNetworkController)
            return false;

        if (!(te is TileEntityComposite composite))
            return false;

        TEFeatureStorage storage = composite.GetFeature<TEFeatureStorage>();
        Log.Out($"[PipeGraph] Composite storage feature={(storage == null ? "null" : "found")}");

        if (storage == null || storage.items == null)
            return false;

        HashSet<Vector3i> openSides = ItemPipeBlock.GetOpenSides(pipeValue);
        Vector3i delta = neighborPos - pipePos;

        return openSides.Contains(delta);
    }

    private static IEnumerable<Vector3i> GetNeighborPositions(Vector3i center)
    {
        yield return center + Vector3i.forward;
        yield return center + Vector3i.back;
        yield return center + Vector3i.left;
        yield return center + Vector3i.right;
        yield return center + Vector3i.up;
        yield return center + Vector3i.down;
    }
}