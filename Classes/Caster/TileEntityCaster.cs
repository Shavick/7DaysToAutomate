using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public class TileEntityCaster : TileEntityMachine
{
    private const int PersistVersion = 1;
    private const int MaxSerializedOutputTargets = 64;
    private const string DefaultMachineRecipeGroup = "mold";

    private sealed class CasterRule
    {
        public string RecipeKey;
        public string DisplayName;
        public string FluidType;
        public int FluidAmountMg;
        public MachineRecipeItemOutput PrimaryOutput;
        public MachineRecipeItemOutput SecondaryOutput;
        public int CraftTimeTicks;
    }

    private static readonly Vector3i[] NeighborOffsets =
    {
        Vector3i.forward,
        Vector3i.back,
        Vector3i.left,
        Vector3i.right,
        Vector3i.up,
        Vector3i.down
    };

    public List<OutputTargetInfo> availableOutputTargets = new List<OutputTargetInfo>();

    public Vector3i SelectedOutputChestPos = Vector3i.zero;
    public OutputTransportMode SelectedOutputMode = OutputTransportMode.Adjacent;
    public Guid SelectedOutputPipeGraphId = Guid.Empty;

    public string SelectedRecipeKey = string.Empty;
    public string SelectedFluidType = string.Empty;
    public Guid SelectedFluidGraphId = Guid.Empty;

    public bool IsProcessing;
    public int CycleTickCounter;
    public int CycleTickLength = 20;
    public string ActiveRecipeKey = string.Empty;

    public string LastAction = "Idle";
    public string LastBlockReason = string.Empty;

    private readonly List<CasterRule> rules = new List<CasterRule>();
    private string machineRecipeGroupsCsv = DefaultMachineRecipeGroup;
    private bool configLoaded;
    private int refreshTicker;
    private string pendingFluidInputType = string.Empty;
    private int pendingFluidInputAmountMg;

    public TileEntityCaster(Chunk chunk) : base(chunk)
    {
    }

    public string MachineRecipeGroupsCsv
    {
        get => string.IsNullOrWhiteSpace(machineRecipeGroupsCsv) ? DefaultMachineRecipeGroup : machineRecipeGroupsCsv;
        set => machineRecipeGroupsCsv = string.IsNullOrWhiteSpace(value) ? DefaultMachineRecipeGroup : value.Trim();
    }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.Caster);
    }

    public override IHLRSnapshot BuildHLRSnapshot(WorldBase world)
    {
        EnsureConfigLoaded();
        ulong now = world?.GetWorldTime() ?? 0UL;
        Dictionary<string, int> pendingOutputs = new Dictionary<string, int>(StringComparer.Ordinal);
        if (pendingOutput != null)
        {
            foreach (KeyValuePair<string, int> kvp in pendingOutput)
            {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                    continue;

                pendingOutputs[kvp.Key] = kvp.Value;
            }
        }

        CasterSnapshot snapshot = new CasterSnapshot
        {
            MachineId = MachineGuid,
            Position = ToWorldPos(),
            WorldTime = now,
            LastHLRSimTime = now,
            IsOn = IsOn,
            SelectedOutputChestPos = SelectedOutputChestPos,
            SelectedOutputMode = SelectedOutputMode,
            SelectedOutputPipeGraphId = SelectedOutputPipeGraphId,
            SelectedRecipeKey = SelectedRecipeKey ?? string.Empty,
            SelectedFluidType = SelectedFluidType ?? string.Empty,
            SelectedFluidGraphId = SelectedFluidGraphId,
            IsProcessing = IsProcessing,
            CycleTickCounter = CycleTickCounter,
            CycleTickLength = Math.Max(1, CycleTickLength),
            ActiveRecipeKey = ActiveRecipeKey ?? string.Empty,
            MachineRecipeGroupsCsv = MachineRecipeGroupsCsv,
            PendingFluidInputType = pendingFluidInputType ?? string.Empty,
            PendingFluidInputAmountMg = Math.Max(0, pendingFluidInputAmountMg),
            PendingOutputs = pendingOutputs,
            LastAction = LastAction ?? string.Empty,
            LastBlockReason = LastBlockReason ?? string.Empty
        };
        PipeGraphManager.TryResolveMachinePipeAnchorPosition(
            world,
            0,
            snapshot.Position,
            snapshot.SelectedOutputPipeGraphId,
            snapshot.SelectedOutputChestPos,
            out snapshot.SelectedOutputPipeAnchorPos);
        return snapshot;
    }

    public override void ApplyHLRSnapshot(object snapshotObj)
    {
        if (!(snapshotObj is CasterSnapshot snapshot))
            return;

        IsOn = snapshot.IsOn;
        SelectedOutputChestPos = snapshot.SelectedOutputChestPos;
        SelectedOutputMode = snapshot.SelectedOutputMode;
        SelectedOutputPipeGraphId = snapshot.SelectedOutputPipeGraphId;
        SelectedRecipeKey = snapshot.SelectedRecipeKey ?? string.Empty;
        SelectedFluidType = (snapshot.SelectedFluidType ?? string.Empty).Trim().ToLowerInvariant();
        SelectedFluidGraphId = snapshot.SelectedFluidGraphId;
        IsProcessing = snapshot.IsProcessing;
        CycleTickCounter = Math.Max(0, snapshot.CycleTickCounter);
        CycleTickLength = Math.Max(1, snapshot.CycleTickLength);
        ActiveRecipeKey = snapshot.ActiveRecipeKey ?? string.Empty;
        MachineRecipeGroupsCsv = snapshot.MachineRecipeGroupsCsv;
        pendingFluidInputType = (snapshot.PendingFluidInputType ?? string.Empty).Trim().ToLowerInvariant();
        pendingFluidInputAmountMg = Math.Max(0, snapshot.PendingFluidInputAmountMg);
        LastAction = snapshot.LastAction ?? string.Empty;
        LastBlockReason = snapshot.LastBlockReason ?? string.Empty;

        pendingOutput.Clear();
        if (snapshot.PendingOutputs != null)
        {
            foreach (KeyValuePair<string, int> kvp in snapshot.PendingOutputs)
            {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                    continue;

                pendingOutput[kvp.Key] = kvp.Value;
            }
        }

        if (!IsProcessing)
        {
            CycleTickCounter = 0;
            ActiveRecipeKey = string.Empty;
            pendingFluidInputType = string.Empty;
            pendingFluidInputAmountMg = 0;
        }

        configLoaded = false;
        EnsureConfigLoaded();
        simulatedByHLR = false;
        NeedsUiRefresh = true;

        World currentWorld = GameManager.Instance?.World;
        if (currentWorld != null && !currentWorld.IsRemote())
            setModified();
    }

    public string GetSelectedRecipeDisplayName()
    {
        EnsureConfigLoaded();
        if (!TryGetRule(SelectedRecipeKey, out CasterRule rule) || rule == null)
            return "None";

        if (!string.IsNullOrWhiteSpace(rule.DisplayName))
            return Localization.Get(rule.DisplayName);

        return GetItemDisplayName(rule.PrimaryOutput?.ItemName);
    }

    public string GetSelectedRecipeDetails()
    {
        EnsureConfigLoaded();
        if (!TryGetRule(SelectedRecipeKey, out CasterRule rule) || rule == null)
            return "Select a recipe.";

        string primary = rule.PrimaryOutput == null ? "None" : $"{GetItemDisplayName(rule.PrimaryOutput.ItemName)} x{rule.PrimaryOutput.Count}";
        string secondary = rule.SecondaryOutput == null ? "None" : $"{GetItemDisplayName(rule.SecondaryOutput.ItemName)} x{rule.SecondaryOutput.Count}";
        return $"Fluid: {ToFluidName(rule.FluidType)} {ToGallons(rule.FluidAmountMg)} gal\nPrimary: {primary}\nByproduct: {secondary}\nTime: {rule.CraftTimeTicks} ticks";
    }

    public string GetCycleTimerText()
    {
        return $"{CycleTickCounter}/{Math.Max(1, CycleTickLength)}";
    }

    public string GetPendingFluidInputType() => pendingFluidInputType ?? string.Empty;
    public int GetPendingFluidInputAmountMg() => Math.Max(0, pendingFluidInputAmountMg);

    public string GetPendingPrimaryOutputItemName() => GetOutputName(0);
    public int GetPendingPrimaryOutputItemCount() => GetPendingOutputCount(GetOutputName(0));
    public string GetPendingSecondaryOutputItemName() => GetOutputName(1);
    public int GetPendingSecondaryOutputItemCount() => GetPendingOutputCount(GetOutputName(1));

    public bool HasSelectedRecipe()
    {
        EnsureConfigLoaded();
        return TryGetRule(SelectedRecipeKey, out _);
    }

    public bool HasItemOutputRequirement(WorldBase world)
    {
        return HasSelectedOutputTarget(world);
    }

    public bool HasFluidInputRequirement(WorldBase world)
    {
        if (world == null || !TryGetRule(SelectedRecipeKey, out CasterRule rule) || rule == null)
            return false;

        if (IsProcessing || pendingFluidInputAmountMg > 0)
            return true;

        if (world.IsRemote())
            return SelectedFluidGraphId != Guid.Empty;

        return TryResolveFluidRequirement(world, rule, out _, out _);
    }

    public bool AreAllRequirementsMet(WorldBase world)
    {
        return AreAllRequirementsMet(world, out _);
    }

    public bool AreAllRequirementsMet(WorldBase world, out string blockedReason)
    {
        blockedReason = string.Empty;
        if (world == null)
        {
            blockedReason = "World unavailable";
            return false;
        }

        if (!HasSelectedRecipe())
        {
            blockedReason = "No recipe selected";
            return false;
        }

        if (!HasSelectedOutputTarget(world))
        {
            blockedReason = "Missing Item Output";
            return false;
        }

        if (!TryGetRule(SelectedRecipeKey, out CasterRule rule) || rule == null)
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        if (!TryResolveFluidRequirement(world, rule, out Guid graphId, out blockedReason))
            return false;

        if (!world.IsRemote())
            SelectedFluidGraphId = graphId;

        return true;
    }

    public void ServerCycleRecipe(int direction)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            return;

        EnsureConfigLoaded();
        if (rules.Count <= 0)
            return;

        int current = FindRuleIndex(SelectedRecipeKey);
        if (current < 0)
            current = 0;

        int step = direction >= 0 ? 1 : -1;
        int next = current + step;
        if (next >= rules.Count)
            next = 0;
        else if (next < 0)
            next = rules.Count - 1;

        CasterRule rule = rules[next];
        if (rule == null)
            return;

        SelectedRecipeKey = rule.RecipeKey ?? string.Empty;
        SelectedFluidType = rule.FluidType ?? string.Empty;
        CycleTickLength = Math.Max(1, rule.CraftTimeTicks);
        ResolveFluidInputGraph(GameManager.Instance?.World);
        MarkDirty();
    }

    public bool ServerSelectOutputContainer(Vector3i chestPos, OutputTransportMode mode, string pipeGraphId)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            return false;

        WorldBase world = GameManager.Instance?.World;
        if (world == null)
            return false;

        Guid parsedPipeGraphId = Guid.Empty;
        if (!string.IsNullOrEmpty(pipeGraphId))
            Guid.TryParse(pipeGraphId, out parsedPipeGraphId);

        if (chestPos == Vector3i.zero)
        {
            SelectedOutputChestPos = Vector3i.zero;
            SelectedOutputMode = OutputTransportMode.Adjacent;
            SelectedOutputPipeGraphId = Guid.Empty;
            MarkDirty();
            return true;
        }

        RefreshAvailableOutputTargets(world);
        List<OutputTargetInfo> targets = availableOutputTargets;
        for (int i = 0; i < targets.Count; i++)
        {
            OutputTargetInfo target = targets[i];
            if (target == null)
                continue;

            if (target.BlockPos == chestPos && target.TransportMode == mode && target.PipeGraphId == parsedPipeGraphId)
            {
                if (!(world.GetTileEntity(chestPos) is TileEntityComposite))
                    return false;

                SelectedOutputChestPos = chestPos;
                SelectedOutputMode = mode;
                SelectedOutputPipeGraphId = parsedPipeGraphId;
                MarkDirty();
                return true;
            }
        }

        return false;
    }

    public List<OutputTargetInfo> GetAvailableOutputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return availableOutputTargets ?? new List<OutputTargetInfo>();

        RefreshAvailableOutputTargets(world);
        return availableOutputTargets;
    }

    public void RefreshAvailableOutputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        List<OutputTargetInfo> discovered = MachineOutputDiscovery.GetAvailableOutputs(world, 0, ToWorldPos(), 8);
        if (AreOutputTargetsEqual(availableOutputTargets, discovered))
            return;

        availableOutputTargets = discovered;
        MarkDirty();
    }

    public bool ResolveFluidInputGraph(WorldBase world)
    {
        if (world == null || !TryGetRule(SelectedRecipeKey, out CasterRule rule) || rule == null)
        {
            bool changed = SelectedFluidGraphId != Guid.Empty;
            SelectedFluidGraphId = Guid.Empty;
            return changed;
        }

        Guid previous = SelectedFluidGraphId;
        if (!TryGetCompatibleFluidGraph(world, rule.FluidType, out Guid graphId))
            graphId = Guid.Empty;

        SelectedFluidGraphId = graphId;
        return previous != SelectedFluidGraphId;
    }

    public override void UpdateTick(World world)
    {
        base.UpdateTick(world);
        if (world == null || world.IsRemote() || IsSimulatingHLR())
            return;

        EnsureConfigLoaded();

        refreshTicker++;
        if (refreshTicker >= 20)
        {
            refreshTicker = 0;
            RefreshAvailableOutputTargets(world);
            ResolveFluidInputGraph(world);
        }

        FlushPendingOutput(world);

        if (!IsOn)
        {
            LastAction = "Off";
            LastBlockReason = string.Empty;
            return;
        }

        if (!IsProcessing)
        {
            if (!AreAllRequirementsMet(world, out string blocked))
            {
                LastAction = "Waiting";
                LastBlockReason = blocked;
                return;
            }

            if (pendingOutput.Count > 0)
            {
                LastAction = "Waiting";
                LastBlockReason = "Output blocked";
                return;
            }

            if (!TryStartCycle(world, out blocked))
            {
                LastAction = "Waiting";
                LastBlockReason = blocked;
                return;
            }
        }

        CycleTickCounter++;
        if (CycleTickCounter < Math.Max(1, CycleTickLength))
        {
            LastAction = "Processing";
            LastBlockReason = string.Empty;
            if (CycleTickCounter % 5 == 0)
                MarkDirty();
            return;
        }

        CompleteCycle();
        setModified();
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);
        bw.Write(PersistVersion);
        bw.Write(IsOn);
        WriteOutputTargets(bw);
        bw.Write(SelectedOutputChestPos.x);
        bw.Write(SelectedOutputChestPos.y);
        bw.Write(SelectedOutputChestPos.z);
        bw.Write((int)SelectedOutputMode);
        bw.Write(SelectedOutputPipeGraphId.ToString());
        bw.Write(SelectedRecipeKey ?? string.Empty);
        bw.Write(SelectedFluidType ?? string.Empty);
        bw.Write(SelectedFluidGraphId.ToString());
        bw.Write(IsProcessing);
        bw.Write(CycleTickCounter);
        bw.Write(CycleTickLength);
        bw.Write(ActiveRecipeKey ?? string.Empty);
        bw.Write(pendingFluidInputType ?? string.Empty);
        bw.Write(pendingFluidInputAmountMg);
        bw.Write(MachineRecipeGroupsCsv);
        bw.Write(LastAction ?? string.Empty);
        bw.Write(LastBlockReason ?? string.Empty);
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);
        try
        {
            int persistVersion = br.ReadInt32();
            IsOn = persistVersion >= 1 && br.ReadBoolean();
            ReadOutputTargets(br);
            SelectedOutputChestPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            SelectedOutputMode = (OutputTransportMode)br.ReadInt32();
            if (!Guid.TryParse(br.ReadString(), out SelectedOutputPipeGraphId))
                SelectedOutputPipeGraphId = Guid.Empty;
            SelectedRecipeKey = br.ReadString() ?? string.Empty;
            SelectedFluidType = br.ReadString() ?? string.Empty;
            if (!Guid.TryParse(br.ReadString(), out SelectedFluidGraphId))
                SelectedFluidGraphId = Guid.Empty;
            IsProcessing = br.ReadBoolean();
            CycleTickCounter = Math.Max(0, br.ReadInt32());
            CycleTickLength = Math.Max(1, br.ReadInt32());
            ActiveRecipeKey = br.ReadString() ?? string.Empty;
            pendingFluidInputType = br.ReadString() ?? string.Empty;
            pendingFluidInputAmountMg = Math.Max(0, br.ReadInt32());
            MachineRecipeGroupsCsv = br.ReadString() ?? DefaultMachineRecipeGroup;
            LastAction = br.ReadString() ?? string.Empty;
            LastBlockReason = br.ReadString() ?? string.Empty;
            configLoaded = false;
            EnsureConfigLoaded();
            NeedsUiRefresh = true;
        }
        catch (Exception ex)
        {
            Log.Error($"[Caster][READ] Failed to deserialize at {ToWorldPos()} mode={mode}: {ex.Message}");
        }
    }

    public static bool TryReadMachineRecipeAsCasterRule(MachineRecipe recipe, int defaultCraftTimeTicks, out string fluidType, out int fluidAmountMg, out MachineRecipeItemOutput primaryOutput, out MachineRecipeItemOutput secondaryOutput, out int craftTimeTicks, out string error)
    {
        fluidType = string.Empty;
        fluidAmountMg = 0;
        primaryOutput = null;
        secondaryOutput = null;
        craftTimeTicks = Math.Max(1, defaultCraftTimeTicks);
        error = string.Empty;

        if (recipe == null) { error = "Recipe is null"; return false; }
        if (recipe.Inputs != null && recipe.Inputs.Count > 0) { error = "Caster recipe does not support <input>"; return false; }
        if (recipe.FluidInputs == null || recipe.FluidInputs.Count != 1) { error = "Caster recipe requires exactly one <fluid_input>"; return false; }
        if (recipe.ItemOutputs == null || recipe.ItemOutputs.Count < 1 || recipe.ItemOutputs.Count > 2) { error = "Caster recipe requires 1-2 <output> nodes"; return false; }
        if (recipe.FluidOutputs != null && recipe.FluidOutputs.Count > 0) { error = "Caster recipe does not support <fluid_output>"; return false; }
        if (recipe.GasOutputs != null && recipe.GasOutputs.Count > 0) { error = "Caster recipe does not support <gas_output>"; return false; }

        MachineRecipeFluidInput fluid = recipe.FluidInputs[0];
        if (fluid == null || string.IsNullOrWhiteSpace(fluid.Type) || fluid.AmountMg <= 0) { error = "Caster recipe has an invalid fluid input"; return false; }
        fluidType = fluid.Type.Trim().ToLowerInvariant();
        fluidAmountMg = Math.Max(1, fluid.AmountMg);

        MachineRecipeItemOutput first = recipe.ItemOutputs[0];
        if (first == null || string.IsNullOrWhiteSpace(first.ItemName) || first.Count <= 0) { error = "Caster recipe has an invalid primary output"; return false; }
        primaryOutput = new MachineRecipeItemOutput(first.ItemName.Trim(), first.Count);

        if (recipe.ItemOutputs.Count > 1)
        {
            MachineRecipeItemOutput second = recipe.ItemOutputs[1];
            if (second == null || string.IsNullOrWhiteSpace(second.ItemName) || second.Count <= 0) { error = "Caster recipe has an invalid secondary output"; return false; }
            secondaryOutput = new MachineRecipeItemOutput(second.ItemName.Trim(), second.Count);
        }

        craftTimeTicks = recipe.CraftTimeTicks.HasValue ? Math.Max(1, recipe.CraftTimeTicks.Value) : Math.Max(1, defaultCraftTimeTicks);
        return true;
    }

    private bool TryStartCycle(WorldBase world, out string blockedReason)
    {
        blockedReason = string.Empty;
        if (!TryGetRule(SelectedRecipeKey, out CasterRule rule) || rule == null)
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        if (!FluidGraphManager.TryConsumeFluid(world, 0, SelectedFluidGraphId, rule.FluidType, rule.FluidAmountMg, out int consumed) || consumed < rule.FluidAmountMg)
        {
            if (consumed > 0)
                FluidGraphManager.TryInjectFluid(world, 0, SelectedFluidGraphId, rule.FluidType, consumed, out _);
            blockedReason = $"Need {ToGallons(rule.FluidAmountMg)} gal {ToFluidName(rule.FluidType)}";
            return false;
        }

        pendingFluidInputType = rule.FluidType;
        pendingFluidInputAmountMg = consumed;
        IsProcessing = true;
        CycleTickCounter = 0;
        CycleTickLength = Math.Max(1, rule.CraftTimeTicks);
        ActiveRecipeKey = rule.RecipeKey ?? string.Empty;
        LastAction = "Consumed Fluid";
        LastBlockReason = string.Empty;
        MarkDirty();
        return true;
    }

    private void CompleteCycle()
    {
        if (TryGetRule(ActiveRecipeKey, out CasterRule rule) && rule != null)
        {
            if (rule.PrimaryOutput != null)
                AddPendingOutput(rule.PrimaryOutput.ItemName, rule.PrimaryOutput.Count);
            if (rule.SecondaryOutput != null)
                AddPendingOutput(rule.SecondaryOutput.ItemName, rule.SecondaryOutput.Count);
        }

        pendingFluidInputType = string.Empty;
        pendingFluidInputAmountMg = 0;
        IsProcessing = false;
        CycleTickCounter = 0;
        ActiveRecipeKey = string.Empty;
        LastAction = "Craft complete";
        LastBlockReason = string.Empty;
        NeedsUiRefresh = true;
    }

    private void FlushPendingOutput(WorldBase world)
    {
        if (pendingOutput == null || pendingOutput.Count == 0)
            return;

        foreach (KeyValuePair<string, int> kvp in new List<KeyValuePair<string, int>>(pendingOutput))
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
            {
                pendingOutput.Remove(kvp.Key);
                continue;
            }

            int moved = 0;
            if (SelectedOutputMode == OutputTransportMode.Pipe)
            {
                Dictionary<string, int> req = new Dictionary<string, int>(StringComparer.Ordinal) { [kvp.Key] = kvp.Value };
                if (PipeGraphManager.TryDepositStorageItems(world, 0, SelectedOutputPipeGraphId, SelectedOutputChestPos, req, out Dictionary<string, int> deposited) &&
                    deposited != null && deposited.TryGetValue(kvp.Key, out int depositedCount))
                {
                    moved = depositedCount;
                }
            }
            else
            {
                TryDepositAdjacent(world, kvp.Key, kvp.Value, out moved);
            }

            if (moved <= 0)
            {
                LastBlockReason = "Output blocked";
                MarkDirty();
                break;
            }

            int remaining = kvp.Value - moved;
            if (remaining > 0) pendingOutput[kvp.Key] = remaining; else pendingOutput.Remove(kvp.Key);
            LastAction = "Output transferred";
            LastBlockReason = string.Empty;
            MarkDirty();
        }
    }

    private bool TryDepositAdjacent(WorldBase world, string itemName, int requestedCount, out int moved)
    {
        moved = 0;
        if (!(world.GetTileEntity(0, SelectedOutputChestPos) is TileEntityComposite comp))
            return false;
        TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
            return false;

        ItemValue value = ItemClass.GetItem(itemName, false);
        if (value?.ItemClass == null)
            return false;

        int remaining = requestedCount;
        for (int i = 0; i < storage.items.Length && remaining > 0; i++)
        {
            ItemStack slot = storage.items[i];
            if (slot.IsEmpty() || slot.itemValue == null || slot.itemValue.type != value.type)
                continue;
            int max = Math.Max(1, slot.itemValue.ItemClass.Stacknumber.Value);
            int delta = Math.Min(max - slot.count, remaining);
            if (delta <= 0) continue;
            slot.count += delta;
            storage.items[i] = slot;
            remaining -= delta;
        }

        for (int i = 0; i < storage.items.Length && remaining > 0; i++)
        {
            if (!storage.items[i].IsEmpty())
                continue;
            int max = Math.Max(1, value.ItemClass.Stacknumber.Value);
            int delta = Math.Min(max, remaining);
            storage.items[i] = new ItemStack(value.Clone(), delta);
            remaining -= delta;
        }

        moved = requestedCount - remaining;
        if (moved > 0)
            storage.SetModified();
        return moved > 0;
    }

    private void EnsureConfigLoaded()
    {
        if (configLoaded)
            return;
        configLoaded = true;
        rules.Clear();
        MachineRecipeGroupsCsv = string.IsNullOrWhiteSpace(blockValue.Block?.Properties?.GetString("MachineRecipes")) ? DefaultMachineRecipeGroup : blockValue.Block.Properties.GetString("MachineRecipes");
        int defaultTicks = ReadIntProperty("InputSpeed", 20, 1, 2000);
        List<MachineRecipe> recipesRaw = MachineRecipeRegistry.GetRecipesForMachineGroups(MachineRecipeGroupsCsv, true);
        for (int i = 0; i < recipesRaw.Count; i++)
        {
            MachineRecipe recipe = recipesRaw[i];
            if (!TryReadMachineRecipeAsCasterRule(recipe, defaultTicks, out string fluidType, out int fluidAmountMg, out MachineRecipeItemOutput primary, out MachineRecipeItemOutput secondary, out int ticks, out _))
                continue;
            rules.Add(new CasterRule { RecipeKey = recipe.NormalizedKey, DisplayName = recipe.Name, FluidType = fluidType, FluidAmountMg = fluidAmountMg, PrimaryOutput = primary, SecondaryOutput = secondary, CraftTimeTicks = ticks });
        }

        if (!TryGetRule(SelectedRecipeKey, out CasterRule selected) && rules.Count > 0)
        {
            selected = rules[0];
            SelectedRecipeKey = selected.RecipeKey ?? string.Empty;
        }

        if (selected != null)
        {
            SelectedFluidType = selected.FluidType ?? string.Empty;
            CycleTickLength = Math.Max(1, selected.CraftTimeTicks);
        }
        else
        {
            SelectedFluidType = string.Empty;
            CycleTickLength = defaultTicks;
        }
    }

    private int ReadIntProperty(string propertyName, int fallback, int min, int max)
    {
        string raw = blockValue.Block?.Properties?.GetString(propertyName);
        if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out int value))
            value = fallback;

        if (value < min)
            value = min;
        else if (value > max)
            value = max;

        return value;
    }

    private bool TryGetRule(string key, out CasterRule rule)
    {
        rule = null;
        if (string.IsNullOrEmpty(key))
            return false;
        for (int i = 0; i < rules.Count; i++)
        {
            CasterRule candidate = rules[i];
            if (candidate != null && string.Equals(candidate.RecipeKey, key, StringComparison.Ordinal))
            {
                rule = candidate;
                return true;
            }
        }
        return false;
    }

    private int FindRuleIndex(string key)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            CasterRule rule = rules[i];
            if (rule != null && string.Equals(rule.RecipeKey, key, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private bool TryResolveFluidRequirement(WorldBase world, CasterRule rule, out Guid graphId, out string blockedReason)
    {
        graphId = Guid.Empty;
        blockedReason = string.Empty;
        if (rule == null || string.IsNullOrEmpty(rule.FluidType) || rule.FluidAmountMg <= 0)
        {
            blockedReason = "Invalid fluid input";
            return false;
        }
        if (!TryGetCompatibleFluidGraph(world, rule.FluidType, out graphId))
        {
            blockedReason = "Missing/Invalid Fluid Input";
            return false;
        }
        if (!FluidGraphManager.TryGetAvailableFluidAmount(world, 0, graphId, rule.FluidType, out int available) || available < rule.FluidAmountMg)
        {
            blockedReason = $"Need {ToGallons(rule.FluidAmountMg)} gal {ToFluidName(rule.FluidType)}";
            return false;
        }
        return true;
    }

    private bool HasSelectedOutputTarget(WorldBase world)
    {
        if (world == null || SelectedOutputChestPos == Vector3i.zero)
            return false;
        List<OutputTargetInfo> targets = GetAvailableOutputTargets(world);
        if (targets == null || targets.Count == 0)
            return false;

        bool foundByPositionAndMode = false;
        Guid reboundGraphId = Guid.Empty;

        for (int i = 0; i < targets.Count; i++)
        {
            OutputTargetInfo target = targets[i];
            if (target == null)
                continue;

            if (target.BlockPos != SelectedOutputChestPos || target.TransportMode != SelectedOutputMode)
                continue;

            if (target.PipeGraphId == SelectedOutputPipeGraphId)
                return true;

            if (!foundByPositionAndMode)
            {
                foundByPositionAndMode = true;
                reboundGraphId = target.PipeGraphId;
            }
        }

        if (!foundByPositionAndMode)
            return false;

        if (!world.IsRemote() && SelectedOutputPipeGraphId != reboundGraphId)
            SelectedOutputPipeGraphId = reboundGraphId;

        return true;
    }

    private bool TryGetCompatibleFluidGraph(WorldBase world, string fluidType, out Guid graphId)
    {
        graphId = Guid.Empty;
        if (world == null || string.IsNullOrWhiteSpace(fluidType))
            return false;
        string normalized = fluidType.Trim().ToLowerInvariant();
        List<Guid> candidates = new List<Guid>();
        Vector3i machinePos = ToWorldPos();
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            TileEntityLiquidPipe pipe = world.GetTileEntity(0, machinePos + NeighborOffsets[i]) as TileEntityLiquidPipe;
            if (pipe == null)
                continue;
            if (pipe.FluidGraphId != Guid.Empty && !candidates.Contains(pipe.FluidGraphId))
                candidates.Add(pipe.FluidGraphId);
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            Guid candidate = candidates[i];
            if (!FluidGraphManager.TryGetGraph(candidate, out FluidGraphData graph) || graph == null)
                continue;
            if (!string.IsNullOrEmpty(graph.FluidType) && !string.Equals(graph.FluidType, normalized, StringComparison.Ordinal))
                continue;
            graphId = candidate;
            return true;
        }

        return false;
    }

    private void WriteOutputTargets(PooledBinaryWriter bw)
    {
        List<OutputTargetInfo> targets = availableOutputTargets ?? new List<OutputTargetInfo>();
        bw.Write(targets.Count);
        for (int i = 0; i < targets.Count; i++)
        {
            OutputTargetInfo t = targets[i];
            bw.Write(t?.BlockPos.x ?? 0);
            bw.Write(t?.BlockPos.y ?? 0);
            bw.Write(t?.BlockPos.z ?? 0);
            bw.Write((int)(t?.TransportMode ?? OutputTransportMode.Adjacent));
            bw.Write((t?.PipeGraphId ?? Guid.Empty).ToString());
        }
    }

    private void ReadOutputTargets(PooledBinaryReader br)
    {
        int count = br.ReadInt32();
        if (count < 0 || count > MaxSerializedOutputTargets)
            throw new InvalidOperationException($"Invalid output target count: {count}");
        availableOutputTargets = new List<OutputTargetInfo>(count);
        for (int i = 0; i < count; i++)
        {
            Vector3i pos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            OutputTransportMode mode = (OutputTransportMode)br.ReadInt32();
            if (!Guid.TryParse(br.ReadString(), out Guid graphId))
                graphId = Guid.Empty;
            availableOutputTargets.Add(new OutputTargetInfo(pos, mode, graphId));
        }
    }

    private void MarkDirty()
    {
        NeedsUiRefresh = true;
        setModified();
    }

    private int GetPendingOutputCount(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return 0;
        return pendingOutput.TryGetValue(itemName, out int count) ? Math.Max(0, count) : 0;
    }

    private string GetOutputName(int idx)
    {
        string key = !string.IsNullOrEmpty(ActiveRecipeKey) ? ActiveRecipeKey : SelectedRecipeKey;
        if (!TryGetRule(key, out CasterRule rule) || rule == null)
            return string.Empty;
        return idx == 0 ? (rule.PrimaryOutput?.ItemName ?? string.Empty) : (rule.SecondaryOutput?.ItemName ?? string.Empty);
    }

    private static bool AreOutputTargetsEqual(List<OutputTargetInfo> left, List<OutputTargetInfo> right)
    {
        int leftCount = left?.Count ?? 0;
        int rightCount = right?.Count ?? 0;
        if (leftCount != rightCount)
            return false;
        for (int i = 0; i < leftCount; i++)
        {
            OutputTargetInfo a = left[i];
            OutputTargetInfo b = right[i];
            if (a == null || b == null)
            {
                if (!ReferenceEquals(a, b))
                    return false;
                continue;
            }
            if (a.BlockPos != b.BlockPos || a.TransportMode != b.TransportMode || a.PipeGraphId != b.PipeGraphId)
                return false;
        }
        return true;
    }

    private static string GetItemDisplayName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return string.Empty;
        ItemValue iv = ItemClass.GetItem(itemName, false);
        return iv?.ItemClass != null ? iv.ItemClass.GetLocalizedItemName() : itemName;
    }

    private static string ToFluidName(string fluidType)
    {
        if (string.IsNullOrWhiteSpace(fluidType))
            return string.Empty;
        string normalized = fluidType.Trim().Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string ToGallons(int mg)
    {
        return (mg / (double)FluidConstants.MilliGallonsPerGallon).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
