using System;
using System.Collections.Generic;
using System.Linq;

public static class UCTileEntityIDs
{
    public const int UniversalExtractor = 132;
    public const int UniversalCrafter = 133;
    public const int UniversalWasher = 134;
    public const int NetworkController = 135;
    public const int ItemPipe = 136;
    public const int LiquidPipe = 137;
    public const int FluidPump = 138;
    public const int FluidStorage = 139;
}

public class TileEntityUniversalExtractor : TileEntityMachine
{
    // ---------------------------------------------
    // STATE
    // ---------------------------------------------
    public bool isExtractorOn = false;
    public bool isEnabledByPlayer = false;
    public readonly List<ResourceTimer> timers = new List<ResourceTimer>();

    public List<OutputTargetInfo> availableOutputTargets = new List<OutputTargetInfo>();

    public TileEntityComposite selectedOutputContainer;
    public Vector3i SelectedOutputChestPos = Vector3i.zero;
    public OutputTransportMode SelectedOutputMode = OutputTransportMode.Adjacent;
    public Guid SelectedPipeGraphId = Guid.Empty;

    public XUiC_ExtractorOutputContainerList xUiC_ExtractorOutputContainerList;

    public ulong LastPipeDispatchWorldTime = 0UL;

    private int pendingOutputRoundRobinIndex = 0;

    public class ResourceTimer
    {
        public string Resource;
        public int MinCount = 1;
        public int MaxCount = 1;
        public int Speed;
        public int Counter;

        public override string ToString()
        {
            return $"Resource={Resource} Count={MinCount}-{MaxCount} Speed={Speed} Counter={Counter}";
        }
    }

    // ---------------------------------------------
    // CONSTRUCTOR
    // ---------------------------------------------
    public TileEntityUniversalExtractor(Chunk chunk) : base(chunk)
    {
        DevLog($"CTOR — TileEntityUniversalExtractor CREATED -> IsOn = {isExtractorOn}");
    }

    // ---------------------------------------------
    // LOGGING
    // ---------------------------------------------
    private void DevLog(string msg)
    {
        if (!IsDevLogging)
            return;

        if (!IsImportantDevLogMessage(msg))
            return;

        Log.Out($"[Extractor][TE][{ToWorldPos()}] {msg}");
    }

    private static bool IsImportantDevLogMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg))
            return false;

        return
            msg.IndexOf("SERVER SET ENABLED", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("SERVER SELECT OUTPUT", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("FAIL", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("ABORT", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("BLOCKED", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("partial", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void ResolveSelectedOutputContainer(WorldBase world)
    {
        selectedOutputContainer = null;

        if (world == null || SelectedOutputChestPos == Vector3i.zero)
            return;

        selectedOutputContainer = world.GetTileEntity(SelectedOutputChestPos) as TileEntityComposite;
    }

    public void ServerSetEnabled(bool enabled)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
        {
            DevLog("ServerSetEnabled called on non-server");
            return;
        }

        isEnabledByPlayer = enabled;

        if (!enabled)
        {
            isExtractorOn = false;
            DevLog("SERVER SET ENABLED — false");
        }
        else
        {
            DevLog("SERVER SET ENABLED — true");
        }

        SetModified();
        NeedsUiRefresh = true;
    }

    public bool ServerSelectOutputContainer(Vector3i chestPos, OutputTransportMode mode, string pipeGraphId)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
        {
            DevLog("ServerSelectOutputContainer called on non-server");
            return false;
        }

        Guid parsedPipeGraphId = Guid.Empty;
        if (!string.IsNullOrEmpty(pipeGraphId))
            Guid.TryParse(pipeGraphId, out parsedPipeGraphId);

        bool changed =
            SelectedOutputChestPos != chestPos ||
            SelectedOutputMode != mode ||
            SelectedPipeGraphId != parsedPipeGraphId;

        if (chestPos == Vector3i.zero)
        {
            SelectedOutputChestPos = Vector3i.zero;
            SelectedOutputMode = OutputTransportMode.Adjacent;
            SelectedPipeGraphId = Guid.Empty;
            selectedOutputContainer = null;

            if (changed)
                DevLog("SERVER SELECT OUTPUT — cleared (changed)");
            else
                DevLog("SERVER SELECT OUTPUT — cleared (no change)");

            SetModified();
            NeedsUiRefresh = true;
            return true;
        }

        WorldBase world = GameManager.Instance.World;
        if (world == null)
        {
            DevLog("SERVER SELECT OUTPUT rejected: world is null");
            return false;
        }

        RefreshAvailableOutputTargets(world);
        List<OutputTargetInfo> outputs = availableOutputTargets;
        if (outputs == null || outputs.Count == 0)
        {
            DevLog("SERVER SELECT OUTPUT rejected: no available outputs");
            return false;
        }

        OutputTargetInfo matchedTarget = null;
        for (int i = 0; i < outputs.Count; i++)
        {
            OutputTargetInfo target = outputs[i];
            if (target == null)
                continue;

            if (target.BlockPos == chestPos &&
                target.TransportMode == mode &&
                target.PipeGraphId == parsedPipeGraphId)
            {
                matchedTarget = target;
                break;
            }
        }

        if (matchedTarget == null)
        {
            DevLog($"SERVER SELECT OUTPUT rejected: target not found pos={chestPos} mode={mode} pipeGraphId={parsedPipeGraphId}");
            return false;
        }

        TileEntity te = world.GetTileEntity(chestPos);
        if (!(te is TileEntityComposite comp))
        {
            DevLog($"SERVER SELECT OUTPUT rejected: no composite TE at {chestPos}");
            return false;
        }

        SelectedOutputChestPos = chestPos;
        SelectedOutputMode = mode;
        SelectedPipeGraphId = parsedPipeGraphId;
        selectedOutputContainer = comp;

        if (changed)
            DevLog($"SERVER SELECT OUTPUT — {chestPos} mode={mode} pipeGraphId={parsedPipeGraphId} (changed)");
        else
            DevLog($"SERVER SELECT OUTPUT — {chestPos} mode={mode} pipeGraphId={parsedPipeGraphId} (same selection)");

        SetModified();
        NeedsUiRefresh = true;
        return true;
    }

    private bool HasValidSelectedOutput(WorldBase world)
    {
        if (world == null)
            return false;

        if (!world.IsRemote())
            RefreshAvailableOutputTargets(world);

        if (SelectedOutputChestPos == Vector3i.zero)
            return false;

        List<OutputTargetInfo> outputs = GetAvailableOutputTargets(world);
        if (outputs == null || outputs.Count == 0)
            return false;

        for (int i = 0; i < outputs.Count; i++)
        {
            OutputTargetInfo target = outputs[i];
            if (target == null)
                continue;

            if (target.BlockPos == SelectedOutputChestPos &&
                target.TransportMode == SelectedOutputMode &&
                target.PipeGraphId == SelectedPipeGraphId)
            {
                return true;
            }
        }

        return false;
    }

    public List<OutputTargetInfo> GetAvailableOutputTargets(WorldBase world)
    {
        if (world == null)
            return availableOutputTargets ?? new List<OutputTargetInfo>();

        if (world.IsRemote())
            return availableOutputTargets ?? new List<OutputTargetInfo>();

        availableOutputTargets = MachineOutputDiscovery.GetAvailableOutputs(world, 0, ToWorldPos(), 8);
        return availableOutputTargets;
    }

    public void RefreshAvailableOutputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        availableOutputTargets = MachineOutputDiscovery.GetAvailableOutputs(world, 0, ToWorldPos(), 8);
        DevLog($"RefreshAvailableOutputTargets — count={availableOutputTargets?.Count ?? 0}");
    }

    protected override void OnPowerStateChanged(bool state)
    {
        if (state)
        {
            Log.Out("[Extractor] Power ON");
            isExtractorOn = true;
            isEnabledByPlayer = true;
            SetModified();
            NeedsUiRefresh = true;
        }
        else
        {
            Log.Out("[Extractor] Power OFF");
            isExtractorOn = false;
            isEnabledByPlayer = false;
            SetModified();
            NeedsUiRefresh = true;
        }
    }


    // ---------------------------------------------
    // ENGINE REQUIRED
    // ---------------------------------------------
    public override TileEntityType GetTileEntityType()
    {
        DevLog("GetTileEntityType()");
        return unchecked((TileEntityType)UCTileEntityIDs.UniversalExtractor);
    }

    // ---------------------------------------------
    // HLR STATE FLAG
    // ---------------------------------------------
    public override void SetSimulatedByHLR(bool value)
    {
        DevLog($"SetSimulatedByHLR({value}) (was {simulatedByHLR})");
        simulatedByHLR = value;
    }

    // ---------------------------------------------
    // HLR SNAPSHOT BUILD
    // ---------------------------------------------
    public override IHLRSnapshot BuildHLRSnapshot(WorldBase world)
    {
        DevLog("HLR SNAPSHOT BUILD — BEGIN");
        DevLog($"WorldTime={world.GetWorldTime()} IsOn={isExtractorOn} Timers={timers.Count}");

        for (int i = 0; i < timers.Count; i++)
            DevLog($"Timer[{i}] {timers[i]}");

        var snapshot = new ExtractorSnapshotV1
        {
            MachineId = this.MachineGuid,
            Position = ToWorldPos(),
            WorldTime = world.GetWorldTime(),
            IsOn = isExtractorOn,
            IsEnabledByPlayer = isEnabledByPlayer,
            LastHLRSimTime = world.GetWorldTime(),
            SelectedOutputChestPos = SelectedOutputChestPos,
            SelectedOutputPipeGraphId = SelectedPipeGraphId,
            Timers = new List<ResourceTimer>(timers),
            OwedResources = new Dictionary<string, int>()
        };

        DevLog("HLR SNAPSHOT BUILD — END");
        return snapshot;
    }

    // ---------------------------------------------
    // HLR SNAPSHOT APPLY
    // ---------------------------------------------
    public override void ApplyHLRSnapshot(object snapshotObj)
    {
        DevLog("HLR SNAPSHOT APPLY — BEGIN");

        if (!(snapshotObj is ExtractorSnapshotV1 snapshot))
        {
            DevLog("HLR SNAPSHOT APPLY — FAILED (invalid snapshot type)");
            return;
        }

        DevLog(
            $"Snapshot WorldTime={snapshot.WorldTime} IsOn={snapshot.IsOn} " +
            $"Timers={snapshot.Timers.Count} Owed={snapshot.OwedResources?.Count ?? 0}"
        );

        // Restore production state
        timers.Clear();
        timers.AddRange(snapshot.Timers);
        isExtractorOn = snapshot.IsOn;
        SelectedOutputChestPos = snapshot.SelectedOutputChestPos;
        SelectedPipeGraphId = snapshot.SelectedOutputPipeGraphId;
        isEnabledByPlayer = snapshot.IsEnabledByPlayer;

        for (int i = 0; i < timers.Count; i++)
            DevLog($"Applied Timer[{i}] {timers[i]}");

        // Apply owed resources into pending output
        if (snapshot.OwedResources != null && snapshot.OwedResources.Count > 0)
        {
            foreach (var kvp in snapshot.OwedResources)
            {
                AddPendingOutput(kvp.Key, kvp.Value);
                DevLog($"Applied OwedResource — {kvp.Value}x {kvp.Key}");
            }
        }
        else
        {
            DevLog("No owed resources to apply");
        }

        // HLR is done with this machine
        simulatedByHLR = false;

        DevLog("HLR SNAPSHOT APPLY — END");
    }

    public void EnsureTimersLoaded()
    {
        if (timers.Count == 0)
            LoadConfig();
    }

    // ---------------------------------------------
    // UPDATE TICK
    // ---------------------------------------------
    public override void UpdateTick(World world)
    {
        DevLog("UpdateTick ENTER");

        if (world.IsRemote())
        {
            DevLog("UpdateTick ABORT — remote world");
            return;
        }

        if (simulatedByHLR)
        {
            DevLog("UpdateTick ABORT — simulated by HLR");
            return;
        }

        bool resolvedOutput = ResolveSelectedOutputIfNeeded(world);
        if (resolvedOutput)
        {
            DevLog("UpdateTick — selected output runtime state refreshed");
        }

        RefreshAvailableOutputTargets(world);

        // -----------------------------
        // Ensure timers are loaded
        // -----------------------------
        if (timers.Count == 0)
        {
            DevLog("No timers loaded — LoadConfig()");
            LoadConfig();
        }


        // -----------------------------
        // Output availability check
        // -----------------------------
        bool hasSelectedOutput = HasValidSelectedOutput(world);
        if (!hasSelectedOutput)
        {
            if (isExtractorOn)
            {
                DevLog("Selected output is invalid or missing — shutting down extractor");
            }

            isExtractorOn = false;
            SetModified();
            return;
        }

        // -----------------------------
        // Player intent check (ABSOLUTE)
        // -----------------------------
        if (!isEnabledByPlayer)
        {
            if (isExtractorOn)
                DevLog("Extractor disabled by player");

            isExtractorOn = false;
            SetModified();
            return;
        }

        // -----------------------------
        // Auto-restart (only if allowed)
        // -----------------------------
        if (!isExtractorOn && isEnabledByPlayer)
        {
            DevLog("Output storage found — extractor running");
            isExtractorOn = true;
            SetModified();
        }

        // -----------------------------
        // Flush pending output first
        // -----------------------------
        if (pendingOutput.Count > 0)
        {
            DevLog($"PendingOutput DETECTED — entries={pendingOutput.Count}");
            LogPendingOutput();
            FlushPendingOutput(world);
        }

        base.UpdateTick(world);

        // -----------------------------
        // Production logic
        // -----------------------------
        foreach (var timer in timers)
        {
            timer.Counter++;
            DevLog($"Timer TICK — {timer.Resource} {timer.Counter}/{timer.Speed}");

            if (timer.Counter >= timer.Speed)
            {
                timer.Counter = 0;
                DevLog($"Timer READY — producing {timer.Resource}");
                ProduceResource(world, timer);
            }
        }

        DevLog("UpdateTick EXIT");
    }


    private bool HasNearbyStorage(World world)
    {
        Vector3i pos = ToWorldPos();
        const int radius = 3;

        for (int x = -radius; x <= radius; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -radius; z <= radius; z++)
                {
                    Vector3i check = pos + new Vector3i(x, y, z);

                    if (!(world.GetTileEntity(0, check) is TileEntityComposite comp))
                        continue;

                    var storage = comp.GetFeature<TEFeatureStorage>();
                    if (storage != null && storage.items != null)
                        return true;
                }

        return false;
    }


    // ---------------------------------------------
    // PRODUCTION
    // ---------------------------------------------
    private void ProduceResource(World world, ResourceTimer timer)
    {
        DevLog($"ProduceResource BEGIN — {timer.Resource}");

        var iv = ItemClass.GetItem(timer.Resource, false);
        if (iv == null || iv.type == ItemValue.None.type)
        {
            DevLog($"ProduceResource FAIL — invalid item '{timer.Resource}'");
            return;
        }

        int amount = timer.MinCount;
        if (timer.MinCount < timer.MaxCount)
        {
            amount = UnityEngine.Random.Range(timer.MinCount, timer.MaxCount + 1);
            DevLog($"Random roll {timer.MinCount}-{timer.MaxCount} ? {amount}");
        }

        AddPendingOutput(timer.Resource, amount);
        DevLog($"ProduceResource QUEUED — {amount}x {timer.Resource}");

        LogPendingOutput();
        DevLog("ProduceResource END");
    }

    // ---------------------------------------------
    // FLUSHING
    // ---------------------------------------------
    private void FlushPendingOutput(WorldBase world)
    {
        DevLog("FLUSH BEGIN");

        if (pendingOutput.Count == 0)
        {
            DevLog("FLUSH ABORT — no pending output");
            return;
        }

        ulong now = world.GetWorldTime();

        if (SelectedOutputMode == OutputTransportMode.Pipe &&
            !PipeTransportManager.CanDispatch(LastPipeDispatchWorldTime, now, world, 0, SelectedPipeGraphId, ToWorldPos(), SelectedOutputChestPos))
        {
            DevLog("FLUSH WAIT — pipe dispatch interval not ready yet");
            return;
        }

        int pipeJobsDispatchedThisPass = 0;
        int pipeJobsAllowedThisPass = 0;

        if (SelectedOutputMode == OutputTransportMode.Pipe && SelectedPipeGraphId != Guid.Empty)
        {
            pipeJobsAllowedThisPass = PipeTransportManager.GetRemainingCapacityForGraph(SelectedPipeGraphId);

            if (pipeJobsAllowedThisPass <= 0)
            {
                DevLog($"FLUSH PIPE BLOCKED — graph {SelectedPipeGraphId} has no remaining job capacity");
                return;
            }
        }

        // PIPE MODE
        if (SelectedOutputMode == OutputTransportMode.Pipe)
        {
            if (SelectedOutputChestPos == Vector3i.zero || SelectedPipeGraphId == Guid.Empty)
            {
                DevLog("FLUSH BLOCKED — pipe output selected but target/graph is invalid");
                return;
            }

            while (pipeJobsDispatchedThisPass < pipeJobsAllowedThisPass && pendingOutput.Count > 0)
            {
                List<string> keys = pendingOutput.Keys.ToList();
                if (keys.Count == 0)
                    break;

                int startIndex = pendingOutputRoundRobinIndex % keys.Count;
                bool dispatchedAnyThisCycle = false;

                for (int step = 0; step < keys.Count && pipeJobsDispatchedThisPass < pipeJobsAllowedThisPass; step++)
                {
                    int index = (startIndex + step) % keys.Count;
                    string itemName = keys[index];

                    if (!pendingOutput.TryGetValue(itemName, out int count))
                        continue;

                    DevLog($"FLUSH ATTEMPT — {count}x {itemName}");

                    if (count <= 0)
                    {
                        DevLog($"FLUSH CLEANUP — removing zero-count entry '{itemName}'");
                        pendingOutput.Remove(itemName);
                        continue;
                    }

                    ItemValue itemValue = ItemClass.GetItem(itemName, false);
                    if (itemValue == null || itemValue.type == ItemValue.None.type)
                    {
                        DevLog($"FLUSH FAILED — invalid item '{itemName}', removing");
                        pendingOutput.Remove(itemName);
                        continue;
                    }

                    if (PipeTransportManager.TryCreateJob(
                        world,
                        0,
                        SelectedPipeGraphId,
                        ToWorldPos(),
                        SelectedOutputChestPos,
                        itemName,
                        count,
                        out PipeTransportJob job,
                        out int acceptedAmount))
                    {
                        if (acceptedAmount >= count)
                        {
                            pendingOutput.Remove(itemName);
                        }
                        else
                        {
                            pendingOutput[itemName] = count - acceptedAmount;
                        }

                        pipeJobsDispatchedThisPass++;
                        dispatchedAnyThisCycle = true;

                        pendingOutputRoundRobinIndex = index + 1;

                        DevLog($"FLUSH PIPE SUCCESS — queued job {job.JobId} for {acceptedAmount}x {itemName} routeLen={job.RoutePipePositions.Count}");
                    }
                    else
                    {
                        DevLog($"FLUSH PIPE BLOCKED — could not create transport job for {count}x {itemName}");
                    }
                }

                if (!dispatchedAnyThisCycle)
                {
                    DevLog("FLUSH PIPE STOP — no dispatches succeeded this cycle");
                    break;
                }
            }

            if (pipeJobsDispatchedThisPass > 0)
            {
                LastPipeDispatchWorldTime = now;
                DevLog($"FLUSH PIPE PASS COMPLETE — dispatched {pipeJobsDispatchedThisPass}/{pipeJobsAllowedThisPass} jobs this pass");
            }

            DevLog("FLUSH END");
            return;
        }

        // ADJACENT MODE
        foreach (var kvp in pendingOutput.ToList())
        {
            string itemName = kvp.Key;
            int count = kvp.Value;

            DevLog($"FLUSH ATTEMPT — {count}x {itemName}");

            if (count <= 0)
            {
                DevLog($"FLUSH CLEANUP — removing zero-count entry '{itemName}'");
                pendingOutput.Remove(itemName);
                continue;
            }

            ItemValue itemValue = ItemClass.GetItem(itemName, false);
            if (itemValue == null || itemValue.type == ItemValue.None.type)
            {
                DevLog($"FLUSH FAILED — invalid item '{itemName}', removing");
                pendingOutput.Remove(itemName);
                continue;
            }

            if (SelectedOutputChestPos == Vector3i.zero)
            {
                DevLog("FLUSH BLOCKED — adjacent output selected but no chest is selected");
                break;
            }

            ItemStack stack = new ItemStack(itemValue, count);
            bool success = TryAddToSelectedAdjacentStorage((World)world, stack);

            if (success)
            {
                pendingOutput.Remove(itemName);
                DevLog($"FLUSH ADJACENT SUCCESS — deposited {count}x {itemName} into selected chest");
            }
            else
            {
                DevLog($"FLUSH ADJACENT BLOCKED — selected chest could not accept {count}x {itemName}");
                break;
            }
        }

        DevLog("FLUSH END");
    }

    public bool ResolveSelectedOutputIfNeeded(WorldBase world)
    {
        if (world == null)
            return false;

        bool changed = false;

        TileEntityComposite resolvedContainer = null;
        if (SelectedOutputChestPos != Vector3i.zero)
        {
            resolvedContainer = world.GetTileEntity(SelectedOutputChestPos) as TileEntityComposite;
        }

        if (!object.ReferenceEquals(selectedOutputContainer, resolvedContainer))
        {
            selectedOutputContainer = resolvedContainer;
            changed = true;
        }

        if (SelectedOutputChestPos == Vector3i.zero)
            return changed;

        if (SelectedOutputMode != OutputTransportMode.Pipe)
            return changed;

        List<OutputTargetInfo> outputs;

        if (world.IsRemote())
        {
            outputs = availableOutputTargets;
        }
        else
        {
            RefreshAvailableOutputTargets(world);
            outputs = availableOutputTargets;
        }

        if (outputs == null || outputs.Count == 0)
        {
            DevLog("ResolveSelectedOutputIfNeeded — no available outputs found");
            return changed;
        }

        OutputTargetInfo fallbackTarget = null;

        for (int i = 0; i < outputs.Count; i++)
        {
            OutputTargetInfo target = outputs[i];
            if (target == null)
                continue;

            if (target.BlockPos != SelectedOutputChestPos)
                continue;

            if (target.TransportMode != OutputTransportMode.Pipe)
                continue;

            if (fallbackTarget == null)
                fallbackTarget = target;

            if (target.PipeGraphId == SelectedPipeGraphId)
                return changed;
        }

        if (fallbackTarget == null)
        {
            DevLog("ResolveSelectedOutputIfNeeded — selected pipe output could not be refreshed");
            return changed;
        }

        if (SelectedPipeGraphId != fallbackTarget.PipeGraphId)
        {
            SelectedPipeGraphId = fallbackTarget.PipeGraphId;
            changed = true;
            DevLog($"ResolveSelectedOutputIfNeeded — refreshed pipe graph id to {SelectedPipeGraphId}");
        }

        return changed;
    }

    // ---------------------------------------------
    // STORAGE
    // ---------------------------------------------
    private bool TryAddToSelectedAdjacentStorage(World world, ItemStack remaining)
    {
        if (remaining == null || remaining.IsEmpty() || remaining.count <= 0)
        {
            DevLog("TryAddToSelectedAdjacentStorage ABORT — remaining stack was null/empty");
            return true;
        }

        if (SelectedOutputChestPos == Vector3i.zero)
        {
            DevLog("TryAddToSelectedAdjacentStorage ABORT — no selected output chest");
            return false;
        }

        Vector3i myPos = ToWorldPos();
        Vector3i delta = SelectedOutputChestPos - myPos;

        int manhattan = Math.Abs(delta.x) + Math.Abs(delta.y) + Math.Abs(delta.z);
        if (manhattan != 1)
        {
            DevLog($"TryAddToSelectedAdjacentStorage ABORT — selected chest at {SelectedOutputChestPos} is not directly adjacent");
            return false;
        }

        if (!(world.GetTileEntity(0, SelectedOutputChestPos) is TileEntityComposite comp))
        {
            DevLog($"TryAddToSelectedAdjacentStorage FAIL — no composite TE at {SelectedOutputChestPos}");
            return false;
        }

        TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
        {
            DevLog($"TryAddToSelectedAdjacentStorage FAIL — selected chest at {SelectedOutputChestPos} has no storage feature");
            return false;
        }

        if (storage.IsUserAccessing())
        {
            DevLog($"TryAddToSelectedAdjacentStorage WAIT — selected chest at {SelectedOutputChestPos} is currently in use");
            return false;
        }

        string itemName = remaining.itemValue?.ItemClass?.GetItemName() ?? "unknown";
        DevLog($"TryAddToSelectedAdjacentStorage BEGIN — {remaining.count}x {itemName} into {SelectedOutputChestPos}");

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
            changed = true;

            DevLog($"  - Merged {toMove} into slot {i} ({slot.count}/{maxStack})");
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

            DevLog($"  - Placed {toMove} into empty slot {i} ({toMove}/{maxStack})");
        }

        if (changed)
        {
            storage.SetModified();
        }

        if (remaining.count <= 0)
        {
            DevLog("TryAddToSelectedAdjacentStorage SUCCESS — fully deposited");
            return true;
        }

        DevLog($"TryAddToSelectedAdjacentStorage BLOCKED — selected chest full, {remaining.count} remaining");
        return false;
    }

    // ---------------------------------------------
    // CONFIG
    // ---------------------------------------------
    private void LoadConfig()
    {
        DevLog("LoadConfig BEGIN");

        Log.Out($"[Extractor][TE][{ToWorldPos()}] LoadConfig() blockValueBlock={(blockValue.Block == null ? "NULL" : blockValue.Block.GetBlockName())} ResourceGenerated='{blockValue.Block?.Properties?.GetString("ResourceGenerated")}'");

        string resStr = blockValue.Block.Properties.GetString("ResourceGenerated");
        if (string.IsNullOrEmpty(resStr))
        {
            DevLog("LoadConfig ABORT — no ResourceGenerated");
            return;
        }

        string countStr = blockValue.Block.Properties.GetString("OutputCount");
        string speedStr = blockValue.Block.Properties.GetString("ExtractionSpeed");

        var res = resStr.Split(',');
        var counts = string.IsNullOrEmpty(countStr) ? new string[0] : countStr.Split(',');
        var speeds = string.IsNullOrEmpty(speedStr) ? new string[0] : speedStr.Split(',');

        for (int i = 0; i < res.Length; i++)
        {
            int min = 1, max = 1;
            if (i < counts.Length && counts[i].Contains("-"))
            {
                var p = counts[i].Split('-');
                int.TryParse(p[0], out min);
                int.TryParse(p[1], out max);
            }

            int speed = (i < speeds.Length && int.TryParse(speeds[i], out int s)) ? s : 300;

            timers.Add(new ResourceTimer
            {
                Resource = res[i].Trim(),
                MinCount = min,
                MaxCount = max,
                Speed = speed,
                Counter = 0
            });

            DevLog($"Configured Timer — {res[i]} {min}-{max} speed={speed}");
        }
        setModified();
        DevLog($"LoadConfig END — timers={timers.Count}");
    }

    // ---------------------------------------------
    // SAVE / LOAD
    // ---------------------------------------------
    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        DevLog($"[Extractor][TE][{ToWorldPos()}] WRITE BEGIN mode={mode}");

        base.write(bw, mode);

        if (mode == StreamModeWrite.ToClient)
        {
            // power-related UI fields
            bw.Write(isExtractorOn);
            bw.Write(isEnabledByPlayer);

            // selected output state
            bw.Write(SelectedOutputChestPos.x);
            bw.Write(SelectedOutputChestPos.y);
            bw.Write(SelectedOutputChestPos.z);
            bw.Write((int)SelectedOutputMode);
            bw.Write(SelectedPipeGraphId.ToString());

            // available output targets
            bw.Write(availableOutputTargets?.Count ?? 0);
            if (availableOutputTargets != null)
            {
                for (int i = 0; i < availableOutputTargets.Count; i++)
                {
                    OutputTargetInfo target = availableOutputTargets[i];
                    bw.Write(target.BlockPos.x);
                    bw.Write(target.BlockPos.y);
                    bw.Write(target.BlockPos.z);
                    bw.Write((int)target.TransportMode);
                    bw.Write(target.PipeGraphId.ToString());
                }
            }

            // timers for production display
            bw.Write(timers.Count);
            foreach (var t in timers)
            {
                bw.Write(t.Resource ?? "");
                bw.Write(t.MinCount);
                bw.Write(t.MaxCount);
                bw.Write(t.Speed);
                bw.Write(t.Counter);
            }

            return;
        }

        if (mode != StreamModeWrite.Persistency)
        {
            DevLog($"[Extractor][TE][{ToWorldPos()}] WRITE SKIP custom (mode={mode})");
            return;
        }

        bw.Write(2); // VERSION

        bw.Write(isExtractorOn);
        bw.Write(isEnabledByPlayer);

        bw.Write(SelectedOutputChestPos.x);
        bw.Write(SelectedOutputChestPos.y);
        bw.Write(SelectedOutputChestPos.z);
        bw.Write((int)SelectedOutputMode);
        bw.Write(SelectedPipeGraphId.ToString());

        bw.Write(timers.Count);
        foreach (var t in timers)
        {
            bw.Write(t.Resource);
            bw.Write(t.MinCount);
            bw.Write(t.MaxCount);
            bw.Write(t.Speed);
            bw.Write(t.Counter);
        }

        DevLog($"[Extractor][TE][{ToWorldPos()}] WRITE END Persistency");
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);

        if (mode == StreamModeRead.FromServer)
        {
            isExtractorOn = br.ReadBoolean();
            isEnabledByPlayer = br.ReadBoolean();

            int outX = br.ReadInt32();
            int outY = br.ReadInt32();
            int outZ = br.ReadInt32();
            SelectedOutputChestPos = new Vector3i(outX, outY, outZ);

            SelectedOutputMode = (OutputTransportMode)br.ReadInt32();

            string pipeGraphId = br.ReadString();
            if (!Guid.TryParse(pipeGraphId, out SelectedPipeGraphId))
                SelectedPipeGraphId = Guid.Empty;

            int outputTargetCount = br.ReadInt32();
            availableOutputTargets = new List<OutputTargetInfo>(outputTargetCount);

            for (int i = 0; i < outputTargetCount; i++)
            {
                int x = br.ReadInt32();
                int y = br.ReadInt32();
                int z = br.ReadInt32();

                OutputTransportMode modeValue = (OutputTransportMode)br.ReadInt32();

                string syncedPipeGraphId = br.ReadString();
                Guid parsedGraphId;
                if (!Guid.TryParse(syncedPipeGraphId, out parsedGraphId))
                    parsedGraphId = Guid.Empty;

                availableOutputTargets.Add(
                    new OutputTargetInfo(new Vector3i(x, y, z), modeValue, parsedGraphId)
                );
            }

            int count = br.ReadInt32();
            timers.Clear();
            for (int i = 0; i < count; i++)
            {
                timers.Add(new ResourceTimer
                {
                    Resource = br.ReadString(),
                    MinCount = br.ReadInt32(),
                    MaxCount = br.ReadInt32(),
                    Speed = br.ReadInt32(),
                    Counter = br.ReadInt32()
                });
            }

            NeedsUiRefresh = true;
            return;
        }

        if (mode != StreamModeRead.Persistency)
        {
            return;
        }

        int version = br.ReadInt32();

        if (version >= 1)
        {
            isExtractorOn = br.ReadBoolean();

            if (version >= 2)
            {
                isEnabledByPlayer = br.ReadBoolean();

                int outX = br.ReadInt32();
                int outY = br.ReadInt32();
                int outZ = br.ReadInt32();
                SelectedOutputChestPos = new Vector3i(outX, outY, outZ);

                SelectedOutputMode = (OutputTransportMode)br.ReadInt32();

                string pipeGraphId = br.ReadString();
                if (!Guid.TryParse(pipeGraphId, out SelectedPipeGraphId))
                    SelectedPipeGraphId = Guid.Empty;
            }
            else
            {
                isEnabledByPlayer = false;
                SelectedOutputChestPos = Vector3i.zero;
                SelectedOutputMode = OutputTransportMode.Adjacent;
                SelectedPipeGraphId = Guid.Empty;
            }

            int count = br.ReadInt32();

            timers.Clear();
            for (int i = 0; i < count; i++)
            {
                timers.Add(new ResourceTimer
                {
                    Resource = br.ReadString(),
                    MinCount = br.ReadInt32(),
                    MaxCount = br.ReadInt32(),
                    Speed = br.ReadInt32(),
                    Counter = br.ReadInt32()
                });
            }
        }
    }
}


