using System;
using System.Collections.Generic;

public static class PipeTransportManager
{
    private const int MaxActiveJobsPerGraph = 10;

    private static readonly Dictionary<Guid, PipeTransportJob> activeJobs = new Dictionary<Guid, PipeTransportJob>();
    private const int DefaultPipeThroughput = 1;
    private const int DefaultPipeSpeed = 1;
    private const int DefaultPipeLatency = 1;
    private const ulong BaseDispatchIntervalTicks = 20UL; // 1 second at 20 ticks/sec
    private const ulong BasePerPipeTravelTicks = 5UL;     // 0.25 sec per pipe
    private const ulong MachineRequestTimeoutTicks = 200UL;

    private sealed class GraphDispatchState
    {
        public readonly Dictionary<Vector3i, int> MachinePriority = new Dictionary<Vector3i, int>();
        public readonly Dictionary<Vector3i, ulong> LastRequestWorldTime = new Dictionary<Vector3i, ulong>();
        public int RoundRobinCursor = 0;
    }

    private static readonly Dictionary<Guid, GraphDispatchState> graphDispatchStates = new Dictionary<Guid, GraphDispatchState>();
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

    public static void ClearAll()
    {
        activeJobs.Clear();
        graphDispatchStates.Clear();
        Log.Out("[PipeTransportManager] ClearAll()");
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
        }

        if (state.RoundRobinCursor < 0)
            state.RoundRobinCursor = 0;
    }

    private static List<Vector3i> BuildHighestPriorityContenders(GraphDispatchState state)
    {
        List<Vector3i> contenders = new List<Vector3i>();
        if (state == null || state.LastRequestWorldTime.Count == 0)
            return contenders;

        int highestPriority = int.MinValue;

        foreach (var kvp in state.LastRequestWorldTime)
        {
            Vector3i machinePos = kvp.Key;
            if (!state.MachinePriority.TryGetValue(machinePos, out int priority))
                priority = TileEntityMachine.DefaultPipePriority;

            priority = NormalizePriority(priority);

            if (priority > highestPriority)
            {
                highestPriority = priority;
                contenders.Clear();
                contenders.Add(machinePos);
                continue;
            }

            if (priority == highestPriority)
                contenders.Add(machinePos);
        }

        contenders.Sort(CompareMachinePos);
        return contenders;
    }

    private static bool TryReserveMachineTurn(Guid pipeGraphId, Vector3i machinePos, int machinePriority, ulong now)
    {
        if (pipeGraphId == Guid.Empty || machinePos == Vector3i.zero)
            return true;

        GraphDispatchState state = GetOrCreateDispatchState(pipeGraphId);
        state.MachinePriority[machinePos] = NormalizePriority(machinePriority);
        state.LastRequestWorldTime[machinePos] = now;

        PruneInactiveMachines(state, now);

        List<Vector3i> contenders = BuildHighestPriorityContenders(state);
        if (contenders.Count == 0)
            return true;

        int cursor = state.RoundRobinCursor % contenders.Count;
        if (cursor < 0)
            cursor = 0;

        return contenders[cursor] == machinePos;
    }

    private static void AdvanceMachineTurn(Guid pipeGraphId, Vector3i machinePos, ulong now)
    {
        if (pipeGraphId == Guid.Empty || machinePos == Vector3i.zero)
            return;

        if (!graphDispatchStates.TryGetValue(pipeGraphId, out GraphDispatchState state) || state == null)
            return;

        PruneInactiveMachines(state, now);

        List<Vector3i> contenders = BuildHighestPriorityContenders(state);
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


    public static bool TryRequestCrafterInputs(
    WorldBase world,
    int clrIdx,
    Guid pipeGraphId,
    Vector3i sourceStoragePos,
    Vector3i targetMachinePos,
    HashSet<string> neededItemNames)
    {
        if (world == null || world.IsRemote())
        {
            return false;
        }

        if (pipeGraphId == Guid.Empty)
        {
            return false;
        }

        if (sourceStoragePos == Vector3i.zero)
        {
            return false;
        }

        if (targetMachinePos == Vector3i.zero)
        {
            return false;
        }

        if (neededItemNames == null || neededItemNames.Count == 0)
        {
            return false;
        }

        if (!PipeGraphManager.TryGetGraph(pipeGraphId, out PipeGraphData graph) || graph == null)
        {
            return false;
        }

        if (graph.StorageEndpoints == null || !graph.StorageEndpoints.Contains(sourceStoragePos))
        {
            return false;
        }

        if (!PipeGraphManager.TryFindRoute(world, clrIdx, pipeGraphId, targetMachinePos, sourceStoragePos, out List<Vector3i> route))
        {
            return false;
        }

        if (route == null || route.Count == 0)
        {
            return false;
        }

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, sourceStoragePos, out TileEntity te))
        {
            return false;
        }
        if (!(te is TileEntityComposite comp))
        {
            return false;
        }

        TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
        {
            return false;
        }

        if (storage.IsUserAccessing())
        {
            return false;
        }

        int remainingCapacity = GetRemainingCapacityForGraph(pipeGraphId);
        if (remainingCapacity <= 0)
        {
            return false;
        }

        int routeThroughput = GetRouteThroughput(world, clrIdx, route);
        if (routeThroughput <= 0)
        {
            return false;
        }

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, targetMachinePos, out TileEntity targetTe))
        {
            return false;
        }

        if (!(targetTe is TileEntityMachine targetMachine))
        {
            return false;
        }

        ulong now = world.GetWorldTime();
        ulong travelTicks = GetRouteTravelTicks(world, clrIdx, route);
        bool createdAnyJobs = false;


        foreach (string itemName in neededItemNames)
        {
            if (remainingCapacity <= 0)
            {
                break;
            }

            if (string.IsNullOrEmpty(itemName))
            {
                continue;
            }

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

                if (existingJob.SourcePos != sourceStoragePos)
                    continue;

                if (existingJob.TargetPos != targetMachinePos)
                    continue;

                if (!string.Equals(existingJob.ItemName, itemName, StringComparison.Ordinal))
                    continue;

                alreadyQueuedForThisItem += existingJob.ItemCount;
            }

            int availableInStorage = 0;
            for (int i = 0; i < storage.items.Length; i++)
            {
                ItemStack stack = storage.items[i];
                if (stack.IsEmpty())
                    continue;

                if (stack.itemValue?.ItemClass == null)
                    continue;

                if (stack.itemValue.ItemClass.GetItemName() != itemName)
                    continue;

                availableInStorage += stack.count;
            }

            int availableToQueue = availableInStorage - alreadyQueuedForThisItem;


            if (availableToQueue <= 0)
            {
                continue;
            }

            int machineRemainingCapacity = targetMachine.GetBufferedInputRemainingCapacity(itemName);
            int machineQueueRoom = machineRemainingCapacity - alreadyQueuedForThisItem;
            if (machineQueueRoom <= 0)
            {
                continue;
            }

            int acceptedAmount = Math.Min(Math.Min(availableToQueue, routeThroughput), machineQueueRoom);
            if (acceptedAmount <= 0)
            {
                continue;
            }

            int machinePriority = GetMachinePriority(world, clrIdx, targetMachinePos);
            if (!TryReserveMachineTurn(pipeGraphId, targetMachinePos, machinePriority, now))
            {
                break;
            }

            ulong arrival = now + travelTicks;


            PipeTransportJob job = new PipeTransportJob(
                pipeGraphId,
                PipeTransportJobDirection.StorageToMachine,
                PipeTransportJobTargetType.Machine,
                sourceStoragePos,
                targetMachinePos,
                itemName,
                acceptedAmount,
                route,
                now,
                arrival
            );

            if (!RegisterJob(job))
            {
                Log.Out($"[PipeTransportManager] TryRequestCrafterInputs failed to register job for {acceptedAmount}x {itemName}");
                continue;
            }

            remainingCapacity--;
            createdAnyJobs = true;
            AdvanceMachineTurn(pipeGraphId, targetMachinePos, now);

        }

        return createdAnyJobs;
    }

    public static bool TryGetJob(Guid jobId, out PipeTransportJob job)
    {
        return activeJobs.TryGetValue(jobId, out job);
    }

    public static int GetRemainingCapacityForGraph(Guid pipeGraphId)
    {
        if (pipeGraphId == Guid.Empty)
            return 0;

        int active = GetActiveJobCountForGraph(pipeGraphId);
        int remaining = MaxActiveJobsPerGraph - active;

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
        job = null;
        acceptedAmount = 0;

        if (world == null || world.IsRemote())
            return false;

        if (pipeGraphId == Guid.Empty)
            return false;

        if (string.IsNullOrEmpty(itemName) || pendingAmount <= 0)
            return false;

        if (!PipeGraphManager.TryFindRoute(world, clrIdx, pipeGraphId, sourceMachinePos, targetStoragePos, out List<Vector3i> route))
        {
            return false;
        }

        if (route == null || route.Count == 0)
            return false;

        int routeThroughput = GetRouteThroughput(world, clrIdx, route);
        if (routeThroughput <= 0)
            return false;

        acceptedAmount = Math.Min(pendingAmount, routeThroughput);
        if (acceptedAmount <= 0)
            return false;

        if (GetActiveJobCountForGraph(pipeGraphId) >= MaxActiveJobsPerGraph)
        {
            return false;
        }

        ulong now = world.GetWorldTime();

        int machinePriority = GetMachinePriority(world, clrIdx, sourceMachinePos);
        if (!TryReserveMachineTurn(pipeGraphId, sourceMachinePos, machinePriority, now))
            return false;

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

        // Machine->storage jobs have already "picked up" items at dispatch time.
        job.HasPickedUpItems = true;
        job.TransitStartWorldTime = now;

        bool registered = RegisterJob(job);
        if (registered)
            AdvanceMachineTurn(pipeGraphId, sourceMachinePos, now);

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

            if (job.IsStorageToMachine() && job.IsWaitingForPickup())
            {
                bool pickedUp = TryPickupInputJob(world, job);
                if (!pickedUp)
                {
                    continue;
                }
            }

            if (!job.IsReadyToArrive(worldTime))
                continue;

            bool delivered = TryDeliverJob(world, job);
            if (delivered)
            {
                job.IsComplete = true;
                completedOrFailed.Add(kvp.Key);
            }
        }

        for (int i = 0; i < completedOrFailed.Count; i++)
            activeJobs.Remove(completedOrFailed[i]);
    }

    private static bool TryPickupInputJob(WorldBase world, PipeTransportJob job)
    {
        if (world == null || job == null)
            return false;

        if (!SafeWorldRead.TryGetTileEntity(world, job.SourcePos, out TileEntity te))
            return false;
        if (!(te is TileEntityComposite comp))
        {
            return false;
        }

        TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
            return false;

        if (storage.IsUserAccessing())
            return false;

        int remainingToRemove = job.ItemCount;

        for (int i = 0; i < storage.items.Length && remainingToRemove > 0; i++)
        {
            ItemStack stack = storage.items[i];
            if (stack.IsEmpty())
                continue;

            if (stack.itemValue?.ItemClass == null)
                continue;

            if (stack.itemValue.ItemClass.GetItemName() != job.ItemName)
                continue;

            int remove = Math.Min(stack.count, remainingToRemove);
            stack.count -= remove;
            remainingToRemove -= remove;

            if (stack.count <= 0)
                storage.items[i] = ItemStack.Empty;
            else
                storage.items[i] = stack;
        }

        int actuallyRemoved = job.ItemCount - remainingToRemove;
        if (actuallyRemoved <= 0)
            return false;

        if (actuallyRemoved != job.ItemCount)
        {
            job.ItemCount = actuallyRemoved;
            Log.Out($"[PipeTransportManager] Input job partial pickup {job.JobId} amount={actuallyRemoved}");
        }

        job.HasPickedUpItems = true;
        job.TransitStartWorldTime = world.GetWorldTime();
        storage.SetModified();

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
        if (job == null || world == null)
            return false;

        if (job.IsMachineToStorage())
        {
            if (!SafeWorldRead.TryGetTileEntity(world, job.TargetPos, out TileEntity te))
                return false;
            if (!(te is TileEntityComposite comp))
                return false;

            TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
            if (storage == null || storage.items == null)
                return false;

            if (storage.IsUserAccessing())
                return false;

            ItemValue itemValue = ItemClass.GetItem(job.ItemName, false);
            if (itemValue == null || itemValue.type == ItemValue.None.type)
                return false;

            ItemStack remaining = new ItemStack(itemValue, job.ItemCount);
            return TryAddToStorage(storage, remaining);
        }

        if (job.IsStorageToMachine())
        {
            if (!SafeWorldRead.TryGetTileEntity(world, job.TargetPos, out TileEntity te))
                return false;
            if (!(te is TileEntityMachine machine))
            {
                return false;
            }

            int accepted = machine.ReceiveBufferedInput(job.ItemName, job.ItemCount);
            if (accepted <= 0)
                return false;

            if (accepted >= job.ItemCount)
                return true;

            job.ItemCount -= accepted;
            if (IsDevLoggingEnabledForJob(world, job))
                Log.Out($"[PipeTransportManager] Input job partial delivery {job.JobId} accepted={accepted} remaining={job.ItemCount}");
            return false;
        }

        return false;
    }

    private static bool TryAddToStorage(TEFeatureStorage storage, ItemStack remaining)
    {
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

        return remaining.count <= 0;
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








