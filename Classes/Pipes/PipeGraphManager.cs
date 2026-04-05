using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

public static class PipeGraphManager
{
    private const int TileEntitySnapshotMaxAttempts = 4;
    private static readonly Dictionary<Guid, PipeGraphData> graphsById = new Dictionary<Guid, PipeGraphData>();
    private static readonly HashSet<Vector3i> dirtyPipePositions = new HashSet<Vector3i>();
    private static readonly Dictionary<Guid, int> graphRouteVersions = new Dictionary<Guid, int>();
    private static readonly Dictionary<Guid, Dictionary<RouteCacheKey, RouteCacheEntry>> routeCacheByGraph = new Dictionary<Guid, Dictionary<RouteCacheKey, RouteCacheEntry>>();
    private static readonly Dictionary<Guid, ulong> lastGraphRebuildWorldTime = new Dictionary<Guid, ulong>();
    private const int MaxRouteCacheEntriesPerGraph = 256;
    private const ulong GraphRebuildCooldownTicks = 20UL;
    private const string PIPE_GRAPH_FOLDER = "PipeGraph";
    private const string PIPE_GRAPH_FILE = "pipe_graphs.dat";
    private const int PIPE_GRAPH_VERSION = 1;
    private static string pipeGraphDir;
    private static string pipeGraphFile;
    private struct RouteCacheKey : IEquatable<RouteCacheKey>
    {
        public int ClrIdx;
        public Vector3i SourceMachinePos;
        public Vector3i TargetStoragePos;

        public bool Equals(RouteCacheKey other)
        {
            return ClrIdx == other.ClrIdx &&
                   SourceMachinePos == other.SourceMachinePos &&
                   TargetStoragePos == other.TargetStoragePos;
        }

        public override bool Equals(object obj)
        {
            return obj is RouteCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + ClrIdx;
                hash = (hash * 31) + SourceMachinePos.GetHashCode();
                hash = (hash * 31) + TargetStoragePos.GetHashCode();
                return hash;
            }
        }
    }

    private sealed class RouteCacheEntry
    {
        public int GraphVersion;
        public List<Vector3i> Route;
    }

    private static bool PipeGraphVerboseLogs => false;

    private static void PipeGraphLog(string message)
    {
        if (!PipeGraphVerboseLogs)
            return;

        Log.Out(message);
    }

    private static void EnsureSavePaths()
    {
        if (!string.IsNullOrEmpty(pipeGraphFile))
            return;

        try
        {
            string saveRoot = GameIO.GetSaveGameDir();
            pipeGraphDir = Path.Combine(saveRoot, PIPE_GRAPH_FOLDER);
            pipeGraphFile = Path.Combine(pipeGraphDir, PIPE_GRAPH_FILE);

            if (!Directory.Exists(pipeGraphDir))
                Directory.CreateDirectory(pipeGraphDir);
        }
        catch (Exception ex)
        {
            Log.Error($"[PipeGraphManager][IO] EnsureSavePaths FAILED: {ex}");
        }
    }

    public static void SaveToDisk(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        EnsureSavePaths();
        if (string.IsNullOrEmpty(pipeGraphFile))
            return;

        string tempFile = pipeGraphFile + ".tmp";
        int persistedGraphCount = 0;

        try
        {
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(new[] { 'P', 'G', 'R' });
                bw.Write(PIPE_GRAPH_VERSION);

                persistedGraphCount = 0;
                foreach (var kvp in graphsById)
                {
                    PipeGraphData graph = kvp.Value;
                    if (graph != null && graph.PipeGraphId != Guid.Empty)
                        persistedGraphCount++;
                }

                bw.Write(persistedGraphCount);

                foreach (var kvp in graphsById)
                {
                    PipeGraphData graph = kvp.Value;
                    if (graph == null || graph.PipeGraphId == Guid.Empty)
                        continue;

                    bw.Write(graph.PipeGraphId.ToByteArray());

                    bw.Write(graph.PipePositions.Count);
                    foreach (Vector3i pipePos in graph.PipePositions)
                        WriteVector3i(bw, pipePos);

                    bw.Write(graph.StorageEndpoints.Count);
                    foreach (Vector3i endpointPos in graph.StorageEndpoints)
                        WriteVector3i(bw, endpointPos);

                    int snapshotCount = graph.UnloadedStorageSnapshots?.Count ?? 0;
                    bw.Write(snapshotCount);
                    if (graph.UnloadedStorageSnapshots != null)
                    {
                        foreach (var snapshotKvp in graph.UnloadedStorageSnapshots)
                        {
                            WriteVector3i(bw, snapshotKvp.Key);
                            WriteSlotSnapshot(bw, snapshotKvp.Value);
                        }
                    }
                }
            }

            if (File.Exists(pipeGraphFile))
                File.Delete(pipeGraphFile);

            File.Move(tempFile, pipeGraphFile);

            PipeGraphLog($"[PipeGraphManager][IO] Saved {persistedGraphCount} graph(s) to disk");
        }
        catch (Exception ex)
        {
            Log.Error($"[PipeGraphManager][IO] SaveToDisk FAILED: {ex}");
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch { }
        }
    }

    public static bool LoadFromDisk(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return false;

        EnsureSavePaths();
        bool devLoggingEnabled = IsDevLoggingEnabled(world);
        ClearAll(devLoggingEnabled);

        if (string.IsNullOrEmpty(pipeGraphFile) || !File.Exists(pipeGraphFile))
            return false;

        List<PipeGraphData> loadedGraphs = new List<PipeGraphData>();

        try
        {
            using (var fs = new FileStream(pipeGraphFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                string magic = new string(br.ReadChars(3));
                if (!string.Equals(magic, "PGR", StringComparison.Ordinal))
                {
                    Log.Error($"[PipeGraphManager][IO] LoadFromDisk FAILED - bad magic '{magic}'");
                    return false;
                }

                int version = br.ReadInt32();
                if (version != PIPE_GRAPH_VERSION)
                {
                    Log.Error($"[PipeGraphManager][IO] LoadFromDisk FAILED - unsupported version {version}");
                    return false;
                }

                int graphCount = Math.Max(0, br.ReadInt32());
                for (int i = 0; i < graphCount; i++)
                {
                    Guid graphId = new Guid(br.ReadBytes(16));
                    PipeGraphData graph = new PipeGraphData(graphId);

                    int pipeCount = Math.Max(0, br.ReadInt32());
                    for (int p = 0; p < pipeCount; p++)
                        graph.AddPipe(ReadVector3i(br));

                    int endpointCount = Math.Max(0, br.ReadInt32());
                    for (int e = 0; e < endpointCount; e++)
                        graph.AddStorageEndpoint(ReadVector3i(br));

                    int snapshotCount = Math.Max(0, br.ReadInt32());
                    for (int s = 0; s < snapshotCount; s++)
                    {
                        Vector3i storagePos = ReadVector3i(br);
                        ItemStack[] snapshot = ReadSlotSnapshot(br);
                        if (storagePos != Vector3i.zero && snapshot != null)
                            graph.SetStorageSnapshot(storagePos, snapshot);
                    }

                    graph.PruneStorageSnapshotsToEndpoints();
                    if (graph.PipePositions.Count > 0)
                        loadedGraphs.Add(graph);
                }
            }

            HashSet<Vector3i> assignedPipePositions = new HashSet<Vector3i>();
            ulong worldTime = world.GetWorldTime();

            for (int i = 0; i < loadedGraphs.Count; i++)
            {
                PipeGraphData graph = loadedGraphs[i];
                if (!ApplyLoadedGraphToWorld(world, 0, graph, assignedPipePositions))
                    continue;

                graph.PruneStorageSnapshotsToEndpoints();
                graphsById[graph.PipeGraphId] = graph;
                BumpGraphRouteVersion(graph.PipeGraphId);
                lastGraphRebuildWorldTime[graph.PipeGraphId] = worldTime;
            }

            List<Vector3i> livePipePositions = CollectAllPipePositions(world);
            for (int i = 0; i < livePipePositions.Count; i++)
            {
                Vector3i pipePos = livePipePositions[i];
                if (assignedPipePositions.Contains(pipePos))
                    continue;

                if (SafeWorldRead.TryGetTileEntity(world, 0, pipePos, out TileEntity tileEntity) && tileEntity is TileEntityItemPipe pipe)
                {
                    pipe.MarkPipeGraphDirty();
                    pipe.MarkNetworkDirty();
                }

                MarkPipeDirty(pipePos);
            }

            while (HasDirtyPipes())
                ProcessDirtyGraphs(world, int.MaxValue);

            PipeGraphLog($"[PipeGraphManager][IO] Loaded {graphsById.Count} graph(s) from disk");
            return true;
        }
        catch (EndOfStreamException)
        {
            Log.Error("[PipeGraphManager][IO] LoadFromDisk FAILED - file truncated");
        }
        catch (Exception ex)
        {
            Log.Error($"[PipeGraphManager][IO] LoadFromDisk FAILED: {ex}");
        }

        ClearAll(devLoggingEnabled);
        return false;
    }

    public static bool TryResolveGraphIdByStorageEndpoint(Vector3i storagePos, out Guid graphId)
    {
        graphId = Guid.Empty;
        if (storagePos == Vector3i.zero)
        {
            PipeGraphLog("[PipeGraphManager] TryResolveGraphIdByStorageEndpoint called with zero position");
            return false;
        }

        Guid found = Guid.Empty;
        int matches = 0;

        foreach (var kvp in graphsById)
        {
            PipeGraphData graph = kvp.Value;
            if (graph == null)
                continue;

            if (!graph.ContainsStorageEndpoint(storagePos))
                continue;

            found = kvp.Key;
            matches++;

            if (matches > 1)
                break; // ambiguous
        }

        if (matches >= 1)
        {
            graphId = found;
            return true;
        }
        return false;
    }

    public static bool TryResolveGraphIdForMachineAndStorage(Vector3i machinePos, Vector3i storagePos, out Guid graphId)
    {
        graphId = Guid.Empty;
        if (machinePos == Vector3i.zero || storagePos == Vector3i.zero)
        {
            PipeGraphLog($"[PipeGraphManager] TryResolveGraphIdForMachineAndStorage invalid machinePos={machinePos} storagePos={storagePos}");
            return false;
        }

        Guid found = Guid.Empty;
        int matches = 0;

        foreach (var kvp in graphsById)
        {
            PipeGraphData graph = kvp.Value;
            if (graph == null)
                continue;

            bool touchesMachine = false;
            foreach (Vector3i neighborPos in GetNeighborPositions(machinePos))
            {
                if (graph.PipePositions.Contains(neighborPos))
                {
                    touchesMachine = true;
                    break;
                }
            }

            if (!touchesMachine)
                continue;

            bool hasStorage = graph.ContainsStorageEndpoint(storagePos) ||
                              (graph.TryGetStorageSnapshot(storagePos, out ItemStack[] snapshotSlots) && snapshotSlots != null);
            if (!hasStorage)
                continue;

            found = kvp.Key;
            matches++;

            if (matches > 1)
                break;
        }

        if (matches >= 1)
        {
            graphId = found;
            return true;
        }
        if (matches == 0)
            PipeGraphLog($"[PipeGraphManager] No graph match for machine+storage machinePos={machinePos} storagePos={storagePos}");

        return false;
    }

    public static bool TryResolveMachinePipeAnchorPosition(
        WorldBase world,
        int clrIdx,
        Vector3i machinePos,
        Guid preferredGraphId,
        Vector3i storagePos,
        out Vector3i anchorPos)
    {
        anchorPos = Vector3i.zero;
        if (world == null || machinePos == Vector3i.zero)
            return false;

        List<Vector3i> neighbors = new List<Vector3i>(GetNeighborPositions(machinePos));

        bool IsValidAdjacentPipe(Vector3i pos)
        {
            return SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity te) && te is TileEntityItemPipe;
        }

        bool IsGraphStorageMatch(Guid graphId)
        {
            if (graphId == Guid.Empty || storagePos == Vector3i.zero)
                return true;

            if (!graphsById.TryGetValue(graphId, out PipeGraphData graph) || graph == null)
                return false;

            return graph.ContainsStorageEndpoint(storagePos) ||
                   (graph.TryGetStorageSnapshot(storagePos, out ItemStack[] snapshotSlots) && snapshotSlots != null);
        }

        // First choice: adjacent pipe that is already on the selected graph.
        if (preferredGraphId != Guid.Empty)
        {
            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector3i pos = neighbors[i];
                if (!IsValidAdjacentPipe(pos))
                    continue;

                if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity te) || !(te is TileEntityItemPipe pipe))
                    continue;

                if (pipe.PipeGraphId != preferredGraphId)
                    continue;

                if (!IsGraphStorageMatch(preferredGraphId))
                    continue;

                anchorPos = pos;
                return true;
            }
        }

        // Second choice: adjacent pipe position that belongs to preferred graph snapshot.
        if (preferredGraphId != Guid.Empty &&
            graphsById.TryGetValue(preferredGraphId, out PipeGraphData preferredGraph) &&
            preferredGraph != null &&
            IsGraphStorageMatch(preferredGraphId))
        {
            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector3i pos = neighbors[i];
                if (!preferredGraph.PipePositions.Contains(pos))
                    continue;

                anchorPos = pos;
                return true;
            }
        }

        // Final fallback: unique adjacent graph that also matches storage.
        Guid matchGraphId = Guid.Empty;
        Vector3i matchPos = Vector3i.zero;
        int matches = 0;
        for (int i = 0; i < neighbors.Count; i++)
        {
            Vector3i pos = neighbors[i];
            if (!IsValidAdjacentPipe(pos))
                continue;

            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity te) || !(te is TileEntityItemPipe pipe))
                continue;

            Guid graphId = pipe.PipeGraphId;
            if (graphId == Guid.Empty || !IsGraphStorageMatch(graphId))
                continue;

            matchGraphId = graphId;
            matchPos = pos;
            matches++;
            if (matches > 1)
                break;
        }

        if (matches == 1)
        {
            anchorPos = matchPos;
            return true;
        }

        if (matches > 1)
            Log.Warning($"[PipeGraphManager] Ambiguous machine pipe anchor machinePos={machinePos} preferredGraph={preferredGraphId} storagePos={storagePos}");

        return false;
    }

    public static bool TryResolveGraphIdForMachineAnchorAndStorage(
        Vector3i machinePos,
        Vector3i machinePipeAnchorPos,
        Vector3i storagePos,
        out Guid graphId)
    {
        graphId = Guid.Empty;
        if (machinePos == Vector3i.zero || storagePos == Vector3i.zero || machinePipeAnchorPos == Vector3i.zero)
            return false;

        bool isAdjacent = false;
        List<Vector3i> neighbors = new List<Vector3i>(GetNeighborPositions(machinePos));
        for (int i = 0; i < neighbors.Count; i++)
        {
            if (neighbors[i] == machinePipeAnchorPos)
            {
                isAdjacent = true;
                break;
            }
        }

        if (!isAdjacent)
        {
            Log.Warning($"[PipeGraphManager] Invalid machine anchor machinePos={machinePos} anchor={machinePipeAnchorPos}");
            return false;
        }

        Guid found = Guid.Empty;
        int matches = 0;
        foreach (var kvp in graphsById)
        {
            PipeGraphData graph = kvp.Value;
            if (graph == null || !graph.PipePositions.Contains(machinePipeAnchorPos))
                continue;

            bool hasStorage = graph.ContainsStorageEndpoint(storagePos) ||
                              (graph.TryGetStorageSnapshot(storagePos, out ItemStack[] snapshotSlots) && snapshotSlots != null);
            if (!hasStorage)
                continue;

            found = kvp.Key;
            matches++;
            if (matches > 1)
                break;
        }

        if (matches >= 1)
        {
            graphId = found;
            return true;
        }
        if (matches == 0)
            PipeGraphLog($"[PipeGraphManager] No graph match for machine anchor+storage machinePos={machinePos} anchor={machinePipeAnchorPos} storagePos={storagePos}");

        return false;
    }

    private static int GetGraphRouteVersion(Guid graphId)
    {
        if (graphId == Guid.Empty)
            return 0;

        return graphRouteVersions.TryGetValue(graphId, out int version) ? version : 0;
    }

    private static void BumpGraphRouteVersion(Guid graphId)
    {
        if (graphId == Guid.Empty)
            return;

        int current = GetGraphRouteVersion(graphId);
        graphRouteVersions[graphId] = current + 1;
        routeCacheByGraph.Remove(graphId);
    }

    private static void RemoveGraphRouteState(Guid graphId)
    {
        if (graphId == Guid.Empty)
            return;

        graphRouteVersions.Remove(graphId);
        routeCacheByGraph.Remove(graphId);
    }

    private static bool TryGetCachedRoute(Guid graphId, int clrIdx, Vector3i sourceMachinePos, Vector3i targetStoragePos, out List<Vector3i> route)
    {
        route = null;

        if (graphId == Guid.Empty)
            return false;

        if (!routeCacheByGraph.TryGetValue(graphId, out Dictionary<RouteCacheKey, RouteCacheEntry> graphCache) || graphCache == null)
            return false;

        RouteCacheKey key = new RouteCacheKey
        {
            ClrIdx = clrIdx,
            SourceMachinePos = sourceMachinePos,
            TargetStoragePos = targetStoragePos
        };

        if (!graphCache.TryGetValue(key, out RouteCacheEntry entry) || entry == null || entry.Route == null || entry.Route.Count == 0)
            return false;

        if (entry.GraphVersion != GetGraphRouteVersion(graphId))
            return false;

        route = new List<Vector3i>(entry.Route);
        return true;
    }

    private static void CacheRoute(Guid graphId, int clrIdx, Vector3i sourceMachinePos, Vector3i targetStoragePos, List<Vector3i> route)
    {
        if (graphId == Guid.Empty || route == null || route.Count == 0)
            return;

        if (!routeCacheByGraph.TryGetValue(graphId, out Dictionary<RouteCacheKey, RouteCacheEntry> graphCache) || graphCache == null)
        {
            graphCache = new Dictionary<RouteCacheKey, RouteCacheEntry>();
            routeCacheByGraph[graphId] = graphCache;
        }

        if (graphCache.Count >= MaxRouteCacheEntriesPerGraph)
        {
            using (var e = graphCache.GetEnumerator())
            {
                if (e.MoveNext())
                    graphCache.Remove(e.Current.Key);
            }
        }

        RouteCacheKey key = new RouteCacheKey
        {
            ClrIdx = clrIdx,
            SourceMachinePos = sourceMachinePos,
            TargetStoragePos = targetStoragePos
        };

        graphCache[key] = new RouteCacheEntry
        {
            GraphVersion = GetGraphRouteVersion(graphId),
            Route = new List<Vector3i>(route)
        };
    }

    public static void ClearAll(bool devLoggingEnabled = false)
    {
        graphsById.Clear();
        dirtyPipePositions.Clear();
        graphRouteVersions.Clear();
        routeCacheByGraph.Clear();
        lastGraphRebuildWorldTime.Clear();

        if (devLoggingEnabled)
            PipeGraphLog("[PipeGraphManager] ClearAll()");
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

        if (TryGetCachedRoute(pipeGraphId, clrIdx, sourceMachinePos, targetStoragePos, out List<Vector3i> cachedRoute))
        {
            route = cachedRoute;
            return true;
        }

        HashSet<Vector3i> startPipes = GetGraphPipesAdjacentTo(world, clrIdx, graph, sourceMachinePos);
        HashSet<Vector3i> endPipes = GetGraphPipesAdjacentTo(world, clrIdx, graph, targetStoragePos);

        if (startPipes.Count == 0 || endPipes.Count == 0)
            return false;

        bool found = TryFindRouteBetweenPipeSets(world, clrIdx, graph, startPipes, endPipes, out route);
        if (found)
            CacheRoute(pipeGraphId, clrIdx, sourceMachinePos, targetStoragePos, route);

        return found;
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

            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out TileEntity neighborTe) || !(neighborTe is TileEntityItemPipe))
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

            if (!SafeWorldRead.TryGetBlock(world, clrIdx, current, out BlockValue currentValue))
                continue;
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
            return;

        bool devLoggingEnabled = IsDevLoggingEnabled(world);

        if (devLoggingEnabled)
            PipeGraphLog("[PipeGraphManager] RebuildAllGraphs BEGIN");

        ClearAll(devLoggingEnabled);

        List<Vector3i> pipePositions = new List<Vector3i>();

        foreach (Chunk chunk in SafeWorldRead.GetChunkArraySnapshot(world))
        {
            if (chunk == null)
                continue;

            List<TileEntity> snapshot = SnapshotTileEntities(chunk);

            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] is TileEntityItemPipe pipe)
                    pipePositions.Add(pipe.ToWorldPos());
            }
        }

        if (devLoggingEnabled)
            PipeGraphLog($"[PipeGraphManager] RebuildAllGraphs: found {pipePositions.Count} pipes");

        for (int i = 0; i < pipePositions.Count; i++)
        {
            Vector3i pipePos = pipePositions[i];

            if (SafeWorldRead.TryGetTileEntity(world, 0, pipePos, out TileEntity rebuildTe) && rebuildTe is TileEntityItemPipe pipe)
            {
                pipe.MarkNetworkDirty();
                MarkPipeDirty(pipePos);
            }
        }

        while (HasDirtyPipes())
            ProcessDirtyGraphs(world, int.MaxValue);

        if (devLoggingEnabled)
            PipeGraphLog($"[PipeGraphManager] RebuildAllGraphs END: graphs={GetGraphCount()} dirty={GetDirtyPipeCount()}");
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
                    Log.Warning($"[PipeGraphManager] Failed to snapshot tile entities for chunk after {attempt} attempts; skipping chunk this rebuild.");
                    break;
                }

                Thread.Yield();
            }
        }

        return new List<TileEntity>();
    }

    public static bool IsDevLoggingEnabled(WorldBase world)
    {
        if (world == null)
            return false;

        foreach (Chunk chunk in SafeWorldRead.GetChunkArraySnapshot(world))
        {
            if (chunk == null)
                continue;

            List<TileEntity> snapshot = SnapshotTileEntities(chunk);
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] is TileEntityItemPipe pipe && pipe.IsDevLogging)
                    return true;
            }
        }

        return false;
    }

    private static bool IsDevLoggingEnabled(WorldBase world, int clrIdx, PipeGraphData graph)
    {
        if (world == null || graph == null || graph.PipePositions == null)
            return false;

        foreach (Vector3i pipePos in graph.PipePositions)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity tileEntity))
                continue;

            if (tileEntity is TileEntityItemPipe pipe && pipe.IsDevLogging)
                return true;
        }

        return false;
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
        storageEndpoints.Sort(ComparePositions);
        return true;
    }

    public static bool TryGetStorageSnapshot(Guid pipeGraphId, Vector3i storagePos, out ItemStack[] slots)
    {
        slots = null;

        if (pipeGraphId == Guid.Empty || storagePos == Vector3i.zero)
            return false;

        if (!graphsById.TryGetValue(pipeGraphId, out PipeGraphData graph) || graph == null)
            return false;

        return graph.TryGetStorageSnapshot(storagePos, out slots);
    }

    public static bool TryGetStorageEndpointsForPipe(WorldBase world, int clrIdx, Vector3i pipePos, out List<Vector3i> storageEndpoints)
    {
        storageEndpoints = null;

        TileEntity pipeEntity;
        var pipeTe = SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out pipeEntity) ? pipeEntity as TileEntityItemPipe : null;
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
        BumpGraphRouteVersion(graph.PipeGraphId);
        return graph;
    }

    public static void RemoveGraph(Guid pipeGraphId)
    {
        if (pipeGraphId == Guid.Empty)
            return;

        graphsById.Remove(pipeGraphId);
        RemoveGraphRouteState(pipeGraphId);
        lastGraphRebuildWorldTime.Remove(pipeGraphId);
    }

    public static void RegisterGraph(PipeGraphData graph)
    {
        if (graph == null || graph.PipeGraphId == Guid.Empty)
            return;

        graphsById[graph.PipeGraphId] = graph;
        BumpGraphRouteVersion(graph.PipeGraphId);
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

        ulong now = world.GetWorldTime();
        int processed = 0;
        List<Vector3i> batch = new List<Vector3i>(dirtyPipePositions);

        for (int i = 0; i < batch.Count && processed < maxGraphsToProcess; i++)
        {
            Vector3i seedPos = batch[i];

            if (!dirtyPipePositions.Contains(seedPos))
                continue;

            if (SafeWorldRead.TryGetTileEntity(world, 0, seedPos, out TileEntity seedTileEntity) && seedTileEntity is TileEntityItemPipe seedPipe)
            {
                Guid currentGraphId = seedPipe.PipeGraphId;
                if (currentGraphId != Guid.Empty &&
                    lastGraphRebuildWorldTime.TryGetValue(currentGraphId, out ulong lastRebuildTime) &&
                    now < lastRebuildTime + GraphRebuildCooldownTicks)
                {
                    continue;
                }
            }

            RebuildGraphFromSeed(world, 0, seedPos, now);
            processed++;
        }
    }

    private static void RebuildGraphFromSeed(WorldBase world, int clrIdx, Vector3i seedPos, ulong worldTime)
    {
        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, seedPos, out TileEntity seedTileEntity) || !(seedTileEntity is TileEntityItemPipe))
        {
            dirtyPipePositions.Remove(seedPos);
            return;
        }

        if (!SafeWorldRead.TryGetBlock(world, clrIdx, seedPos, out BlockValue seedValue))
        {
            dirtyPipePositions.Remove(seedPos);
            return;
        }
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
        Dictionary<Vector3i, ItemStack[]> carriedStorageSnapshots =
            new Dictionary<Vector3i, ItemStack[]>();

        foreach (Vector3i pipePos in connectedPipes)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity oldGraphPipeEntity) || !(oldGraphPipeEntity is TileEntityItemPipe pipeTe))
                continue;

            if (pipeTe.PipeGraphId == Guid.Empty)
                continue;

            oldGraphIds.Add(pipeTe.PipeGraphId);
        }

        foreach (Guid oldGraphId in oldGraphIds)
        {
            if (graphsById.TryGetValue(oldGraphId, out PipeGraphData oldGraph) &&
                oldGraph != null &&
                oldGraph.UnloadedStorageSnapshots != null)
            {
                foreach (var kvp in oldGraph.UnloadedStorageSnapshots)
                {
                    if (kvp.Value == null)
                        continue;

                    carriedStorageSnapshots[kvp.Key] = CloneSlots(kvp.Value);
                }
            }

            graphsById.Remove(oldGraphId);
            RemoveGraphRouteState(oldGraphId);
            lastGraphRebuildWorldTime.Remove(oldGraphId);

        }

        PipeGraphData newGraph = new PipeGraphData();

        foreach (Vector3i pipePos in connectedPipes)
        {
            newGraph.AddPipe(pipePos);

            if (SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity newGraphPipeEntity) && newGraphPipeEntity is TileEntityItemPipe pipeTe)
            {
                pipeTe.SetPipeGraphId(newGraph.PipeGraphId);
                pipeTe.setModified();
            }

            dirtyPipePositions.Remove(pipePos);
        }

        bool graphDevLoggingEnabled = IsDevLoggingEnabled(world, clrIdx, newGraph);
        CollectStorageEndpoints(world, clrIdx, newGraph, graphDevLoggingEnabled);
        RestoreSnapshotEndpoints(world, clrIdx, newGraph, carriedStorageSnapshots, graphDevLoggingEnabled);

        if (carriedStorageSnapshots.Count > 0)
        {
            foreach (Vector3i endpointPos in newGraph.StorageEndpoints)
            {
                if (!carriedStorageSnapshots.TryGetValue(endpointPos, out ItemStack[] snapshot) || snapshot == null)
                    continue;

                newGraph.SetStorageSnapshot(endpointPos, snapshot);
            }
        }

        newGraph.PruneStorageSnapshotsToEndpoints();
        graphsById[newGraph.PipeGraphId] = newGraph;
        BumpGraphRouteVersion(newGraph.PipeGraphId);
        lastGraphRebuildWorldTime[newGraph.PipeGraphId] = worldTime;

        if (graphDevLoggingEnabled)
            PipeGraphLog($"[PipeGraphManager] Rebuilt graph {newGraph.PipeGraphId} Pipes={newGraph.PipePositions.Count} StorageEndpoints={newGraph.StorageEndpoints.Count}");
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
            if (!SafeWorldRead.TryGetBlock(world, clrIdx, current, out BlockValue currentValue))
                continue;

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

    private static void CollectStorageEndpoints(WorldBase world, int clrIdx, PipeGraphData graph, bool devLoggingEnabled)
    {
        foreach (Vector3i pipePos in graph.PipePositions)
        {
            if (!SafeWorldRead.TryGetBlock(world, clrIdx, pipePos, out BlockValue pipeValue))
                continue;

            foreach (Vector3i neighborPos in GetNeighborPositions(pipePos))
            {
                if (!IsStorageConnectedNeighbor(world, clrIdx, pipePos, pipeValue, neighborPos, devLoggingEnabled))
                    continue;

                graph.AddStorageEndpoint(neighborPos);
                if (devLoggingEnabled)
                    PipeGraphLog($"[PipeGraph] Added storage endpoint {neighborPos} to graph {graph.PipeGraphId} count={graph.StorageEndpoints.Count}");
            }
        }
        if (devLoggingEnabled)
            PipeGraphLog($"[PipeGraph] Graph {graph.PipeGraphId} final storage endpoint count={graph.StorageEndpoints.Count}");
    }

    private static void RestoreSnapshotEndpoints(
        WorldBase world,
        int clrIdx,
        PipeGraphData graph,
        Dictionary<Vector3i, ItemStack[]> carriedStorageSnapshots,
        bool devLoggingEnabled)
    {
        if (graph == null || carriedStorageSnapshots == null || carriedStorageSnapshots.Count == 0)
            return;

        foreach (var kvp in carriedStorageSnapshots)
        {
            Vector3i storagePos = kvp.Key;
            ItemStack[] snapshot = kvp.Value;
            if (storagePos == Vector3i.zero || snapshot == null)
                continue;

            // Snapshot endpoints are only valid if the current graph still has
            // at least one pipe side connected to this storage position.
            if (!IsPositionConnectedToGraphPipe(world, clrIdx, graph, storagePos))
                continue;

            // Keep unloaded storage endpoints alive when they have a cached slot snapshot.
            // This allows graph-side simulation to continue while the storage chunk is out.
            if (!graph.ContainsStorageEndpoint(storagePos))
                graph.AddStorageEndpoint(storagePos);

            if (devLoggingEnabled)
            {
                PipeGraphLog(
                    $"[PipeGraphSnapshot] Restored endpoint from snapshot " +
                    $"pos={storagePos} graph={graph.PipeGraphId} slots={snapshot.Length}");
            }
        }
    }

    private static bool IsPositionConnectedToGraphPipe(WorldBase world, int clrIdx, PipeGraphData graph, Vector3i storagePos)
    {
        if (world == null || graph == null || storagePos == Vector3i.zero)
            return false;

        foreach (Vector3i neighborPos in GetNeighborPositions(storagePos))
        {
            if (!graph.PipePositions.Contains(neighborPos))
                continue;

            if (!SafeWorldRead.TryGetBlock(world, clrIdx, neighborPos, out BlockValue pipeValue))
                continue;

            if (!(pipeValue.Block is ItemPipeBlock))
                continue;

            HashSet<Vector3i> openSides = ItemPipeBlock.GetOpenSides(pipeValue);
            Vector3i delta = storagePos - neighborPos;
            if (openSides.Contains(delta))
                return true;
        }

        return false;
    }

    public static int RemoveStorageSnapshotAtPosition(Vector3i storagePos, bool removeEndpoint = true)
    {
        if (storagePos == Vector3i.zero || graphsById.Count == 0)
            return 0;

        int changedGraphs = 0;
        List<Guid> changedGraphIds = null;

        foreach (var kvp in graphsById)
        {
            PipeGraphData graph = kvp.Value;
            if (graph == null)
                continue;

            bool changed = false;

            if (graph.UnloadedStorageSnapshots.ContainsKey(storagePos))
            {
                graph.RemoveStorageSnapshot(storagePos);
                changed = true;
            }

            if (removeEndpoint && graph.StorageEndpoints.Contains(storagePos))
            {
                graph.StorageEndpoints.Remove(storagePos);
                changed = true;
            }

            if (!changed)
                continue;

            changedGraphs++;
            if (changedGraphIds == null)
                changedGraphIds = new List<Guid>();

            changedGraphIds.Add(kvp.Key);
        }

        if (changedGraphIds != null)
        {
            for (int i = 0; i < changedGraphIds.Count; i++)
                BumpGraphRouteVersion(changedGraphIds[i]);
        }

        return changedGraphs;
    }

    private static bool IsStorageConnectedNeighbor(
        WorldBase world,
        int clrIdx,
        Vector3i pipePos,
        BlockValue pipeValue,
        Vector3i neighborPos,
        bool devLoggingEnabled)
    {
        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out TileEntity te))
            return false;
        string blockName = "unknown";
        if (SafeWorldRead.TryGetBlock(world, neighborPos, out BlockValue neighborBlockValue))
            blockName = neighborBlockValue.Block?.GetBlockName() ?? "null";

        if (devLoggingEnabled)
            PipeGraphLog($"[PipeGraph] Storage check at {neighborPos} block={blockName} te={(te == null ? "null" : te.GetType().Name)}");

        if (te is TileEntityItemPipe)
            return false;

        if (te is TileEntityNetworkController)
            return false;

        if (!(te is TileEntityComposite composite))
            return false;

        TEFeatureStorage storage = composite.GetFeature<TEFeatureStorage>();
        if (devLoggingEnabled)
            PipeGraphLog($"[PipeGraph] Composite storage feature={(storage == null ? "null" : "found")}");

        if (storage == null || storage.items == null)
            return false;

        HashSet<Vector3i> openSides = ItemPipeBlock.GetOpenSides(pipeValue);
        Vector3i delta = neighborPos - pipePos;

        return openSides.Contains(delta);
    }

    public static void CaptureStorageSnapshotForPosition(WorldBase world, int clrIdx, Vector3i storagePos)
    {
        if (world == null || world.IsRemote() || storagePos == Vector3i.zero)
            return;

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, storagePos, out TileEntity storageEntity) || !(storageEntity is TileEntityComposite comp))
            return;

        TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
            return;

        ItemStack[] slotSnapshot = CloneSlots(storage.items);
        HashSet<Guid> adjacentGraphIds = GetAdjacentPipeGraphIds(world, clrIdx, storagePos);
        HashSet<Guid> endpointGraphIds = GetGraphsContainingStorageEndpoint(storagePos);
        foreach (Guid endpointGraphId in endpointGraphIds)
            adjacentGraphIds.Add(endpointGraphId);
        SummarizeSlots(slotSnapshot, out int itemTypes, out int totalItems, out int nonEmptySlots);

        int savedToGraphs = 0;

        foreach (Guid graphId in adjacentGraphIds)
        {
            if (!graphsById.TryGetValue(graphId, out PipeGraphData graph) || graph == null)
                continue;

            if (!graph.ContainsStorageEndpoint(storagePos))
                continue;

            graph.SetStorageSnapshot(storagePos, slotSnapshot);
            savedToGraphs++;

            PipeGraphLog($"[PipeGraphSnapshot] Captured unload snapshot pos={storagePos} graph={graphId} slots={slotSnapshot.Length} nonEmpty={nonEmptySlots} itemTypes={itemTypes} totalItems={totalItems}");
        }

        if (savedToGraphs == 0)
        {
            PipeGraphLog($"[PipeGraphSnapshot] Storage unloaded without graph endpoint match pos={storagePos} adjacentGraphs={adjacentGraphIds.Count} slots={slotSnapshot.Length} nonEmpty={nonEmptySlots} itemTypes={itemTypes} totalItems={totalItems}");
        }
    }

    public static void TryApplyStorageSnapshotForPosition(WorldBase world, int clrIdx, Vector3i storagePos)
    {
        if (world == null || world.IsRemote() || storagePos == Vector3i.zero)
            return;

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, storagePos, out TileEntity storageEntity) || !(storageEntity is TileEntityComposite comp))
            return;

        TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
            return;

        HashSet<Guid> adjacentGraphIds = GetAdjacentPipeGraphIds(world, clrIdx, storagePos);
        HashSet<Guid> endpointGraphIds = GetGraphsContainingStorageEndpoint(storagePos);
        foreach (Guid endpointGraphId in endpointGraphIds)
            adjacentGraphIds.Add(endpointGraphId);
        if (adjacentGraphIds.Count == 0)
            return;

        ItemStack[] liveSlots = CloneSlots(storage.items);

        foreach (Guid graphId in adjacentGraphIds)
        {
            if (!graphsById.TryGetValue(graphId, out PipeGraphData graph) || graph == null)
                continue;

            if (!graph.ContainsStorageEndpoint(storagePos))
                continue;

            if (!graph.TryGetStorageSnapshot(storagePos, out ItemStack[] snapshotSlots) || snapshotSlots == null)
                continue;

            if (AreSlotsEqual(liveSlots, snapshotSlots))
            {
                SummarizeSlots(liveSlots, out int unchangedItemTypes, out int unchangedTotalItems, out int unchangedNonEmptySlots);
                PipeGraphLog($"[PipeGraphSnapshot] Reapply skipped (not dirty) pos={storagePos} graph={graphId} slots={liveSlots.Length} nonEmpty={unchangedNonEmptySlots} itemTypes={unchangedItemTypes} totalItems={unchangedTotalItems}");
                RemoveStorageSnapshotAtPosition(storagePos, false);
                continue;
            }

            SummarizeSlots(liveSlots, out int oldItemTypes, out int oldTotalItems, out int oldNonEmptySlots);
            SummarizeSlots(snapshotSlots, out int newItemTypes, out int newTotalItems, out int newNonEmptySlots);

            int droppedStacks = ApplySlotSnapshotToStorage(storage, snapshotSlots, out int droppedItems);
            storage.SetModified();

            PipeGraphLog($"[PipeGraphSnapshot] Reapplied snapshot pos={storagePos} graph={graphId} oldSlots={liveSlots.Length} oldNonEmpty={oldNonEmptySlots} oldTypes={oldItemTypes} oldTotal={oldTotalItems} newSlots={snapshotSlots.Length} newNonEmpty={newNonEmptySlots} newTypes={newItemTypes} newTotal={newTotalItems} droppedStacks={droppedStacks} droppedItems={droppedItems}");

            RemoveStorageSnapshotAtPosition(storagePos, false);
            return;
        }
    }

    public static bool TryGetStorageItemCounts(
    WorldBase world,
    int clrIdx,
    ref Guid pipeGraphId,
    Vector3i storagePos,
    out Dictionary<string, int> itemCounts)
    {
        itemCounts = new Dictionary<string, int>();

        if (world == null || storagePos == Vector3i.zero)
        {
            Log.Warning($"[PipeGraphManager] Invalid storage count request world={world} storagePos={storagePos}");
            return false;
        }

        PipeGraphData graph = null;
        bool hadPreferredGraph = pipeGraphId != Guid.Empty &&
                                 graphsById.TryGetValue(pipeGraphId, out graph) &&
                                 graph != null &&
                                 (graph.ContainsStorageEndpoint(storagePos) ||
                                  (graph.TryGetStorageSnapshot(storagePos, out ItemStack[] preferredSnapshot) && preferredSnapshot != null));

        if (!hadPreferredGraph)
        {
            Guid oldGraphId = pipeGraphId;

            if (!TryResolveGraphIdByStorageEndpoint(storagePos, out Guid resolvedGraphId))
            {
                PipeGraphLog($"[PipeGraphManager] No graph endpoint match for storagePos={storagePos} requestedGraphId={pipeGraphId}");
                return false;
            }

            pipeGraphId = resolvedGraphId;

            if (!graphsById.TryGetValue(pipeGraphId, out graph) || graph == null)
            {
                Log.Warning($"[PipeGraphManager] Resolved graph missing for pipeGraphId={pipeGraphId} storagePos={storagePos}");
                return false;
            }

            bool resolvedHasStorage = graph.ContainsStorageEndpoint(storagePos) ||
                                      (graph.TryGetStorageSnapshot(storagePos, out ItemStack[] resolvedSnapshot) && resolvedSnapshot != null);
            if (!resolvedHasStorage)
            {
                Log.Warning($"[PipeGraphManager] Resolved graph {pipeGraphId} does not contain storage {storagePos}");
                return false;
            }

            if (oldGraphId != Guid.Empty && oldGraphId != pipeGraphId)
                PipeGraphLog($"[PipeGraphManager] Rebound stale graph id {oldGraphId} -> {pipeGraphId} for storagePos={storagePos}");
        }

        if (!graph.ContainsStorageEndpoint(storagePos))
        {
            Log.Warning($"[PipeGraphManager] storagePos {storagePos} is not an endpoint in graph {pipeGraphId}");
            return false;
        }

        if (SafeWorldRead.TryGetTileEntity(world, clrIdx, storagePos, out TileEntity storageEntity) &&
            storageEntity is TileEntityComposite comp)
        {
            TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
            if (storage != null && storage.items != null)
            {
                // Live container state is authoritative whenever the storage is loaded.
                itemCounts = BuildItemCountsFromSlots(storage.items);
                RemoveStorageSnapshotAtPosition(storagePos, false);
                return true;
            }

            Log.Warning($"[PipeGraphManager] storage TE or items null for storagePos={storagePos}");
            return false;
        }

        if (graph.TryGetStorageSnapshot(storagePos, out ItemStack[] snapshotSlots) && snapshotSlots != null)
        {
            itemCounts = BuildItemCountsFromSlots(snapshotSlots);
            return true;
        }

        Log.Warning($"[PipeGraphManager] storage TE not found and no snapshot for storagePos={storagePos}");
        return false;
    }


    public static bool TryFindFirstMatchingStorageItem(
        WorldBase world,
        int clrIdx,
        Guid pipeGraphId,
        Vector3i storagePos,
        HashSet<string> candidateItemNames,
        out string matchedItemName,
        out int matchedCount,
        out string blockedReason)
    {
        matchedItemName = string.Empty;
        matchedCount = 0;
        blockedReason = "Unknown";

        if (world == null || pipeGraphId == Guid.Empty || storagePos == Vector3i.zero)
        {
            blockedReason = "Invalid input graph selection";
            return false;
        }

        if (candidateItemNames == null || candidateItemNames.Count == 0)
        {
            blockedReason = "No conversion rules for selected fluid";
            return false;
        }

        if (!graphsById.TryGetValue(pipeGraphId, out PipeGraphData graph) || graph == null)
        {
            blockedReason = "Input graph unavailable";
            return false;
        }

        if (!TryGetStorageItemCounts(world, clrIdx, ref pipeGraphId, storagePos, out Dictionary<string, int> itemCounts))
        {
            blockedReason = "Input storage unavailable";
            return false;
        }

        if (itemCounts == null || itemCounts.Count == 0)
        {
            blockedReason = "Input storage empty";
            return false;
        }

        List<string> orderedCandidates = new List<string>(candidateItemNames);
        orderedCandidates.Sort(StringComparer.Ordinal);

        for (int i = 0; i < orderedCandidates.Count; i++)
        {
            string itemName = orderedCandidates[i];
            if (string.IsNullOrEmpty(itemName))
                continue;

            if (!itemCounts.TryGetValue(itemName, out int count) || count <= 0)
                continue;

            matchedItemName = itemName;
            matchedCount = count;
            blockedReason = string.Empty;
            return true;
        }

        blockedReason = "No matching input item";
        return false;
    }

    public static bool TryConsumeStorageItems(
        WorldBase world,
        int clrIdx,
        Guid pipeGraphId,
        Vector3i storagePos,
        Dictionary<string, int> requested,
        out Dictionary<string, int> consumed)
    {
        consumed = new Dictionary<string, int>();

        if (requested == null || requested.Count == 0)
            return false;

        if (!TryGetMutableStorageSlots(world, clrIdx, pipeGraphId, storagePos, out PipeGraphData graph, out ItemStack[] slots, out TEFeatureStorage liveStorage, out bool usingSnapshot))
            return false;

        Dictionary<string, int> available = BuildItemCountsFromSlots(slots);

        foreach (var kvp in requested)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            if (!available.TryGetValue(kvp.Key, out int count) || count < kvp.Value)
                return false;
        }

        foreach (var kvp in requested)
        {
            string itemName = kvp.Key;
            int remaining = kvp.Value;

            if (string.IsNullOrEmpty(itemName) || remaining <= 0)
                continue;

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                ItemStack stack = slots[i];
                if (stack.IsEmpty() || stack.count <= 0 || stack.itemValue?.ItemClass == null)
                    continue;

                if (!string.Equals(stack.itemValue.ItemClass.GetItemName(), itemName, StringComparison.Ordinal))
                    continue;

                int remove = Math.Min(stack.count, remaining);
                stack.count -= remove;
                remaining -= remove;

                if (stack.count <= 0)
                    slots[i] = ItemStack.Empty;
                else
                    slots[i] = stack;
            }

            consumed[itemName] = kvp.Value - remaining;
        }

        PersistMutatedStorageSlots(graph, storagePos, slots, liveStorage, usingSnapshot);
        return consumed.Count > 0;
    }

    public static bool TryDepositStorageItems(
        WorldBase world,
        int clrIdx,
        Guid pipeGraphId,
        Vector3i storagePos,
        Dictionary<string, int> toDeposit,
        out Dictionary<string, int> deposited)
    {
        deposited = new Dictionary<string, int>();

        if (toDeposit == null || toDeposit.Count == 0)
            return false;

        if (!TryGetMutableStorageSlots(world, clrIdx, pipeGraphId, storagePos, out PipeGraphData graph, out ItemStack[] slots, out TEFeatureStorage liveStorage, out bool usingSnapshot))
            return false;

        foreach (var kvp in toDeposit)
        {
            string itemName = kvp.Key;
            int original = kvp.Value;
            int remaining = original;

            if (string.IsNullOrEmpty(itemName) || original <= 0)
                continue;

            ItemValue itemValue = ItemClass.GetItem(itemName, false);
            if (itemValue == null || itemValue.type == ItemValue.None.type || itemValue.ItemClass == null)
                continue;

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                ItemStack slot = slots[i];
                if (slot.IsEmpty() || slot.count <= 0 || slot.itemValue == null)
                    continue;

                if (slot.itemValue.type != itemValue.type)
                    continue;

                int maxStack = slot.itemValue.ItemClass.Stacknumber.Value;
                if (maxStack <= 0)
                    maxStack = 1;

                int space = maxStack - slot.count;
                if (space <= 0)
                    continue;

                int move = Math.Min(space, remaining);
                slot.count += move;
                remaining -= move;
                slots[i] = slot;
            }

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty())
                    continue;

                int maxStack = itemValue.ItemClass.Stacknumber.Value;
                if (maxStack <= 0)
                    maxStack = 1;

                int move = Math.Min(maxStack, remaining);
                slots[i] = new ItemStack(itemValue.Clone(), move);
                remaining -= move;
            }

            int accepted = original - remaining;
            if (accepted > 0)
                deposited[itemName] = accepted;
        }

        if (deposited.Count == 0)
            return false;

        PersistMutatedStorageSlots(graph, storagePos, slots, liveStorage, usingSnapshot);
        return true;
    }

    private static bool TryGetMutableStorageSlots(
        WorldBase world,
        int clrIdx,
        Guid pipeGraphId,
        Vector3i storagePos,
        out PipeGraphData graph,
        out ItemStack[] slots,
        out TEFeatureStorage liveStorage,
        out bool usingSnapshot)
    {
        graph = null;
        slots = null;
        liveStorage = null;
        usingSnapshot = false;

        if (world == null || pipeGraphId == Guid.Empty || storagePos == Vector3i.zero)
            return false;

        if (!graphsById.TryGetValue(pipeGraphId, out graph) || graph == null)
            return false;

        if (!graph.ContainsStorageEndpoint(storagePos))
            return false;

        if (SafeWorldRead.TryGetTileEntity(world, clrIdx, storagePos, out TileEntity storageEntity) &&
            storageEntity is TileEntityComposite comp)
        {
            liveStorage = comp.GetFeature<TEFeatureStorage>();
            if (liveStorage == null || liveStorage.items == null)
                return false;

            slots = CloneSlots(liveStorage.items);
            RemoveStorageSnapshotAtPosition(storagePos, false);
            return true;
        }

        if (graph.TryGetStorageSnapshot(storagePos, out ItemStack[] snapshotSlots) && snapshotSlots != null)
        {
            slots = CloneSlots(snapshotSlots);
            usingSnapshot = true;
            return true;
        }

        return false;
    }

    private static void PersistMutatedStorageSlots(PipeGraphData graph, Vector3i storagePos, ItemStack[] slots, TEFeatureStorage liveStorage, bool usingSnapshot)
    {
        if (graph == null || slots == null)
            return;

        if (usingSnapshot)
        {
            SetStorageSnapshotAtPosition(storagePos, slots);
            return;
        }

        if (liveStorage?.items == null)
            return;

        ItemStack[] target = liveStorage.items;
        int copyCount = Math.Min(target.Length, slots.Length);

        for (int i = 0; i < copyCount; i++)
        {
            ItemStack stack = slots[i];
            if (stack.IsEmpty() || stack.count <= 0 || stack.itemValue == null)
                target[i] = ItemStack.Empty;
            else
                target[i] = new ItemStack(stack.itemValue.Clone(), stack.count);
        }

        for (int i = copyCount; i < target.Length; i++)
            target[i] = ItemStack.Empty;

        liveStorage.SetModified();
    }

    private static Dictionary<string, int> BuildItemCountsFromSlots(ItemStack[] slots)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();

        if (slots == null || slots.Length == 0)
            return counts;

        for (int i = 0; i < slots.Length; i++)
        {
            ItemStack stack = slots[i];
            if (stack.IsEmpty() || stack.count <= 0 || stack.itemValue?.ItemClass == null)
                continue;

            string itemName = stack.itemValue.ItemClass.GetItemName();
            if (string.IsNullOrEmpty(itemName))
                continue;

            if (counts.TryGetValue(itemName, out int existing))
                counts[itemName] = existing + stack.count;
            else
                counts[itemName] = stack.count;
        }

        return counts;
    }

    private static ItemStack[] CloneSlots(ItemStack[] source)
    {
        if (source == null)
            return new ItemStack[0];

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

    private static HashSet<Guid> GetGraphsContainingStorageEndpoint(Vector3i storagePos)
    {
        HashSet<Guid> graphIds = new HashSet<Guid>();
        if (storagePos == Vector3i.zero)
            return graphIds;

        foreach (var kvp in graphsById)
        {
            PipeGraphData graph = kvp.Value;
            if (graph == null || graph.StorageEndpoints == null)
                continue;

            if (graph.StorageEndpoints.Contains(storagePos))
                graphIds.Add(kvp.Key);
        }

        return graphIds;
    }

    private static int SetStorageSnapshotAtPosition(Vector3i storagePos, ItemStack[] slots)
    {
        if (storagePos == Vector3i.zero || graphsById.Count == 0)
            return 0;

        int changedGraphs = 0;
        ItemStack[] snapshot = CloneSlots(slots);

        foreach (var kvp in graphsById)
        {
            PipeGraphData graph = kvp.Value;
            if (graph == null || graph.StorageEndpoints == null || !graph.StorageEndpoints.Contains(storagePos))
                continue;

            graph.SetStorageSnapshot(storagePos, snapshot);
            changedGraphs++;
        }

        return changedGraphs;
    }

    private static bool AreSlotsEqual(ItemStack[] a, ItemStack[] b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a == null || b == null)
            return false;

        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            ItemStack left = a[i];
            ItemStack right = b[i];

            bool leftEmpty = left.IsEmpty() || left.count <= 0 || left.itemValue == null;
            bool rightEmpty = right.IsEmpty() || right.count <= 0 || right.itemValue == null;

            if (leftEmpty != rightEmpty)
                return false;

            if (leftEmpty)
                continue;

            if (left.count != right.count)
                return false;

            if (!left.itemValue.Equals(right.itemValue))
                return false;
        }

        return true;
    }

    private static void SummarizeSlots(ItemStack[] slots, out int itemTypes, out int totalItems, out int nonEmptySlots)
    {
        itemTypes = 0;
        totalItems = 0;
        nonEmptySlots = 0;

        if (slots == null || slots.Length == 0)
            return;

        HashSet<string> types = new HashSet<string>();

        for (int i = 0; i < slots.Length; i++)
        {
            ItemStack stack = slots[i];
            if (stack.IsEmpty() || stack.count <= 0 || stack.itemValue?.ItemClass == null)
                continue;

            nonEmptySlots++;
            totalItems += stack.count;
            types.Add(stack.itemValue.ItemClass.GetItemName());
        }

        itemTypes = types.Count;
    }

    private static int ApplySlotSnapshotToStorage(TEFeatureStorage storage, ItemStack[] snapshotSlots, out int droppedItems)
    {
        droppedItems = 0;

        if (storage == null || storage.items == null)
            return 0;

        ItemStack[] target = storage.items;
        int slotsToWrite = Math.Min(target.Length, snapshotSlots?.Length ?? 0);

        for (int i = 0; i < target.Length; i++)
            target[i] = ItemStack.Empty;

        int droppedStacks = 0;

        for (int i = 0; i < slotsToWrite; i++)
        {
            ItemStack snapshot = snapshotSlots[i];
            if (snapshot.IsEmpty() || snapshot.count <= 0 || snapshot.itemValue == null)
            {
                target[i] = ItemStack.Empty;
                continue;
            }

            target[i] = new ItemStack(snapshot.itemValue.Clone(), snapshot.count);
        }

        if (snapshotSlots != null && snapshotSlots.Length > target.Length)
        {
            for (int i = target.Length; i < snapshotSlots.Length; i++)
            {
                ItemStack snapshot = snapshotSlots[i];
                if (snapshot.IsEmpty() || snapshot.count <= 0)
                    continue;

                droppedStacks++;
                droppedItems += snapshot.count;
            }
        }

        return droppedStacks;
    }

    private static HashSet<Guid> GetAdjacentPipeGraphIds(WorldBase world, int clrIdx, Vector3i storagePos)
    {
        HashSet<Guid> graphIds = new HashSet<Guid>();

        foreach (Vector3i neighborPos in GetNeighborPositions(storagePos))
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out TileEntity neighborEntity) || !(neighborEntity is TileEntityItemPipe pipeTe))
                continue;

            if (pipeTe.PipeGraphId == Guid.Empty)
                continue;

            graphIds.Add(pipeTe.PipeGraphId);
        }

        return graphIds;
    }

    private static bool ApplyLoadedGraphToWorld(
        WorldBase world,
        int clrIdx,
        PipeGraphData graph,
        HashSet<Vector3i> assignedPipePositions)
    {
        if (world == null || graph == null || graph.PipeGraphId == Guid.Empty || graph.PipePositions.Count == 0)
            return false;

        foreach (Vector3i pipePos in graph.PipePositions)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity tileEntity) || !(tileEntity is TileEntityItemPipe pipe))
                continue;

            if (!SafeWorldRead.TryGetBlock(world, clrIdx, pipePos, out BlockValue pipeValue) || !(pipeValue.Block is ItemPipeBlock))
                continue;

            pipe.SetPipeGraphId(graph.PipeGraphId);
            pipe.setModified();
            assignedPipePositions?.Add(pipePos);
        }

        return true;
    }

    private static List<Vector3i> CollectAllPipePositions(WorldBase world)
    {
        List<Vector3i> pipePositions = new List<Vector3i>();
        if (world == null)
            return pipePositions;

        foreach (Chunk chunk in SafeWorldRead.GetChunkArraySnapshot(world))
        {
            if (chunk == null)
                continue;

            List<TileEntity> snapshot = SnapshotTileEntities(chunk);
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] is TileEntityItemPipe pipe)
                    pipePositions.Add(pipe.ToWorldPos());
            }
        }

        return pipePositions;
    }

    private static void WriteVector3i(BinaryWriter writer, Vector3i value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    private static Vector3i ReadVector3i(BinaryReader reader)
    {
        int x = reader.ReadInt32();
        int y = reader.ReadInt32();
        int z = reader.ReadInt32();
        return new Vector3i(x, y, z);
    }

    private static void WriteSlotSnapshot(BinaryWriter writer, ItemStack[] slots)
    {
        if (slots == null)
        {
            writer.Write(0);
            return;
        }

        writer.Write(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            ItemStack slot = slots[i];
            bool hasItem = !(slot.IsEmpty() || slot.count <= 0 || slot.itemValue?.ItemClass == null);
            writer.Write(hasItem);
            if (!hasItem)
                continue;

            writer.Write(slot.itemValue.ItemClass.GetItemName() ?? string.Empty);
            writer.Write(slot.count);
        }
    }

    private static ItemStack[] ReadSlotSnapshot(BinaryReader reader)
    {
        int slotCount = Math.Max(0, reader.ReadInt32());
        ItemStack[] slots = new ItemStack[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            bool hasItem = reader.ReadBoolean();
            if (!hasItem)
            {
                slots[i] = ItemStack.Empty;
                continue;
            }

            string itemName = reader.ReadString();
            int count = reader.ReadInt32();

            if (string.IsNullOrEmpty(itemName) || count <= 0)
            {
                slots[i] = ItemStack.Empty;
                continue;
            }

            ItemValue itemValue = ItemClass.GetItem(itemName, false);
            if (itemValue == null || itemValue.type == ItemValue.None.type || itemValue.ItemClass == null)
            {
                slots[i] = ItemStack.Empty;
                continue;
            }

            slots[i] = new ItemStack(itemValue.Clone(), count);
        }

        return slots;
    }

    private static int ComparePositions(Vector3i a, Vector3i b)
    {
        int x = a.x.CompareTo(b.x);
        if (x != 0)
            return x;

        int y = a.y.CompareTo(b.y);
        if (y != 0)
            return y;

        return a.z.CompareTo(b.z);
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













