using System;
using System.Collections.Generic;

public static class PipeTransportManager
{
    // Active in-flight job window scales with graph throughput so higher tiers can sustain factory concurrency.
    private const int MinActiveJobsPerGraph = 24;
    private const int ActiveJobsPerThroughputUnit = 4;
    private const int MaxActiveJobsPerGraph = 256;

    private static readonly Dictionary<Guid, PipeTransportJob> activeJobs = new Dictionary<Guid, PipeTransportJob>();
    private const int DefaultPipeThroughput = 1;
    private const int DefaultPipeSpeed = 1;
    private const int DefaultPipeLatency = 1;
    private const ulong BaseDispatchIntervalTicks = 20UL; // 1 second at 20 ticks/sec
    private const ulong BasePerPipeTravelTicks = 5UL;     // 0.25 sec per pipe
    private const ulong MachineRequestTimeoutTicks = 200UL;
    private const int PrefetchPipesPerPacketStep = 8;
    private const int MaxPrefetchPacketSteps = 3;

    private sealed class GraphDispatchState
    {
        public readonly Dictionary<Vector3i, int> MachinePriority = new Dictionary<Vector3i, int>();
        public readonly Dictionary<Vector3i, ulong> LastRequestWorldTime = new Dictionary<Vector3i, ulong>();
        public readonly Dictionary<Vector3i, int> MachineFairCredits = new Dictionary<Vector3i, int>();
        public ulong BudgetWindowWorldTime = ulong.MaxValue;
        public int RemainingThroughputBudget = 0;
        public int RoundRobinCursor = 0;
    }

    private static readonly Dictionary<Guid, GraphDispatchState> graphDispatchStates = new Dictionary<Guid, GraphDispatchState>();
    private static readonly Dictionary<Guid, string> lastBlockedReasonByJob = new Dictionary<Guid, string>();
    private static readonly Dictionary<Guid, ulong> lastBlockedReasonLogTimeByJob = new Dictionary<Guid, ulong>();
    private const ulong RepeatBlockedReasonLogIntervalTicks = 20UL;
    private static bool IsDevLoggingEnabledForJob(WorldBase world, PipeTransportJob job)
    {
        if (world == null || job == null || job.RoutePipePositions == null)
            return false;

        for (int i = 0; i < job.RoutePipePositions.Count; i++)
        {
            Vector3i pipePos = job.RoutePipePositions[i];
            if (!SafeWorldRead.TryGetTileEntity(world, 0, pipePos, out TileEntity tileEntity))
                continue;

            if (tileEntity is TileEntityItemPipe pipe)
                return pipe.IsDevLogging;
        }

        return false;
    }
    private static bool IsDevLoggingEnabledForMachine(WorldBase world, int clrIdx, Vector3i machinePos)
    {
        if (world == null || machinePos == Vector3i.zero)
            return false;

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, machinePos, out TileEntity machineTe))
            return false;

        if (!(machineTe is TileEntityMachine machine))
            return false;

        return machine.IsDevLogging;
    }

    private static void ClearJobDebugState(Guid jobId)
    {
        if (jobId == Guid.Empty)
            return;

        lastBlockedReasonByJob.Remove(jobId);
        lastBlockedReasonLogTimeByJob.Remove(jobId);
    }

    private static bool ShouldEmitBlockedJobLog(PipeTransportJob job, string blockedReason, ulong worldTime)
    {
        if (job == null || job.JobId == Guid.Empty || string.IsNullOrEmpty(blockedReason))
            return false;

        if (!lastBlockedReasonByJob.TryGetValue(job.JobId, out string previousReason))
        {
            lastBlockedReasonByJob[job.JobId] = blockedReason;
            lastBlockedReasonLogTimeByJob[job.JobId] = worldTime;
            return true;
        }

        if (!lastBlockedReasonLogTimeByJob.TryGetValue(job.JobId, out ulong previousLogTime))
            previousLogTime = 0UL;

        if (!string.Equals(previousReason, blockedReason, StringComparison.Ordinal) ||
            worldTime >= previousLogTime + RepeatBlockedReasonLogIntervalTicks)
        {
            lastBlockedReasonByJob[job.JobId] = blockedReason;
            lastBlockedReasonLogTimeByJob[job.JobId] = worldTime;
            return true;
        }

        return false;
    }

    public static void ClearAll()
    {
        activeJobs.Clear();
        graphDispatchStates.Clear();
        lastBlockedReasonByJob.Clear();
        lastBlockedReasonLogTimeByJob.Clear();
        //Log.Out("[PipeTransportManager] ClearAll()");
    }

    public static int GetActiveJobCount()
    {
        return activeJobs.Count;
    }

    private static GraphDispatchState GetOrCreateDispatchState(Guid pipeGraphId)
    {
        if (!graphDispatchStates.TryGetValue(pipeGraphId, out GraphDispatchState state) || state == null)
        {
            state = new GraphDispatchState();
            graphDispatchStates[pipeGraphId] = state;
        }

        return state;
    }

    private static int NormalizePriority(int requestedPriority)
    {
        if (requestedPriority < TileEntityMachine.MinPipePriority)
            return TileEntityMachine.MinPipePriority;

        if (requestedPriority > TileEntityMachine.MaxPipePriority)
            return TileEntityMachine.MaxPipePriority;

        return requestedPriority;
    }

    private static int CompareMachinePos(Vector3i a, Vector3i b)
    {
        int cmp = a.x.CompareTo(b.x);
        if (cmp != 0)
            return cmp;

        cmp = a.y.CompareTo(b.y);
        if (cmp != 0)
            return cmp;

        return a.z.CompareTo(b.z);
    }

    private static void PruneInactiveMachines(GraphDispatchState state, ulong now)
    {
        if (state == null || state.LastRequestWorldTime.Count == 0)
            return;

        List<Vector3i> staleMachines = null;

        foreach (var kvp in state.LastRequestWorldTime)
        {
            if (now <= kvp.Value + MachineRequestTimeoutTicks)
                continue;

            if (staleMachines == null)
                staleMachines = new List<Vector3i>();

            staleMachines.Add(kvp.Key);
        }

        if (staleMachines == null)
            return;

        for (int i = 0; i < staleMachines.Count; i++)
        {
            Vector3i machinePos = staleMachines[i];
            state.LastRequestWorldTime.Remove(machinePos);
            state.MachinePriority.Remove(machinePos);
            state.MachineFairCredits.Remove(machinePos);
        }

        if (state.RoundRobinCursor < 0)
            state.RoundRobinCursor = 0;
    }

    private static List<Vector3i> BuildActiveContenders(GraphDispatchState state)
    {
        List<Vector3i> contenders = new List<Vector3i>();
        if (state == null || state.LastRequestWorldTime.Count == 0)
            return contenders;

        foreach (var kvp in state.LastRequestWorldTime)
            contenders.Add(kvp.Key);

        contenders.Sort(CompareMachinePos);
        return contenders;
    }

    private static int GetPriorityWeight(int machinePriority)
    {
        int clamped = NormalizePriority(machinePriority);
        return clamped + 1;
    }

    private static int FindMachineIndex(List<Vector3i> contenders, Vector3i machinePos)
    {
        if (contenders == null)
            return -1;

        for (int i = 0; i < contenders.Count; i++)
        {
            if (contenders[i] == machinePos)
                return i;
        }

        return -1;
    }

    private static bool TryReserveMachineTurn(Guid pipeGraphId, Vector3i machinePos, int machinePriority, ulong now)
    {
        if (pipeGraphId == Guid.Empty || machinePos == Vector3i.zero)
            return true;

        GraphDispatchState state = GetOrCreateDispatchState(pipeGraphId);
        state.MachinePriority[machinePos] = NormalizePriority(machinePriority);
        state.LastRequestWorldTime[machinePos] = now;

        PruneInactiveMachines(state, now);

        List<Vector3i> contenders = BuildActiveContenders(state);
        if (contenders.Count == 0)
            return true;

        for (int i = 0; i < contenders.Count; i++)
        {
            Vector3i contender = contenders[i];
            if (!state.MachineFairCredits.ContainsKey(contender))
                state.MachineFairCredits[contender] = 0;
        }

        int machineIndex = FindMachineIndex(contenders, machinePos);
        if (machineIndex < 0)
            return false;

        int cursor = state.RoundRobinCursor % contenders.Count;
        if (cursor < 0)
            cursor = 0;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int offset = 0; offset < contenders.Count; offset++)
            {
                int idx = (cursor + offset) % contenders.Count;
                Vector3i contender = contenders[idx];
                int credits = state.MachineFairCredits.TryGetValue(contender, out int existingCredits) ? existingCredits : 0;
                if (credits <= 0)
                    continue;

                if (contender != machinePos)
                    return false;

                state.MachineFairCredits[contender] = credits - 1;
                state.RoundRobinCursor = (idx + 1) % contenders.Count;
                return true;
            }

            // Start a new weighted round when all contenders consumed their credits.
            for (int i = 0; i < contenders.Count; i++)
            {
                Vector3i contender = contenders[i];
                int priority = state.MachinePriority.TryGetValue(contender, out int p)
                    ? p
                    : TileEntityMachine.DefaultPipePriority;
                int weight = GetPriorityWeight(priority);
                int credits = state.MachineFairCredits.TryGetValue(contender, out int existingCredits) ? existingCredits : 0;
                state.MachineFairCredits[contender] = credits + weight;
            }
        }

        return false;
    }

    private static void AdvanceMachineTurn(Guid pipeGraphId, Vector3i machinePos, ulong now)
    {
        if (pipeGraphId == Guid.Empty || machinePos == Vector3i.zero)
            return;

        if (!graphDispatchStates.TryGetValue(pipeGraphId, out GraphDispatchState state) || state == null)
            return;

        PruneInactiveMachines(state, now);

        List<Vector3i> contenders = BuildActiveContenders(state);
        if (contenders.Count == 0)
        {
            state.RoundRobinCursor = 0;
            return;
        }

        int currentIndex = contenders.IndexOf(machinePos);
        if (currentIndex < 0)
        {
            state.RoundRobinCursor = (state.RoundRobinCursor + 1) % contenders.Count;
            return;
        }

        state.RoundRobinCursor = (currentIndex + 1) % contenders.Count;
    }

    private static int GetMachinePriority(WorldBase world, int clrIdx, Vector3i machinePos)
    {
        if (world == null || machinePos == Vector3i.zero)
            return TileEntityMachine.DefaultPipePriority;

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, machinePos, out TileEntity machineTe))
            return TileEntityMachine.DefaultPipePriority;
        if (!(machineTe is TileEntityMachine machine))
            return TileEntityMachine.DefaultPipePriority;

        return NormalizePriority(machine.PipePriority);
    }

    private static int ComputeGraphThroughputBudget(WorldBase world, int clrIdx, Guid pipeGraphId)
    {
        if (world == null || world.IsRemote() || pipeGraphId == Guid.Empty)
            return DefaultPipeThroughput;

        if (!PipeGraphManager.TryGetGraph(pipeGraphId, out PipeGraphData graph) || graph == null || graph.PipePositions == null || graph.PipePositions.Count == 0)
            return DefaultPipeThroughput;

        int lowestThroughput = int.MaxValue;

        foreach (Vector3i pipePos in graph.PipePositions)
        {
            if (!SafeWorldRead.TryGetBlock(world, clrIdx, pipePos, out BlockValue blockValue))
                continue;

            int throughput = GetPipeThroughput(blockValue);
            if (throughput <= 0)
                continue;

            if (throughput < lowestThroughput)
                lowestThroughput = throughput;
        }

        if (lowestThroughput == int.MaxValue)
            return DefaultPipeThroughput;

        return lowestThroughput;
    }

    private static int GetRemainingThroughputBudget(WorldBase world, int clrIdx, Guid pipeGraphId, ulong now)
    {
        if (pipeGraphId == Guid.Empty)
            return 0;

        GraphDispatchState state = GetOrCreateDispatchState(pipeGraphId);
        if (state.BudgetWindowWorldTime != now)
        {
            state.BudgetWindowWorldTime = now;
            state.RemainingThroughputBudget = ComputeGraphThroughputBudget(world, clrIdx, pipeGraphId);
        }

        if (state.RemainingThroughputBudget < 0)
            state.RemainingThroughputBudget = 0;

        return state.RemainingThroughputBudget;
    }

    private static void ConsumeThroughputBudget(Guid pipeGraphId, ulong now, int amount)
    {
        if (pipeGraphId == Guid.Empty || amount <= 0)
            return;

        GraphDispatchState state = GetOrCreateDispatchState(pipeGraphId);
        if (state.BudgetWindowWorldTime != now)
            return;

        state.RemainingThroughputBudget -= amount;
        if (state.RemainingThroughputBudget < 0)
            state.RemainingThroughputBudget = 0;
    }
    private static int ComputeInputPrefetchReserveItems(List<Vector3i> routePipePositions, int routeThroughput)
    {
        if (routePipePositions == null || routePipePositions.Count == 0 || routeThroughput <= 0)
            return 0;

        int extraPacketSteps = routePipePositions.Count / PrefetchPipesPerPacketStep;
        if (extraPacketSteps <= 0)
            return 0;

        if (extraPacketSteps > MaxPrefetchPacketSteps)
            extraPacketSteps = MaxPrefetchPacketSteps;

        return extraPacketSteps * routeThroughput;
    }
    public static bool TryRequestCrafterInputs(
    WorldBase world,
    int clrIdx,
    Guid pipeGraphId,
    Vector3i sourceStoragePos,
    Vector3i targetMachinePos,
    HashSet<string> neededItemNames)
    {
        if (world == null || world.IsRemote())
            return false;

        if (pipeGraphId == Guid.Empty)
            return false;

        if (sourceStoragePos == Vector3i.zero || targetMachinePos == Vector3i.zero)
            return false;

        if (neededItemNames == null || neededItemNames.Count == 0)
            return false;

        if (!PipeGraphManager.TryGetGraph(pipeGraphId, out PipeGraphData graph) || graph == null)
            return false;

        if (graph.StorageEndpoints == null || !graph.StorageEndpoints.Contains(sourceStoragePos))
            return false;

        if (!PipeGraphManager.TryFindRoute(world, clrIdx, pipeGraphId, targetMachinePos, sourceStoragePos, out List<Vector3i> route))
            return false;

        if (route == null || route.Count == 0)
            return false;

        // Resolve source availability from graph state (live storage or unload snapshot).
        if (!PipeGraphManager.TryGetStorageItemCounts(world, clrIdx, ref pipeGraphId, sourceStoragePos, out Dictionary<string, int> sourceItemCounts))
            return false;

        // Preserve the "don't touch while player is using" rule when the source chest is loaded.
        if (SafeWorldRead.TryGetTileEntity(world, clrIdx, sourceStoragePos, out TileEntity te) &&
            te is TileEntityComposite comp)
        {
            TEFeatureStorage sourceStorage = comp.GetFeature<TEFeatureStorage>();
            if (sourceStorage != null && sourceStorage.items != null && sourceStorage.IsUserAccessing())
                return false;
        }

        int remainingCapacity = GetRemainingCapacityForGraph(world, clrIdx, pipeGraphId);
        if (remainingCapacity <= 0)
            return false;

        int routeThroughput = GetRouteThroughput(world, clrIdx, route);
        if (routeThroughput <= 0)
            return false;

        int inputPrefetchReserve = ComputeInputPrefetchReserveItems(route, routeThroughput);

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, targetMachinePos, out TileEntity targetTe))
            return false;

        if (!(targetTe is TileEntityMachine targetMachine))
            return false;

        ulong now = world.GetWorldTime();
        ulong travelTicks = GetRouteTravelTicks(world, clrIdx, route);
        int machinePriority = GetMachinePriority(world, clrIdx, targetMachinePos);

        int remainingBudget = GetRemainingThroughputBudget(world, clrIdx, pipeGraphId, now);
        if (remainingBudget <= 0)
            return false;

        int packetCapacity = Math.Min(routeThroughput, remainingBudget);
        if (packetCapacity <= 0)
            return false;

        if (!TryReserveMachineTurn(pipeGraphId, targetMachinePos, machinePriority, now))
            return false;

        List<string> orderedNeededItems = new List<string>(neededItemNames);
        orderedNeededItems.Sort(StringComparer.Ordinal);

        Dictionary<string, int> packetItems = new Dictionary<string, int>();

        for (int i = 0; i < orderedNeededItems.Count && packetCapacity > 0; i++)
        {
            string itemName = orderedNeededItems[i];
            if (string.IsNullOrEmpty(itemName))
                continue;

            int alreadyQueuedForThisItem = 0;
            foreach (var kvp in activeJobs)
            {
                PipeTransportJob existingJob = kvp.Value;
                if (existingJob == null)
                    continue;

                if (existingJob.IsComplete || existingJob.IsFailed)
                    continue;

                if (!existingJob.IsStorageToMachine())
                    continue;

                if (existingJob.PipeGraphId != pipeGraphId)
                    continue;

                if (existingJob.SourcePos != sourceStoragePos || existingJob.TargetPos != targetMachinePos)
                    continue;

                alreadyQueuedForThisItem += existingJob.GetItemCount(itemName);
            }

            int availableInStorage = sourceItemCounts.TryGetValue(itemName, out int inStorage) ? inStorage : 0;
            int availableToQueue = availableInStorage - alreadyQueuedForThisItem;
            if (availableToQueue <= 0)
                continue;

            int machineRemainingCapacity = targetMachine.GetBufferedInputRemainingCapacity(itemName);
            int machineQueueRoom = machineRemainingCapacity + inputPrefetchReserve - alreadyQueuedForThisItem;
            if (machineQueueRoom <= 0)
                continue;

            int acceptedAmount = Math.Min(Math.Min(availableToQueue, machineQueueRoom), packetCapacity);
            if (acceptedAmount <= 0)
                continue;

            packetItems[itemName] = acceptedAmount;
            packetCapacity -= acceptedAmount;
        }

        if (packetItems.Count == 0)
            return false;

        ulong arrival = now + travelTicks;
        PipeTransportJob job = new PipeTransportJob(
            pipeGraphId,
            PipeTransportJobDirection.StorageToMachine,
            PipeTransportJobTargetType.Machine,
            sourceStoragePos,
            targetMachinePos,
            packetItems,
            route,
            now,
            arrival
        );

        if (!RegisterJob(job))
        {
            //Log.Out($"[PipeTransportManager] TryRequestCrafterInputs failed to register packet job for machine={targetMachinePos}");
            return false;
        }

        remainingCapacity--;
        int packetItemCount = job.GetTotalItemCount();
        ConsumeThroughputBudget(pipeGraphId, now, packetItemCount);
        AdvanceMachineTurn(pipeGraphId, targetMachinePos, now);

        return true;
    }

    public static bool TryGetJob(Guid jobId, out PipeTransportJob job)
    {
        return activeJobs.TryGetValue(jobId, out job);
    }
    private static int ComputeMaxActiveJobsForGraph(WorldBase world, int clrIdx, Guid pipeGraphId)
    {
        int throughput = ComputeGraphThroughputBudget(world, clrIdx, pipeGraphId);
        if (throughput <= 0)
            throughput = DefaultPipeThroughput;

        int scaled = throughput * ActiveJobsPerThroughputUnit;
        int capped = Math.Min(MaxActiveJobsPerGraph, Math.Max(MinActiveJobsPerGraph, scaled));
        return capped > 0 ? capped : MinActiveJobsPerGraph;
    }

    public static int GetRemainingCapacityForGraph(Guid pipeGraphId)
    {
        return GetRemainingCapacityForGraph(null, 0, pipeGraphId);
    }

    public static int GetRemainingCapacityForGraph(WorldBase world, int clrIdx, Guid pipeGraphId)
    {
        if (pipeGraphId == Guid.Empty)
            return 0;

        int active = GetActiveJobCountForGraph(pipeGraphId);
        int capacity = ComputeMaxActiveJobsForGraph(world, clrIdx, pipeGraphId);
        int remaining = capacity - active;

        return remaining > 0 ? remaining : 0;
    }

    public static bool TryCreateJob(
    WorldBase world,
    int clrIdx,
    Guid pipeGraphId,
    Vector3i sourceMachinePos,
    Vector3i targetStoragePos,
    string itemName,
    int pendingAmount,
    out PipeTransportJob job,
    out int acceptedAmount)
    {
        return TryCreateJob(
            world,
            clrIdx,
            pipeGraphId,
            sourceMachinePos,
            targetStoragePos,
            itemName,
            pendingAmount,
            out job,
            out acceptedAmount,
            out _);
    }

    public static bool TryCreateJob(
    WorldBase world,
    int clrIdx,
    Guid pipeGraphId,
    Vector3i sourceMachinePos,
    Vector3i targetStoragePos,
    string itemName,
    int pendingAmount,
    out PipeTransportJob job,
    out int acceptedAmount,
    out string blockedReason)
    {
        job = null;
        acceptedAmount = 0;
        blockedReason = string.Empty;

        bool devLog = IsDevLoggingEnabledForMachine(world, clrIdx, sourceMachinePos);

        void LogCreateBlocked(string reason, int accepted)
        {
            if (!devLog)
                return;

            Log.Out($"[PipeTransportManager][CreateJob][{sourceMachinePos}] BLOCKED graph={pipeGraphId} target={targetStoragePos} item={itemName} pending={pendingAmount} accepted={accepted} reason={reason}");
        }

        if (world == null || world.IsRemote())
        {
            blockedReason = "Invalid world context for dispatch";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        if (pipeGraphId == Guid.Empty)
        {
            blockedReason = "No pipe graph selected";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        if (string.IsNullOrEmpty(itemName) || pendingAmount <= 0)
        {
            blockedReason = "No valid pending item to dispatch";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        if (!PipeGraphManager.TryFindRoute(world, clrIdx, pipeGraphId, sourceMachinePos, targetStoragePos, out List<Vector3i> route))
        {
            blockedReason = "No route between machine and selected storage";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        if (route == null || route.Count == 0)
        {
            blockedReason = "Resolved route is empty";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        int routeThroughput = GetRouteThroughput(world, clrIdx, route);
        if (routeThroughput <= 0)
        {
            blockedReason = $"Route throughput is zero (routeLen={route.Count})";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        acceptedAmount = Math.Min(pendingAmount, routeThroughput);
        if (acceptedAmount <= 0)
        {
            blockedReason = $"Route accepted amount is zero (pending={pendingAmount} routeThroughput={routeThroughput})";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        int activeJobsForGraph = GetActiveJobCountForGraph(pipeGraphId);
        int maxJobsForGraph = ComputeMaxActiveJobsForGraph(world, clrIdx, pipeGraphId);
        int remainingCapacityForGraph = maxJobsForGraph - activeJobsForGraph;
        if (remainingCapacityForGraph <= 0)
        {
            blockedReason = $"Pipe graph active-job capacity is exhausted (active={activeJobsForGraph} max={maxJobsForGraph})";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        ulong now = world.GetWorldTime();
        int remainingBudget = GetRemainingThroughputBudget(world, clrIdx, pipeGraphId, now);
        if (remainingBudget <= 0)
        {
            blockedReason = $"Pipe graph throughput budget is exhausted for this tick (worldTime={now})";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        acceptedAmount = Math.Min(acceptedAmount, remainingBudget);
        if (acceptedAmount <= 0)
        {
            blockedReason = $"Accepted amount reduced to zero by throughput budget (remainingBudget={remainingBudget})";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        int machinePriority = GetMachinePriority(world, clrIdx, sourceMachinePos);
        if (!TryReserveMachineTurn(pipeGraphId, sourceMachinePos, machinePriority, now))
        {
            blockedReason = $"Fairness scheduler denied this machine dispatch turn (priority={machinePriority})";
            LogCreateBlocked(blockedReason, acceptedAmount);
            return false;
        }

        ulong travelTicks = GetRouteTravelTicks(world, clrIdx, route);
        ulong arrival = now + travelTicks;

        job = new PipeTransportJob(
            pipeGraphId,
            PipeTransportJobDirection.MachineToStorage,
            PipeTransportJobTargetType.Storage,
            sourceMachinePos,
            targetStoragePos,
            itemName,
            acceptedAmount,
            route,
            now,
            arrival
        );

        // Machine->storage jobs have already picked up items at dispatch time.
        job.HasPickedUpItems = true;
        job.TransitStartWorldTime = now;

        bool registered = RegisterJob(job);
        if (registered)
        {
            ConsumeThroughputBudget(pipeGraphId, now, acceptedAmount);
            AdvanceMachineTurn(pipeGraphId, sourceMachinePos, now);

            if (devLog)
            {
                Log.Out($"[PipeTransportManager][CreateJob][{sourceMachinePos}] SUCCESS graph={pipeGraphId} target={targetStoragePos} item={itemName} accepted={acceptedAmount} pending={pendingAmount} routeLen={route.Count} routeThroughput={routeThroughput} budgetBefore={remainingBudget} active={activeJobsForGraph} max={maxJobsForGraph} priority={machinePriority}");
            }
        }
        else
        {
            blockedReason = "Failed to register transport job";
            LogCreateBlocked(blockedReason, acceptedAmount);
        }

        return registered;
    }

    public static bool CanDispatch(
    ulong lastDispatchWorldTime,
    ulong currentWorldTime,
    WorldBase world,
    int clrIdx,
    Guid pipeGraphId,
    Vector3i sourcePos,
    Vector3i targetPos)
    {
        if (lastDispatchWorldTime == 0UL)
            return true;

        ulong dispatchIntervalTicks = GetRouteDispatchIntervalTicks(world, clrIdx, pipeGraphId, sourcePos, targetPos);
        return currentWorldTime >= lastDispatchWorldTime + dispatchIntervalTicks;
    }

    public static bool RegisterJob(PipeTransportJob job)
    {
        if (job == null)
            return false;

        if (job.JobId == Guid.Empty)
            return false;

        if (!job.HasValidGraphId())
            return false;

        if (!job.HasValidItem())
            return false;

        if (!job.HasValidEndpoints())
            return false;

        if (!job.HasRoute())
            return false;

        if (activeJobs.ContainsKey(job.JobId))
            return false;

        activeJobs[job.JobId] = job;
        return true;
    }

    public static void RemoveJob(Guid jobId)
    {
        if (jobId == Guid.Empty)
            return;

        activeJobs.Remove(jobId);
        ClearJobDebugState(jobId);
    }

    public static void ProcessJobs(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        if (activeJobs.Count == 0)
            return;

        ulong worldTime = world.GetWorldTime();
        List<Guid> completedOrFailed = new List<Guid>();

        foreach (var kvp in activeJobs)
        {
            PipeTransportJob job = kvp.Value;
            if (job == null)
            {
                completedOrFailed.Add(kvp.Key);
                continue;
            }

            if (job.IsComplete || job.IsFailed)
            {
                completedOrFailed.Add(kvp.Key);
                continue;
            }

            bool devLogForJob = IsDevLoggingEnabledForJob(world, job) || IsDevLoggingEnabledForMachine(world, 0, job.SourcePos);

            if (job.IsStorageToMachine() && job.IsWaitingForPickup())
            {
                bool pickedUp = TryPickupInputJob(world, job, out string pickupBlockedReason);
                if (!pickedUp)
                {
                    string reasonText = string.IsNullOrEmpty(pickupBlockedReason) ? "Pickup blocked" : pickupBlockedReason;
                    string throttledReason = "PICKUP: " + reasonText;
                    if (devLogForJob && ShouldEmitBlockedJobLog(job, throttledReason, worldTime))
                        Log.Out($"[PipeTransportManager][Job][{job.JobId}] WAIT reason={throttledReason} graph={job.PipeGraphId} source={job.SourcePos} target={job.TargetPos}");

                    continue;
                }
            }

            if (!job.IsReadyToArrive(worldTime))
                continue;

            bool delivered = TryDeliverJob(world, job, out string deliveryBlockedReason);
            if (delivered)
            {
                if (devLogForJob)
                    Log.Out($"[PipeTransportManager][Job][{job.JobId}] DELIVERED graph={job.PipeGraphId} source={job.SourcePos} target={job.TargetPos} items={job.GetTotalItemCount()}");

                job.IsComplete = true;
                completedOrFailed.Add(kvp.Key);
                continue;
            }

            string deliveryReasonText = string.IsNullOrEmpty(deliveryBlockedReason) ? "Delivery blocked" : deliveryBlockedReason;
            string deliveryThrottledReason = "DELIVERY: " + deliveryReasonText;
            if (devLogForJob && ShouldEmitBlockedJobLog(job, deliveryThrottledReason, worldTime))
                Log.Out($"[PipeTransportManager][Job][{job.JobId}] WAIT reason={deliveryThrottledReason} graph={job.PipeGraphId} source={job.SourcePos} target={job.TargetPos}");
        }

        for (int i = 0; i < completedOrFailed.Count; i++)
            RemoveJob(completedOrFailed[i]);
    }

    private static bool TryPickupInputJob(WorldBase world, PipeTransportJob job)
    {
        return TryPickupInputJob(world, job, out _);
    }

    private static bool TryPickupInputJob(WorldBase world, PipeTransportJob job, out string blockedReason)
    {
        blockedReason = string.Empty;

        if (world == null || job == null)
        {
            blockedReason = "Invalid world or job while attempting pickup";
            return false;
        }

        // If the source chest is loaded and open, preserve existing behavior and wait.
        if (SafeWorldRead.TryGetTileEntity(world, job.SourcePos, out TileEntity te) &&
            te is TileEntityComposite comp)
        {
            TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
            if (storage != null && storage.items != null && storage.IsUserAccessing())
            {
                blockedReason = "Source storage is being accessed by a player";
                return false;
            }
        }

        Dictionary<string, int> request = job.GetPacketItemCountsCopy();
        if (request == null || request.Count == 0)
        {
            blockedReason = "Input packet is empty";
            return false;
        }

        if (!PipeGraphManager.TryConsumeStorageItems(world, 0, job.PipeGraphId, job.SourcePos, request, out Dictionary<string, int> consumed))
        {
            blockedReason = "Source storage could not provide requested packet";
            return false;
        }

        Dictionary<string, int> pickedPacket = new Dictionary<string, int>();
        int totalPicked = 0;

        foreach (var kvp in request)
        {
            int removed = consumed != null && consumed.TryGetValue(kvp.Key, out int value) ? value : 0;
            if (removed <= 0)
                continue;

            pickedPacket[kvp.Key] = removed;
            totalPicked += removed;
        }

        if (totalPicked <= 0)
        {
            blockedReason = "No items were removed from source storage during pickup";
            return false;
        }

        if (totalPicked != job.GetTotalItemCount())
            //Log.Out($"[PipeTransportManager] Input packet partial pickup {job.JobId} picked={totalPicked}");

            job.SetPacketItemCounts(pickedPacket);
        job.HasPickedUpItems = true;
        job.TransitStartWorldTime = world.GetWorldTime();

        return true;
    }

    private static int GetActiveJobCountForGraph(Guid pipeGraphId)
    {
        if (pipeGraphId == Guid.Empty)
            return 0;

        int count = 0;

        foreach (var kvp in activeJobs)
        {
            PipeTransportJob job = kvp.Value;
            if (job == null)
                continue;

            if (job.PipeGraphId == pipeGraphId && !job.IsComplete && !job.IsFailed)
                count++;
        }

        return count;
    }
    private static bool TryDeliverJob(WorldBase world, PipeTransportJob job)
    {
        return TryDeliverJob(world, job, out _);
    }

    private static bool TryDeliverJob(WorldBase world, PipeTransportJob job, out string blockedReason)
    {
        blockedReason = string.Empty;

        if (job == null || world == null)
        {
            blockedReason = "Invalid world or job while attempting delivery";
            return false;
        }

        if (job.IsMachineToStorage())
        {
            if (!SafeWorldRead.TryGetTileEntity(world, job.TargetPos, out TileEntity te))
            {
                blockedReason = "Target storage tile entity is missing";
                return false;
            }

            if (!(te is TileEntityComposite comp))
            {
                blockedReason = "Target tile entity is not a composite storage";
                return false;
            }

            TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
            if (storage == null || storage.items == null)
            {
                blockedReason = "Target storage feature is unavailable";
                return false;
            }

            if (storage.IsUserAccessing())
            {
                blockedReason = "Target storage is currently open by a player";
                return false;
            }

            Dictionary<string, int> packet = job.GetPacketItemCountsCopy();
            if (packet.Count == 0)
            {
                blockedReason = "Transport packet is empty";
                return false;
            }

            foreach (var kvp in packet)
            {
                ItemValue itemValue = ItemClass.GetItem(kvp.Key, false);
                if (itemValue == null || itemValue.type == ItemValue.None.type)
                {
                    blockedReason = $"Invalid item in transport packet: {kvp.Key}";
                    return false;
                }

                ItemStack remaining = new ItemStack(itemValue, kvp.Value);
                if (!TryAddToStorage(storage, remaining, out string storageBlockedReason))
                {
                    string reasonText = string.IsNullOrEmpty(storageBlockedReason) ? "Unknown storage insert failure" : storageBlockedReason;
                    blockedReason = $"Failed to deposit {kvp.Value}x {kvp.Key} into target storage ({reasonText})";
                    return false;
                }
            }

            return true;
        }

        if (job.IsStorageToMachine())
        {
            if (!SafeWorldRead.TryGetTileEntity(world, job.TargetPos, out TileEntity te))
            {
                blockedReason = "Target machine tile entity is missing";
                return false;
            }

            if (!(te is TileEntityMachine machine))
            {
                blockedReason = "Target tile entity is not a machine";
                return false;
            }

            Dictionary<string, int> requestedPacket = job.GetPacketItemCountsCopy();
            if (requestedPacket.Count == 0)
            {
                blockedReason = "Input packet is empty";
                return false;
            }

            Dictionary<string, int> remainingPacket = new Dictionary<string, int>();
            int deliveredTotal = 0;

            foreach (var kvp in requestedPacket)
            {
                int requested = kvp.Value;
                if (requested <= 0)
                    continue;

                int accepted = machine.ReceiveBufferedInput(kvp.Key, requested);
                if (accepted < 0)
                    accepted = 0;

                if (accepted > requested)
                    accepted = requested;

                deliveredTotal += accepted;

                int stillNeeded = requested - accepted;
                if (stillNeeded > 0)
                    remainingPacket[kvp.Key] = stillNeeded;
            }

            if (deliveredTotal <= 0)
            {
                blockedReason = "Target machine accepted zero items from packet";
                return false;
            }

            if (remainingPacket.Count == 0)
                return true;

            job.SetPacketItemCounts(remainingPacket);
            blockedReason = $"Target machine partially accepted packet (delivered={deliveredTotal} remaining={job.GetTotalItemCount()})";

            if (IsDevLoggingEnabledForJob(world, job))
                Log.Out($"[PipeTransportManager] Input packet partial delivery {job.JobId} delivered={deliveredTotal} remaining={job.GetTotalItemCount()}");

            return false;
        }

        blockedReason = "Unsupported transport job direction";
        return false;
    }

    private static bool TryAddToStorage(TEFeatureStorage storage, ItemStack remaining)
    {
        return TryAddToStorage(storage, remaining, out _);
    }

    private static bool TryAddToStorage(TEFeatureStorage storage, ItemStack remaining, out string blockedReason)
    {
        blockedReason = string.Empty;

        if (remaining == null || remaining.IsEmpty() || remaining.count <= 0)
            return true;

        bool changed = false;

        for (int i = 0; i < storage.items.Length && remaining.count > 0; i++)
        {
            ItemStack slot = storage.items[i];

            if (slot.IsEmpty())
                continue;

            if (slot.itemValue.type != remaining.itemValue.type)
                continue;

            int maxStack = slot.itemValue.ItemClass.Stacknumber.Value;
            int space = maxStack - slot.count;
            if (space <= 0)
                continue;

            int toMove = Math.Min(space, remaining.count);
            slot.count += toMove;
            remaining.count -= toMove;
            storage.items[i] = slot;
            changed = true;
        }

        for (int i = 0; i < storage.items.Length && remaining.count > 0; i++)
        {
            if (!storage.items[i].IsEmpty())
                continue;

            int maxStack = remaining.itemValue.ItemClass.Stacknumber.Value;
            int toMove = Math.Min(maxStack, remaining.count);

            storage.items[i] = new ItemStack(remaining.itemValue.Clone(), toMove);
            remaining.count -= toMove;
            changed = true;
        }

        if (changed)
            storage.SetModified();

        if (remaining.count <= 0)
            return true;

        string itemName = remaining.itemValue?.ItemClass?.GetItemName() ?? "unknown";
        blockedReason = $"Target storage has no room for {remaining.count}x {itemName}";
        return false;
    }

    private static int GetRouteThroughput(WorldBase world, int clrIdx, List<Vector3i> routePipePositions)
    {
        if (world == null || routePipePositions == null || routePipePositions.Count == 0)
            return DefaultPipeThroughput;

        int lowestThroughput = int.MaxValue;

        for (int i = 0; i < routePipePositions.Count; i++)
        {
            Vector3i pipePos = routePipePositions[i];
            if (!SafeWorldRead.TryGetBlock(world, clrIdx, pipePos, out BlockValue blockValue))
                continue;

            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity routePipeEntity) || !(routePipeEntity is TileEntityItemPipe))
                continue;

            int throughput = GetPipeThroughput(blockValue);
            if (throughput < lowestThroughput)
                lowestThroughput = throughput;
        }

        if (lowestThroughput == int.MaxValue || lowestThroughput <= 0)
            return DefaultPipeThroughput;

        return lowestThroughput;
    }

    private static ulong GetRouteTravelTicks(WorldBase world, int clrIdx, List<Vector3i> routePipePositions)
    {
        if (routePipePositions == null || routePipePositions.Count == 0)
            return BasePerPipeTravelTicks;

        int speedMultiplier = GetRouteSpeed(world, clrIdx, routePipePositions);
        if (speedMultiplier <= 0)
            speedMultiplier = DefaultPipeSpeed;

        ulong perPipeTicks = (ulong)Math.Ceiling(BasePerPipeTravelTicks / (double)speedMultiplier);
        if (perPipeTicks < 1UL)
            perPipeTicks = 1UL;

        ulong totalTicks = (ulong)routePipePositions.Count * perPipeTicks;
        return totalTicks > 0UL ? totalTicks : 1UL;
    }

    private static ulong GetRouteDispatchIntervalTicks(
    WorldBase world,
    int clrIdx,
    Guid pipeGraphId,
    Vector3i sourcePos,
    Vector3i targetPos)
    {
        if (world == null || world.IsRemote())
            return BaseDispatchIntervalTicks;

        if (pipeGraphId == Guid.Empty || sourcePos == Vector3i.zero || targetPos == Vector3i.zero)
            return BaseDispatchIntervalTicks;

        if (!PipeGraphManager.TryFindRoute(world, clrIdx, pipeGraphId, sourcePos, targetPos, out List<Vector3i> route))
            return BaseDispatchIntervalTicks;

        if (route == null || route.Count == 0)
            return BaseDispatchIntervalTicks;

        int latencyMultiplier = GetRouteLatency(world, clrIdx, route);
        if (latencyMultiplier <= 0)
            latencyMultiplier = DefaultPipeLatency;

        ulong interval = (ulong)Math.Ceiling(BaseDispatchIntervalTicks / (double)latencyMultiplier);
        return interval > 0UL ? interval : 1UL;
    }

    private static int GetRouteSpeed(WorldBase world, int clrIdx, List<Vector3i> routePipePositions)
    {
        if (world == null || routePipePositions == null || routePipePositions.Count == 0)
            return DefaultPipeSpeed;

        int lowestSpeed = int.MaxValue;

        for (int i = 0; i < routePipePositions.Count; i++)
        {
            Vector3i pipePos = routePipePositions[i];
            if (!SafeWorldRead.TryGetBlock(world, clrIdx, pipePos, out BlockValue blockValue))
                continue;

            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity routePipeEntity) || !(routePipeEntity is TileEntityItemPipe))
                continue;

            int speed = GetPipeSpeed(blockValue);
            if (speed < lowestSpeed)
                lowestSpeed = speed;
        }

        if (lowestSpeed == int.MaxValue || lowestSpeed <= 0)
            return DefaultPipeSpeed;

        return lowestSpeed;
    }

    private static int GetRouteLatency(WorldBase world, int clrIdx, List<Vector3i> routePipePositions)
    {
        if (world == null || routePipePositions == null || routePipePositions.Count == 0)
            return DefaultPipeLatency;

        int lowestLatency = int.MaxValue;

        for (int i = 0; i < routePipePositions.Count; i++)
        {
            Vector3i pipePos = routePipePositions[i];
            if (!SafeWorldRead.TryGetBlock(world, clrIdx, pipePos, out BlockValue blockValue))
                continue;

            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity routePipeEntity) || !(routePipeEntity is TileEntityItemPipe))
                continue;

            int latency = GetPipeLatency(blockValue);
            if (latency < lowestLatency)
                lowestLatency = latency;
        }

        if (lowestLatency == int.MaxValue || lowestLatency <= 0)
            return DefaultPipeLatency;

        return lowestLatency;
    }

    private static int GetPipeThroughput(BlockValue blockValue)
    {
        if (blockValue.Block?.Properties == null)
            return DefaultPipeThroughput;

        string raw = blockValue.Block.Properties.GetString("PipeThroughput");
        if (string.IsNullOrEmpty(raw))
            return DefaultPipeThroughput;

        if (!int.TryParse(raw, out int throughput) || throughput <= 0)
            return DefaultPipeThroughput;

        return throughput;
    }

    private static int GetPipeSpeed(BlockValue blockValue)
    {
        if (blockValue.Block?.Properties == null)
            return DefaultPipeSpeed;

        string raw = blockValue.Block.Properties.GetString("PipeSpeed");
        if (string.IsNullOrEmpty(raw))
            return DefaultPipeSpeed;

        if (!int.TryParse(raw, out int speed) || speed <= 0)
            return DefaultPipeSpeed;

        return speed;
    }

    private static int GetPipeLatency(BlockValue blockValue)
    {
        if (blockValue.Block?.Properties == null)
            return DefaultPipeLatency;

        string raw = blockValue.Block.Properties.GetString("PipeLatency");
        if (string.IsNullOrEmpty(raw))
            return DefaultPipeLatency;

        if (!int.TryParse(raw, out int latency) || latency <= 0)
            return DefaultPipeLatency;

        return latency;
    }
}




