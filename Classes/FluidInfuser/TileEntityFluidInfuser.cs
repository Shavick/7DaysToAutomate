using System;
using System.Collections.Generic;

public class TileEntityFluidInfuser : TileEntityMachine
{
    private const int PersistVersion = 3;
    private const int MaxSerializedInputTargets = 64;
    private const int MaxSerializedOutputTargets = 64;
    private const int MaxSerializedPendingInputs = 16;
    private const string DefaultMachineRecipeGroup = "fluidinfuser";

    private sealed class FluidInfusionRule
    {
        public string RecipeKey;
        public string DisplayName;
        public List<MachineRecipeInput> ItemInputs = new List<MachineRecipeInput>();
        public string FluidType;
        public int FluidAmountMg;
        public List<MachineRecipeItemOutput> ItemOutputs = new List<MachineRecipeItemOutput>();
        public int CraftTimeTicks;
    }

    public List<InputTargetInfo> availableInputTargets = new List<InputTargetInfo>();
    public List<OutputTargetInfo> availableOutputTargets = new List<OutputTargetInfo>();

    public Vector3i SelectedInputChestPos = Vector3i.zero;
    public Guid SelectedInputPipeGraphId = Guid.Empty;
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

    private readonly List<FluidInfusionRule> recipes = new List<FluidInfusionRule>();
    private string machineRecipeGroupsCsv = DefaultMachineRecipeGroup;
    private bool configLoaded;
    private int refreshTicker;
    private readonly Dictionary<string, int> pendingInputItems = new Dictionary<string, int>(StringComparer.Ordinal);
    private string pendingFluidInputType = string.Empty;
    private int pendingFluidInputAmountMg;

    private TileEntityComposite selectedInputContainer;
    private TileEntityComposite selectedOutputContainer;

    private static readonly Vector3i[] NeighborOffsets =
    {
        Vector3i.forward,
        Vector3i.back,
        Vector3i.left,
        Vector3i.right,
        Vector3i.up,
        Vector3i.down
    };

    public TileEntityFluidInfuser(Chunk chunk) : base(chunk)
    {
    }

    public string MachineRecipeGroupsCsv
    {
        get => string.IsNullOrWhiteSpace(machineRecipeGroupsCsv) ? DefaultMachineRecipeGroup : machineRecipeGroupsCsv;
        set => machineRecipeGroupsCsv = string.IsNullOrWhiteSpace(value) ? DefaultMachineRecipeGroup : value.Trim();
    }

    private void DevLog(string msg)
    {
        if (!IsDevLogging)
            return;

        Log.Out($"[FluidInfuser][TE][{ToWorldPos()}] {msg}");
    }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.FluidInfuser);
    }

    public override IHLRSnapshot BuildHLRSnapshot(WorldBase world)
    {
        EnsureConfigLoaded();

        ulong now = world?.GetWorldTime() ?? 0UL;
        Dictionary<string, int> pendingInputs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> kvp in pendingInputItems)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            pendingInputs[kvp.Key] = kvp.Value;
        }

        Dictionary<string, int> pendingOutputs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> kvp in pendingOutput)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            pendingOutputs[kvp.Key] = kvp.Value;
        }

        FluidInfuserSnapshot snapshot = new FluidInfuserSnapshot
        {
            MachineId = MachineGuid,
            Position = ToWorldPos(),
            WorldTime = now,
            LastHLRSimTime = now,
            IsOn = IsOn,
            SelectedInputChestPos = SelectedInputChestPos,
            SelectedInputPipeGraphId = SelectedInputPipeGraphId,
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
            PendingInputs = pendingInputs,
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
            snapshot.SelectedInputPipeGraphId,
            snapshot.SelectedInputChestPos,
            out snapshot.SelectedInputPipeAnchorPos);
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
        if (!(snapshotObj is FluidInfuserSnapshot snapshot))
            return;

        IsOn = snapshot.IsOn;
        SelectedInputChestPos = snapshot.SelectedInputChestPos;
        SelectedInputPipeGraphId = snapshot.SelectedInputPipeGraphId;
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
        MachineRecipeGroupsCsv = string.IsNullOrWhiteSpace(snapshot.MachineRecipeGroupsCsv)
            ? DefaultMachineRecipeGroup
            : snapshot.MachineRecipeGroupsCsv;
        pendingInputItems.Clear();
        if (snapshot.PendingInputs != null)
        {
            foreach (KeyValuePair<string, int> kvp in snapshot.PendingInputs)
            {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                    continue;

                pendingInputItems[kvp.Key] = kvp.Value;
            }
        }

        pendingFluidInputType = snapshot.PendingFluidInputType ?? string.Empty;
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

        configLoaded = false;
        EnsureConfigLoaded();
        ResolveSelectedInputContainer();
        ResolveSelectedOutputContainer();

        simulatedByHLR = false;
        NeedsUiRefresh = true;

        World currentWorld = GameManager.Instance?.World;
        if (currentWorld != null && !currentWorld.IsRemote())
            setModified();
    }

    public override void SetSimulatedByHLR(bool value)
    {
        simulatedByHLR = value;
    }

    protected override void OnPowerStateChanged(bool state)
    {
        if (!state)
        {
            LastAction = "Off";
            LastBlockReason = string.Empty;
            return;
        }

        if (IsProcessing)
            LastAction = "Processing";
        else if (pendingOutput.Count > 0)
            LastAction = "Output blocked";
        else
            LastAction = "Idle";
    }

    public List<MachineRecipe> GetAvailableRecipes()
    {
        EnsureConfigLoaded();
        return MachineRecipeRegistry.GetRecipesForMachineGroups(MachineRecipeGroupsCsv, true);
    }

    public string GetSelectedRecipeDisplayName()
    {
        EnsureConfigLoaded();
        return TryGetSelectedRule(out FluidInfusionRule rule) && rule != null
            ? GetRecipeDisplayName(rule)
            : "None";
    }

    public string GetSelectedFluidDisplayName()
    {
        EnsureConfigLoaded();
        return TryGetSelectedRule(out FluidInfusionRule rule) && rule != null
            ? ToFluidDisplayName(rule.FluidType)
            : "None";
    }

    public string GetCycleTimerText()
    {
        return $"{CycleTickCounter}/{Math.Max(1, CycleTickLength)}";
    }

    public string GetSelectedRecipeDetails()
    {
        EnsureConfigLoaded();
        if (!TryGetSelectedRule(out FluidInfusionRule rule) || rule == null)
            return "Select a recipe.";

        List<string> inputParts = new List<string>();
        for (int i = 0; i < rule.ItemInputs.Count; i++)
        {
            MachineRecipeInput input = rule.ItemInputs[i];
            if (input == null)
                continue;

            inputParts.Add($"{GetItemDisplayName(input.ItemName)} x{input.Count}");
        }

        List<string> outputParts = new List<string>();
        for (int i = 0; i < rule.ItemOutputs.Count; i++)
        {
            MachineRecipeItemOutput output = rule.ItemOutputs[i];
            if (output == null)
                continue;

            outputParts.Add($"{GetItemDisplayName(output.ItemName)} x{output.Count}");
        }

        string inputs = inputParts.Count > 0 ? string.Join(", ", inputParts.ToArray()) : "None";
        string outputs = outputParts.Count > 0 ? string.Join(", ", outputParts.ToArray()) : "None";
        return $"Inputs: {inputs}\nFluid: {ToFluidDisplayName(rule.FluidType)} {FormatGallons(rule.FluidAmountMg)} gal\nOutputs: {outputs}\nTime: {rule.CraftTimeTicks} ticks";
    }

    public string GetPendingInputItemName()
    {
        if (TryGetActiveRule(out FluidInfusionRule rule) && rule?.ItemInputs != null)
        {
            for (int i = 0; i < rule.ItemInputs.Count; i++)
            {
                MachineRecipeInput input = rule.ItemInputs[i];
                if (input == null || string.IsNullOrEmpty(input.ItemName))
                    continue;

                if (pendingInputItems.TryGetValue(input.ItemName, out int count) && count > 0)
                    return input.ItemName;
            }
        }

        foreach (KeyValuePair<string, int> kvp in pendingInputItems)
        {
            if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value > 0)
                return kvp.Key;
        }

        return string.Empty;
    }

    public int GetPendingInputItemCount()
    {
        if (TryGetActiveRule(out FluidInfusionRule rule) && rule?.ItemInputs != null)
        {
            for (int i = 0; i < rule.ItemInputs.Count; i++)
            {
                MachineRecipeInput input = rule.ItemInputs[i];
                if (input == null || string.IsNullOrEmpty(input.ItemName))
                    continue;

                if (pendingInputItems.TryGetValue(input.ItemName, out int count) && count > 0)
                    return count;
            }
        }

        foreach (KeyValuePair<string, int> kvp in pendingInputItems)
        {
            if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value > 0)
                return kvp.Value;
        }

        return 0;
    }

    public string GetPendingOutputItemName()
    {
        if (pendingOutput == null || pendingOutput.Count == 0)
            return string.Empty;

        foreach (KeyValuePair<string, int> kvp in pendingOutput)
        {
            if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value > 0)
                return kvp.Key;
        }

        return string.Empty;
    }

    public int GetPendingOutputItemCount()
    {
        if (pendingOutput == null || pendingOutput.Count == 0)
            return 0;

        foreach (KeyValuePair<string, int> kvp in pendingOutput)
        {
            if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value > 0)
                return kvp.Value;
        }

        return 0;
    }

    public string GetPendingFluidInputType()
    {
        return pendingFluidInputType ?? string.Empty;
    }

    public int GetPendingFluidInputAmountMg()
    {
        return Math.Max(0, pendingFluidInputAmountMg);
    }

    public bool HasSelectedRecipe()
    {
        EnsureConfigLoaded();
        return TryGetSelectedRule(out _);
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
            RefreshAvailableInputTargets(world);
            RefreshAvailableOutputTargets(world);
            ResolveSelectedInputContainer();
            ResolveSelectedOutputContainer();
            ResolveFluidInputGraph(world);
        }

        TryFlushPendingOutput(world);

        if (!IsOn)
        {
            LastAction = "Off";
            LastBlockReason = string.Empty;
            return;
        }

        if (!IsProcessing)
        {
            if (!AreAllRequirementsMet(world, out string blockedReason))
            {
                LastAction = "Waiting";
                LastBlockReason = blockedReason;
                return;
            }

            if (pendingOutput != null && pendingOutput.Count > 0)
            {
                LastAction = "Waiting";
                LastBlockReason = "Output blocked";
                return;
            }

            if (!TryBeginCycle(world, out blockedReason))
            {
                LastAction = "Waiting";
                LastBlockReason = blockedReason;
                return;
            }

            return;
        }

        CycleTickCounter++;
        LastAction = "Processing";
        LastBlockReason = string.Empty;
        NeedsUiRefresh = true;

        if (CycleTickCounter < Math.Max(1, CycleTickLength))
            return;

        CompleteCycle();
        setModified();
    }

    public bool ServerSelectRecipe(string recipeKey)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            return false;

        EnsureConfigLoaded();

        if (!TryGetRule(recipeKey, out FluidInfusionRule rule) || rule == null)
            return false;

        if (IsProcessing || (pendingOutput != null && pendingOutput.Count > 0))
            return false;

        if (string.Equals(SelectedRecipeKey, recipeKey, StringComparison.Ordinal))
            return false;

        SelectedRecipeKey = recipeKey ?? string.Empty;
        SelectedFluidType = rule.FluidType ?? string.Empty;
        SelectedFluidGraphId = Guid.Empty;
        CycleTickLength = Math.Max(1, rule.CraftTimeTicks);
        CycleTickCounter = 0;
        ActiveRecipeKey = string.Empty;
        LastAction = "Recipe Applied";
        LastBlockReason = string.Empty;
        MarkDirty();
        return true;
    }

    public bool ServerSelectInputContainer(Vector3i chestPos, string pipeGraphId)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            return false;

        Guid parsedPipeGraphId = Guid.Empty;
        if (!string.IsNullOrEmpty(pipeGraphId))
            Guid.TryParse(pipeGraphId, out parsedPipeGraphId);

        bool changed = SelectedInputChestPos != chestPos || SelectedInputPipeGraphId != parsedPipeGraphId;
        if (chestPos == Vector3i.zero)
        {
            SelectedInputChestPos = Vector3i.zero;
            SelectedInputPipeGraphId = Guid.Empty;
            selectedInputContainer = null;
            if (changed)
                MarkDirty();
            return true;
        }

        WorldBase world = GameManager.Instance?.World;
        if (world == null)
            return false;

        RefreshAvailableInputTargets(world);
        for (int i = 0; i < availableInputTargets.Count; i++)
        {
            InputTargetInfo target = availableInputTargets[i];
            if (target == null || target.BlockPos != chestPos || target.PipeGraphId != parsedPipeGraphId)
                continue;

            TileEntityComposite comp = world.GetTileEntity(chestPos) as TileEntityComposite;
            if (comp == null)
                return false;

            SelectedInputChestPos = chestPos;
            SelectedInputPipeGraphId = parsedPipeGraphId;
            selectedInputContainer = comp;
            if (changed)
                MarkDirty();
            return true;
        }

        return false;
    }

    public bool ServerSelectOutputContainer(Vector3i chestPos, OutputTransportMode mode, string pipeGraphId)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            return false;

        Guid parsedPipeGraphId = Guid.Empty;
        if (!string.IsNullOrEmpty(pipeGraphId))
            Guid.TryParse(pipeGraphId, out parsedPipeGraphId);

        bool changed =
            SelectedOutputChestPos != chestPos ||
            SelectedOutputMode != mode ||
            SelectedOutputPipeGraphId != parsedPipeGraphId;

        if (chestPos == Vector3i.zero)
        {
            SelectedOutputChestPos = Vector3i.zero;
            SelectedOutputMode = OutputTransportMode.Adjacent;
            SelectedOutputPipeGraphId = Guid.Empty;
            selectedOutputContainer = null;
            if (changed)
                MarkDirty();
            return true;
        }

        WorldBase world = GameManager.Instance?.World;
        if (world == null)
            return false;

        RefreshAvailableOutputTargets(world);
        for (int i = 0; i < availableOutputTargets.Count; i++)
        {
            OutputTargetInfo target = availableOutputTargets[i];
            if (target == null ||
                target.BlockPos != chestPos ||
                target.TransportMode != mode ||
                target.PipeGraphId != parsedPipeGraphId)
            {
                continue;
            }

            TileEntityComposite comp = world.GetTileEntity(chestPos) as TileEntityComposite;
            if (comp == null)
                return false;

            SelectedOutputChestPos = chestPos;
            SelectedOutputMode = mode;
            SelectedOutputPipeGraphId = parsedPipeGraphId;
            selectedOutputContainer = comp;
            if (changed)
                MarkDirty();
            return true;
        }

        return false;
    }

    public bool HasItemInputRequirement(WorldBase world)
    {
        if (world == null || !HasSelectedRecipe())
            return false;

        if (IsProcessing || pendingInputItems.Count > 0)
            return true;

        if (!HasSelectedInputTarget(world))
            return false;

        if (world.IsRemote())
            return true;

        return HasAllRequiredItems(world, out _);
    }

    public bool HasItemOutputRequirement(WorldBase world)
    {
        return HasSelectedOutputTarget(world);
    }

    public bool HasFluidInputRequirement(WorldBase world)
    {
        if (world == null || !TryGetSelectedRule(out FluidInfusionRule rule) || rule == null)
            return false;

        if (IsProcessing || pendingFluidInputAmountMg > 0)
            return true;

        if (world.IsRemote())
            return SelectedFluidGraphId != Guid.Empty;

        return TryResolveFluidAvailability(world, rule, out _, out _);
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

        if (!HasSelectedInputTarget(world))
        {
            blockedReason = "Missing Item Input";
            return false;
        }

        if (!HasSelectedOutputTarget(world))
        {
            blockedReason = "Missing Item Output";
            return false;
        }

        if (!TryGetSelectedRule(out FluidInfusionRule rule) || rule == null)
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        if (!world.IsRemote() && !HasAllRequiredItems(world, out blockedReason))
            return false;

        if (!TryResolveFluidAvailability(world, rule, out Guid graphId, out blockedReason))
            return false;

        if (!world.IsRemote())
            SelectedFluidGraphId = graphId;

        return true;
    }

    public List<InputTargetInfo> GetAvailableInputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return availableInputTargets ?? new List<InputTargetInfo>();

        RefreshAvailableInputTargets(world);
        return availableInputTargets;
    }

    public void RefreshAvailableInputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        List<InputTargetInfo> discovered = DiscoverAvailableInputTargets(world);
        if (AreInputTargetsEqual(availableInputTargets, discovered))
            return;

        availableInputTargets = discovered;
        MarkDirty();
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

    public bool ResolveSelectedInputContainer()
    {
        WorldBase world = GameManager.Instance?.World;
        TileEntityComposite resolved = null;
        if (world != null && SelectedInputChestPos != Vector3i.zero)
            resolved = world.GetTileEntity(SelectedInputChestPos) as TileEntityComposite;

        if (ReferenceEquals(selectedInputContainer, resolved))
            return false;

        selectedInputContainer = resolved;
        return true;
    }

    public bool ResolveSelectedOutputContainer()
    {
        WorldBase world = GameManager.Instance?.World;
        TileEntityComposite resolved = null;
        if (world != null && SelectedOutputChestPos != Vector3i.zero)
            resolved = world.GetTileEntity(SelectedOutputChestPos) as TileEntityComposite;

        if (ReferenceEquals(selectedOutputContainer, resolved))
            return false;

        selectedOutputContainer = resolved;
        return true;
    }

    public bool ResolveFluidInputGraph(WorldBase world)
    {
        if (world == null || !TryGetSelectedRule(out FluidInfusionRule rule) || rule == null)
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

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        bw.Write(PersistVersion);
        bw.Write(IsOn);
        WriteInputTargets(bw);
        WriteOutputTargets(bw);

        bw.Write(SelectedInputChestPos.x);
        bw.Write(SelectedInputChestPos.y);
        bw.Write(SelectedInputChestPos.z);
        bw.Write(SelectedInputPipeGraphId.ToString());

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
        WritePendingInputs(bw);
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
            if (persistVersion >= 2)
                IsOn = br.ReadBoolean();
            else
                IsOn = false;

            ReadInputTargets(br);
            ReadOutputTargets(br);

            SelectedInputChestPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            if (!Guid.TryParse(br.ReadString(), out SelectedInputPipeGraphId))
                SelectedInputPipeGraphId = Guid.Empty;

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
            if (persistVersion >= 3)
            {
                ReadPendingInputs(br);
                pendingFluidInputType = br.ReadString() ?? string.Empty;
                pendingFluidInputAmountMg = Math.Max(0, br.ReadInt32());
            }
            else
            {
                ClearPendingInputs();
            }

            MachineRecipeGroupsCsv = br.ReadString() ?? DefaultMachineRecipeGroup;
            LastAction = br.ReadString() ?? string.Empty;
            LastBlockReason = br.ReadString() ?? string.Empty;

            configLoaded = false;
            EnsureConfigLoaded();
            ResolveSelectedInputContainer();
            ResolveSelectedOutputContainer();
        }
        catch (Exception ex)
        {
            Log.Error($"[FluidInfuser][READ] Failed to deserialize at {ToWorldPos()} mode={mode}: {ex.Message}");
            ResetToDefaults();
        }
    }

    private bool TryBeginCycle(WorldBase world, out string blockedReason)
    {
        blockedReason = string.Empty;

        if (!TryGetSelectedRule(out FluidInfusionRule rule) || rule == null)
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        Dictionary<string, int> itemRequest = BuildItemRequest(rule);
        if (!PipeGraphManager.TryConsumeStorageItems(world, 0, SelectedInputPipeGraphId, SelectedInputChestPos, itemRequest, out Dictionary<string, int> consumed) ||
            !DidConsumeAllRequested(itemRequest, consumed))
        {
            if (!TryBuildMissingItemReason(world, rule, out blockedReason))
                blockedReason = "Input items changed";
            return false;
        }

        if (!FluidGraphManager.TryConsumeFluid(world, 0, SelectedFluidGraphId, rule.FluidType, rule.FluidAmountMg, out int consumedMg) ||
            consumedMg < rule.FluidAmountMg)
        {
            if (consumed != null && consumed.Count > 0)
                PipeGraphManager.TryDepositStorageItems(world, 0, SelectedInputPipeGraphId, SelectedInputChestPos, consumed, out _);

            if (consumedMg > 0)
                FluidGraphManager.TryInjectFluid(world, 0, SelectedFluidGraphId, rule.FluidType, consumedMg, out _);

            blockedReason = $"Need {FormatGallons(rule.FluidAmountMg)} gal {ToFluidDisplayName(rule.FluidType)}";
            return false;
        }

        CapturePendingInputs(consumed, rule.FluidType, consumedMg);
        IsProcessing = true;
        CycleTickCounter = 0;
        CycleTickLength = Math.Max(1, rule.CraftTimeTicks);
        ActiveRecipeKey = rule.RecipeKey ?? string.Empty;
        LastAction = "Requested Input";
        LastBlockReason = string.Empty;
        MarkDirty();
        return true;
    }

    private void CompleteCycle()
    {
        if (TryGetRule(ActiveRecipeKey, out FluidInfusionRule rule) && rule != null)
        {
            for (int i = 0; i < rule.ItemOutputs.Count; i++)
            {
                MachineRecipeItemOutput output = rule.ItemOutputs[i];
                if (output == null || string.IsNullOrEmpty(output.ItemName) || output.Count <= 0)
                    continue;

                AddPendingOutput(output.ItemName, output.Count);
            }
        }

        ClearPendingInputs();
        IsProcessing = false;
        CycleTickCounter = 0;
        ActiveRecipeKey = string.Empty;
        LastAction = "Craft complete";
        LastBlockReason = string.Empty;
        NeedsUiRefresh = true;
    }

    private void TryFlushPendingOutput(WorldBase world)
    {
        if (world == null || pendingOutput == null || pendingOutput.Count == 0)
            return;

        foreach (KeyValuePair<string, int> kvp in new List<KeyValuePair<string, int>>(pendingOutput))
        {
            string itemName = kvp.Key;
            int count = kvp.Value;
            if (string.IsNullOrEmpty(itemName) || count <= 0)
            {
                pendingOutput.Remove(itemName);
                continue;
            }

            int moved = 0;
            string blockedReason = string.Empty;

            if (SelectedOutputMode == OutputTransportMode.Pipe)
            {
                Dictionary<string, int> request = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [itemName] = count
                };

                if (PipeGraphManager.TryDepositStorageItems(world, 0, SelectedOutputPipeGraphId, SelectedOutputChestPos, request, out Dictionary<string, int> deposited) &&
                    deposited != null &&
                    deposited.TryGetValue(itemName, out moved))
                {
                    blockedReason = string.Empty;
                }
                else
                {
                    blockedReason = "Output blocked";
                }
            }
            else
            {
                TryDepositToAdjacentOutput(world, itemName, count, out moved, out blockedReason);
            }

            if (moved <= 0)
            {
                LastBlockReason = string.IsNullOrEmpty(blockedReason) ? "Output blocked" : blockedReason;
                break;
            }

            int remaining = count - moved;
            if (remaining > 0)
                pendingOutput[itemName] = remaining;
            else
                pendingOutput.Remove(itemName);

            LastAction = "Output transferred";
            LastBlockReason = string.Empty;
            NeedsUiRefresh = true;
        }
    }

    private bool TryDepositToAdjacentOutput(WorldBase world, string itemName, int requestedCount, out int depositedCount, out string blockedReason)
    {
        depositedCount = 0;
        blockedReason = string.Empty;

        if (SelectedOutputChestPos == Vector3i.zero)
        {
            blockedReason = "Missing Item Output";
            return false;
        }

        Vector3i delta = SelectedOutputChestPos - ToWorldPos();
        if (Math.Abs(delta.x) + Math.Abs(delta.y) + Math.Abs(delta.z) != 1)
        {
            blockedReason = "Selected adjacent output is not next to machine";
            return false;
        }

        if (!(world.GetTileEntity(0, SelectedOutputChestPos) is TileEntityComposite comp))
        {
            blockedReason = "Output container unavailable";
            return false;
        }

        TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
        {
            blockedReason = "Output has no storage";
            return false;
        }

        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        if (itemValue == null || itemValue.type == ItemValue.None.type || itemValue.ItemClass == null)
        {
            blockedReason = "Invalid output item";
            return false;
        }

        int remaining = requestedCount;
        for (int i = 0; i < storage.items.Length && remaining > 0; i++)
        {
            ItemStack slot = storage.items[i];
            if (slot.IsEmpty() || slot.itemValue == null || slot.itemValue.type != itemValue.type)
                continue;

            int maxStack = Math.Max(1, slot.itemValue.ItemClass.Stacknumber.Value);
            int move = Math.Min(maxStack - slot.count, remaining);
            if (move <= 0)
                continue;

            slot.count += move;
            storage.items[i] = slot;
            remaining -= move;
        }

        for (int i = 0; i < storage.items.Length && remaining > 0; i++)
        {
            if (!storage.items[i].IsEmpty())
                continue;

            int maxStack = Math.Max(1, itemValue.ItemClass.Stacknumber.Value);
            int move = Math.Min(maxStack, remaining);
            storage.items[i] = new ItemStack(itemValue.Clone(), move);
            remaining -= move;
        }

        depositedCount = requestedCount - remaining;
        if (depositedCount > 0)
            storage.SetModified();

        if (depositedCount <= 0)
        {
            blockedReason = "Output blocked";
            return false;
        }

        return true;
    }

    private bool HasAllRequiredItems(WorldBase world, out string blockedReason)
    {
        blockedReason = string.Empty;
        if (!TryGetSelectedRule(out FluidInfusionRule rule) || rule == null)
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        return !TryBuildMissingItemReason(world, rule, out blockedReason);
    }

    private bool TryResolveFluidAvailability(WorldBase world, FluidInfusionRule rule, out Guid graphId, out string blockedReason)
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

        if (!FluidGraphManager.TryGetAvailableFluidAmount(world, 0, graphId, rule.FluidType, out int availableMg) ||
            availableMg < rule.FluidAmountMg)
        {
            blockedReason = $"Need {FormatGallons(rule.FluidAmountMg)} gal {ToFluidDisplayName(rule.FluidType)}";
            return false;
        }

        return true;
    }

    private bool HasSelectedInputTarget(WorldBase world)
    {
        if (world == null || SelectedInputChestPos == Vector3i.zero)
            return false;

        List<InputTargetInfo> targets = GetAvailableInputTargets(world);
        if (targets == null || targets.Count == 0)
            return false;

        bool foundByPosition = false;
        Guid reboundGraphId = Guid.Empty;

        for (int i = 0; i < targets.Count; i++)
        {
            InputTargetInfo target = targets[i];
            if (target == null)
                continue;

            if (target.BlockPos != SelectedInputChestPos)
                continue;

            if (target.PipeGraphId == SelectedInputPipeGraphId)
                return true;

            if (!foundByPosition)
            {
                foundByPosition = true;
                reboundGraphId = target.PipeGraphId;
            }
        }

        if (!foundByPosition)
            return false;

        if (!world.IsRemote() && SelectedInputPipeGraphId != reboundGraphId)
            SelectedInputPipeGraphId = reboundGraphId;

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

    private void EnsureConfigLoaded()
    {
        if (configLoaded)
            return;

        configLoaded = true;
        recipes.Clear();
        MachineRecipeGroupsCsv = GetMachineRecipeGroupsCsv();

        int defaultCycleTickLength = ReadIntProperty("InputSpeed", 20, 1, 2000);
        List<MachineRecipe> machineRecipes = MachineRecipeRegistry.GetRecipesForMachineGroups(MachineRecipeGroupsCsv, true);
        for (int i = 0; i < machineRecipes.Count; i++)
        {
            MachineRecipe recipe = machineRecipes[i];
            if (!TryReadMachineRecipeAsInfuserRule(
                    recipe,
                    defaultCycleTickLength,
                    out List<MachineRecipeInput> itemInputs,
                    out string fluidType,
                    out int fluidAmountMg,
                    out List<MachineRecipeItemOutput> itemOutputs,
                    out int craftTimeTicks,
                    out string error))
            {
                DevLog($"Skipped infuser recipe '{recipe?.NormalizedKey}': {error}");
                continue;
            }

            recipes.Add(new FluidInfusionRule
            {
                RecipeKey = recipe.NormalizedKey ?? string.Empty,
                DisplayName = recipe.Name ?? string.Empty,
                ItemInputs = itemInputs,
                FluidType = fluidType,
                FluidAmountMg = fluidAmountMg,
                ItemOutputs = itemOutputs,
                CraftTimeTicks = craftTimeTicks
            });
        }

        if (!TryGetSelectedRule(out FluidInfusionRule rule))
        {
            if (recipes.Count > 0)
            {
                rule = recipes[0];
                SelectedRecipeKey = rule.RecipeKey ?? string.Empty;
            }
            else
            {
                SelectedRecipeKey = string.Empty;
            }
        }

        if (rule != null)
        {
            SelectedFluidType = rule.FluidType ?? string.Empty;
            CycleTickLength = Math.Max(1, rule.CraftTimeTicks);
        }
        else
        {
            SelectedFluidType = string.Empty;
            CycleTickLength = defaultCycleTickLength;
        }
    }

    public static bool TryReadMachineRecipeAsInfuserRule(
        MachineRecipe recipe,
        int defaultCraftTimeTicks,
        out List<MachineRecipeInput> itemInputs,
        out string fluidType,
        out int fluidAmountMg,
        out List<MachineRecipeItemOutput> itemOutputs,
        out int craftTimeTicks,
        out string error)
    {
        itemInputs = new List<MachineRecipeInput>();
        fluidType = string.Empty;
        fluidAmountMg = 0;
        itemOutputs = new List<MachineRecipeItemOutput>();
        craftTimeTicks = Math.Max(1, defaultCraftTimeTicks);
        error = string.Empty;

        if (recipe == null)
        {
            error = "Recipe is null";
            return false;
        }

        if (recipe.Inputs == null || recipe.Inputs.Count <= 0)
        {
            error = "Infuser recipe requires at least one <input>";
            return false;
        }

        if (recipe.FluidInputs == null || recipe.FluidInputs.Count != 1)
        {
            error = "Infuser recipe requires exactly one <fluid_input>";
            return false;
        }

        if (recipe.ItemOutputs == null || recipe.ItemOutputs.Count <= 0)
        {
            error = "Infuser recipe requires at least one <output>";
            return false;
        }

        if (recipe.FluidOutputs != null && recipe.FluidOutputs.Count > 0)
        {
            error = "Infuser recipe does not support <fluid_output>";
            return false;
        }

        if (recipe.GasOutputs != null && recipe.GasOutputs.Count > 0)
        {
            error = "Infuser recipe does not support <gas_output>";
            return false;
        }

        for (int i = 0; i < recipe.Inputs.Count; i++)
        {
            MachineRecipeInput input = recipe.Inputs[i];
            if (input == null || string.IsNullOrWhiteSpace(input.ItemName) || input.Count <= 0)
            {
                error = "Infuser recipe has an invalid item input";
                return false;
            }

            itemInputs.Add(new MachineRecipeInput(input.ItemName.Trim(), input.Count));
        }

        MachineRecipeFluidInput fluidInput = recipe.FluidInputs[0];
        if (fluidInput == null || string.IsNullOrWhiteSpace(fluidInput.Type) || fluidInput.AmountMg <= 0)
        {
            error = "Infuser recipe has an invalid fluid input";
            return false;
        }

        fluidType = fluidInput.Type.Trim().ToLowerInvariant();
        fluidAmountMg = Math.Max(1, fluidInput.AmountMg);

        for (int i = 0; i < recipe.ItemOutputs.Count; i++)
        {
            MachineRecipeItemOutput output = recipe.ItemOutputs[i];
            if (output == null || string.IsNullOrWhiteSpace(output.ItemName) || output.Count <= 0)
            {
                error = "Infuser recipe has an invalid output";
                return false;
            }

            itemOutputs.Add(new MachineRecipeItemOutput(output.ItemName.Trim(), output.Count));
        }

        craftTimeTicks = recipe.CraftTimeTicks.HasValue
            ? Math.Max(1, recipe.CraftTimeTicks.Value)
            : Math.Max(1, defaultCraftTimeTicks);

        return true;
    }

    private bool TryGetSelectedRule(out FluidInfusionRule rule)
    {
        return TryGetRule(SelectedRecipeKey, out rule);
    }

    private bool TryGetActiveRule(out FluidInfusionRule rule)
    {
        string activeKey = !string.IsNullOrEmpty(ActiveRecipeKey) ? ActiveRecipeKey : SelectedRecipeKey;
        return TryGetRule(activeKey, out rule);
    }

    private bool TryGetRule(string recipeKey, out FluidInfusionRule rule)
    {
        rule = null;
        EnsureConfigLoaded();

        if (string.IsNullOrEmpty(recipeKey))
            return false;

        for (int i = 0; i < recipes.Count; i++)
        {
            FluidInfusionRule candidate = recipes[i];
            if (candidate == null || !string.Equals(candidate.RecipeKey, recipeKey, StringComparison.Ordinal))
                continue;

            rule = candidate;
            return true;
        }

        return false;
    }

    private string GetMachineRecipeGroupsCsv()
    {
        string configured = blockValue.Block?.Properties?.GetString("MachineRecipes");
        return string.IsNullOrWhiteSpace(configured) ? DefaultMachineRecipeGroup : configured.Trim();
    }

    private List<InputTargetInfo> DiscoverAvailableInputTargets(WorldBase world)
    {
        List<InputTargetInfo> results = new List<InputTargetInfo>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        Vector3i machinePos = ToWorldPos();

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i pipePos = machinePos + NeighborOffsets[i];
            TileEntityItemPipe pipeTe = world.GetTileEntity(0, pipePos) as TileEntityItemPipe;
            if (pipeTe == null || pipeTe.PipeGraphId == Guid.Empty)
                continue;

            if (!PipeGraphManager.TryGetStorageEndpoints(pipeTe.PipeGraphId, out List<Vector3i> storageEndpoints) ||
                storageEndpoints == null ||
                storageEndpoints.Count == 0)
            {
                continue;
            }

            for (int j = 0; j < storageEndpoints.Count; j++)
            {
                Vector3i storagePos = storageEndpoints[j];
                string key = $"{storagePos}|{pipeTe.PipeGraphId}";
                if (!seen.Add(key))
                    continue;

                if (!(world.GetTileEntity(0, storagePos) is TileEntityComposite))
                    continue;

                results.Add(new InputTargetInfo(storagePos, pipeTe.PipeGraphId));
            }
        }

        return results;
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
            Vector3i pipePos = machinePos + NeighborOffsets[i];
            TileEntityLiquidPipe pipe = world.GetTileEntity(0, pipePos) as TileEntityLiquidPipe;
            if (pipe == null)
                continue;

            Guid candidateId = pipe.FluidGraphId;
            if (candidateId == Guid.Empty && !world.IsRemote())
            {
                if (FluidGraphManager.TryEnsureGraphForPipe(world, 0, pipePos, out FluidGraphData rebuilt) && rebuilt != null)
                    candidateId = rebuilt.FluidGraphId;
            }

            if (candidateId != Guid.Empty && !candidates.Contains(candidateId))
                candidates.Add(candidateId);
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            Guid candidateId = candidates[i];
            if (!FluidGraphManager.TryGetGraph(candidateId, out FluidGraphData graph) || graph == null)
                continue;

            if (!string.IsNullOrEmpty(graph.FluidType) && !string.Equals(graph.FluidType, normalized, StringComparison.Ordinal))
                continue;

            if (!GraphHasActivePump(world, candidateId))
                continue;

            graphId = candidateId;
            return true;
        }

        return false;
    }

    private static bool GraphHasActivePump(WorldBase world, Guid graphId)
    {
        if (world == null || graphId == Guid.Empty)
            return false;

        if (!FluidGraphManager.TryGetGraph(graphId, out FluidGraphData graph) || graph == null || graph.PumpEndpoints == null)
            return false;

        foreach (Vector3i pumpPos in graph.PumpEndpoints)
        {
            if (SafeWorldRead.TryGetTileEntity(world, 0, pumpPos, out TileEntity te) && te is TileEntityFluidPump livePump)
            {
                if (livePump.IsActivePump() && livePump.GetOutputCapMgPerTick() > 0)
                    return true;
            }

            if (graph.TryGetPumpSnapshot(pumpPos, out FluidGraphData.PumpSnapshot snapshot) &&
                snapshot != null &&
                snapshot.PumpEnabled &&
                snapshot.OutputCapMgPerTick > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void CapturePendingInputs(Dictionary<string, int> consumedItems, string fluidType, int consumedFluidMg)
    {
        pendingInputItems.Clear();

        if (consumedItems != null)
        {
            foreach (KeyValuePair<string, int> kvp in consumedItems)
            {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                    continue;

                pendingInputItems[kvp.Key] = kvp.Value;
            }
        }

        pendingFluidInputType = fluidType ?? string.Empty;
        pendingFluidInputAmountMg = Math.Max(0, consumedFluidMg);
    }

    private void ClearPendingInputs()
    {
        pendingInputItems.Clear();
        pendingFluidInputType = string.Empty;
        pendingFluidInputAmountMg = 0;
    }

    private void WritePendingInputs(PooledBinaryWriter bw)
    {
        int count = 0;
        foreach (KeyValuePair<string, int> kvp in pendingInputItems)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            count++;
        }

        count = Math.Min(MaxSerializedPendingInputs, count);
        bw.Write(count);

        int written = 0;
        foreach (KeyValuePair<string, int> kvp in pendingInputItems)
        {
            if (written >= count)
                break;

            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            bw.Write(kvp.Key);
            bw.Write(kvp.Value);
            written++;
        }
    }

    private void ReadPendingInputs(PooledBinaryReader br)
    {
        pendingInputItems.Clear();

        int count = br.ReadInt32();
        if (count < 0 || count > MaxSerializedPendingInputs)
            throw new InvalidOperationException($"Invalid pending input count: {count}");

        for (int i = 0; i < count; i++)
        {
            string itemName = br.ReadString() ?? string.Empty;
            int itemCount = Math.Max(0, br.ReadInt32());
            if (string.IsNullOrEmpty(itemName) || itemCount <= 0)
                continue;

            pendingInputItems[itemName] = itemCount;
        }
    }

    private void WriteInputTargets(PooledBinaryWriter bw)
    {
        List<InputTargetInfo> targets = new List<InputTargetInfo>(availableInputTargets ?? new List<InputTargetInfo>());
        bw.Write(targets.Count);
        for (int i = 0; i < targets.Count; i++)
        {
            InputTargetInfo target = targets[i];
            bw.Write(target?.BlockPos.x ?? 0);
            bw.Write(target?.BlockPos.y ?? 0);
            bw.Write(target?.BlockPos.z ?? 0);
            bw.Write((target?.PipeGraphId ?? Guid.Empty).ToString());
        }
    }

    private void WriteOutputTargets(PooledBinaryWriter bw)
    {
        List<OutputTargetInfo> targets = new List<OutputTargetInfo>(availableOutputTargets ?? new List<OutputTargetInfo>());
        bw.Write(targets.Count);
        for (int i = 0; i < targets.Count; i++)
        {
            OutputTargetInfo target = targets[i];
            bw.Write(target?.BlockPos.x ?? 0);
            bw.Write(target?.BlockPos.y ?? 0);
            bw.Write(target?.BlockPos.z ?? 0);
            bw.Write((int)(target?.TransportMode ?? OutputTransportMode.Adjacent));
            bw.Write((target?.PipeGraphId ?? Guid.Empty).ToString());
        }
    }

    private void ReadInputTargets(PooledBinaryReader br)
    {
        int count = br.ReadInt32();
        if (count < 0 || count > MaxSerializedInputTargets)
            throw new InvalidOperationException($"Invalid input target count: {count}");

        availableInputTargets = new List<InputTargetInfo>(count);
        for (int i = 0; i < count; i++)
        {
            Vector3i pos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            Guid graphId;
            if (!Guid.TryParse(br.ReadString(), out graphId))
                graphId = Guid.Empty;

            availableInputTargets.Add(new InputTargetInfo(pos, graphId));
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
            OutputTransportMode modeValue = (OutputTransportMode)br.ReadInt32();
            Guid graphId;
            if (!Guid.TryParse(br.ReadString(), out graphId))
                graphId = Guid.Empty;

            availableOutputTargets.Add(new OutputTargetInfo(pos, modeValue, graphId));
        }
    }

    private void MarkDirty()
    {
        NeedsUiRefresh = true;
        setModified();
    }

    private void ResetToDefaults()
    {
        availableInputTargets = new List<InputTargetInfo>();
        availableOutputTargets = new List<OutputTargetInfo>();
        ClearPendingOutput();
        SelectedInputChestPos = Vector3i.zero;
        SelectedInputPipeGraphId = Guid.Empty;
        SelectedOutputChestPos = Vector3i.zero;
        SelectedOutputMode = OutputTransportMode.Adjacent;
        SelectedOutputPipeGraphId = Guid.Empty;
        SelectedRecipeKey = string.Empty;
        SelectedFluidType = string.Empty;
        SelectedFluidGraphId = Guid.Empty;
        ClearPendingInputs();
        IsProcessing = false;
        CycleTickCounter = 0;
        CycleTickLength = 20;
        ActiveRecipeKey = string.Empty;
        MachineRecipeGroupsCsv = DefaultMachineRecipeGroup;
        LastAction = "Idle";
        LastBlockReason = string.Empty;
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

    private static Dictionary<string, int> BuildItemRequest(FluidInfusionRule rule)
    {
        Dictionary<string, int> request = new Dictionary<string, int>(StringComparer.Ordinal);
        if (rule?.ItemInputs == null)
            return request;

        for (int i = 0; i < rule.ItemInputs.Count; i++)
        {
            MachineRecipeInput input = rule.ItemInputs[i];
            if (input == null || string.IsNullOrEmpty(input.ItemName) || input.Count <= 0)
                continue;

            request[input.ItemName] = input.Count;
        }

        return request;
    }

    private static bool DidConsumeAllRequested(Dictionary<string, int> requested, Dictionary<string, int> consumed)
    {
        if (requested == null || consumed == null)
            return false;

        foreach (KeyValuePair<string, int> kvp in requested)
        {
            if (!consumed.TryGetValue(kvp.Key, out int count) || count < kvp.Value)
                return false;
        }

        return true;
    }

    private static bool AreInputTargetsEqual(List<InputTargetInfo> left, List<InputTargetInfo> right)
    {
        int leftCount = left?.Count ?? 0;
        int rightCount = right?.Count ?? 0;
        if (leftCount != rightCount)
            return false;

        for (int i = 0; i < leftCount; i++)
        {
            InputTargetInfo l = left[i];
            InputTargetInfo r = right[i];
            if (l == null || r == null)
            {
                if (!ReferenceEquals(l, r))
                    return false;
                continue;
            }

            if (l.BlockPos != r.BlockPos || l.PipeGraphId != r.PipeGraphId)
                return false;
        }

        return true;
    }

    private static bool AreOutputTargetsEqual(List<OutputTargetInfo> left, List<OutputTargetInfo> right)
    {
        int leftCount = left?.Count ?? 0;
        int rightCount = right?.Count ?? 0;
        if (leftCount != rightCount)
            return false;

        for (int i = 0; i < leftCount; i++)
        {
            OutputTargetInfo l = left[i];
            OutputTargetInfo r = right[i];
            if (l == null || r == null)
            {
                if (!ReferenceEquals(l, r))
                    return false;
                continue;
            }

            if (l.BlockPos != r.BlockPos ||
                l.TransportMode != r.TransportMode ||
                l.PipeGraphId != r.PipeGraphId)
            {
                return false;
            }
        }

        return true;
    }

    private static string GetRecipeDisplayName(FluidInfusionRule rule)
    {
        if (rule == null)
            return "None";

        if (!string.IsNullOrWhiteSpace(rule.DisplayName))
            return Localization.Get(rule.DisplayName);

        if (rule.ItemOutputs != null && rule.ItemOutputs.Count > 0)
            return GetItemDisplayName(rule.ItemOutputs[0].ItemName);

        return rule.RecipeKey ?? "None";
    }

    private static string GetItemDisplayName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return string.Empty;

        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        return itemValue?.ItemClass != null ? itemValue.ItemClass.GetLocalizedItemName() : itemName;
    }

    private static string ToFluidDisplayName(string fluidType)
    {
        if (string.IsNullOrWhiteSpace(fluidType))
            return string.Empty;

        string normalized = fluidType.Trim().Replace('_', ' ');
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string FormatGallons(int milliGallons)
    {
        double gallons = milliGallons / (double)FluidConstants.MilliGallonsPerGallon;
        return gallons.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private bool TryBuildMissingItemReason(WorldBase world, FluidInfusionRule rule, out string blockedReason)
    {
        blockedReason = string.Empty;

        if (rule == null)
        {
            blockedReason = "Selected recipe unavailable";
            return true;
        }

        if (!PipeGraphManager.TryGetStorageItemCounts(world, 0, ref SelectedInputPipeGraphId, SelectedInputChestPos, out Dictionary<string, int> itemCounts) ||
            itemCounts == null)
        {
            blockedReason = "Input storage unavailable";
            return true;
        }

        for (int i = 0; i < rule.ItemInputs.Count; i++)
        {
            MachineRecipeInput input = rule.ItemInputs[i];
            if (input == null)
                continue;

            int available = 0;
            if (itemCounts.TryGetValue(input.ItemName, out int count))
                available = Math.Max(0, count);

            if (available >= input.Count)
                continue;

            blockedReason = $"Need {input.Count}x {GetItemDisplayName(input.ItemName)} (have {available})";
            return true;
        }

        return false;
    }
}
