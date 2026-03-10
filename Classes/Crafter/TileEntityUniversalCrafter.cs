using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TileEntityUniversalCrafter : TileEntityMachine
{
    public bool IsDevLogging => blockValue.Block.Properties.GetBool("DevLogs");

    public string SelectedRecipeName = "";
    public Recipe _recipe;

    // Storage scanning
    public List<TileEntityComposite> nearbyContainers = new List<TileEntityComposite>();
    public Dictionary<string, RecipeScanResult> LastScanResults = new Dictionary<string, RecipeScanResult>();
    private Dictionary<string, int> _pendingUsedIngredients;
    public bool reqHasNearbyStorage = false;

    public Vector3i SelectedInputChestPos = Vector3i.zero;
    public List<TileEntityComposite> selectedInputContainers = new List<TileEntityComposite>();
    public XUiC_InputContainerList xUiC_InputContainerList;
    public TileEntityComposite selectedInputContainer;

    public List<OutputTargetInfo> availableOutputTargets = new List<OutputTargetInfo>();

    public TileEntityComposite selectedOutputContainer;
    public Vector3i SelectedOutputChestPos = Vector3i.zero;
    public OutputTransportMode SelectedOutputMode = OutputTransportMode.Adjacent;
    public Guid SelectedPipeGraphId = Guid.Empty;
    public XUiC_OutputContainerList xUiC_OutputContainerList;

    public ulong LastPipeDispatchWorldTime = 0UL;
    private int pendingOutputRoundRobinIndex = 0;

    public ulong craftStartTime;
    public bool isCrafting = false;
    public bool disabledByPlayer = true;
    public bool isWaitingForIngredients = false;

    public float BaseRecipeDuration = 10f;
    public float CraftSpeed;
    public string ActiveCraftRecipeName = "";

    public string PendingSelectedRecipeName = "";
    public float PendingSelectedRecipeDuration = 0f;

    public List<InputTargetInfo> availableInputTargets = new List<InputTargetInfo>();
    public Guid SelectedInputPipeGraphId = Guid.Empty;

    public Dictionary<string, int> inputBuffer = new Dictionary<string, int>();

    public struct RecipeScanResult
    {
        public Recipe recipe;
        public bool hasAllIngredients;
        public Dictionary<ItemClass, int> missingCounts;
    }

    private enum DevLogLevel
    {
        Info,
        Warning,
        Error
    }

    public TileEntityUniversalCrafter(Chunk chunk) : base(chunk) { }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.UniversalCrafter);
    }

    private void DevLog(string msg, DevLogLevel level = DevLogLevel.Info)
    {
        if (!IsDevLogging)
            return;

        string prefix = $"[Crafter][TE][{ToWorldPos()}] ";

        switch (level)
        {
            case DevLogLevel.Warning:
                Log.Warning(msg, prefix);
                break;

            case DevLogLevel.Error:
                Log.Error(msg, prefix);
                break;

            default:
                Log.Out(msg, prefix);
                break;
        }
    }

    public int GetBufferedItemCount(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return 0;

        return inputBuffer != null && inputBuffer.TryGetValue(itemName, out int count) ? count : 0;
    }

    public override int ReceiveBufferedInput(string itemName, int count)
    {
        if (string.IsNullOrEmpty(itemName) || count <= 0)
            return 0;

        if (inputBuffer == null)
            inputBuffer = new Dictionary<string, int>();

        if (inputBuffer.TryGetValue(itemName, out int existing))
            inputBuffer[itemName] = existing + count;
        else
            inputBuffer[itemName] = count;

        DevLog($"RECEIVE BUFFERED INPUT — accepted {count}x {itemName} (total={inputBuffer[itemName]})");
        setModified();
        return count;
    }

    public override IHLRSnapshot BuildHLRSnapshot(WorldBase world)
    {
        DevLog("========================================");
        DevLog("HLR SNAPSHOT BUILD — BEGIN");
        DevLog("========================================");

        DevLog($"STATE — MachineGuid={MachineGuid}");
        DevLog($"STATE — Position={ToWorldPos()}");
        DevLog($"STATE — SelectedRecipeName='{SelectedRecipeName}'");
        DevLog($"STATE — isCrafting={isCrafting}");
        DevLog($"STATE — disabledByPlayer={disabledByPlayer}");
        DevLog($"STATE — craftStartTime={craftStartTime}");
        DevLog($"STATE — BaseRecipeDuration={BaseRecipeDuration}");
        DevLog($"STATE — CraftSpeed={CraftSpeed}");

        if (string.IsNullOrEmpty(SelectedRecipeName))
        {
            DevLog("ABORT — No recipe selected", DevLogLevel.Warning);
            DevLog("========================================");
            return null;
        }

        Recipe recipe = CraftingManager.GetRecipe(SelectedRecipeName);
        if (recipe == null)
        {
            DevLog($"ABORT — CraftingManager returned NULL for recipe '{SelectedRecipeName}'", DevLogLevel.Error);
            DevLog("========================================");
            return null;
        }

        _recipe = recipe;
        DevLog($"RECIPE — Resolved '{recipe.GetName()}'");

        var snapshot = new CrafterSnapshot
        {
            MachineId = this.MachineGuid,
            Position = ToWorldPos(),
            RecipeName = SelectedRecipeName,
            IsCrafting = isCrafting,
            DisabledByPlayer = disabledByPlayer,
            CraftStartTime = craftStartTime,
            BaseRecipeDuration = BaseRecipeDuration,
            CraftSpeed = this.CraftSpeed,
            LastHLRSimTime = world.GetWorldTime(),
            IngredientCount = new Dictionary<string, int>(),
            OwedResources = new Dictionary<string, int>(),
            UsedIngredients = new Dictionary<string, int>()
        };

        DevLog("SNAPSHOT — Base fields populated");

        TileEntityComposite input = selectedInputContainer;

        if (input == null)
        {
            DevLog("INPUT — selectedInputContainer is NULL", DevLogLevel.Warning);
        }
        else
        {
            DevLog($"INPUT — Container at {input.ToWorldPos()}");

            TEFeatureStorage storage = input.GetFeature<TEFeatureStorage>();
            if (storage == null)
            {
                DevLog("INPUT — TEFeatureStorage is NULL", DevLogLevel.Warning);
            }
            else if (storage.items == null)
            {
                DevLog("INPUT — storage.items is NULL", DevLogLevel.Warning);
            }
            else
            {
                DevLog($"INPUT — storage slot count = {storage.items.Length}");
            }

            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient.count == 0)
                {
                    DevLog($"INGREDIENT SKIP — '{ingredient.itemValue.ItemClass.GetItemName()}' requires 0");
                    continue;
                }

                string itemName = ingredient.itemValue.ItemClass.GetItemName();
                int total = 0;

                if (storage?.items != null)
                {
                    foreach (var stack in storage.items)
                    {
                        if (!stack.IsEmpty() &&
                            stack.itemValue.ItemClass.GetItemName() == itemName)
                        {
                            total += stack.count;
                        }
                    }
                }

                snapshot.IngredientCount[itemName] = total;

                DevLog(
                    $"INGREDIENT — '{itemName}' " +
                    $"requiredPerCraft={ingredient.count} " +
                    $"availableInInput={total}"
                );
            }
        }

        DevLog("SNAPSHOT — IngredientCount populated");

        DevLog("========================================");
        DevLog("HLR SNAPSHOT BUILD — COMPLETE");
        DevLog("========================================");

        return snapshot;
    }

    public override void SetSimulatedByHLR(bool value)
    {
        DevLog($"SetSimulatedByHLR({value}) (was {simulatedByHLR})");
        simulatedByHLR = value;
    }

    public override void ApplyHLRSnapshot(object snapshotObj)
    {
        DevLog("HLR SNAPSHOT APPLY - BEGIN");

        if (!(snapshotObj is CrafterSnapshot snapshot))
        {
            DevLog("HLR SNAPSHOT APPLY - FAILED (invalid snapshot type)", DevLogLevel.Error);
            return;
        }

        DevLog(
            $"Snapshot Recipe={snapshot.RecipeName} " +
            $"IsCrafting={snapshot.IsCrafting} " +
            $"CraftStartTime={snapshot.CraftStartTime} " +
            $"BaseDuration={snapshot.BaseRecipeDuration} " +
            $"CraftSpeed={snapshot.CraftSpeed} " +
            $"Owed={snapshot.OwedResources?.Count ?? 0}"
        );

        SelectedRecipeName = snapshot.RecipeName;
        _recipe = CraftingManager.GetRecipe(snapshot.RecipeName);

        isCrafting = snapshot.IsCrafting;
        craftStartTime = snapshot.CraftStartTime;

        BaseRecipeDuration = snapshot.BaseRecipeDuration;
        CraftSpeed = snapshot.CraftSpeed;

        _pendingUsedIngredients = snapshot.UsedIngredients;

        if (snapshot.OwedResources != null && snapshot.OwedResources.Count > 0)
        {
            foreach (var kvp in snapshot.OwedResources)
            {
                AddPendingOutput(kvp.Key, kvp.Value);
                DevLog($"Applied OwedResources - {kvp.Value}x {kvp.Key}");
            }
        }
        else
        {
            DevLog("No owed resources to apply");
        }

        simulatedByHLR = false;
        DevLog("HLR SNAPSHOT APPLY - END");
    }

    public void ApplyUsedIngredients(Dictionary<string, int> usedIngredients)
    {
        DevLog("========================================");
        DevLog("APPLY USED INGREDIENTS — ENTER");
        DevLog("========================================");

        if (usedIngredients == null)
        {
            DevLog("ABORT — usedIngredients dictionary is NULL", DevLogLevel.Warning);
            return;
        }

        DevLog($"STATE — usedIngredients.Count = {usedIngredients.Count}");

        if (usedIngredients.Count == 0)
        {
            DevLog("ABORT — no ingredients to apply");
            return;
        }

        if (selectedInputContainer == null)
        {
            DevLog("ABORT — selectedInputContainer is NULL", DevLogLevel.Warning);
            return;
        }

        Vector3i inputPos = selectedInputContainer.ToWorldPos();
        DevLog($"INPUT CONTAINER — position = {inputPos}");

        var storage = selectedInputContainer.GetFeature<TEFeatureStorage>();
        if (storage == null)
        {
            DevLog("ABORT — input container has NO TEFeatureStorage", DevLogLevel.Warning);
            return;
        }

        if (storage.items == null)
        {
            DevLog("ABORT — storage.items is NULL", DevLogLevel.Warning);
            return;
        }

        DevLog($"STORAGE — slot count = {storage.items.Length}");
        DevLog("----------------------------------------");

        foreach (var kvp in usedIngredients)
        {
            string itemName = kvp.Key;
            int totalToRemove = kvp.Value;

            DevLog($"INGREDIENT BEGIN — '{itemName}'");
            DevLog($"TARGET REMOVE — {totalToRemove}");

            if (totalToRemove <= 0)
            {
                DevLog("SKIP — remove amount <= 0");
                DevLog("----------------------------------------");
                continue;
            }

            int beforeTotal = 0;
            foreach (var s in storage.items)
            {
                if (!s.IsEmpty() &&
                    s.itemValue.ItemClass.GetItemName() == itemName)
                {
                    beforeTotal += s.count;
                }
            }

            DevLog($"CHEST TOTAL BEFORE — {beforeTotal}");

            int remainingToRemove = totalToRemove;

            for (int i = 0; i < storage.items.Length && remainingToRemove > 0; i++)
            {
                var stack = storage.items[i];

                if (stack.IsEmpty())
                    continue;

                string stackName = stack.itemValue.ItemClass.GetItemName();
                if (stackName != itemName)
                    continue;

                int stackBefore = stack.count;
                int remove = System.Math.Min(stackBefore, remainingToRemove);

                stack.count -= remove;
                remainingToRemove -= remove;

                DevLog(
                    $"  SLOT {i} — had {stackBefore}, removed {remove}, now {stack.count}, remainingToRemove={remainingToRemove}"
                );

                if (stack.count <= 0)
                {
                    storage.items[i] = ItemStack.Empty;
                    DevLog($"  SLOT {i} — stack emptied");
                }
            }

            int afterTotal = 0;
            foreach (var s in storage.items)
            {
                if (!s.IsEmpty() &&
                    s.itemValue.ItemClass.GetItemName() == itemName)
                {
                    afterTotal += s.count;
                }
            }

            DevLog($"CHEST TOTAL AFTER — {afterTotal}");

            if (remainingToRemove > 0)
            {
                DevLog(
                    $"WARNING — Unable to remove full amount for '{itemName}', missing {remainingToRemove}",
                    DevLogLevel.Warning
                );
            }
            else
            {
                DevLog($"INGREDIENT COMPLETE — '{itemName}' removal successful");
            }

            DevLog("----------------------------------------");
        }

        storage.SetModified();

        DevLog("APPLY USED INGREDIENTS — COMPLETE");
        DevLog("========================================");
    }

    public List<InputTargetInfo> GetAvailableInputTargets(WorldBase world)
    {
        if (world == null)
            return availableInputTargets ?? new List<InputTargetInfo>();

        if (world.IsRemote())
            return availableInputTargets ?? new List<InputTargetInfo>();

        availableInputTargets = DiscoverAvailableInputTargets(world);
        return availableInputTargets;
    }

    public void RefreshAvailableInputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        availableInputTargets = DiscoverAvailableInputTargets(world);
        DevLog($"RefreshAvailableInputTargets — count={availableInputTargets?.Count ?? 0}");
    }

    private List<InputTargetInfo> DiscoverAvailableInputTargets(WorldBase world)
    {
        List<InputTargetInfo> results = new List<InputTargetInfo>();
        HashSet<string> seen = new HashSet<string>();

        Vector3i machinePos = ToWorldPos();
        Vector3i[] sides =
        {
        Vector3i.back,
        Vector3i.right,
        Vector3i.forward,
        Vector3i.left,
        Vector3i.up,
        Vector3i.down
    };

        for (int i = 0; i < sides.Length; i++)
        {
            Vector3i pipePos = machinePos + sides[i];

            TileEntityItemPipe pipeTe = world.GetTileEntity(0, pipePos) as TileEntityItemPipe;
            if (pipeTe == null || pipeTe.PipeGraphId == Guid.Empty)
                continue;

            if (!PipeGraphManager.TryGetStorageEndpoints(pipeTe.PipeGraphId, out List<Vector3i> storageEndpoints) ||
                storageEndpoints == null ||
                storageEndpoints.Count == 0)
                continue;

            for (int j = 0; j < storageEndpoints.Count; j++)
            {
                Vector3i storagePos = storageEndpoints[j];
                string key = $"{storagePos}|{pipeTe.PipeGraphId}";
                if (!seen.Add(key))
                    continue;

                TileEntityComposite comp = world.GetTileEntity(0, storagePos) as TileEntityComposite;
                if (comp == null)
                    continue;

                results.Add(new InputTargetInfo(storagePos, pipeTe.PipeGraphId));
            }
        }

        return results;
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
            storage.SetModified();

        if (remaining.count <= 0)
        {
            DevLog("TryAddToSelectedAdjacentStorage SUCCESS — fully deposited");
            return true;
        }

        DevLog($"TryAddToSelectedAdjacentStorage BLOCKED — selected chest full, {remaining.count} remaining");
        return false;
    }

    private void FlushPendingOutput(WorldBase world)
    {
        DevLog("========== FLUSH BEGIN ==========");

        if (pendingOutput.Count == 0)
        {
            DevLog("FLUSH ABORT — No pending output");
            DevLog("========== FLUSH END ==========");
            return;
        }

        ulong now = world.GetWorldTime();

        if (SelectedOutputMode == OutputTransportMode.Pipe &&
            !PipeTransportManager.CanDispatch(LastPipeDispatchWorldTime, now))
        {
            DevLog("FLUSH WAIT — pipe dispatch interval not ready yet");
            DevLog("========== FLUSH END ==========");
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
                DevLog("========== FLUSH END ==========");
                return;
            }
        }

        if (SelectedOutputMode == OutputTransportMode.Pipe)
        {
            if (SelectedOutputChestPos == Vector3i.zero || SelectedPipeGraphId == Guid.Empty)
            {
                DevLog("FLUSH BLOCKED — pipe output selected but target/graph is invalid");
                DevLog("========== FLUSH END ==========");
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
                            pendingOutput.Remove(itemName);
                        else
                            pendingOutput[itemName] = count - acceptedAmount;

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

            DevLog("========== FLUSH END ==========");
            return;
        }

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

        DevLog("========== FLUSH END ==========");
    }

    public override void UpdateTick(World world)
    {
        base.UpdateTick(world);

        if (simulatedByHLR)
            return;

        bool resolvedOutput = ResolveSelectedOutputIfNeeded(world);
        if (resolvedOutput)
            DevLog("UpdateTick — selected output runtime state refreshed");

        RefreshAvailableInputTargets(world);
        RefreshAvailableOutputTargets(world);

        if (pendingOutput.Count > 0)
        {
            DevLog($"UPDATE — PendingOutput detected ({pendingOutput.Count}), attempting flush");
            FlushPendingOutput(world);
        }

        if ((pendingOutput == null || pendingOutput.Count == 0) && !string.IsNullOrEmpty(PendingSelectedRecipeName))
        {
            Recipe pendingRecipe = CraftingManager.GetRecipe(PendingSelectedRecipeName);
            if (pendingRecipe == null)
            {
                DevLog($"APPLY QUEUED RECIPE FAILED — '{PendingSelectedRecipeName}' not found", DevLogLevel.Warning);
                PendingSelectedRecipeName = "";
                PendingSelectedRecipeDuration = 0f;
            }
            else
            {
                SelectedRecipeName = PendingSelectedRecipeName;
                _recipe = pendingRecipe;
                BaseRecipeDuration = PendingSelectedRecipeDuration > 0f ? PendingSelectedRecipeDuration : 10f;

                DevLog($"APPLY QUEUED RECIPE — '{PendingSelectedRecipeName}'");

                PendingSelectedRecipeName = "";
                PendingSelectedRecipeDuration = 0f;

                MarkCrafterDirty();
            }
        }

        ResolveRecipeIfNeeded();

        if (disabledByPlayer)
        {
            if (isCrafting || isWaitingForIngredients)
            {
                DevLog("UPDATE — disabled, clearing runtime craft state");
                isCrafting = false;
                isWaitingForIngredients = false;
                setModified();
            }

            UpdateBlockState();
            return;
        }

        TryRequestInputs(world);

        if (isCrafting)
        {
            Recipe activeRecipe = GetActiveRecipe();
            if (activeRecipe == null)
            {
                DevLog("CRAFT STOP — Active recipe is null", DevLogLevel.Warning);
                ResetCraftRuntimeState(false);
                disabledByPlayer = true;
                setModified();
                UpdateBlockState();
                return;
            }

            if (!HasBufferedIngredientsForNextCraft())
            {
                DevLog("CRAFT PAUSED — Buffered ingredients missing", DevLogLevel.Warning);
                isCrafting = false;
                isWaitingForIngredients = true;
                setModified();
                UpdateBlockState();
                return;
            }

            float speed = GetCraftingSpeed();

            ulong ticksPassed = world.worldTime - craftStartTime;
            float secondsPassed = ticksPassed / 20f;
            float actualDuration = BaseRecipeDuration / speed;

            int craftsToRun = Mathf.FloorToInt(secondsPassed / actualDuration);

            DevLog(
                $"CRAFT TICK — elapsed={secondsPassed:0.000}s " +
                $"duration={actualDuration:0.000}s " +
                $"speed={speed:0.00} " +
                $"cycles={craftsToRun}"
            );

            if (craftsToRun > 0)
            {
                int completed = 0;

                for (int i = 0; i < craftsToRun; i++)
                {
                    if (!ValidateCraftStart(activeRecipe))
                    {
                        DevLog($"CRAFT STOP — Validation failed at cycle {i}", DevLogLevel.Warning);
                        break;
                    }

                    if (!ConsumeIngredients(activeRecipe))
                    {
                        DevLog($"CRAFT STOP — Ingredient consumption failed at cycle {i}", DevLogLevel.Warning);
                        break;
                    }

                    AddPendingOutput(
                        activeRecipe.GetOutputItemClass().GetItemName(),
                        activeRecipe.count
                    );

                    completed++;
                    DevLog($"CRAFT OK — Cycle {i + 1}/{craftsToRun}");
                }

                craftStartTime += (ulong)(completed * actualDuration * 20f);
                DevLog($"CRAFT ADVANCE — completed={completed} newStartTime={craftStartTime}");
            }
        }

        if (!isCrafting)
        {
            if (HasBufferedIngredientsForNextCraft())
            {
                if (_recipe != null && HasValidSelectedOutput(world))
                {
                    StartCraft();
                    isWaitingForIngredients = false;
                    DevLog("UPDATE — craft started from buffered ingredients");
                }
            }
            else
            {
                isWaitingForIngredients = true;

                if (IsInputBufferEmpty() && !HasValidUpstreamInputSource(world))
                {
                    DevLog("UPDATE — no buffered ingredients and no valid upstream input source, disabling");
                    disabledByPlayer = true;
                    isWaitingForIngredients = false;
                    setModified();
                }
            }
        }

        if (selectedInputContainer == null && SelectedInputChestPos != Vector3i.zero)
        {
            if (IsDevLogging)
                Log.Out($"[Crafter][UPDATE] Retrying container resolve for {SelectedInputChestPos}");

            ResolveSelectedInputContainer();
        }

        if (selectedOutputContainer == null && SelectedOutputChestPos != Vector3i.zero)
        {
            if (IsDevLogging)
                Log.Out($"[Crafter][UPDATE] Retrying output container resolve for {SelectedOutputChestPos}");

            ResolveSelectedOutputContainer();
        }

        UpdateBlockState();
    }

    public HashSet<string> GetNeededInputItemNames()
    {
        HashSet<string> needed = new HashSet<string>();

        ResolveRecipeIfNeeded();

        if (_recipe == null)
        {
            DevLog("GET NEEDED INPUT ITEM NAMES — ABORT (_recipe is null)", DevLogLevel.Warning);
            return needed;
        }

        if (_recipe.ingredients == null || _recipe.ingredients.Count == 0)
        {
            DevLog("GET NEEDED INPUT ITEM NAMES — ABORT (recipe has no ingredients)", DevLogLevel.Warning);
            return needed;
        }

        foreach (ItemStack ingredient in _recipe.ingredients)
        {
            if (ingredient.itemValue == null || ingredient.itemValue.ItemClass == null)
                continue;

            int required = ingredient.count;
            if (required <= 0)
                continue;

            string itemName = ingredient.itemValue.ItemClass.GetItemName();
            if (string.IsNullOrEmpty(itemName))
                continue;

            int buffered = GetBufferedItemCount(itemName);
            if (buffered < required)
                needed.Add(itemName);
        }

        DevLog($"GET NEEDED INPUT ITEM NAMES — count={needed.Count}");
        foreach (string itemName in needed)
            DevLog($"GET NEEDED INPUT ITEM NAMES — needs '{itemName}'");

        return needed;
    }

    public bool CanRequestInputs(World world)
    {
        if (world == null)
        {
            DevLog("CAN REQUEST INPUTS — FALSE (world is null)", DevLogLevel.Warning);
            return false;
        }

        if (disabledByPlayer)
        {
            DevLog("CAN REQUEST INPUTS — FALSE (disabled by player)");
            return false;
        }

        ResolveRecipeIfNeeded();

        if (_recipe == null)
        {
            DevLog("CAN REQUEST INPUTS — FALSE (_recipe is null)", DevLogLevel.Warning);
            return false;
        }

        if (_recipe.ingredients == null || _recipe.ingredients.Count == 0)
        {
            DevLog("CAN REQUEST INPUTS — FALSE (recipe has no ingredients)", DevLogLevel.Warning);
            return false;
        }

        if (HasBufferedIngredientsForNextCraft())
        {
            DevLog("CAN REQUEST INPUTS — FALSE (buffer already satisfies next craft)");
            return false;
        }

        if (!HasValidUpstreamInputSource(world))
        {
            DevLog("CAN REQUEST INPUTS — FALSE (no valid upstream input source)");
            return false;
        }

        DevLog("CAN REQUEST INPUTS — TRUE");
        return true;
    }

    private bool IsInputBufferEmpty()
    {
        return inputBuffer == null || inputBuffer.Count == 0;
    }

    private void TryRequestInputs(World world)
    {
        if (!CanRequestInputs(world))
            return;

        if (SelectedInputChestPos == Vector3i.zero)
        {
            DevLog("TRY REQUEST INPUTS — ABORT (no selected input chest)", DevLogLevel.Warning);
            return;
        }

        if (SelectedInputPipeGraphId == Guid.Empty &&
            !TryResolveInputPipeGraphFromSelectedChest(world))
        {
            DevLog("TRY REQUEST INPUTS — ABORT (no selected input pipe graph)", DevLogLevel.Warning);
            return;
        }

        HashSet<string> neededItemNames = GetNeededInputItemNames();
        if (neededItemNames == null || neededItemNames.Count == 0)
        {
            DevLog("TRY REQUEST INPUTS — ABORT (no needed item names)");
            return;
        }

        bool requested = PipeTransportManager.TryRequestCrafterInputs(
            world,
            0,
            SelectedInputPipeGraphId,
            SelectedInputChestPos,
            ToWorldPos(),
            neededItemNames
        );

        DevLog($"TRY REQUEST INPUTS — requested={requested} neededTypes={neededItemNames.Count}");
    }

    private void UpdateBlockState()
    {
        // TODO: Disabled / Idle / Active visual state handling.
    }

    private bool TryResolveInputPipeGraphFromSelectedChest(World world)
    {
        if (world == null || SelectedInputChestPos == Vector3i.zero)
            return false;

        if (SelectedInputPipeGraphId != Guid.Empty)
            return true;

        RefreshAvailableInputTargets(world);

        if (availableInputTargets == null || availableInputTargets.Count == 0)
            return false;

        Guid resolved = Guid.Empty;
        bool hasConflict = false;

        for (int i = 0; i < availableInputTargets.Count; i++)
        {
            InputTargetInfo target = availableInputTargets[i];
            if (target == null || target.BlockPos != SelectedInputChestPos)
                continue;

            if (resolved == Guid.Empty)
            {
                resolved = target.PipeGraphId;
                continue;
            }

            if (resolved != target.PipeGraphId)
            {
                hasConflict = true;
                break;
            }
        }

        if (hasConflict || resolved == Guid.Empty)
            return false;

        SelectedInputPipeGraphId = resolved;
        MarkCrafterDirty();
        DevLog($"Resolved SelectedInputPipeGraphId from selected chest: {SelectedInputPipeGraphId}");
        return true;
    }

    private bool HasValidUpstreamInputSource(World world)
    {
        if (world == null)
            return false;

        if (SelectedInputChestPos == Vector3i.zero)
            return false;

        if (SelectedInputPipeGraphId == Guid.Empty &&
            !TryResolveInputPipeGraphFromSelectedChest(world))
            return false;

        if (selectedInputContainer != null)
            return true;

        if (!PipeGraphManager.TryGetGraph(SelectedInputPipeGraphId, out PipeGraphData graph) || graph == null)
            return false;

        if (graph.StorageEndpoints == null || graph.StorageEndpoints.Count == 0)
            return false;

        return graph.StorageEndpoints.Contains(SelectedInputChestPos);
    }

    public float GetCraftingSpeed()
    {
        var block = blockValue.Block;

        if (block?.Properties?.Values.TryGetValue("CraftingSpeed", out var craftingSpeed) == true)
            return float.TryParse(craftingSpeed, out var speed) ? speed : 1f;

        return 1f;
    }

    public void StartCraft()
    {
        var world = GameManager.Instance.World;
        if (world == null)
            return;

        ResolveRecipeIfNeeded();

        if (_recipe == null || string.IsNullOrEmpty(SelectedRecipeName))
        {
            DevLog("StartCraft aborted - no selected recipe", DevLogLevel.Warning);
            return;
        }

        ActiveCraftRecipeName = SelectedRecipeName;

        isCrafting = true;
        disabledByPlayer = false;
        isWaitingForIngredients = false;
        craftStartTime = world.worldTime;

        DevLog($"START CRAFT — SelectedRecipe='{SelectedRecipeName}' ActiveRecipe='{ActiveCraftRecipeName}'");

        base.setModified();
    }

    public List<TileEntityComposite> FindNearbyContainers(World world, int range = 7)
    {
        nearbyContainers.Clear();

        if (world == null)
        {
            Log.Error("[Crafter][TE] FindNearbyContainers called but world is NULL");
            return nearbyContainers;
        }

        Vector3i center = this.ToWorldPos();
        Log.Out($"[Crafter][TE] FindNearbyContainers: center={center} range={range}");

        int checks = 0;
        int found = 0;

        for (int x = -range; x <= range; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -range; z <= range; z++)
                {
                    Vector3i pos = new Vector3i(center.x + x, center.y + y, center.z + z);
                    checks++;

                    TileEntity te = world.GetTileEntity(pos);
                    if (te is TileEntityComposite comp)
                    {
                        nearbyContainers.Add(comp);
                        found++;
                        Log.Out($"[Crafter][TE]   Found composite at {pos} block={comp.blockValue.Block.GetBlockName()}");
                    }
                }
            }
        }

        Log.Out($"[Crafter][TE] Scan complete. Checked={checks} Found={found} Stored={nearbyContainers.Count}");
        return nearbyContainers;
    }

    public List<TileEntityComposite> GetClosestContainers()
    {
        if (nearbyContainers == null || nearbyContainers.Count == 0)
            return new List<TileEntityComposite>();

        Vector3i crafterPos = this.ToWorldPos();
        var sorted = nearbyContainers.OrderBy(te => DistanceSq(te.ToWorldPos(), crafterPos)).Take(10).ToList();
        return sorted;
    }

    private TileEntityComposite GetSelectedInputContainerTE()
    {
        if (SelectedInputChestPos == Vector3i.zero)
            return null;

        var world = GameManager.Instance.World;
        if (world == null)
            return null;

        TileEntity te = world.GetTileEntity(SelectedInputChestPos);
        return te as TileEntityComposite;
    }

    public void RefreshSelectedInputContainers()
    {
        selectedInputContainers = GetClosestContainers();
    }

    private static int DistanceSq(Vector3i a, Vector3i b)
    {
        int dx = a.x - b.x;
        int dy = a.y - b.y;
        int dz = a.z - b.z;
        return dx * dx + dy * dy + dz * dz;
    }

    public void LogNearbyContainers()
    {
        Log.Warning("[Crafter] LogNearbyContainers called");

        var world = GameManager.Instance.World;

        if (world == null)
        {
            if (IsDevLogging)
                Log.Warning("[Crafter] LogNearbyContainers called but world is NULL");

            reqHasNearbyStorage = false;
            return;
        }

        var nearby = FindNearbyContainers(world);
        nearbyContainers = nearby ?? new List<TileEntityComposite>();

        nearbyContainers.RemoveAll(c => c == null);

        if (nearbyContainers.Count == 0)
        {
            reqHasNearbyStorage = false;
            selectedOutputContainer = null;
            selectedInputContainer = null;
            SelectedInputChestPos = Vector3i.zero;
            SelectedOutputChestPos = Vector3i.zero;
            return;
        }

        nearbyContainers = GetClosestContainers();
        reqHasNearbyStorage = true;

        if (IsDevLogging)
        {
            Log.Out($"[Crafter] Found {nearbyContainers.Count} containers near {ToWorldPos()}");
            foreach (var comp in nearbyContainers)
            {
                if (comp == null) continue;
                Vector3i pos = comp.ToWorldPos();
                Log.Out($"[Crafter] Container at {pos.x}, {pos.y}, {pos.z}");
            }
        }
    }

    public void ListItemsInContainer(World world, Vector3i checkPos)
    {
        var te = world.GetTileEntity(0, checkPos);

        if (te is TileEntityComposite composite && !(te is TileEntityUniversalCrafter))
        {
            var storage = composite.GetFeature<TEFeatureStorage>();

            if (storage != null && storage.items != null && storage.items.Length > 0)
            {
                Log.Out($"[Crafter] Found items in storage at {checkPos}:");
                foreach (var item in storage.items)
                {
                    if (!item.IsEmpty())
                    {
                        string localizedItemName = item.itemValue.ItemClass.GetLocalizedItemName();
                        Log.Out($"[Crafter] - {localizedItemName} (Quantity: {item.count})");
                    }
                }
            }
            else
            {
                Log.Out($"[Crafter] No items found in storage at {checkPos}");
            }
        }
        else
        {
            Log.Out($"[Crafter] Invalid or unsupported TileEntity at {checkPos}");
        }
    }

    public void ResolveSelectedInputContainer()
    {
        if (SelectedInputChestPos == Vector3i.zero)
        {
            Log.Warning($"[Crafter] Selected input container position is 0,0,0");
            selectedInputContainer = null;
            return;
        }

        var world = GameManager.Instance.World;
        if (world == null)
        {
            selectedInputContainer = null;
            return;
        }

        var te = world.GetTileEntity(SelectedInputChestPos) as TileEntityComposite;
        selectedInputContainer = te;

        if (IsDevLogging)
        {
            if (selectedInputContainer != null)
                Log.Out($"[Crafter] Resolved input container at {SelectedInputChestPos}");
            else
                Log.Warning($"[Crafter] Could not resolve input container at {SelectedInputChestPos}");
        }
    }

    public void ResolveSelectedOutputContainer()
    {
        if (SelectedOutputChestPos == Vector3i.zero)
        {
            selectedOutputContainer = null;
            return;
        }

        var world = GameManager.Instance.World;
        if (world == null)
        {
            selectedOutputContainer = null;
            return;
        }

        var teOut = world.GetTileEntity(SelectedOutputChestPos) as TileEntityComposite;
        selectedOutputContainer = teOut;

        if (IsDevLogging)
        {
            if (selectedOutputContainer != null)
                Log.Out($"[Crafter] Resolved output container at {SelectedOutputChestPos}");
            else
                Log.Warning($"[Crafter] Could not resolve output container at {SelectedOutputChestPos}");
        }
    }

    public bool ValidateCraftStart(Recipe recipe)
    {
        if (recipe == null)
        {
            isCrafting = false;
            return false;
        }

        if (!HasValidSelectedOutput(GameManager.Instance.World))
        {
            isCrafting = false;
            return false;
        }

        if (!HasBufferedIngredientsForNextCraft())
        {
            if (isCrafting)
                isWaitingForIngredients = true;

            isCrafting = false;
            return false;
        }

        return true;
    }

    public bool ConsumeIngredients(Recipe recipe)
    {
        if (recipe == null)
        {
            Log.Warning("[Crafter][Craft] FAIL: ConsumeIngredients called with null recipe.");
            return false;
        }

        if (!ConsumeBufferedIngredientsForNextCraft())
        {
            Log.Warning("[Crafter][Craft] FAIL: Buffered ingredient consumption failed.");
            return false;
        }

        Log.Out("[Crafter][Craft] Buffered ingredients consumed successfully.");
        return true;
    }

    public bool TryDepositOutput(ItemClass outputClass, int count)
    {
        if (outputClass == null || count <= 0)
            return false;

        if (selectedOutputContainer == null)
        {
            Log.Warning("[Crafter][Craft] FAIL: No output container selected.");
            return false;
        }

        if (IsLocked(selectedOutputContainer))
        {
            DevLog("Output Container is server locked", DevLogLevel.Warning);
            return false;
        }

        var storage = selectedOutputContainer.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
        {
            Log.Warning("[Crafter][Craft] FAIL: Output container has no storage feature.");
            return false;
        }

        ItemValue outputValue = new ItemValue(outputClass.Id, 1, 1, false);
        ItemStack remaining = new ItemStack(outputValue, count);

        for (int i = 0; i < storage.items.Length && remaining.count > 0; i++)
        {
            var slot = storage.items[i];
            if (slot.IsEmpty())
                continue;

            if (slot.itemValue.type != outputValue.type)
                continue;

            int maxStack = slot.itemValue.ItemClass.Stacknumber.Value;
            int space = maxStack - slot.count;
            if (space <= 0)
                continue;

            int toMove = System.Math.Min(space, remaining.count);
            slot.count += toMove;
            remaining.count -= toMove;

            storage.items[i] = slot;
        }

        if (remaining.count > 0)
        {
            for (int i = 0; i < storage.items.Length; i++)
            {
                if (storage.items[i].IsEmpty())
                {
                    storage.items[i] = remaining;
                    storage.SetModified();
                    return true;
                }
            }
        }
        else
        {
            storage.SetModified();
            return true;
        }

        return false;
    }

    public bool TryDepositOutput(Recipe recipe)
    {
        if (recipe == null)
            return false;

        return TryDepositOutput(recipe.GetOutputItemClass(), recipe.count);
    }

    public void ResolveRecipeIfNeeded()
    {
        if (string.IsNullOrEmpty(SelectedRecipeName))
        {
            _recipe = null;
            DevLog("RESOLVE RECIPE — ABORT (SelectedRecipeName is empty)", DevLogLevel.Warning);
            return;
        }

        if (_recipe != null && _recipe.GetName() == SelectedRecipeName)
            return;

        Recipe resolved = CraftingManager.GetRecipe(SelectedRecipeName);

        if (resolved == null)
        {
            _recipe = null;
            DevLog($"RESOLVE RECIPE — FAILED (CraftingManager returned NULL for '{SelectedRecipeName}')", DevLogLevel.Warning);
            return;
        }

        _recipe = resolved;
        DevLog($"RESOLVE RECIPE — SUCCESS '{_recipe.GetName()}'");
    }

    public Dictionary<string, int> GetMissingIngredientsForNextCraft()
    {
        Dictionary<string, int> missing = new Dictionary<string, int>();

        ResolveRecipeIfNeeded();

        if (_recipe == null)
        {
            DevLog("GET MISSING INGREDIENTS — ABORT (_recipe is null)", DevLogLevel.Warning);
            return missing;
        }

        if (_recipe.ingredients == null || _recipe.ingredients.Count == 0)
        {
            DevLog("GET MISSING INGREDIENTS — ABORT (recipe has no ingredients)", DevLogLevel.Warning);
            return missing;
        }

        foreach (var ingredient in _recipe.ingredients)
        {
            if (ingredient.itemValue == null || ingredient.itemValue.ItemClass == null)
                continue;

            int required = ingredient.count;
            if (required <= 0)
                continue;

            string itemName = ingredient.itemValue.ItemClass.GetItemName();
            if (string.IsNullOrEmpty(itemName))
                continue;

            int buffered = GetBufferedItemCount(itemName);
            int missingCount = required - buffered;

            if (missingCount > 0)
                missing[itemName] = missingCount;
        }

        DevLog($"GET MISSING INGREDIENTS — missingCount={missing.Count}");
        foreach (var kvp in missing)
            DevLog($"GET MISSING INGREDIENTS — {kvp.Key} missing={kvp.Value}");

        return missing;
    }

    public bool HasBufferedIngredientsForNextCraft()
    {
        ResolveRecipeIfNeeded();

        if (_recipe == null)
        {
            DevLog("HAS BUFFERED INGREDIENTS — FALSE (_recipe is null)", DevLogLevel.Warning);
            return false;
        }

        if (_recipe.ingredients == null || _recipe.ingredients.Count == 0)
        {
            DevLog("HAS BUFFERED INGREDIENTS — FALSE (recipe has no ingredients)", DevLogLevel.Warning);
            return false;
        }

        foreach (ItemStack ingredient in _recipe.ingredients)
        {
            if (ingredient.itemValue == null || ingredient.itemValue.ItemClass == null)
                return false;

            int required = ingredient.count;
            if (required <= 0)
                continue;

            string itemName = ingredient.itemValue.ItemClass.GetItemName();
            if (string.IsNullOrEmpty(itemName))
                return false;

            int buffered = GetBufferedItemCount(itemName);
            if (buffered < required)
            {
                DevLog($"HAS BUFFERED INGREDIENTS — FALSE ({itemName} buffered={buffered} required={required})");
                return false;
            }
        }

        DevLog("HAS BUFFERED INGREDIENTS — TRUE");
        return true;
    }

    public bool ConsumeBufferedIngredientsForNextCraft()
    {
        ResolveRecipeIfNeeded();

        if (_recipe == null)
        {
            DevLog("CONSUME BUFFERED INGREDIENTS — FALSE (_recipe is null)", DevLogLevel.Warning);
            return false;
        }

        if (_recipe.ingredients == null || _recipe.ingredients.Count == 0)
        {
            DevLog("CONSUME BUFFERED INGREDIENTS — FALSE (recipe has no ingredients)", DevLogLevel.Warning);
            return false;
        }

        if (!HasBufferedIngredientsForNextCraft())
        {
            DevLog("CONSUME BUFFERED INGREDIENTS — FALSE (insufficient buffered ingredients)");
            return false;
        }

        foreach (ItemStack ingredient in _recipe.ingredients)
        {
            if (ingredient.itemValue == null || ingredient.itemValue.ItemClass == null)
                return false;

            int required = ingredient.count;
            if (required <= 0)
                continue;

            string itemName = ingredient.itemValue.ItemClass.GetItemName();
            if (string.IsNullOrEmpty(itemName))
                return false;

            if (!inputBuffer.TryGetValue(itemName, out int buffered))
            {
                DevLog($"CONSUME BUFFERED INGREDIENTS — FALSE ({itemName} missing from buffer)", DevLogLevel.Warning);
                return false;
            }

            int newCount = buffered - required;
            if (newCount > 0)
            {
                inputBuffer[itemName] = newCount;
                DevLog($"CONSUME BUFFERED INGREDIENTS — {itemName} consumed={required} remaining={newCount}");
            }
            else
            {
                inputBuffer.Remove(itemName);
                DevLog($"CONSUME BUFFERED INGREDIENTS — {itemName} consumed={required} removed from buffer");
            }
        }

        setModified();
        return true;
    }

    public bool IsLocked(TileEntityComposite te)
    {
        return GameManager.Instance.lockedTileEntities.ContainsKey(te);
    }

    private void MarkCrafterDirty()
    {
        base.setModified();
    }

    private void ResetCraftRuntimeState(bool clearPendingOutput = false)
    {
        isCrafting = false;
        isWaitingForIngredients = false;
        craftStartTime = 0;
        ActiveCraftRecipeName = "";

        if (clearPendingOutput)
            pendingOutput.Clear();
    }

    public bool ServerSelectRecipe(string recipeName)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
        {
            DevLog("ServerSelectRecipe called on non-server", DevLogLevel.Warning);
            return false;
        }

        if (string.IsNullOrEmpty(recipeName))
        {
            DevLog("ServerSelectRecipe rejected: recipeName empty", DevLogLevel.Warning);
            return false;
        }

        Recipe recipe = CraftingManager.GetRecipe(recipeName);
        if (recipe == null)
        {
            DevLog($"ServerSelectRecipe rejected: recipe '{recipeName}' not found", DevLogLevel.Warning);
            return false;
        }

        bool changed = !string.Equals(SelectedRecipeName, recipeName);

        if (!changed)
        {
            DevLog($"SERVER SELECT RECIPE — same recipe '{recipeName}', no change");
            return true;
        }

        ResetCraftRuntimeState(false);
        disabledByPlayer = true;

        if (pendingOutput != null && pendingOutput.Count > 0)
        {
            PendingSelectedRecipeName = recipeName;
            PendingSelectedRecipeDuration = recipe.craftingTime > 0f ? recipe.craftingTime : 10f;

            DevLog($"SERVER SELECT RECIPE — queued '{recipeName}' until pending output is flushed");
            MarkCrafterDirty();
            return true;
        }

        SelectedRecipeName = recipeName;
        _recipe = recipe;
        BaseRecipeDuration = recipe.craftingTime > 0f ? recipe.craftingTime : 10f;

        DevLog($"SERVER SELECT RECIPE — applied '{recipeName}' immediately");
        MarkCrafterDirty();
        return true;
    }

    public bool ServerSelectInputContainer(Vector3i chestPos, string pipeGraphId)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
        {
            DevLog("ServerSelectInputContainer called on non-server", DevLogLevel.Warning);
            return false;
        }

        Guid parsedPipeGraphId = Guid.Empty;
        if (!string.IsNullOrEmpty(pipeGraphId))
            Guid.TryParse(pipeGraphId, out parsedPipeGraphId);

        bool changed =
            SelectedInputChestPos != chestPos ||
            SelectedInputPipeGraphId != parsedPipeGraphId;

        if (chestPos == Vector3i.zero)
        {
            SelectedInputChestPos = Vector3i.zero;
            SelectedInputPipeGraphId = Guid.Empty;
            selectedInputContainer = null;

            if (changed)
            {
                ResetCraftRuntimeState(false);
                disabledByPlayer = true;
                DevLog("SERVER SELECT INPUT — cleared (changed, crafter disabled)");
            }
            else
            {
                DevLog("SERVER SELECT INPUT — cleared (no change)");
            }

            MarkCrafterDirty();
            return true;
        }

        var world = GameManager.Instance.World;
        if (world == null)
        {
            DevLog("SERVER SELECT INPUT rejected: world is null", DevLogLevel.Warning);
            return false;
        }

        RefreshAvailableInputTargets(world);
        List<InputTargetInfo> inputs = availableInputTargets;
        if (inputs == null || inputs.Count == 0)
        {
            DevLog("SERVER SELECT INPUT rejected: no available input targets", DevLogLevel.Warning);
            return false;
        }

        InputTargetInfo matchedTarget = null;
        for (int i = 0; i < inputs.Count; i++)
        {
            InputTargetInfo target = inputs[i];
            if (target == null)
                continue;

            if (target.BlockPos == chestPos && target.PipeGraphId == parsedPipeGraphId)
            {
                matchedTarget = target;
                break;
            }
        }

        if (matchedTarget == null)
        {
            DevLog($"SERVER SELECT INPUT rejected: target not found pos={chestPos} pipeGraphId={parsedPipeGraphId}", DevLogLevel.Warning);
            return false;
        }

        TileEntity te = world.GetTileEntity(chestPos);
        if (!(te is TileEntityComposite comp))
        {
            DevLog($"SERVER SELECT INPUT rejected: no composite TE at {chestPos}", DevLogLevel.Warning);
            return false;
        }

        SelectedInputChestPos = chestPos;
        SelectedInputPipeGraphId = parsedPipeGraphId;
        selectedInputContainer = comp;

        if (changed)
        {
            ResetCraftRuntimeState(false);
            disabledByPlayer = true;
            DevLog($"SERVER SELECT INPUT — {chestPos} pipeGraphId={parsedPipeGraphId} (changed, crafter disabled)");
        }
        else
        {
            DevLog($"SERVER SELECT INPUT — {chestPos} pipeGraphId={parsedPipeGraphId} (same selection, crafting state preserved)");
        }

        MarkCrafterDirty();
        return true;
    }

    public bool ServerSelectOutputContainer(Vector3i chestPos, OutputTransportMode mode, string pipeGraphId)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
        {
            DevLog("ServerSelectOutputContainer called on non-server", DevLogLevel.Warning);
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
            {
                ResetCraftRuntimeState(false);
                disabledByPlayer = true;
                DevLog("SERVER SELECT OUTPUT — cleared (changed, crafter disabled)");
            }
            else
            {
                DevLog("SERVER SELECT OUTPUT — cleared (no change)");
            }

            MarkCrafterDirty();
            return true;
        }

        WorldBase world = GameManager.Instance.World;
        if (world == null)
        {
            DevLog("SERVER SELECT OUTPUT rejected: world is null", DevLogLevel.Warning);
            return false;
        }

        RefreshAvailableOutputTargets(world);
        List<OutputTargetInfo> outputs = availableOutputTargets;
        if (outputs == null || outputs.Count == 0)
        {
            DevLog("SERVER SELECT OUTPUT rejected: no available outputs", DevLogLevel.Warning);
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
            DevLog($"SERVER SELECT OUTPUT rejected: target not found pos={chestPos} mode={mode} pipeGraphId={parsedPipeGraphId}", DevLogLevel.Warning);
            return false;
        }

        TileEntity te = world.GetTileEntity(chestPos);
        if (!(te is TileEntityComposite comp))
        {
            DevLog($"SERVER SELECT OUTPUT rejected: no composite TE at {chestPos}", DevLogLevel.Warning);
            return false;
        }

        SelectedOutputChestPos = chestPos;
        SelectedOutputMode = mode;
        SelectedPipeGraphId = parsedPipeGraphId;
        selectedOutputContainer = comp;

        if (changed)
        {
            ResetCraftRuntimeState(false);
            disabledByPlayer = true;
            DevLog($"SERVER SELECT OUTPUT — {chestPos} mode={mode} pipeGraphId={parsedPipeGraphId} (changed, crafter disabled)");
        }
        else
        {
            DevLog($"SERVER SELECT OUTPUT — {chestPos} mode={mode} pipeGraphId={parsedPipeGraphId} (same selection, crafting state preserved)");
        }

        MarkCrafterDirty();
        return true;
    }

    public bool ServerSetEnabled(bool enabled)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
        {
            DevLog("ServerSetEnabled called on non-server", DevLogLevel.Warning);
            return false;
        }

        ResolveRecipeIfNeeded();
        ResolveSelectedInputContainer();
        ResolveSelectedOutputContainer();

        if (!enabled)
        {
            disabledByPlayer = true;
            isCrafting = false;
            isWaitingForIngredients = false;

            DevLog("SERVER SET ENABLED — disabled");
            MarkCrafterDirty();
            return true;
        }

        disabledByPlayer = false;

        if (!ValidateCraftStart(_recipe))
        {
            isCrafting = false;
            isWaitingForIngredients = true;
            craftStartTime = GameManager.Instance.World.worldTime;

            DevLog("SERVER SET ENABLED — enabled, waiting for valid craft start");
            MarkCrafterDirty();
            return true;
        }

        StartCraft();
        DevLog("SERVER SET ENABLED — enabled, craft started");
        return true;
    }

    private Recipe GetActiveRecipe()
    {
        if (string.IsNullOrEmpty(ActiveCraftRecipeName))
            return null;

        return CraftingManager.GetRecipe(ActiveCraftRecipeName);
    }

    public override void write(PooledBinaryWriter _bw, StreamModeWrite mode)
    {
        base.write(_bw, mode);

        const int VERSION = 8;

        if (IsDevLogging)
        {
            Log.Out("========================================");
            Log.Out($"[Crafter][WRITE] BEGIN — mode={mode}");
            Log.Out("========================================");
        }

        _bw.Write(VERSION);
        _bw.Write((int)0);

        _bw.Write(SelectedRecipeName ?? "");

        _bw.Write(SelectedInputChestPos.x);
        _bw.Write(SelectedInputChestPos.y);
        _bw.Write(SelectedInputChestPos.z);
        _bw.Write(SelectedInputPipeGraphId.ToString());
        _bw.Write(SelectedOutputChestPos.x);
        _bw.Write(SelectedOutputChestPos.y);
        _bw.Write(SelectedOutputChestPos.z);

        _bw.Write((int)SelectedOutputMode);
        _bw.Write(SelectedPipeGraphId.ToString());

        _bw.Write(availableOutputTargets?.Count ?? 0);
        if (availableOutputTargets != null)
        {
            for (int i = 0; i < availableOutputTargets.Count; i++)
            {
                OutputTargetInfo target = availableOutputTargets[i];
                _bw.Write(target.BlockPos.x);
                _bw.Write(target.BlockPos.y);
                _bw.Write(target.BlockPos.z);
                _bw.Write((int)target.TransportMode);
                _bw.Write(target.PipeGraphId.ToString());
            }
        }

        _bw.Write(isCrafting);
        _bw.Write(BaseRecipeDuration);
        _bw.Write(disabledByPlayer);

        _bw.Write(craftStartTime);
        _bw.Write(isWaitingForIngredients);

        _bw.Write(inputBuffer?.Count ?? 0);
        if (inputBuffer != null)
        {
            foreach (var kvp in inputBuffer)
            {
                _bw.Write(kvp.Key ?? "");
                _bw.Write(kvp.Value);
            }
        }

        if (IsDevLogging)
        {
            Log.Out($"[Crafter][WRITE] SelectedRecipeName='{SelectedRecipeName}'");
            Log.Out($"[Crafter][WRITE] InputChestPos={SelectedInputChestPos}");
            Log.Out($"[Crafter][WRITE] SelectedInputPipeGraphId={SelectedInputPipeGraphId}");
            Log.Out($"[Crafter][WRITE] OutputChestPos={SelectedOutputChestPos}");
            Log.Out($"[Crafter][WRITE] SelectedOutputMode={SelectedOutputMode}");
            Log.Out($"[Crafter][WRITE] SelectedPipeGraphId={SelectedPipeGraphId}");
            Log.Out($"[Crafter][WRITE] availableOutputTargets={availableOutputTargets?.Count ?? 0}");
            Log.Out($"[Crafter][WRITE] isCrafting={isCrafting}");
            Log.Out($"[Crafter][WRITE] BaseRecipeDuration={BaseRecipeDuration}");
            Log.Out($"[Crafter][WRITE] disabledByPlayer={disabledByPlayer}");
            Log.Out($"[Crafter][WRITE] craftStartTime={craftStartTime}");
            Log.Out($"[Crafter][WRITE] isWaitingForIngredients={isWaitingForIngredients}");
            Log.Out($"[Crafter][WRITE] inputBufferCount={inputBuffer?.Count ?? 0}");
            Log.Out("========================================");
            Log.Out("[Crafter][WRITE] COMPLETE");
            Log.Out("========================================");
        }
    }

    public override void read(PooledBinaryReader _br, StreamModeRead mode)
    {
        base.read(_br, mode);

        if (IsDevLogging)
        {
            Log.Out("========================================");
            Log.Out($"[Crafter][READ] BEGIN — mode={mode}");
            Log.Out("========================================");
        }

        int version = _br.ReadInt32();
        int count = _br.ReadInt32();

        if (IsDevLogging)
            Log.Out($"[Crafter][READ] Header — version={version} count={count}");

        SelectedRecipeName = _br.ReadString();

        int x = _br.ReadInt32();
        int y = _br.ReadInt32();
        int z = _br.ReadInt32();
        SelectedInputChestPos = new Vector3i(x, y, z);

        if (version >= 8)
        {
            string inputPipeGraphId = _br.ReadString();
            if (!Guid.TryParse(inputPipeGraphId, out SelectedInputPipeGraphId))
                SelectedInputPipeGraphId = Guid.Empty;
        }
        else
        {
            SelectedInputPipeGraphId = Guid.Empty;
        }

        int ox = _br.ReadInt32();
        int oy = _br.ReadInt32();
        int oz = _br.ReadInt32();
        SelectedOutputChestPos = new Vector3i(ox, oy, oz);

        if (version >= 5)
        {
            SelectedOutputMode = (OutputTransportMode)_br.ReadInt32();

            string pipeGraphId = _br.ReadString();
            if (!Guid.TryParse(pipeGraphId, out SelectedPipeGraphId))
                SelectedPipeGraphId = Guid.Empty;

            int outputTargetCount = _br.ReadInt32();
            availableOutputTargets = new List<OutputTargetInfo>(outputTargetCount);

            for (int i = 0; i < outputTargetCount; i++)
            {
                int tx = _br.ReadInt32();
                int ty = _br.ReadInt32();
                int tz = _br.ReadInt32();

                OutputTransportMode modeValue = (OutputTransportMode)_br.ReadInt32();

                string syncedPipeGraphId = _br.ReadString();
                Guid parsedGraphId;
                if (!Guid.TryParse(syncedPipeGraphId, out parsedGraphId))
                    parsedGraphId = Guid.Empty;

                availableOutputTargets.Add(
                    new OutputTargetInfo(new Vector3i(tx, ty, tz), modeValue, parsedGraphId)
                );
            }
        }
        else
        {
            SelectedOutputMode = OutputTransportMode.Adjacent;
            SelectedPipeGraphId = Guid.Empty;
            availableOutputTargets = new List<OutputTargetInfo>();
        }

        isCrafting = _br.ReadBoolean();
        BaseRecipeDuration = _br.ReadSingle();
        disabledByPlayer = _br.ReadBoolean();

        if (version >= 4)
        {
            craftStartTime = _br.ReadUInt64();
            isWaitingForIngredients = _br.ReadBoolean();
        }
        else
        {
            craftStartTime = 0;
            isWaitingForIngredients = false;
        }

        if (version >= 7)
        {
            int inputBufferCount = _br.ReadInt32();
            inputBuffer = new Dictionary<string, int>(inputBufferCount);

            for (int i = 0; i < inputBufferCount; i++)
            {
                string itemName = _br.ReadString();
                int itemCount = _br.ReadInt32();

                if (!string.IsNullOrEmpty(itemName) && itemCount > 0)
                    inputBuffer[itemName] = itemCount;
            }
        }
        else if (version >= 6)
        {
            int inputBufferCount = _br.ReadInt32();
            inputBuffer = new Dictionary<string, int>(inputBufferCount);

            for (int i = 0; i < inputBufferCount; i++)
            {
                string itemName = _br.ReadString();
                int itemCount = _br.ReadInt32();

                if (!string.IsNullOrEmpty(itemName) && itemCount > 0)
                    inputBuffer[itemName] = itemCount;
            }

            int pendingInputRequestCount = _br.ReadInt32();
            for (int i = 0; i < pendingInputRequestCount; i++)
            {
                _br.ReadString();
                _br.ReadInt32();
            }

            _br.ReadInt32();
        }
        else
        {
            inputBuffer = new Dictionary<string, int>();
        }

        _recipe = null;
        ResolveRecipeIfNeeded();
        ResolveSelectedInputContainer();
        ResolveSelectedOutputContainer();

        if (IsDevLogging)
        {
            Log.Out($"[Crafter][READ] Recipe='{SelectedRecipeName}'");
            Log.Out($"[Crafter][READ] InputChestPos={SelectedInputChestPos}");
            Log.Out($"[Crafter][READ] SelectedInputPipeGraphId={SelectedInputPipeGraphId}");
            Log.Out($"[Crafter][READ] OutputChestPos={SelectedOutputChestPos}");
            Log.Out($"[Crafter][READ] SelectedOutputMode={SelectedOutputMode}");
            Log.Out($"[Crafter][READ] SelectedPipeGraphId={SelectedPipeGraphId}");
            Log.Out($"[Crafter][READ] availableOutputTargets={availableOutputTargets?.Count ?? 0}");
            Log.Out($"[Crafter][READ] isCrafting={isCrafting}");
            Log.Out($"[Crafter][READ] BaseRecipeDuration={BaseRecipeDuration}");
            Log.Out($"[Crafter][READ] disabledByPlayer={disabledByPlayer}");
            Log.Out($"[Crafter][READ] craftStartTime={craftStartTime}");
            Log.Out($"[Crafter][READ] isWaitingForIngredients={isWaitingForIngredients}");
            Log.Out($"[Crafter][READ] inputBufferCount={inputBuffer?.Count ?? 0}");
            Log.Out("========================================");
            Log.Out("[Crafter][READ] COMPLETE");
            Log.Out("========================================");
        }
    }
}


