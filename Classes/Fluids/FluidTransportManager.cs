using System;
using System.Collections.Generic;

public static class FluidTransportManager
{
    private const string ReasonNoPump = "NoActivePump";
    private const string ReasonNoStorage = "NoStorage";
    private const string ReasonStorageFull = "AllStorageFull";
    private const string ReasonNoSource = "NoSourceFluid";
    private const string ReasonTypeMismatch = "TypeMismatch";

    private const string FluidWater = "water";
    private const string IntakePropOutputFluid = "FluidIntakeOutputFluid";
    private const string IntakePropAllowedBlocks = "FluidIntakeAllowedBlocks";
    private const string IntakePropAllowWorldWaterVoxel = "FluidIntakeAllowWorldWaterVoxel";
    private const string IntakePropDisallowNameContains = "FluidIntakeDisallowNameContains";

    private static ulong lastProcessWorldTime = 0UL;

    private sealed class StorageRuntime
    {
        public Vector3i Pos;
        public TileEntityFluidStorage Storage;
        public FluidGraphData.StorageSnapshot Snapshot;
        public bool UsingSnapshot;
        public int RemainingInputMg;
        public int RemainingFreeSpaceMg;
        public int RemainingOutputMg;
        public int AllocatedIntakeMg;
        public int AllocatedDrainMg;
    }

    private sealed class PumpResolution
    {
        public readonly List<TileEntityFluidPump> LivePumps = new List<TileEntityFluidPump>();
        public int ActivePumpCount;
        public int TotalPumpCapMgPerTick;
    }

    public static void Process(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        ulong now = world.GetWorldTime();
        if (now == lastProcessWorldTime)
            return;

        lastProcessWorldTime = now;

        if (now % (ulong)FluidConstants.WorldTicksPerSimulationTick != 0UL)
            return;

        if (now < FluidGraphManager.LastRebuildWorldTime)
            return;

        foreach (var kvp in FluidGraphManager.GetAllGraphs())
            ProcessGraph(world, 0, kvp.Value, now);
    }

    private static void ProcessGraph(WorldBase world, int clrIdx, FluidGraphData graph, ulong now)
    {
        if (graph == null || graph.PipePositions.Count == 0)
            return;

        PumpResolution pumpState = ResolveActivePumps(world, clrIdx, graph);
        if (pumpState.ActivePumpCount == 0)
        {
            RecordBlocked(graph, pumpState.LivePumps, now, ReasonNoPump, "No enabled pump connected to graph");
            return;
        }

        List<StorageRuntime> storage = ResolveStorage(world, clrIdx, graph, now);
        if (storage.Count == 0)
        {
            RecordBlocked(graph, pumpState.LivePumps, now, ReasonNoStorage, "No fluid storage connected to graph");
            return;
        }

        int pipeCap = GetGraphPipeCapMgPerTick(world, clrIdx, graph);
        int pumpCap = pumpState.TotalPumpCapMgPerTick;
        int flowBudget = Math.Min(pipeCap, pumpCap);

        if (flowBudget <= 0)
            return;

        string graphFluidType = ResolveGraphFluidType(graph, storage);
        string intakeResolvedFluidType = ResolveIntakeSourceMgPerTick(world, clrIdx, graph, graphFluidType, out int sourceAvailableFromIntakes);

        if (string.IsNullOrEmpty(graphFluidType) && !string.IsNullOrEmpty(intakeResolvedFluidType))
            graphFluidType = intakeResolvedFluidType;

        if (string.IsNullOrEmpty(graphFluidType))
            return;

        graph.FluidType = graphFluidType;

        bool hasMismatchedSource = HasMismatchedSourceType(storage, graphFluidType);
        if (hasMismatchedSource)
        {
            RecordBlocked(graph, pumpState.LivePumps, now, ReasonTypeMismatch, $"Graph fluid={graphFluidType} has mismatched stored fluid");
            return;
        }

        // Re-evaluate intake allowance after fluid type is finalized.
        ResolveIntakeSourceMgPerTick(world, clrIdx, graph, graphFluidType, out sourceAvailableFromIntakes);

        List<StorageRuntime> sinkNodes = new List<StorageRuntime>();

        for (int i = 0; i < storage.Count; i++)
        {
            StorageRuntime node = storage[i];
            PrepareStorageNodeForTick(node, now);
            node.RemainingInputMg = GetStorageNodeRemainingInputBudgetMg(node, now);
            node.RemainingFreeSpaceMg = GetStorageNodeFreeSpaceMg(node);
            node.RemainingOutputMg = GetStorageNodeAvailableOutputMg(node);
            node.AllocatedIntakeMg = 0;
            node.AllocatedDrainMg = 0;

            if (!CanStorageNodeAcceptType(node, graphFluidType))
                continue;

            if (node.RemainingInputMg > 0 && node.RemainingFreeSpaceMg > 0)
                sinkNodes.Add(node);
        }

        if (sinkNodes.Count == 0)
        {
            RecordBlocked(graph, pumpState.LivePumps, now, ReasonStorageFull, "All storage is full or input-capped");
            return;
        }

        int sourceAvailable = sourceAvailableFromIntakes;
        if (sourceAvailable <= 0)
        {
            RecordBlocked(graph, pumpState.LivePumps, now, ReasonNoSource, "No intake has available fluid");
            return;
        }

        if (flowBudget > sourceAvailable)
            flowBudget = sourceAvailable;

        int acceptedTotal = AllocateAcrossSinks(flowBudget, sinkNodes);
        if (acceptedTotal <= 0)
        {
            RecordBlocked(graph, pumpState.LivePumps, now, ReasonStorageFull, "Storage could not accept flow this tick");
            return;
        }

        int providedTotal = Math.Min(sourceAvailableFromIntakes, acceptedTotal);
        if (providedTotal <= 0)
            return;

        if (providedTotal < acceptedTotal)
        {
            int reduction = acceptedTotal - providedTotal;
            RollbackSinkAllocations(sinkNodes, reduction);
            acceptedTotal = providedTotal;
        }

        ApplySinkIntake(graph, graphFluidType, sinkNodes, now);
    }

    private static PumpResolution ResolveActivePumps(WorldBase world, int clrIdx, FluidGraphData graph)
    {
        PumpResolution state = new PumpResolution();
        if (graph == null || graph.PumpEndpoints == null || graph.PumpEndpoints.Count == 0)
            return state;

        foreach (Vector3i pos in graph.PumpEndpoints)
        {
            if (SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity te) && te is TileEntityFluidPump pump)
            {
                if (!pump.IsActivePump())
                    continue;

                int cap = pump.GetOutputCapMgPerTick();
                if (cap <= 0)
                    continue;

                state.LivePumps.Add(pump);
                state.ActivePumpCount++;
                state.TotalPumpCapMgPerTick += cap;
                continue;
            }

            if (!graph.TryGetPumpSnapshot(pos, out FluidGraphData.PumpSnapshot snapshot) || snapshot == null)
                continue;

            if (!snapshot.PumpEnabled || snapshot.OutputCapMgPerTick <= 0)
                continue;

            state.ActivePumpCount++;
            state.TotalPumpCapMgPerTick += snapshot.OutputCapMgPerTick;
        }

        return state;
    }

    private static List<StorageRuntime> ResolveStorage(WorldBase world, int clrIdx, FluidGraphData graph, ulong now)
    {
        List<StorageRuntime> storage = new List<StorageRuntime>();
        if (graph == null || graph.StorageEndpoints == null)
            return storage;

        foreach (Vector3i pos in graph.StorageEndpoints)
        {
            if (SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity te) && te is TileEntityFluidStorage tank)
            {
                storage.Add(new StorageRuntime
                {
                    Pos = pos,
                    Storage = tank,
                    UsingSnapshot = false
                });
                continue;
            }

            if (!graph.TryGetStorageSnapshot(pos, out FluidGraphData.StorageSnapshot snapshot) || snapshot == null)
                continue;

            if (snapshot.LastInputBudgetWorldTime != now)
            {
                snapshot.LastInputBudgetWorldTime = now;
                snapshot.AcceptedThisTickMg = 0;
            }

            storage.Add(new StorageRuntime
            {
                Pos = pos,
                Snapshot = snapshot,
                UsingSnapshot = true
            });
        }

        storage.Sort((a, b) => ComparePos(a.Pos, b.Pos));
        return storage;
    }

    private static string ResolveGraphFluidType(FluidGraphData graph, List<StorageRuntime> storage)
    {
        if (graph == null)
            return string.Empty;

        if (!string.IsNullOrEmpty(graph.FluidType))
            return graph.FluidType;

        string found = string.Empty;
        for (int i = 0; i < storage.Count; i++)
        {
            string localType = GetStorageNodeFluidType(storage[i]);
            int localAmount = GetStorageNodeFluidAmountMg(storage[i]);
            if (string.IsNullOrEmpty(localType) || localAmount <= 0)
                continue;

            if (string.IsNullOrEmpty(found))
            {
                found = localType;
                continue;
            }

            if (!string.Equals(found, localType, StringComparison.Ordinal))
                return string.Empty;
        }

        return found;
    }

    private static bool HasMismatchedSourceType(List<StorageRuntime> storage, string graphFluidType)
    {
        for (int i = 0; i < storage.Count; i++)
        {
            string localType = GetStorageNodeFluidType(storage[i]);
            int localAmount = GetStorageNodeFluidAmountMg(storage[i]);
            if (string.IsNullOrEmpty(localType) || localAmount <= 0)
                continue;

            if (!string.Equals(localType, graphFluidType, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string ResolveIntakeSourceMgPerTick(WorldBase world, int clrIdx, FluidGraphData graph, string graphFluidType, out int totalMgPerTick)
    {
        totalMgPerTick = 0;

        if (graph == null || graph.IntakeEndpoints == null || graph.IntakeEndpoints.Count == 0)
            return string.Empty;

        string desiredFluidType = string.IsNullOrEmpty(graphFluidType)
            ? string.Empty
            : graphFluidType.Trim().ToLowerInvariant();

        string resolvedFluidType = string.Empty;
        List<Vector3i> intakePositions = new List<Vector3i>(graph.IntakeEndpoints);
        intakePositions.Sort(ComparePos);

        foreach (Vector3i intakePos in intakePositions)
        {
            if (!SafeWorldRead.TryGetBlock(world, clrIdx, intakePos, out BlockValue intakeBlockValue))
                continue;

            string outputFluidType = GetIntakeOutputFluidType(intakeBlockValue);
            if (string.IsNullOrEmpty(outputFluidType))
                continue;

            if (!string.IsNullOrEmpty(desiredFluidType) && !string.Equals(outputFluidType, desiredFluidType, StringComparison.Ordinal))
                continue;

            if (!CanIntakeFromBelow(world, clrIdx, intakePos, intakeBlockValue))
                continue;

            int capGps = GetPropertyInt(intakeBlockValue, "FluidPipeCapacityGps", 250);
            int capMgPerTick = (capGps * FluidConstants.MilliGallonsPerGallon) / FluidConstants.SimulationTicksPerSecond;
            if (capMgPerTick <= 0)
                continue;

            if (string.IsNullOrEmpty(resolvedFluidType))
                resolvedFluidType = outputFluidType;

            if (!string.Equals(outputFluidType, resolvedFluidType, StringComparison.Ordinal))
                continue;

            totalMgPerTick += capMgPerTick;
        }

        return totalMgPerTick > 0 ? resolvedFluidType : string.Empty;
    }

    private static string GetIntakeOutputFluidType(BlockValue intakeBlockValue)
    {
        string raw = intakeBlockValue.Block?.Properties?.GetString(IntakePropOutputFluid);
        if (string.IsNullOrEmpty(raw))
            return FluidWater;

        string trimmed = raw.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? FluidWater : trimmed;
    }

    private static bool CanIntakeFromBelow(WorldBase world, int clrIdx, Vector3i intakePos, BlockValue intakeBlockValue)
    {
        Vector3i below = intakePos + Vector3i.down;

        if (!SafeWorldRead.TryGetBlock(world, clrIdx, below, out BlockValue belowValue))
            return false;

        string belowName = belowValue.Block?.GetBlockName();
        if (IsDeniedByNameContains(intakeBlockValue, belowName))
            return false;

        HashSet<string> allowedBlocks = GetCsvSet(intakeBlockValue, IntakePropAllowedBlocks, "water,waterMoving,terrWaterPOI");
        if (!string.IsNullOrEmpty(belowName) && allowedBlocks.Contains(belowName))
            return true;

        bool allowWorldWaterVoxel = GetPropertyBool(intakeBlockValue, IntakePropAllowWorldWaterVoxel, false);
        if (allowWorldWaterVoxel && world.IsWater(below))
            return true;

        return false;
    }

    private static bool IsDeniedByNameContains(BlockValue intakeBlockValue, string belowName)
    {
        if (string.IsNullOrEmpty(belowName))
            return false;

        HashSet<string> deniedTokens = GetCsvSet(intakeBlockValue, IntakePropDisallowNameContains, "Bucket");
        foreach (string token in deniedTokens)
        {
            if (string.IsNullOrEmpty(token))
                continue;

            if (belowName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool GetPropertyBool(BlockValue blockValue, string propertyName, bool fallback)
    {
        string raw = blockValue.Block?.Properties?.GetString(propertyName);
        if (string.IsNullOrEmpty(raw) || !bool.TryParse(raw, out bool value))
            return fallback;

        return value;
    }

    private static HashSet<string> GetCsvSet(BlockValue blockValue, string propertyName, string fallbackCsv)
    {
        string raw = blockValue.Block?.Properties?.GetString(propertyName);
        if (string.IsNullOrEmpty(raw))
            raw = fallbackCsv;

        HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] parts = raw.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string value = parts[i].Trim();
            if (string.IsNullOrEmpty(value))
                continue;

            values.Add(value);
        }

        return values;
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

    private static int AllocateAcrossSinks(int flowBudgetMg, List<StorageRuntime> sinks)
    {
        int remaining = flowBudgetMg;

        while (remaining > 0)
        {
            List<StorageRuntime> eligible = new List<StorageRuntime>();
            for (int i = 0; i < sinks.Count; i++)
            {
                StorageRuntime sink = sinks[i];
                if (sink.RemainingInputMg <= 0 || sink.RemainingFreeSpaceMg <= 0)
                    continue;

                eligible.Add(sink);
            }

            if (eligible.Count == 0)
                break;

            int share = remaining / eligible.Count;
            if (share <= 0)
                share = 1;

            int movedThisPass = 0;

            for (int i = 0; i < eligible.Count && remaining > 0; i++)
            {
                StorageRuntime sink = eligible[i];
                int accepted = Math.Min(share, remaining);
                accepted = Math.Min(accepted, sink.RemainingInputMg);
                accepted = Math.Min(accepted, sink.RemainingFreeSpaceMg);
                if (accepted <= 0)
                    continue;

                sink.AllocatedIntakeMg += accepted;
                sink.RemainingInputMg -= accepted;
                sink.RemainingFreeSpaceMg -= accepted;

                remaining -= accepted;
                movedThisPass += accepted;
            }

            if (movedThisPass <= 0)
                break;
        }

        return flowBudgetMg - remaining;
    }

    private static int AllocateAcrossSources(int requestedDrainMg, List<StorageRuntime> sources)
    {
        int remaining = requestedDrainMg;

        while (remaining > 0)
        {
            List<StorageRuntime> eligible = new List<StorageRuntime>();
            for (int i = 0; i < sources.Count; i++)
            {
                StorageRuntime source = sources[i];
                if (source.RemainingOutputMg <= 0)
                    continue;

                eligible.Add(source);
            }

            if (eligible.Count == 0)
                break;

            int share = remaining / eligible.Count;
            if (share <= 0)
                share = 1;

            int drainedThisPass = 0;

            for (int i = 0; i < eligible.Count && remaining > 0; i++)
            {
                StorageRuntime source = eligible[i];
                int drained = Math.Min(share, remaining);
                drained = Math.Min(drained, source.RemainingOutputMg);
                if (drained <= 0)
                    continue;

                source.AllocatedDrainMg += drained;
                source.RemainingOutputMg -= drained;
                remaining -= drained;
                drainedThisPass += drained;
            }

            if (drainedThisPass <= 0)
                break;
        }

        return requestedDrainMg - remaining;
    }

    private static void RollbackSinkAllocations(List<StorageRuntime> sinks, int reductionMg)
    {
        if (reductionMg <= 0)
            return;

        for (int i = sinks.Count - 1; i >= 0 && reductionMg > 0; i--)
        {
            StorageRuntime sink = sinks[i];
            if (sink.AllocatedIntakeMg <= 0)
                continue;

            int takeBack = Math.Min(sink.AllocatedIntakeMg, reductionMg);
            sink.AllocatedIntakeMg -= takeBack;
            reductionMg -= takeBack;
        }
    }

    private static void ApplySourceDrains(FluidGraphData graph, List<StorageRuntime> sources, ulong now)
    {
        for (int i = 0; i < sources.Count; i++)
        {
            StorageRuntime source = sources[i];
            if (source.AllocatedDrainMg <= 0)
                continue;

            DrainFromStorageNode(source, source.AllocatedDrainMg, now);
            PersistStorageNodeSnapshot(graph, source);
        }
    }

    private static void ApplySinkIntake(FluidGraphData graph, string fluidType, List<StorageRuntime> sinks, ulong now)
    {
        for (int i = 0; i < sinks.Count; i++)
        {
            StorageRuntime sink = sinks[i];
            if (sink.AllocatedIntakeMg <= 0)
                continue;

            AcceptIntoStorageNode(sink, fluidType, sink.AllocatedIntakeMg, now);
            PersistStorageNodeSnapshot(graph, sink);
        }
    }

    private static void PrepareStorageNodeForTick(StorageRuntime node, ulong now)
    {
        if (node == null)
            return;

        if (node.UsingSnapshot)
        {
            if (node.Snapshot == null)
                return;

            if (node.Snapshot.LastInputBudgetWorldTime != now)
            {
                node.Snapshot.LastInputBudgetWorldTime = now;
                node.Snapshot.AcceptedThisTickMg = 0;
            }

            return;
        }

        node.Storage?.ResetTickBudget();
    }

    private static string GetStorageNodeFluidType(StorageRuntime node)
    {
        if (node == null)
            return string.Empty;

        if (node.UsingSnapshot)
            return node.Snapshot?.FluidType ?? string.Empty;

        return node.Storage?.FluidType ?? string.Empty;
    }

    private static int GetStorageNodeFluidAmountMg(StorageRuntime node)
    {
        if (node == null)
            return 0;

        if (node.UsingSnapshot)
            return Math.Max(0, node.Snapshot?.FluidAmountMg ?? 0);

        return Math.Max(0, node.Storage?.FluidAmountMg ?? 0);
    }

    private static bool CanStorageNodeAcceptType(StorageRuntime node, string fluidType)
    {
        if (node == null || string.IsNullOrEmpty(fluidType))
            return false;

        if (node.UsingSnapshot)
        {
            if (node.Snapshot == null)
                return false;

            if (string.IsNullOrEmpty(node.Snapshot.FluidType))
                return true;

            return string.Equals(node.Snapshot.FluidType, fluidType, StringComparison.Ordinal);
        }

        if (node.Storage == null)
            return false;

        return node.Storage.CanAcceptType(fluidType);
    }

    private static int GetStorageNodeRemainingInputBudgetMg(StorageRuntime node, ulong now)
    {
        if (node == null)
            return 0;

        if (node.UsingSnapshot)
        {
            if (node.Snapshot == null)
                return 0;

            if (node.Snapshot.LastInputBudgetWorldTime != now)
            {
                node.Snapshot.LastInputBudgetWorldTime = now;
                node.Snapshot.AcceptedThisTickMg = 0;
            }

            int inputCap = Math.Max(0, node.Snapshot.InputCapMgPerTick);
            int remaining = inputCap - Math.Max(0, node.Snapshot.AcceptedThisTickMg);
            return remaining > 0 ? remaining : 0;
        }

        if (node.Storage == null)
            return 0;

        return node.Storage.GetRemainingInputBudgetMg();
    }

    private static int GetStorageNodeFreeSpaceMg(StorageRuntime node)
    {
        if (node == null)
            return 0;

        if (node.UsingSnapshot)
        {
            if (node.Snapshot == null)
                return 0;

            int capacity = Math.Max(0, node.Snapshot.CapacityMg);
            int amount = Math.Max(0, node.Snapshot.FluidAmountMg);
            int free = capacity - amount;
            return free > 0 ? free : 0;
        }

        if (node.Storage == null)
            return 0;

        return node.Storage.GetFreeSpaceMg();
    }

    private static int GetStorageNodeAvailableOutputMg(StorageRuntime node)
    {
        if (node == null)
            return 0;

        if (node.UsingSnapshot)
        {
            if (node.Snapshot == null)
                return 0;

            int amount = Math.Max(0, node.Snapshot.FluidAmountMg);
            int outputCap = Math.Max(0, node.Snapshot.OutputCapMgPerTick);
            int available = Math.Min(amount, outputCap);
            return available > 0 ? available : 0;
        }

        if (node.Storage == null)
            return 0;

        return node.Storage.GetAvailableOutputMg();
    }

    private static int AcceptIntoStorageNode(StorageRuntime node, string fluidType, int requestedMg, ulong now)
    {
        if (node == null || string.IsNullOrEmpty(fluidType) || requestedMg <= 0)
            return 0;

        if (!node.UsingSnapshot)
        {
            if (node.Storage == null)
                return 0;

            return node.Storage.AcceptFluid(fluidType, requestedMg);
        }

        if (node.Snapshot == null || !CanStorageNodeAcceptType(node, fluidType))
            return 0;

        int freeSpace = GetStorageNodeFreeSpaceMg(node);
        int remainingBudget = GetStorageNodeRemainingInputBudgetMg(node, now);
        int accepted = Math.Min(requestedMg, freeSpace);
        accepted = Math.Min(accepted, remainingBudget);
        if (accepted <= 0)
            return 0;

        node.Snapshot.FluidAmountMg += accepted;
        node.Snapshot.AcceptedThisTickMg += accepted;
        node.Snapshot.LastInputBudgetWorldTime = now;

        int cap = Math.Max(0, node.Snapshot.CapacityMg);
        if (node.Snapshot.FluidAmountMg > cap)
            node.Snapshot.FluidAmountMg = cap;

        if (string.IsNullOrEmpty(node.Snapshot.FluidType) && node.Snapshot.FluidAmountMg > 0)
            node.Snapshot.FluidType = fluidType;

        return accepted;
    }

    private static int DrainFromStorageNode(StorageRuntime node, int requestedMg, ulong now)
    {
        if (node == null || requestedMg <= 0)
            return 0;

        if (!node.UsingSnapshot)
        {
            if (node.Storage == null)
                return 0;

            return node.Storage.RemoveFluid(requestedMg);
        }

        if (node.Snapshot == null || node.Snapshot.FluidAmountMg <= 0)
            return 0;

        int removed = Math.Min(requestedMg, node.Snapshot.FluidAmountMg);
        node.Snapshot.FluidAmountMg -= removed;
        if (node.Snapshot.FluidAmountMg <= 0)
        {
            node.Snapshot.FluidAmountMg = 0;
            node.Snapshot.FluidType = string.Empty;
        }

        node.Snapshot.LastInputBudgetWorldTime = now;
        return removed;
    }

    private static void PersistStorageNodeSnapshot(FluidGraphData graph, StorageRuntime node)
    {
        if (graph == null || node == null || !node.UsingSnapshot || node.Snapshot == null)
            return;

        graph.SetStorageSnapshot(node.Pos, node.Snapshot);
    }

    private static void RecordBlocked(FluidGraphData graph, List<TileEntityFluidPump> pumps, ulong now, string reason, string detail)
    {
        if (graph != null)
            graph.RecordBlocked(reason);

        for (int i = 0; i < pumps.Count; i++)
            pumps[i].RecordBlockedEvent(now, reason, detail);
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






