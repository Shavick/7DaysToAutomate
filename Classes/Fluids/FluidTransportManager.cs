using System;
using System.Collections.Generic;

public static class FluidTransportManager
{
    private const string ReasonNoPump = "NoActivePump";
    private const string ReasonNoStorage = "NoStorage";
    private const string ReasonStorageFull = "AllStorageFull";
    private const string ReasonNoSource = "NoSourceFluid";
    private const string ReasonTypeMismatch = "TypeMismatch";

    private static ulong lastProcessWorldTime = 0UL;

    private sealed class StorageRuntime
    {
        public Vector3i Pos;
        public TileEntityFluidStorage Storage;
        public int RemainingInputMg;
        public int RemainingFreeSpaceMg;
        public int RemainingOutputMg;
        public int AllocatedIntakeMg;
        public int AllocatedDrainMg;
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

        if (now <= FluidGraphManager.LastRebuildWorldTime)
            return;

        foreach (var kvp in FluidGraphManager.GetAllGraphs())
            ProcessGraph(world, 0, kvp.Value, now);
    }

    private static void ProcessGraph(WorldBase world, int clrIdx, FluidGraphData graph, ulong now)
    {
        if (graph == null || graph.PipePositions.Count == 0)
            return;

        List<TileEntityFluidPump> activePumps = ResolveActivePumps(world, clrIdx, graph.PumpEndpoints);
        if (activePumps.Count == 0)
        {
            RecordBlocked(graph, activePumps, now, ReasonNoPump, "No enabled pump connected to graph");
            return;
        }

        List<StorageRuntime> storage = ResolveStorage(world, clrIdx, graph.StorageEndpoints);
        if (storage.Count == 0)
        {
            RecordBlocked(graph, activePumps, now, ReasonNoStorage, "No fluid storage connected to graph");
            return;
        }

        string graphFluidType = ResolveGraphFluidType(graph, storage);
        if (string.IsNullOrEmpty(graphFluidType))
            return;

        graph.FluidType = graphFluidType;

        bool hasMismatchedSource = HasMismatchedSourceType(storage, graphFluidType);
        if (hasMismatchedSource)
        {
            RecordBlocked(graph, activePumps, now, ReasonTypeMismatch, $"Graph fluid={graphFluidType} has mismatched stored fluid");
            return;
        }

        int pipeCap = GetGraphPipeCapMgPerTick(world, clrIdx, graph);
        int pumpCap = GetPumpCapMgPerTick(activePumps);
        int flowBudget = Math.Min(pipeCap, pumpCap);

        if (flowBudget <= 0)
            return;

        int sourceAvailable = 0;
        List<StorageRuntime> sourceNodes = new List<StorageRuntime>();
        List<StorageRuntime> sinkNodes = new List<StorageRuntime>();

        for (int i = 0; i < storage.Count; i++)
        {
            StorageRuntime node = storage[i];
            node.Storage.ResetTickBudget();
            node.RemainingInputMg = node.Storage.GetRemainingInputBudgetMg();
            node.RemainingFreeSpaceMg = node.Storage.GetFreeSpaceMg();
            node.RemainingOutputMg = node.Storage.GetAvailableOutputMg();
            node.AllocatedIntakeMg = 0;
            node.AllocatedDrainMg = 0;

            if (!node.Storage.CanAcceptType(graphFluidType))
                continue;

            if (node.RemainingInputMg > 0 && node.RemainingFreeSpaceMg > 0)
                sinkNodes.Add(node);

            if (node.RemainingOutputMg > 0)
            {
                sourceNodes.Add(node);
                sourceAvailable += node.RemainingOutputMg;
            }
        }

        if (sinkNodes.Count == 0)
        {
            RecordBlocked(graph, activePumps, now, ReasonStorageFull, "All storage is full or input-capped");
            return;
        }

        if (sourceNodes.Count == 0 || sourceAvailable <= 0)
        {
            RecordBlocked(graph, activePumps, now, ReasonNoSource, "No source storage has available fluid");
            return;
        }

        if (flowBudget > sourceAvailable)
            flowBudget = sourceAvailable;

        int acceptedTotal = AllocateAcrossSinks(flowBudget, sinkNodes);
        if (acceptedTotal <= 0)
        {
            RecordBlocked(graph, activePumps, now, ReasonStorageFull, "Storage could not accept flow this tick");
            return;
        }

        int drainedTotal = AllocateAcrossSources(acceptedTotal, sourceNodes);
        if (drainedTotal <= 0)
            return;

        if (drainedTotal < acceptedTotal)
        {
            int reduction = acceptedTotal - drainedTotal;
            RollbackSinkAllocations(sinkNodes, reduction);
            acceptedTotal = drainedTotal;
        }

        ApplySourceDrains(sourceNodes);
        ApplySinkIntake(graphFluidType, sinkNodes);
    }

    private static List<TileEntityFluidPump> ResolveActivePumps(WorldBase world, int clrIdx, HashSet<Vector3i> pumpPositions)
    {
        List<TileEntityFluidPump> pumps = new List<TileEntityFluidPump>();

        foreach (Vector3i pos in pumpPositions)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity te) || !(te is TileEntityFluidPump pump))
                continue;

            if (!pump.IsActivePump())
                continue;

            pumps.Add(pump);
        }

        return pumps;
    }

    private static List<StorageRuntime> ResolveStorage(WorldBase world, int clrIdx, HashSet<Vector3i> storagePositions)
    {
        List<StorageRuntime> storage = new List<StorageRuntime>();

        foreach (Vector3i pos in storagePositions)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity te) || !(te is TileEntityFluidStorage tank))
                continue;

            storage.Add(new StorageRuntime
            {
                Pos = pos,
                Storage = tank
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
            string localType = storage[i].Storage.FluidType;
            if (string.IsNullOrEmpty(localType) || storage[i].Storage.FluidAmountMg <= 0)
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
            string localType = storage[i].Storage.FluidType;
            if (string.IsNullOrEmpty(localType) || storage[i].Storage.FluidAmountMg <= 0)
                continue;

            if (!string.Equals(localType, graphFluidType, StringComparison.Ordinal))
                return true;
        }

        return false;
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

    private static int GetPumpCapMgPerTick(List<TileEntityFluidPump> activePumps)
    {
        int total = 0;
        for (int i = 0; i < activePumps.Count; i++)
            total += activePumps[i].GetOutputCapMgPerTick();

        return total;
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

    private static void ApplySourceDrains(List<StorageRuntime> sources)
    {
        for (int i = 0; i < sources.Count; i++)
        {
            StorageRuntime source = sources[i];
            if (source.AllocatedDrainMg <= 0)
                continue;

            source.Storage.RemoveFluid(source.AllocatedDrainMg);
        }
    }

    private static void ApplySinkIntake(string fluidType, List<StorageRuntime> sinks)
    {
        for (int i = 0; i < sinks.Count; i++)
        {
            StorageRuntime sink = sinks[i];
            if (sink.AllocatedIntakeMg <= 0)
                continue;

            sink.Storage.AcceptFluid(fluidType, sink.AllocatedIntakeMg);
        }
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
