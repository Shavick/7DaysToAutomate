using System;
using System.Collections.Generic;
using System.Threading;

public static class FluidGraphManager
{
    private const int TileEntitySnapshotMaxAttempts = 3;

        private sealed class StorageNode
    {
        public Vector3i Pos;
        public TileEntityFluidStorage Storage;
        public int AvailableMg;
        public int DrainMg;
        public int RemainingInputMg;
        public int RemainingFreeSpaceMg;
        public int IntakeMg;
    }

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

    public static bool TryGetGraphFromAdjacentPipe(WorldBase world, int clrIdx, Vector3i machinePos, out Guid fluidGraphId, out FluidGraphData graph)
    {
        fluidGraphId = Guid.Empty;
        graph = null;

        if (world == null)
            return false;

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i pipePos = machinePos + NeighborOffsets[i];
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity te) || !(te is TileEntityLiquidPipe pipe))
                continue;

            if (pipe.FluidGraphId == Guid.Empty)
            {
                if (!TryEnsureGraphForPipe(world, clrIdx, pipePos, out FluidGraphData rebuiltGraph) || rebuiltGraph == null)
                    continue;

                fluidGraphId = rebuiltGraph.FluidGraphId;
                graph = rebuiltGraph;
                return true;
            }

            if (!graphsById.TryGetValue(pipe.FluidGraphId, out FluidGraphData existingGraph) || existingGraph == null)
            {
                if (!TryEnsureGraphForPipe(world, clrIdx, pipePos, out FluidGraphData rebuiltGraph) || rebuiltGraph == null)
                    continue;

                fluidGraphId = rebuiltGraph.FluidGraphId;
                graph = rebuiltGraph;
                return true;
            }

            fluidGraphId = existingGraph.FluidGraphId;
            graph = existingGraph;
            return true;
        }

        return false;
    }

    public static bool TryGetAvailableFluidAmount(WorldBase world, int clrIdx, Guid fluidGraphId, string fluidType, out int availableMg)
    {
        availableMg = 0;

        if (world == null || fluidGraphId == Guid.Empty || string.IsNullOrEmpty(fluidType))
            return false;

        if (!graphsById.TryGetValue(fluidGraphId, out FluidGraphData graph) || graph == null)
            return false;

        string normalized = fluidType.Trim().ToLowerInvariant();

        foreach (Vector3i pos in graph.StorageEndpoints)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity te) || !(te is TileEntityFluidStorage storage))
                continue;

            if (storage.FluidAmountMg <= 0 || string.IsNullOrEmpty(storage.FluidType))
                continue;

            string storageType = storage.FluidType.Trim().ToLowerInvariant();
            if (!string.Equals(storageType, normalized, StringComparison.Ordinal))
                continue;

            availableMg += storage.FluidAmountMg;
        }

        return true;
    }

    public static bool TryConsumeFluid(WorldBase world, int clrIdx, Guid fluidGraphId, string fluidType, int requestedMg, out int consumedMg)
    {
        consumedMg = 0;

        if (world == null || fluidGraphId == Guid.Empty || string.IsNullOrEmpty(fluidType) || requestedMg <= 0)
            return false;

        if (!graphsById.TryGetValue(fluidGraphId, out FluidGraphData graph) || graph == null)
            return false;

        string normalized = fluidType.Trim().ToLowerInvariant();
        List<StorageNode> sources = new List<StorageNode>();

        foreach (Vector3i pos in graph.StorageEndpoints)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity te) || !(te is TileEntityFluidStorage storage))
                continue;

            if (storage.FluidAmountMg <= 0 || string.IsNullOrEmpty(storage.FluidType))
                continue;

            string storageType = storage.FluidType.Trim().ToLowerInvariant();
            if (!string.Equals(storageType, normalized, StringComparison.Ordinal))
                continue;

            int available = storage.GetAvailableOutputMg();
            if (available <= 0)
                continue;

            sources.Add(new StorageNode
            {
                Pos = pos,
                Storage = storage,
                AvailableMg = available
            });
        }

        if (sources.Count == 0)
            return true;

        sources.Sort((a, b) => ComparePos(a.Pos, b.Pos));

        int totalAvailable = 0;
        for (int i = 0; i < sources.Count; i++)
            totalAvailable += sources[i].AvailableMg;

        if (totalAvailable <= 0)
            return true;

        int targetDrain = Math.Min(requestedMg, totalAvailable);
        int assigned = 0;

        for (int i = 0; i < sources.Count; i++)
        {
            StorageNode node = sources[i];
            int share = (int)((long)targetDrain * node.AvailableMg / totalAvailable);
            if (share > node.AvailableMg)
                share = node.AvailableMg;

            node.DrainMg = share;
            assigned += share;
        }

        int remaining = targetDrain - assigned;
        for (int i = 0; i < sources.Count && remaining > 0; i++)
        {
            StorageNode node = sources[i];
            int room = node.AvailableMg - node.DrainMg;
            if (room <= 0)
                continue;

            int add = Math.Min(room, remaining);
            node.DrainMg += add;
            remaining -= add;
        }

        for (int i = 0; i < sources.Count; i++)
        {
            StorageNode node = sources[i];
            if (node.DrainMg <= 0)
                continue;

            consumedMg += node.Storage.RemoveFluid(node.DrainMg);
        }

        return true;
    }

    public static bool TryInjectFluid(WorldBase world, int clrIdx, Guid fluidGraphId, string fluidType, int requestedMg, out string blockedReason)
    {
        blockedReason = "Unknown";

        if (world == null || fluidGraphId == Guid.Empty || string.IsNullOrEmpty(fluidType) || requestedMg <= 0)
        {
            blockedReason = "Invalid request";
            return false;
        }

        if (!graphsById.TryGetValue(fluidGraphId, out FluidGraphData graph) || graph == null)
        {
            blockedReason = "Fluid graph unavailable";
            return false;
        }

        if (graph.PipePositions == null || graph.PipePositions.Count == 0)
        {
            blockedReason = "No connected pipes";
            graph.RecordBlocked("NoPipe");
            return false;
        }

        List<TileEntityFluidPump> activePumps = new List<TileEntityFluidPump>();
        foreach (Vector3i pumpPos in graph.PumpEndpoints)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pumpPos, out TileEntity pumpTe) || !(pumpTe is TileEntityFluidPump pump))
                continue;

            if (!pump.IsActivePump())
                continue;

            activePumps.Add(pump);
        }

        if (activePumps.Count == 0)
        {
            blockedReason = "No active pump";
            graph.RecordBlocked("NoActivePump");
            return false;
        }

        List<StorageNode> sinks = new List<StorageNode>();
        foreach (Vector3i storagePos in graph.StorageEndpoints)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, storagePos, out TileEntity storageTe) || !(storageTe is TileEntityFluidStorage storage))
                continue;

            sinks.Add(new StorageNode
            {
                Pos = storagePos,
                Storage = storage
            });
        }

        if (sinks.Count == 0)
        {
            blockedReason = "No storage endpoint";
            graph.RecordBlocked("NoStorage");
            return false;
        }

        string normalizedFluid = fluidType.Trim().ToLowerInvariant();

        string graphFluidType = graph.FluidType;
        if (string.IsNullOrEmpty(graphFluidType))
        {
            for (int i = 0; i < sinks.Count; i++)
            {
                StorageNode sink = sinks[i];
                string sinkType = sink.Storage.FluidType;
                if (string.IsNullOrEmpty(sinkType) || sink.Storage.FluidAmountMg <= 0)
                    continue;

                if (string.IsNullOrEmpty(graphFluidType))
                {
                    graphFluidType = sinkType;
                    continue;
                }

                if (!string.Equals(graphFluidType, sinkType, StringComparison.Ordinal))
                {
                    blockedReason = "Storage fluid mismatch";
                    graph.RecordBlocked("TypeMismatch");
                    return false;
                }
            }
        }

        if (!string.IsNullOrEmpty(graphFluidType) && !string.Equals(graphFluidType, normalizedFluid, StringComparison.Ordinal))
        {
            blockedReason = "Graph fluid type mismatch";
            graph.RecordBlocked("TypeMismatch");
            return false;
        }

        int pipeCap = GetGraphPipeCapMgPerTick(world, clrIdx, graph);
        int pumpCap = 0;
        for (int i = 0; i < activePumps.Count; i++)
            pumpCap += activePumps[i].GetOutputCapMgPerTick();

        int flowBudget = Math.Min(pipeCap, pumpCap);
        if (flowBudget <= 0)
        {
            blockedReason = "No graph throughput";
            graph.RecordBlocked("NoThroughput");
            return false;
        }

        if (flowBudget < requestedMg)
        {
            blockedReason = "Graph throughput full";
            graph.RecordBlocked("ThroughputFull");
            return false;
        }

        sinks.Sort((a, b) => ComparePos(a.Pos, b.Pos));

        int totalSinkCapacity = 0;
        List<StorageNode> eligible = new List<StorageNode>();

        for (int i = 0; i < sinks.Count; i++)
        {
            StorageNode sink = sinks[i];

            if (!sink.Storage.CanAcceptType(normalizedFluid))
                continue;

            sink.RemainingInputMg = sink.Storage.GetRemainingInputBudgetMg();
            sink.RemainingFreeSpaceMg = sink.Storage.GetFreeSpaceMg();

            int sinkCapacity = Math.Min(sink.RemainingInputMg, sink.RemainingFreeSpaceMg);
            if (sinkCapacity <= 0)
                continue;

            totalSinkCapacity += sinkCapacity;
            eligible.Add(sink);
        }

        if (totalSinkCapacity < requestedMg)
        {
            blockedReason = "No storage room";
            graph.RecordBlocked("StorageFull");
            return false;
        }

        int remaining = requestedMg;
        while (remaining > 0)
        {
            int progressed = 0;
            int active = 0;

            for (int i = 0; i < eligible.Count; i++)
            {
                StorageNode sink = eligible[i];
                if (sink.RemainingInputMg <= 0 || sink.RemainingFreeSpaceMg <= 0)
                    continue;

                active++;
            }

            if (active <= 0)
                break;

            int share = remaining / active;
            if (share <= 0)
                share = 1;

            for (int i = 0; i < eligible.Count && remaining > 0; i++)
            {
                StorageNode sink = eligible[i];
                if (sink.RemainingInputMg <= 0 || sink.RemainingFreeSpaceMg <= 0)
                    continue;

                int move = Math.Min(share, remaining);
                move = Math.Min(move, sink.RemainingInputMg);
                move = Math.Min(move, sink.RemainingFreeSpaceMg);
                if (move <= 0)
                    continue;

                sink.IntakeMg += move;
                sink.RemainingInputMg -= move;
                sink.RemainingFreeSpaceMg -= move;
                remaining -= move;
                progressed += move;
            }

            if (progressed <= 0)
                break;
        }

        if (remaining > 0)
        {
            blockedReason = "No storage room";
            graph.RecordBlocked("StorageFull");
            return false;
        }

        int injected = 0;
        for (int i = 0; i < eligible.Count; i++)
        {
            StorageNode sink = eligible[i];
            if (sink.IntakeMg <= 0)
                continue;

            injected += sink.Storage.AcceptFluid(normalizedFluid, sink.IntakeMg);
        }

        if (injected < requestedMg)
        {
            blockedReason = "Graph throughput full";
            graph.RecordBlocked("ThroughputFull");
            return false;
        }

        if (string.IsNullOrEmpty(graph.FluidType))
            graph.FluidType = normalizedFluid;

        blockedReason = string.Empty;
        return true;
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


    public static void RebuildAllGraphs(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        ClearAll();

        List<Vector3i> pipePositions = new List<Vector3i>();

        foreach (Chunk chunk in SafeWorldRead.GetChunkArraySnapshot(world))
        {
            if (chunk == null)
                continue;

            List<TileEntity> snapshot = SnapshotTileEntities(chunk);
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] is TileEntityLiquidPipe pipe)
                    pipePositions.Add(pipe.ToWorldPos());
            }
        }

        for (int i = 0; i < pipePositions.Count; i++)
            MarkPipeDirty(pipePositions[i]);

        while (dirtyPipePositions.Count > 0)
            ProcessDirtyGraphs(world, int.MaxValue);
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


    private static List<TileEntity> SnapshotTileEntities(Chunk chunk)
    {
        for (int attempt = 1; attempt <= TileEntitySnapshotMaxAttempts; attempt++)
        {
            try
            {
                return new List<TileEntity>(chunk.GetTileEntities().list);
            }
            catch (InvalidOperationException)
            {
                if (attempt == TileEntitySnapshotMaxAttempts)
                {
                    Log.Warning($"[FluidGraphManager] Failed to snapshot tile entities for chunk after {attempt} attempts; skipping chunk this rebuild.");
                    break;
                }

                Thread.Yield();
            }
        }

        return new List<TileEntity>();
    }
    private static void PopulateGraphEndpoints(WorldBase world, int clrIdx, FluidGraphData graph)
    {
        if (graph == null)
            return;

        foreach (Vector3i pipePos in graph.PipePositions)
        {
            if (IsFluidIntakePipe(world, clrIdx, pipePos))
                graph.AddIntakeEndpoint(pipePos);

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

    private static bool IsFluidIntakePipe(WorldBase world, int clrIdx, Vector3i pos)
    {
        if (!SafeWorldRead.TryGetBlock(world, clrIdx, pos, out BlockValue blockValue))
            return false;

        string value = blockValue.Block?.Properties?.GetString("IsFluidIntake");
        return bool.TryParse(value, out bool isIntake) && isIntake;
    }

    private static int GetGraphPipeCapMgPerTick(WorldBase world, int clrIdx, FluidGraphData graph)
    {
        int lowest = int.MaxValue;

        foreach (Vector3i pipePos in graph.PipePositions)
        {
            if (!SafeWorldRead.TryGetBlock(world, clrIdx, pipePos, out BlockValue blockValue))
                continue;

            int capGps = GetPropertyInt(blockValue, "FluidPipeCapacityGps", 250);
            int capMgPerTick = (capGps * FluidConstants.MilliGallonsPerGallon) / FluidConstants.SimulationTicksPerSecond;
            if (capMgPerTick <= 0)
                continue;

            if (capMgPerTick < lowest)
                lowest = capMgPerTick;
        }

        if (lowest == int.MaxValue)
            return 0;

        return lowest;
    }

    private static int GetPropertyInt(BlockValue blockValue, string propertyName, int fallback)
    {
        string raw = blockValue.Block?.Properties?.GetString(propertyName);
        if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out int value))
            return fallback;

        return value;
    }
    private static int ComparePos(Vector3i a, Vector3i b)
    {
        int cmp = a.x.CompareTo(b.x);
        if (cmp != 0)
            return cmp;

        cmp = a.y.CompareTo(b.y);
        if (cmp != 0)
            return cmp;

        return a.z.CompareTo(b.z);
    }
}





