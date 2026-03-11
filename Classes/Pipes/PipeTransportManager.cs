using System;
using System.Collections.Generic;

public static class PipeTransportManager
{
    private const int MaxActiveJobsPerGraph = 5;

    private static readonly Dictionary<Guid, PipeTransportJob> activeJobs = new Dictionary<Guid, PipeTransportJob>();
    private const int DefaultPipeThroughput = 1;
    private const ulong DispatchIntervalTicks = 20UL;     // 1 second at 20 ticks/sec
    private const ulong PerPipeTravelTicks = 5UL;         // 0.25 sec per pipe

    public static void ClearAll()
    {
        activeJobs.Clear();
        Log.Out("[PipeTransportManager] ClearAll()");
    }

    public static int GetActiveJobCount()
    {
        return activeJobs.Count;
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

        TileEntity te = world.GetTileEntity(clrIdx, sourceStoragePos);
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

        ulong now = world.GetWorldTime();
        ulong travelTicks = (ulong)route.Count * PerPipeTravelTicks;
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

            int acceptedAmount = Math.Min(availableToQueue, routeThroughput);
            if (acceptedAmount <= 0)
            {
                continue;
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
        ulong travelTicks = (ulong)route.Count * PerPipeTravelTicks;
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

        return RegisterJob(job);
    }

    public static bool CanDispatch(ulong lastDispatchWorldTime, ulong currentWorldTime)
    {
        if (lastDispatchWorldTime == 0UL)
            return true;

        return currentWorldTime >= lastDispatchWorldTime + DispatchIntervalTicks;
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

        TileEntity te = world.GetTileEntity(job.SourcePos);
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
            TileEntity te = world.GetTileEntity(job.TargetPos);
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
            TileEntity te = world.GetTileEntity(job.TargetPos);
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
            BlockValue blockValue = world.GetBlock(clrIdx, pipePos);

            if (!(world.GetTileEntity(clrIdx, pipePos) is TileEntityItemPipe))
                continue;

            int throughput = GetPipeThroughput(blockValue);
            if (throughput < lowestThroughput)
                lowestThroughput = throughput;
        }

        if (lowestThroughput == int.MaxValue || lowestThroughput <= 0)
            return DefaultPipeThroughput;

        return lowestThroughput;
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
}

