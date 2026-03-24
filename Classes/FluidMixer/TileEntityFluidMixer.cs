using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class TileEntityFluidMixer : TileEntityMachine
{
    private const int PersistVersion = 1;
    private const string DefaultMachineRecipeGroup = "fluid_mixer";

    private static readonly Vector3i[] NeighborOffsets =
    {
        Vector3i.forward,
        Vector3i.back,
        Vector3i.left,
        Vector3i.right,
        Vector3i.up,
        Vector3i.down
    };

    private sealed class FluidMixerRule
    {
        public string RecipeKey;
        public string DisplayName;
        public string InputAType;
        public int InputAAmountMg;
        public string InputBType;
        public int InputBAmountMg;
        public string OutputType;
        public int OutputAmountMg;
        public int CraftTimeTicks;
    }

    public string SelectedRecipeKey = string.Empty;
    public string SelectedFluidType = string.Empty;
    public Guid SelectedFluidGraphId = Guid.Empty;

    public bool IsProcessing;
    public int CycleTickCounter;
    public int CycleTickLength = 20;
    public string ActiveRecipeKey = string.Empty;

    public string PendingFluidInputAType = string.Empty;
    public int PendingFluidInputAAmountMg;
    public string PendingFluidInputBType = string.Empty;
    public int PendingFluidInputBAmountMg;

    public string PendingFluidOutputType = string.Empty;
    public int pendingFluidOutput;
    public int pendingFluidOutputCapacityMg = 5000;

    public string LastAction = "Idle";
    public string LastBlockReason = string.Empty;

    private readonly List<FluidMixerRule> rules = new List<FluidMixerRule>();
    private string machineRecipeGroupsCsv = DefaultMachineRecipeGroup;
    private bool configLoaded;
    private int refreshTicker;
    private int lastStateSignature = int.MinValue;
    private ulong lastUiSyncWorldTime;

    public TileEntityFluidMixer(Chunk chunk) : base(chunk)
    {
    }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.FluidMixer);
    }

    protected override void OnPowerStateChanged(bool state)
    {
        if (!state)
        {
            IsProcessing = false;
            CycleTickCounter = 0;
            ActiveRecipeKey = string.Empty;
            PendingFluidInputAType = string.Empty;
            PendingFluidInputAAmountMg = 0;
            PendingFluidInputBType = string.Empty;
            PendingFluidInputBAmountMg = 0;
            LastAction = "Off";
            LastBlockReason = string.Empty;
        }
        else
        {
            LastAction = "Idle";
            LastBlockReason = string.Empty;
        }
    }

    public string GetSelectedRecipeDisplayName()
    {
        EnsureConfigLoaded();
        if (!TryGetSelectedRule(out FluidMixerRule rule) || rule == null)
            return "None";

        return string.IsNullOrEmpty(rule.OutputType) ? "None" : rule.OutputType;
    }

    public bool HasSelectedRecipe()
    {
        EnsureConfigLoaded();
        return TryGetSelectedRule(out _);
    }

    public bool HasFluidInputRequirement(WorldBase world)
    {
        if (world == null)
            return false;

        if (!TryGetSelectedRule(out FluidMixerRule rule) || rule == null)
            return false;

        if (IsProcessing || PendingFluidInputAAmountMg > 0 || PendingFluidInputBAmountMg > 0)
            return true;

        if (world.IsRemote())
            return true;

        return TryResolveInputGraph(world, rule.InputAType, out _, out _) &&
               TryResolveInputGraph(world, rule.InputBType, out _, out _);
    }

    public bool HasFluidOutputRequirement(WorldBase world)
    {
        EnsureConfigLoaded();

        if (world == null || string.IsNullOrEmpty(SelectedFluidType))
            return false;

        if (world.IsRemote())
            return SelectedFluidGraphId != Guid.Empty;

        return TryGetCompatibleFluidGraph(world, SelectedFluidType, out _);
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

        if (!TryGetSelectedRule(out FluidMixerRule rule) || rule == null)
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        if (pendingFluidOutput >= pendingFluidOutputCapacityMg)
        {
            blockedReason = "Pending fluid output full";
            return false;
        }

        if (!TryResolveInputGraph(world, rule.InputAType, out _, out blockedReason))
            return false;

        if (!TryResolveInputGraph(world, rule.InputBType, out _, out blockedReason))
            return false;

        if (!TryGetCompatibleFluidGraph(world, rule.OutputType, out Guid outputGraphId))
        {
            blockedReason = "Missing/Invalid Fluid Output";
            return false;
        }

        if (!world.IsRemote())
            SelectedFluidGraphId = outputGraphId;

        return true;
    }

    public bool ServerCycleRecipe(int direction)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            return false;

        EnsureConfigLoaded();
        if (rules.Count == 0)
            return false;

        if (IsProcessing)
            return false;

        int index = -1;
        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i] == null)
                continue;

            if (string.Equals(rules[i].RecipeKey, SelectedRecipeKey, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            index = 0;

        int step = direction < 0 ? -1 : 1;
        int next = (index + step + rules.Count) % rules.Count;
        FluidMixerRule nextRule = rules[next];
        if (nextRule == null)
            return false;

        if (string.Equals(nextRule.RecipeKey, SelectedRecipeKey, StringComparison.Ordinal))
            return false;

        SelectedRecipeKey = nextRule.RecipeKey ?? string.Empty;
        SelectedFluidType = nextRule.OutputType ?? string.Empty;
        CycleTickLength = Math.Max(1, nextRule.CraftTimeTicks);
        CycleTickCounter = 0;
        ActiveRecipeKey = string.Empty;
        PendingFluidOutputType = nextRule.OutputType ?? string.Empty;

        WorldBase world = GameManager.Instance?.World;
        if (world != null)
            ResolveFluidOutputGraph(world);

        MarkDirty();
        return true;
    }

    public bool ResolveFluidOutputGraph(WorldBase world)
    {
        EnsureConfigLoaded();

        Guid resolved = Guid.Empty;
        bool hasGraph = TryGetCompatibleFluidGraph(world, SelectedFluidType, out resolved);
        if (!hasGraph)
            resolved = Guid.Empty;

        if (SelectedFluidGraphId == resolved)
            return false;

        SelectedFluidGraphId = resolved;
        return true;
    }

    public override void UpdateTick(World world)
    {
        if (world == null || world.IsRemote() || IsSimulatingHLR())
            return;

        EnsureConfigLoaded();

        bool changed = false;
        refreshTicker++;
        if (refreshTicker >= 20)
        {
            refreshTicker = 0;
            changed |= ResolveFluidOutputGraph(world);
        }

        changed |= TryFlushPendingFluidOutput(world, out string flushBlockedReason);

        string nextAction = LastAction;
        string nextReason = string.Empty;

        if (!IsOn)
        {
            nextAction = "Off";
            nextReason = string.Empty;
            IsProcessing = false;
            CycleTickCounter = 0;
            ActiveRecipeKey = string.Empty;
            ClearPendingInputs();
        }
        else if (!IsProcessing)
        {
            if (!AreAllRequirementsMet(world, out string blockedReason))
            {
                nextAction = "Waiting";
                nextReason = blockedReason;
                CycleTickCounter = 0;
            }
            else if (!TryBeginCycle(world, out blockedReason))
            {
                nextAction = "Waiting";
                nextReason = blockedReason;
                CycleTickCounter = 0;
            }
            else
            {
                nextAction = "Mixing";
                nextReason = string.Empty;
                changed = true;
            }
        }
        else
        {
            CycleTickCounter++;
            nextAction = "Mixing";
            nextReason = string.Empty;

            if (CycleTickCounter >= Math.Max(1, CycleTickLength))
            {
                CompleteCycle();
                changed = true;
                nextAction = "Mix complete";
                nextReason = string.Empty;
            }
        }

        if (!string.IsNullOrEmpty(flushBlockedReason))
            nextReason = flushBlockedReason;

        if (!string.Equals(LastAction, nextAction, StringComparison.Ordinal))
        {
            LastAction = nextAction;
            changed = true;
        }

        if (!string.Equals(LastBlockReason, nextReason, StringComparison.Ordinal))
        {
            LastBlockReason = nextReason;
            changed = true;
        }

        int signature = BuildStateSignature();
        if (signature != lastStateSignature)
        {
            lastStateSignature = signature;
            changed = true;
        }

        ulong now = (ulong)world.worldTime;
        bool periodicUiSync = now >= lastUiSyncWorldTime + 20UL;
        if (periodicUiSync)
            lastUiSyncWorldTime = now;

        if (changed || periodicUiSync)
            MarkDirty();
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        bw.Write(PersistVersion);
        bw.Write(IsOn);
        bw.Write(SelectedRecipeKey ?? string.Empty);
        bw.Write(SelectedFluidType ?? string.Empty);
        bw.Write(SelectedFluidGraphId.ToString());

        bw.Write(IsProcessing);
        bw.Write(CycleTickCounter);
        bw.Write(CycleTickLength);
        bw.Write(ActiveRecipeKey ?? string.Empty);

        bw.Write(PendingFluidInputAType ?? string.Empty);
        bw.Write(PendingFluidInputAAmountMg);
        bw.Write(PendingFluidInputBType ?? string.Empty);
        bw.Write(PendingFluidInputBAmountMg);

        bw.Write(PendingFluidOutputType ?? string.Empty);
        bw.Write(pendingFluidOutput);
        bw.Write(pendingFluidOutputCapacityMg);

        bw.Write(machineRecipeGroupsCsv ?? DefaultMachineRecipeGroup);
        bw.Write(LastAction ?? string.Empty);
        bw.Write(LastBlockReason ?? string.Empty);
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);

        try
        {
            int version = br.ReadInt32();
            if (version < 1)
            {
                ResetState();
                return;
            }

            IsOn = br.ReadBoolean();
            SelectedRecipeKey = br.ReadString() ?? string.Empty;
            SelectedFluidType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!Guid.TryParse(br.ReadString(), out SelectedFluidGraphId))
                SelectedFluidGraphId = Guid.Empty;

            IsProcessing = br.ReadBoolean();
            CycleTickCounter = Math.Max(0, br.ReadInt32());
            CycleTickLength = Math.Max(1, br.ReadInt32());
            ActiveRecipeKey = br.ReadString() ?? string.Empty;

            PendingFluidInputAType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
            PendingFluidInputAAmountMg = Math.Max(0, br.ReadInt32());
            PendingFluidInputBType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
            PendingFluidInputBAmountMg = Math.Max(0, br.ReadInt32());

            PendingFluidOutputType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
            pendingFluidOutput = Math.Max(0, br.ReadInt32());
            pendingFluidOutputCapacityMg = Math.Max(0, br.ReadInt32());

            machineRecipeGroupsCsv = br.ReadString() ?? DefaultMachineRecipeGroup;
            LastAction = br.ReadString() ?? string.Empty;
            LastBlockReason = br.ReadString() ?? string.Empty;

            configLoaded = false;
            EnsureConfigLoaded();
            NeedsUiRefresh = true;
        }
        catch (Exception ex)
        {
            Log.Error($"[FluidMixer][READ] Failed at {ToWorldPos()} mode={mode}: {ex.Message}");
            ResetState();
        }
    }

    private bool TryBeginCycle(WorldBase world, out string blockedReason)
    {
        blockedReason = string.Empty;

        if (!TryGetSelectedRule(out FluidMixerRule rule) || rule == null)
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        if (!TryResolveInputGraph(world, rule.InputAType, out Guid graphA, out blockedReason))
            return false;

        if (!TryResolveInputGraph(world, rule.InputBType, out Guid graphB, out blockedReason))
            return false;

        if (!FluidGraphManager.TryConsumeFluid(world, 0, graphA, rule.InputAType, rule.InputAAmountMg, out int consumedA) ||
            consumedA < rule.InputAAmountMg)
        {
            blockedReason = $"Need {FormatGallons(rule.InputAAmountMg)} gal {ToFluidDisplayName(rule.InputAType)}";
            return false;
        }

        if (!FluidGraphManager.TryConsumeFluid(world, 0, graphB, rule.InputBType, rule.InputBAmountMg, out int consumedB) ||
            consumedB < rule.InputBAmountMg)
        {
            if (consumedA > 0)
                FluidGraphManager.TryInjectFluid(world, 0, graphA, rule.InputAType, consumedA, out _);

            blockedReason = $"Need {FormatGallons(rule.InputBAmountMg)} gal {ToFluidDisplayName(rule.InputBType)}";
            return false;
        }

        PendingFluidInputAType = rule.InputAType;
        PendingFluidInputAAmountMg = consumedA;
        PendingFluidInputBType = rule.InputBType;
        PendingFluidInputBAmountMg = consumedB;

        IsProcessing = true;
        CycleTickCounter = 0;
        CycleTickLength = Math.Max(1, rule.CraftTimeTicks);
        ActiveRecipeKey = rule.RecipeKey ?? string.Empty;
        LastAction = "Requested Inputs";
        LastBlockReason = string.Empty;
        return true;
    }

    private void CompleteCycle()
    {
        if (TryGetRule(ActiveRecipeKey, out FluidMixerRule rule) && rule != null)
        {
            PendingFluidOutputType = rule.OutputType ?? string.Empty;
            SelectedFluidType = PendingFluidOutputType;

            long total = (long)pendingFluidOutput + Math.Max(0, rule.OutputAmountMg);
            pendingFluidOutput = (int)Math.Min(Math.Max(0, pendingFluidOutputCapacityMg), total);
        }

        IsProcessing = false;
        CycleTickCounter = 0;
        ActiveRecipeKey = string.Empty;
        ClearPendingInputs();
        LastAction = "Mix complete";
        LastBlockReason = string.Empty;
    }

    private bool TryFlushPendingFluidOutput(WorldBase world, out string blockedReason)
    {
        blockedReason = string.Empty;

        if (pendingFluidOutput <= 0)
            return false;

        if (string.IsNullOrEmpty(PendingFluidOutputType))
        {
            pendingFluidOutput = 0;
            blockedReason = "Pending output invalid";
            return true;
        }

        if (SelectedFluidGraphId == Guid.Empty || !TryGetCompatibleFluidGraph(world, PendingFluidOutputType, out Guid graphId))
        {
            blockedReason = "Missing/Invalid Fluid Output";
            return false;
        }

        SelectedFluidGraphId = graphId;

        if (!TryInjectFluidPartial(world, PendingFluidOutputType, pendingFluidOutput, out int injectedMg, out blockedReason))
            return false;

        if (injectedMg <= 0)
            return false;

        pendingFluidOutput -= injectedMg;
        if (pendingFluidOutput < 0)
            pendingFluidOutput = 0;

        if (pendingFluidOutput == 0)
            PendingFluidOutputType = string.Empty;

        return true;
    }

    private bool TryInjectFluidPartial(WorldBase world, string fluidType, int requestedMg, out int injectedMg, out string blockedReason)
    {
        injectedMg = 0;
        blockedReason = string.Empty;

        if (requestedMg <= 0)
            return true;

        if (world == null || SelectedFluidGraphId == Guid.Empty || string.IsNullOrEmpty(fluidType))
        {
            blockedReason = "Missing/Invalid Fluid Output";
            return false;
        }

        if (FluidGraphManager.TryInjectFluid(world, 0, SelectedFluidGraphId, fluidType, requestedMg, out blockedReason))
        {
            injectedMg = requestedMg;
            blockedReason = string.Empty;
            return true;
        }

        bool retryWithSmallerAmount =
            string.Equals(blockedReason, "Graph throughput full", StringComparison.Ordinal) ||
            string.Equals(blockedReason, "No storage room", StringComparison.Ordinal);

        if (!retryWithSmallerAmount || requestedMg <= 1)
            return false;

        int attempt = requestedMg / 2;
        while (attempt > 0)
        {
            if (FluidGraphManager.TryInjectFluid(world, 0, SelectedFluidGraphId, fluidType, attempt, out string smallerReason))
            {
                injectedMg = attempt;
                blockedReason = string.Empty;
                return true;
            }

            bool canContinue =
                string.Equals(smallerReason, "Graph throughput full", StringComparison.Ordinal) ||
                string.Equals(smallerReason, "No storage room", StringComparison.Ordinal);

            if (!canContinue)
            {
                blockedReason = smallerReason;
                return false;
            }

            attempt /= 2;
        }

        return false;
    }

    private bool TryResolveInputGraph(WorldBase world, string inputFluidType, out Guid graphId, out string blockedReason)
    {
        graphId = Guid.Empty;
        blockedReason = string.Empty;

        if (world == null || string.IsNullOrEmpty(inputFluidType))
        {
            blockedReason = "Invalid fluid input";
            return false;
        }

        string normalized = inputFluidType.Trim().ToLowerInvariant();
        List<Guid> candidates = GetAdjacentFluidGraphCandidates(world);
        if (candidates.Count == 0)
        {
            blockedReason = "Missing fluid network";
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            Guid candidate = candidates[i];
            if (!IsGraphCompatible(candidate, normalized))
                continue;

            graphId = candidate;
            return true;
        }

        blockedReason = $"Need {ToFluidDisplayName(normalized)}";
        return false;
    }

    private bool TryGetCompatibleFluidGraph(WorldBase world, string selectedFluidType, out Guid graphId)
    {
        graphId = Guid.Empty;

        if (world == null || string.IsNullOrEmpty(selectedFluidType))
            return false;

        string normalizedFluid = selectedFluidType.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedFluid))
            return false;

        List<Guid> candidates = GetAdjacentFluidGraphCandidates(world);
        if (candidates.Count == 0)
            return false;

        bool selectedIsCompatible = false;
        if (SelectedFluidGraphId != Guid.Empty && candidates.Contains(SelectedFluidGraphId))
            selectedIsCompatible = IsGraphCompatible(SelectedFluidGraphId, normalizedFluid);

        if (selectedIsCompatible && GraphHasActivePump(world, SelectedFluidGraphId))
        {
            graphId = SelectedFluidGraphId;
            return true;
        }

        Guid firstCompatible = Guid.Empty;
        for (int i = 0; i < candidates.Count; i++)
        {
            Guid candidate = candidates[i];
            if (!IsGraphCompatible(candidate, normalizedFluid))
                continue;

            if (firstCompatible == Guid.Empty)
                firstCompatible = candidate;

            if (!GraphHasActivePump(world, candidate))
                continue;

            graphId = candidate;
            return true;
        }

        if (selectedIsCompatible)
        {
            graphId = SelectedFluidGraphId;
            return true;
        }

        if (firstCompatible != Guid.Empty)
        {
            graphId = firstCompatible;
            return true;
        }

        return false;
    }

    private List<Guid> GetAdjacentFluidGraphCandidates(WorldBase world)
    {
        List<Guid> candidates = new List<Guid>();
        if (world == null)
            return candidates;

        Vector3i machinePos = ToWorldPos();
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i pipePos = machinePos + NeighborOffsets[i];
            TileEntityLiquidPipe pipe = world.GetTileEntity(0, pipePos) as TileEntityLiquidPipe;
            if (pipe == null)
                continue;

            Guid graphId = pipe.FluidGraphId;
            if (graphId == Guid.Empty && !world.IsRemote())
            {
                if (FluidGraphManager.TryEnsureGraphForPipe(world, 0, pipePos, out FluidGraphData graph) && graph != null)
                    graphId = graph.FluidGraphId;
            }

            if (graphId == Guid.Empty)
                continue;

            if (!candidates.Contains(graphId))
                candidates.Add(graphId);
        }

        return candidates;
    }

    private static bool IsGraphCompatible(Guid graphId, string fluidType)
    {
        if (graphId == Guid.Empty || string.IsNullOrEmpty(fluidType))
            return false;

        if (!FluidGraphManager.TryGetGraph(graphId, out FluidGraphData graph) || graph == null)
            return false;

        string graphFluid = graph.FluidType;
        if (string.IsNullOrEmpty(graphFluid))
            return true;

        return string.Equals(graphFluid, fluidType, StringComparison.Ordinal);
    }

    private static bool GraphHasActivePump(WorldBase world, Guid graphId)
    {
        if (world == null || graphId == Guid.Empty)
            return false;

        if (!FluidGraphManager.TryGetGraph(graphId, out FluidGraphData graph) || graph == null)
            return false;

        if (graph.PumpEndpoints == null || graph.PumpEndpoints.Count == 0)
            return false;

        foreach (Vector3i pumpPos in graph.PumpEndpoints)
        {
            if (SafeWorldRead.TryGetTileEntity(world, 0, pumpPos, out TileEntity pumpTe) && pumpTe is TileEntityFluidPump livePump)
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

    private bool TryGetSelectedRule(out FluidMixerRule rule)
    {
        EnsureConfigLoaded();
        return TryGetRule(SelectedRecipeKey, out rule);
    }

    private bool TryGetRule(string recipeKey, out FluidMixerRule rule)
    {
        rule = null;
        if (string.IsNullOrEmpty(recipeKey))
            return false;

        for (int i = 0; i < rules.Count; i++)
        {
            FluidMixerRule candidate = rules[i];
            if (candidate == null)
                continue;

            if (!string.Equals(candidate.RecipeKey, recipeKey, StringComparison.Ordinal))
                continue;

            rule = candidate;
            return true;
        }

        return false;
    }

    private void EnsureConfigLoaded()
    {
        if (configLoaded)
            return;

        configLoaded = true;

        int defaultCycleTickLength = ReadIntProperty("InputSpeed", 20, 1, 2000);
        int capGallons = ReadIntProperty("PendingFluidOutputCapacityGallons", 5, 1, 1000000);
        pendingFluidOutputCapacityMg = capGallons * FluidConstants.MilliGallonsPerGallon;

        machineRecipeGroupsCsv = GetMachineRecipeGroupsCsv();

        rules.Clear();
        List<MachineRecipe> machineRecipes = MachineRecipeRegistry.GetRecipesForMachineGroups(machineRecipeGroupsCsv, true);
        for (int i = 0; i < machineRecipes.Count; i++)
        {
            MachineRecipe machineRecipe = machineRecipes[i];
            if (!TryReadMachineRecipeAsMixerRule(machineRecipe, defaultCycleTickLength, out FluidMixerRule rule, out _))
                continue;

            rules.Add(rule);
        }

        if (!TryGetSelectedRule(out FluidMixerRule selectedRule) || selectedRule == null)
        {
            if (rules.Count > 0)
            {
                selectedRule = rules[0];
                SelectedRecipeKey = selectedRule.RecipeKey ?? string.Empty;
                SelectedFluidType = selectedRule.OutputType ?? string.Empty;
                CycleTickLength = Math.Max(1, selectedRule.CraftTimeTicks);
                if (string.IsNullOrEmpty(PendingFluidOutputType))
                    PendingFluidOutputType = SelectedFluidType;
            }
            else
            {
                SelectedRecipeKey = string.Empty;
                SelectedFluidType = string.Empty;
                CycleTickLength = defaultCycleTickLength;
                PendingFluidOutputType = string.Empty;
            }
        }
    }

    private string GetMachineRecipeGroupsCsv()
    {
        string configured = blockValue.Block?.Properties?.GetString("MachineRecipes");
        List<string> groups = MachineRecipeRegistry.ParseMachineGroups(configured);
        if (groups.Count == 0)
            groups.Add(DefaultMachineRecipeGroup);

        return string.Join(",", groups.ToArray());
    }

    private static bool TryReadMachineRecipeAsMixerRule(
        MachineRecipe recipe,
        int defaultCraftTimeTicks,
        out FluidMixerRule rule,
        out string error)
    {
        rule = null;
        error = string.Empty;

        if (recipe == null)
        {
            error = "Recipe null";
            return false;
        }

        if (recipe.Inputs != null && recipe.Inputs.Count > 0)
        {
            error = "Mixer recipe cannot have item inputs";
            return false;
        }

        if (recipe.ItemOutputs != null && recipe.ItemOutputs.Count > 0)
        {
            error = "Mixer recipe cannot have item outputs";
            return false;
        }

        if (recipe.FluidInputs == null || recipe.FluidInputs.Count != 2)
        {
            error = "Mixer recipe requires exactly 2 fluid inputs";
            return false;
        }

        if (recipe.FluidOutputs == null || recipe.FluidOutputs.Count != 1)
        {
            error = "Mixer recipe requires exactly 1 fluid output";
            return false;
        }

        MachineRecipeFluidInput a = recipe.FluidInputs[0];
        MachineRecipeFluidInput b = recipe.FluidInputs[1];
        MachineRecipeFluidOutput o = recipe.FluidOutputs[0];
        if (a == null || b == null || o == null)
        {
            error = "Mixer recipe has null fluid nodes";
            return false;
        }

        if (string.IsNullOrEmpty(a.Type) || string.IsNullOrEmpty(b.Type) || string.IsNullOrEmpty(o.Type))
        {
            error = "Mixer fluid type empty";
            return false;
        }

        rule = new FluidMixerRule
        {
            RecipeKey = recipe.NormalizedKey ?? string.Empty,
            DisplayName = recipe.Name ?? string.Empty,
            InputAType = a.Type.Trim().ToLowerInvariant(),
            InputAAmountMg = Math.Max(1, a.AmountMg),
            InputBType = b.Type.Trim().ToLowerInvariant(),
            InputBAmountMg = Math.Max(1, b.AmountMg),
            OutputType = o.Type.Trim().ToLowerInvariant(),
            OutputAmountMg = Math.Max(1, o.AmountMg),
            CraftTimeTicks = recipe.CraftTimeTicks.HasValue
                ? Math.Max(1, recipe.CraftTimeTicks.Value)
                : Math.Max(1, defaultCraftTimeTicks)
        };

        return true;
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

    private int BuildStateSignature()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + (IsOn ? 1 : 0);
            hash = (hash * 31) + (IsProcessing ? 1 : 0);
            hash = (hash * 31) + CycleTickCounter;
            hash = (hash * 31) + CycleTickLength;
            hash = (hash * 31) + pendingFluidOutput;
            hash = (hash * 31) + pendingFluidOutputCapacityMg;
            hash = (hash * 31) + (SelectedRecipeKey?.GetHashCode() ?? 0);
            hash = (hash * 31) + (SelectedFluidType?.GetHashCode() ?? 0);
            hash = (hash * 31) + SelectedFluidGraphId.GetHashCode();
            hash = (hash * 31) + (PendingFluidInputAType?.GetHashCode() ?? 0);
            hash = (hash * 31) + PendingFluidInputAAmountMg;
            hash = (hash * 31) + (PendingFluidInputBType?.GetHashCode() ?? 0);
            hash = (hash * 31) + PendingFluidInputBAmountMg;
            hash = (hash * 31) + (PendingFluidOutputType?.GetHashCode() ?? 0);
            hash = (hash * 31) + (LastAction?.GetHashCode() ?? 0);
            hash = (hash * 31) + (LastBlockReason?.GetHashCode() ?? 0);
            return hash;
        }
    }

    private void ClearPendingInputs()
    {
        PendingFluidInputAType = string.Empty;
        PendingFluidInputAAmountMg = 0;
        PendingFluidInputBType = string.Empty;
        PendingFluidInputBAmountMg = 0;
    }

    private void MarkDirty()
    {
        NeedsUiRefresh = true;
        if (!GameManager.Instance.World.IsRemote())
            setModified();
    }

    private void ResetState()
    {
        IsOn = false;
        IsProcessing = false;
        SelectedRecipeKey = string.Empty;
        SelectedFluidType = string.Empty;
        SelectedFluidGraphId = Guid.Empty;
        CycleTickCounter = 0;
        CycleTickLength = 20;
        ActiveRecipeKey = string.Empty;
        ClearPendingInputs();
        PendingFluidOutputType = string.Empty;
        pendingFluidOutput = 0;
        pendingFluidOutputCapacityMg = 5000;
        LastAction = "Idle";
        LastBlockReason = string.Empty;
        machineRecipeGroupsCsv = DefaultMachineRecipeGroup;
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
}
