using System;
using System.Collections.Generic;

public partial class HigherLogicRegistry
{
    private void SimulateFluidMixer(FluidMixerSnapshot mixer, ulong worldTime, int hlrTicksToSimulate)
    {
        if (mixer == null)
            return;

        if (!mixer.IsOn)
        {
            mixer.IsProcessing = false;
            mixer.CycleTickCounter = 0;
            mixer.ActiveRecipeKey = string.Empty;
            mixer.PendingFluidInputAType = string.Empty;
            mixer.PendingFluidInputAAmountMg = 0;
            mixer.PendingFluidInputBType = string.Empty;
            mixer.PendingFluidInputBAmountMg = 0;
            mixer.LastAction = "Off";
            mixer.LastBlockReason = string.Empty;
            mixer.WorldTime = worldTime;
            return;
        }

        int ticksRemaining = Math.Max(1, hlrTicksToSimulate);
        string nextAction = mixer.LastAction ?? "Idle";
        string nextReason = string.Empty;

        if (!TryFlushFluidMixerPendingOutput(mixer, out string outputBlockedReason) && mixer.PendingFluidOutput > 0)
        {
            mixer.LastAction = "Waiting";
            mixer.LastBlockReason = string.IsNullOrEmpty(outputBlockedReason) ? "Output blocked" : outputBlockedReason;
            mixer.WorldTime = worldTime;
            return;
        }

        while (ticksRemaining > 0)
        {
            if (!mixer.IsProcessing)
            {
                string requirementsReason = GetFluidMixerMissingRequirementReason(mixer);
                if (!string.IsNullOrEmpty(requirementsReason))
                {
                    nextAction = "Waiting";
                    nextReason = requirementsReason;
                    break;
                }

                if (!TryBeginFluidMixerCycle(mixer, out string startBlockedReason))
                {
                    nextAction = "Waiting";
                    nextReason = string.IsNullOrEmpty(startBlockedReason) ? "Waiting" : startBlockedReason;
                    break;
                }

                nextAction = mixer.LastAction ?? "Requested Inputs";
                nextReason = string.Empty;
                ticksRemaining--;
                if (ticksRemaining <= 0)
                    break;

                continue;
            }

            int cycleLength = Math.Max(1, mixer.CycleTickLength);
            int needed = cycleLength - mixer.CycleTickCounter;
            if (needed <= 0)
                needed = 1;

            int advance = Math.Min(ticksRemaining, needed);
            mixer.CycleTickCounter += advance;
            ticksRemaining -= advance;
            nextAction = "Mixing";
            nextReason = string.Empty;

            if (mixer.CycleTickCounter < cycleLength)
                break;

            CompleteFluidMixerCycle(mixer);
            nextAction = mixer.LastAction ?? "Mix complete";
            nextReason = string.Empty;

            if (!TryFlushFluidMixerPendingOutput(mixer, out outputBlockedReason) && mixer.PendingFluidOutput > 0)
            {
                nextAction = "Waiting";
                nextReason = string.IsNullOrEmpty(outputBlockedReason) ? "Output blocked" : outputBlockedReason;
                break;
            }
        }

        mixer.LastAction = nextAction;
        mixer.LastBlockReason = nextReason;
        mixer.WorldTime = worldTime;
    }

    private string GetFluidMixerMissingRequirementReason(FluidMixerSnapshot mixer)
    {
        if (mixer == null)
            return "World unavailable";

        if (mixer.PendingFluidOutput >= Math.Max(1, mixer.PendingFluidOutputCapacityMg))
            return "Pending fluid output full";

        if (!TryGetFluidMixerRule(
                mixer,
                mixer.SelectedRecipeKey,
                out string normalizedRecipeKey,
                out string inputAType,
                out int inputAAmountMg,
                out string inputBType,
                out int inputBAmountMg,
                out string outputType,
                out _,
                out int craftTimeTicks))
        {
            return "Selected recipe unavailable";
        }

        mixer.SelectedRecipeKey = normalizedRecipeKey;
        mixer.SelectedFluidType = outputType ?? string.Empty;
        mixer.CycleTickLength = Math.Max(1, craftTimeTicks);

        if (!TryResolveFluidMixerInputGraph(mixer.Position, inputAType, Math.Max(1, inputAAmountMg), out Guid graphA, out string blockedA))
            return blockedA;

        if (!TryResolveFluidMixerInputGraph(mixer.Position, inputBType, Math.Max(1, inputBAmountMg), out Guid graphB, out string blockedB))
            return blockedB;

        if (!TryResolveFluidMixerOutputGraph(mixer, outputType, out Guid outputGraph))
            return "Missing/Invalid Fluid Output";

        mixer.SelectedFluidGraphId = outputGraph;

        if (!FluidGraphManager.TryGetAvailableFluidAmount(world, 0, graphA, inputAType, out int availableA) ||
            availableA < inputAAmountMg)
        {
            return $"Need {FormatGallons(inputAAmountMg)} gal {ToFluidDisplayName(inputAType)}";
        }

        if (!FluidGraphManager.TryGetAvailableFluidAmount(world, 0, graphB, inputBType, out int availableB) ||
            availableB < inputBAmountMg)
        {
            return $"Need {FormatGallons(inputBAmountMg)} gal {ToFluidDisplayName(inputBType)}";
        }

        return string.Empty;
    }

    private bool TryBeginFluidMixerCycle(FluidMixerSnapshot mixer, out string blockedReason)
    {
        blockedReason = string.Empty;
        if (mixer == null)
        {
            blockedReason = "World unavailable";
            return false;
        }

        if (!TryGetFluidMixerRule(
                mixer,
                mixer.SelectedRecipeKey,
                out string normalizedRecipeKey,
                out string inputAType,
                out int inputAAmountMg,
                out string inputBType,
                out int inputBAmountMg,
                out string outputType,
                out _,
                out int craftTimeTicks))
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        if (!TryResolveFluidMixerInputGraph(mixer.Position, inputAType, Math.Max(1, inputAAmountMg), out Guid graphA, out blockedReason))
            return false;

        if (!TryResolveFluidMixerInputGraph(mixer.Position, inputBType, Math.Max(1, inputBAmountMg), out Guid graphB, out blockedReason))
            return false;

        if (!FluidGraphManager.TryConsumeFluid(world, 0, graphA, inputAType, inputAAmountMg, out int consumedA) || consumedA < inputAAmountMg)
        {
            if (consumedA > 0)
                FluidGraphManager.TryInjectFluid(world, 0, graphA, inputAType, consumedA, out _);

            blockedReason = $"Need {FormatGallons(inputAAmountMg)} gal {ToFluidDisplayName(inputAType)}";
            return false;
        }

        if (!FluidGraphManager.TryConsumeFluid(world, 0, graphB, inputBType, inputBAmountMg, out int consumedB) || consumedB < inputBAmountMg)
        {
            if (consumedA > 0)
                FluidGraphManager.TryInjectFluid(world, 0, graphA, inputAType, consumedA, out _);
            if (consumedB > 0)
                FluidGraphManager.TryInjectFluid(world, 0, graphB, inputBType, consumedB, out _);

            blockedReason = $"Need {FormatGallons(inputBAmountMg)} gal {ToFluidDisplayName(inputBType)}";
            return false;
        }

        mixer.SelectedRecipeKey = normalizedRecipeKey;
        mixer.SelectedFluidType = outputType ?? string.Empty;
        mixer.PendingFluidInputAType = inputAType;
        mixer.PendingFluidInputAAmountMg = consumedA;
        mixer.PendingFluidInputBType = inputBType;
        mixer.PendingFluidInputBAmountMg = consumedB;
        mixer.IsProcessing = true;
        mixer.CycleTickCounter = 0;
        mixer.CycleTickLength = Math.Max(1, craftTimeTicks);
        mixer.ActiveRecipeKey = normalizedRecipeKey;
        mixer.LastAction = "Requested Inputs";
        mixer.LastBlockReason = string.Empty;
        return true;
    }

    private void CompleteFluidMixerCycle(FluidMixerSnapshot mixer)
    {
        if (mixer == null)
            return;

        string recipeKey = string.IsNullOrEmpty(mixer.ActiveRecipeKey) ? mixer.SelectedRecipeKey : mixer.ActiveRecipeKey;
        if (TryGetFluidMixerRule(
                mixer,
                recipeKey,
                out string normalizedRecipeKey,
                out _,
                out _,
                out _,
                out _,
                out string outputType,
                out int outputAmountMg,
                out _))
        {
            mixer.SelectedRecipeKey = normalizedRecipeKey;
            mixer.SelectedFluidType = outputType ?? string.Empty;
            mixer.PendingFluidOutputType = mixer.SelectedFluidType;
            long total = (long)mixer.PendingFluidOutput + Math.Max(0, outputAmountMg);
            mixer.PendingFluidOutput = (int)Math.Min(Math.Max(1, mixer.PendingFluidOutputCapacityMg), total);
        }

        mixer.IsProcessing = false;
        mixer.CycleTickCounter = 0;
        mixer.ActiveRecipeKey = string.Empty;
        mixer.PendingFluidInputAType = string.Empty;
        mixer.PendingFluidInputAAmountMg = 0;
        mixer.PendingFluidInputBType = string.Empty;
        mixer.PendingFluidInputBAmountMg = 0;
        mixer.LastAction = "Mix complete";
        mixer.LastBlockReason = string.Empty;
    }

    private bool TryFlushFluidMixerPendingOutput(FluidMixerSnapshot mixer, out string blockedReason)
    {
        blockedReason = string.Empty;
        if (mixer == null || mixer.PendingFluidOutput <= 0)
            return true;

        if (string.IsNullOrEmpty(mixer.PendingFluidOutputType))
        {
            mixer.PendingFluidOutput = 0;
            blockedReason = "Pending output invalid";
            return true;
        }

        if (!TryResolveFluidMixerOutputGraph(mixer, mixer.PendingFluidOutputType, out Guid graphId))
        {
            blockedReason = "Missing/Invalid Fluid Output";
            return false;
        }

        mixer.SelectedFluidGraphId = graphId;

        if (!TryInjectFluidPartialForSnapshot(graphId, mixer.PendingFluidOutputType, mixer.PendingFluidOutput, out int injectedMg, out blockedReason))
            return false;

        if (injectedMg <= 0)
            return false;

        mixer.PendingFluidOutput -= injectedMg;
        if (mixer.PendingFluidOutput < 0)
            mixer.PendingFluidOutput = 0;

        if (mixer.PendingFluidOutput == 0)
            mixer.PendingFluidOutputType = string.Empty;

        return true;
    }

    private bool TryInjectFluidPartialForSnapshot(Guid graphId, string fluidType, int requestedMg, out int injectedMg, out string blockedReason)
    {
        injectedMg = 0;
        blockedReason = string.Empty;

        if (requestedMg <= 0)
            return true;

        if (graphId == Guid.Empty || string.IsNullOrEmpty(fluidType))
        {
            blockedReason = "Missing/Invalid Fluid Output";
            return false;
        }

        if (FluidGraphManager.TryInjectFluid(world, 0, graphId, fluidType, requestedMg, out blockedReason))
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
            if (FluidGraphManager.TryInjectFluid(world, 0, graphId, fluidType, attempt, out string smallerReason))
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

    private bool TryResolveFluidMixerInputGraph(Vector3i mixerPos, string inputFluidType, int requiredMg, out Guid graphId, out string blockedReason)
    {
        graphId = Guid.Empty;
        blockedReason = string.Empty;
        if (string.IsNullOrEmpty(inputFluidType))
        {
            blockedReason = "Invalid fluid input";
            return false;
        }

        string normalized = inputFluidType.Trim().ToLowerInvariant();
        List<Guid> candidates = GetDecanterAdjacentFluidGraphCandidates(mixerPos);
        if (candidates.Count == 0)
        {
            blockedReason = "Missing fluid network";
            return false;
        }

        Guid bestAvailableGraph = Guid.Empty;
        int bestAvailableMg = -1;
        int normalizedRequiredMg = Math.Max(1, requiredMg);

        for (int i = 0; i < candidates.Count; i++)
        {
            Guid candidate = candidates[i];
            if (!IsDecanterFluidGraphCompatible(candidate, normalized))
                continue;

            if (!FluidGraphManager.TryGetAvailableFluidAmount(world, 0, candidate, normalized, out int availableMg))
                continue;

            if (availableMg >= normalizedRequiredMg)
            {
                graphId = candidate;
                return true;
            }

            if (availableMg > bestAvailableMg)
            {
                bestAvailableMg = availableMg;
                bestAvailableGraph = candidate;
            }
        }

        if (bestAvailableGraph != Guid.Empty)
            blockedReason = $"Need {FormatGallons(normalizedRequiredMg)} gal {ToFluidDisplayName(normalized)}";
        else
            blockedReason = $"Need {ToFluidDisplayName(normalized)}";

        return false;
    }

    private bool TryResolveFluidMixerOutputGraph(FluidMixerSnapshot mixer, string outputFluidType, out Guid graphId)
    {
        graphId = Guid.Empty;
        if (mixer == null || string.IsNullOrWhiteSpace(outputFluidType))
            return false;

        string normalizedFluid = outputFluidType.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedFluid))
            return false;

        List<Guid> candidates = GetDecanterAdjacentFluidGraphCandidates(mixer.Position);
        if (candidates.Count == 0)
            return false;

        bool selectedIsCompatible = false;
        if (mixer.SelectedFluidGraphId != Guid.Empty && candidates.Contains(mixer.SelectedFluidGraphId))
            selectedIsCompatible = IsDecanterFluidGraphCompatible(mixer.SelectedFluidGraphId, normalizedFluid);

        if (selectedIsCompatible && DoesDecanterGraphHaveActivePump(mixer.SelectedFluidGraphId))
        {
            graphId = mixer.SelectedFluidGraphId;
            return true;
        }

        Guid firstCompatible = Guid.Empty;
        for (int i = 0; i < candidates.Count; i++)
        {
            Guid candidate = candidates[i];
            if (!IsDecanterFluidGraphCompatible(candidate, normalizedFluid))
                continue;

            if (firstCompatible == Guid.Empty)
                firstCompatible = candidate;

            if (!DoesDecanterGraphHaveActivePump(candidate))
                continue;

            graphId = candidate;
            return true;
        }

        if (selectedIsCompatible)
        {
            graphId = mixer.SelectedFluidGraphId;
            return true;
        }

        if (firstCompatible != Guid.Empty)
        {
            graphId = firstCompatible;
            return true;
        }

        return false;
    }

    private bool TryGetFluidMixerRule(
        FluidMixerSnapshot mixer,
        string recipeKey,
        out string normalizedRecipeKey,
        out string inputAType,
        out int inputAAmountMg,
        out string inputBType,
        out int inputBAmountMg,
        out string outputType,
        out int outputAmountMg,
        out int craftTimeTicks)
    {
        normalizedRecipeKey = string.Empty;
        inputAType = string.Empty;
        inputAAmountMg = 0;
        inputBType = string.Empty;
        inputBAmountMg = 0;
        outputType = string.Empty;
        outputAmountMg = 0;
        craftTimeTicks = Math.Max(1, mixer?.CycleTickLength ?? 1);

        if (mixer == null)
            return false;

        if (!string.IsNullOrEmpty(recipeKey) &&
            MachineRecipeRegistry.TryGetRecipeByKey(recipeKey, out MachineRecipe selected) &&
            TryReadMachineRecipeAsFluidMixerRule(
                selected,
                Math.Max(1, mixer.CycleTickLength),
                out inputAType,
                out inputAAmountMg,
                out inputBType,
                out inputBAmountMg,
                out outputType,
                out outputAmountMg,
                out craftTimeTicks))
        {
            normalizedRecipeKey = selected?.NormalizedKey ?? recipeKey;
            return true;
        }

        List<MachineRecipe> recipes = MachineRecipeRegistry.GetRecipesForMachineGroups(
            GetSnapshotMachineGroupsCsv(mixer.MachineRecipeGroupsCsv, "fluid_mixer"),
            true);

        for (int i = 0; i < recipes.Count; i++)
        {
            MachineRecipe recipe = recipes[i];
            if (TryReadMachineRecipeAsFluidMixerRule(
                    recipe,
                    Math.Max(1, mixer.CycleTickLength),
                    out inputAType,
                    out inputAAmountMg,
                    out inputBType,
                    out inputBAmountMg,
                    out outputType,
                    out outputAmountMg,
                    out craftTimeTicks))
            {
                normalizedRecipeKey = recipe?.NormalizedKey ?? string.Empty;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadMachineRecipeAsFluidMixerRule(
        MachineRecipe recipe,
        int defaultCraftTimeTicks,
        out string inputAType,
        out int inputAAmountMg,
        out string inputBType,
        out int inputBAmountMg,
        out string outputType,
        out int outputAmountMg,
        out int craftTimeTicks)
    {
        inputAType = string.Empty;
        inputAAmountMg = 0;
        inputBType = string.Empty;
        inputBAmountMg = 0;
        outputType = string.Empty;
        outputAmountMg = 0;
        craftTimeTicks = Math.Max(1, defaultCraftTimeTicks);

        if (recipe == null)
            return false;

        if (recipe.Inputs != null && recipe.Inputs.Count > 0)
            return false;

        if (recipe.ItemOutputs != null && recipe.ItemOutputs.Count > 0)
            return false;

        if (recipe.FluidInputs == null || recipe.FluidInputs.Count != 2)
            return false;

        if (recipe.FluidOutputs == null || recipe.FluidOutputs.Count != 1)
            return false;

        MachineRecipeFluidInput a = recipe.FluidInputs[0];
        MachineRecipeFluidInput b = recipe.FluidInputs[1];
        MachineRecipeFluidOutput o = recipe.FluidOutputs[0];
        if (a == null || b == null || o == null)
            return false;

        if (string.IsNullOrEmpty(a.Type) || string.IsNullOrEmpty(b.Type) || string.IsNullOrEmpty(o.Type))
            return false;

        inputAType = a.Type.Trim().ToLowerInvariant();
        inputAAmountMg = Math.Max(1, a.AmountMg);
        inputBType = b.Type.Trim().ToLowerInvariant();
        inputBAmountMg = Math.Max(1, b.AmountMg);
        outputType = o.Type.Trim().ToLowerInvariant();
        outputAmountMg = Math.Max(1, o.AmountMg);
        craftTimeTicks = recipe.CraftTimeTicks.HasValue
            ? Math.Max(1, recipe.CraftTimeTicks.Value)
            : Math.Max(1, defaultCraftTimeTicks);
        return true;
    }

    private void SimulateCaster(CasterSnapshot caster, ulong worldTime, int hlrTicksToSimulate)
    {
        if (caster == null)
            return;

        if (caster.PendingOutputs == null)
            caster.PendingOutputs = new Dictionary<string, int>(StringComparer.Ordinal);

        if (!caster.IsOn)
        {
            caster.LastAction = "Off";
            caster.LastBlockReason = string.Empty;
            caster.IsProcessing = false;
            caster.CycleTickCounter = 0;
            caster.ActiveRecipeKey = string.Empty;
            caster.PendingFluidInputType = string.Empty;
            caster.PendingFluidInputAmountMg = 0;
            caster.WorldTime = worldTime;
            return;
        }

        if (!TryFlushCasterPendingOutput(caster, out string blockedReason) && caster.PendingOutputs.Count > 0)
        {
            caster.LastAction = "Waiting";
            caster.LastBlockReason = string.IsNullOrEmpty(blockedReason) ? "Output blocked" : blockedReason;
            caster.WorldTime = worldTime;
            return;
        }

        int ticksRemaining = Math.Max(1, hlrTicksToSimulate);
        string nextAction = caster.LastAction ?? "Idle";
        string nextReason = string.Empty;

        while (ticksRemaining > 0)
        {
            if (!caster.IsProcessing)
            {
                string requirementsReason = GetCasterMissingRequirementReason(caster);
                if (!string.IsNullOrEmpty(requirementsReason))
                {
                    nextAction = "Waiting";
                    nextReason = requirementsReason;
                    break;
                }

                if (!TryBeginCasterCycle(caster, out string startBlockedReason))
                {
                    nextAction = "Waiting";
                    nextReason = string.IsNullOrEmpty(startBlockedReason) ? "Waiting" : startBlockedReason;
                    break;
                }

                nextAction = caster.LastAction ?? "Consumed Fluid";
                nextReason = string.Empty;
                ticksRemaining--;
                if (ticksRemaining <= 0)
                    break;

                continue;
            }

            int cycleLength = Math.Max(1, caster.CycleTickLength);
            int needed = cycleLength - caster.CycleTickCounter;
            if (needed <= 0)
                needed = 1;

            int advance = Math.Min(ticksRemaining, needed);
            caster.CycleTickCounter += advance;
            ticksRemaining -= advance;
            nextAction = "Processing";
            nextReason = string.Empty;

            if (caster.CycleTickCounter < cycleLength)
                break;

            CompleteCasterCycle(caster);
            nextAction = caster.LastAction ?? "Craft complete";
            nextReason = string.Empty;

            if (!TryFlushCasterPendingOutput(caster, out blockedReason) && caster.PendingOutputs.Count > 0)
            {
                nextAction = "Waiting";
                nextReason = string.IsNullOrEmpty(blockedReason) ? "Output blocked" : blockedReason;
                break;
            }
        }

        caster.LastAction = nextAction;
        caster.LastBlockReason = nextReason;
        caster.WorldTime = worldTime;
    }

    private string GetCasterMissingRequirementReason(CasterSnapshot caster)
    {
        if (caster == null)
            return "World unavailable";

        if (!TryGetCasterRule(
                caster,
                caster.SelectedRecipeKey,
                out string normalizedRecipeKey,
                out string fluidType,
                out int fluidAmountMg,
                out _,
                out _,
                out int craftTimeTicks))
        {
            return "Selected recipe unavailable";
        }

        caster.SelectedRecipeKey = normalizedRecipeKey;
        caster.SelectedFluidType = fluidType ?? string.Empty;
        caster.CycleTickLength = Math.Max(1, craftTimeTicks);

        if (caster.SelectedOutputChestPos == Vector3i.zero)
            return "Missing Item Output";

        if (caster.SelectedOutputMode != OutputTransportMode.Pipe)
            return "HLR requires pipe item output";

        if (caster.SelectedOutputPipeGraphId == Guid.Empty)
            return "Missing Item Output";

        if (!HasValidGraphStorageEndpoint(ref caster.SelectedOutputPipeGraphId, ref caster.SelectedOutputPipeAnchorPos, caster.Position, caster.SelectedOutputChestPos))
            return "Missing Item Output";

        if (!TryResolveCasterFluidInputGraph(caster, fluidType, out Guid graphId))
            return "Missing/Invalid Fluid Input";

        caster.SelectedFluidGraphId = graphId;

        if (!FluidGraphManager.TryGetAvailableFluidAmount(world, 0, graphId, fluidType, out int availableMg) ||
            availableMg < fluidAmountMg)
        {
            return $"Need {FormatGallons(fluidAmountMg)} gal {ToFluidDisplayName(fluidType)}";
        }

        return string.Empty;
    }

    private bool TryBeginCasterCycle(CasterSnapshot caster, out string blockedReason)
    {
        blockedReason = string.Empty;
        if (caster == null)
        {
            blockedReason = "World unavailable";
            return false;
        }

        if (!TryGetCasterRule(
                caster,
                caster.SelectedRecipeKey,
                out string normalizedRecipeKey,
                out string fluidType,
                out int fluidAmountMg,
                out _,
                out _,
                out int craftTimeTicks))
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        if (!TryResolveCasterFluidInputGraph(caster, fluidType, out Guid graphId))
        {
            blockedReason = "Missing/Invalid Fluid Input";
            return false;
        }

        if (!FluidGraphManager.TryConsumeFluid(world, 0, graphId, fluidType, fluidAmountMg, out int consumedMg) || consumedMg < fluidAmountMg)
        {
            if (consumedMg > 0)
                FluidGraphManager.TryInjectFluid(world, 0, graphId, fluidType, consumedMg, out _);

            blockedReason = $"Need {FormatGallons(fluidAmountMg)} gal {ToFluidDisplayName(fluidType)}";
            return false;
        }

        caster.SelectedRecipeKey = normalizedRecipeKey;
        caster.SelectedFluidType = fluidType ?? string.Empty;
        caster.SelectedFluidGraphId = graphId;
        caster.PendingFluidInputType = fluidType;
        caster.PendingFluidInputAmountMg = consumedMg;
        caster.IsProcessing = true;
        caster.CycleTickCounter = 0;
        caster.CycleTickLength = Math.Max(1, craftTimeTicks);
        caster.ActiveRecipeKey = normalizedRecipeKey;
        caster.LastAction = "Consumed Fluid";
        caster.LastBlockReason = string.Empty;
        return true;
    }

    private void CompleteCasterCycle(CasterSnapshot caster)
    {
        if (caster == null)
            return;

        string recipeKey = string.IsNullOrEmpty(caster.ActiveRecipeKey) ? caster.SelectedRecipeKey : caster.ActiveRecipeKey;
        if (TryGetCasterRule(
                caster,
                recipeKey,
                out string normalizedRecipeKey,
                out _,
                out _,
                out MachineRecipeItemOutput primary,
                out MachineRecipeItemOutput secondary,
                out _))
        {
            caster.SelectedRecipeKey = normalizedRecipeKey;
            if (primary != null && !string.IsNullOrEmpty(primary.ItemName) && primary.Count > 0)
                AddPendingInfuserOutput(caster.PendingOutputs, primary.ItemName, primary.Count);

            if (secondary != null && !string.IsNullOrEmpty(secondary.ItemName) && secondary.Count > 0)
                AddPendingInfuserOutput(caster.PendingOutputs, secondary.ItemName, secondary.Count);
        }

        caster.PendingFluidInputType = string.Empty;
        caster.PendingFluidInputAmountMg = 0;
        caster.IsProcessing = false;
        caster.CycleTickCounter = 0;
        caster.ActiveRecipeKey = string.Empty;
        caster.LastAction = "Craft complete";
        caster.LastBlockReason = string.Empty;
    }

    private bool TryFlushCasterPendingOutput(CasterSnapshot caster, out string blockedReason)
    {
        blockedReason = string.Empty;
        if (caster == null || caster.PendingOutputs == null || caster.PendingOutputs.Count == 0)
            return true;

        if (caster.SelectedOutputMode != OutputTransportMode.Pipe)
        {
            blockedReason = "HLR requires pipe item output";
            return false;
        }

        if (caster.SelectedOutputPipeGraphId == Guid.Empty || caster.SelectedOutputChestPos == Vector3i.zero)
        {
            blockedReason = "Missing Item Output";
            return false;
        }

        foreach (var kvp in new List<KeyValuePair<string, int>>(caster.PendingOutputs))
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
            {
                caster.PendingOutputs.Remove(kvp.Key);
                continue;
            }

            Dictionary<string, int> request = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { kvp.Key, kvp.Value }
            };

            if (!TryDepositSnapshotOutput(caster.SelectedOutputPipeGraphId, caster.SelectedOutputChestPos, request, out Dictionary<string, int> deposited) ||
                deposited == null ||
                !deposited.TryGetValue(kvp.Key, out int moved) ||
                moved <= 0)
            {
                blockedReason = "Output blocked";
                return false;
            }

            int remaining = kvp.Value - moved;
            if (remaining > 0)
            {
                caster.PendingOutputs[kvp.Key] = remaining;
                blockedReason = "Output blocked";
                return false;
            }

            caster.PendingOutputs.Remove(kvp.Key);
            caster.LastAction = "Output transferred";
            caster.LastBlockReason = string.Empty;
        }

        return true;
    }

    private bool TryResolveCasterFluidInputGraph(CasterSnapshot caster, string fluidType, out Guid graphId)
    {
        graphId = Guid.Empty;
        if (caster == null || string.IsNullOrWhiteSpace(fluidType))
            return false;

        string normalizedFluid = fluidType.Trim().ToLowerInvariant();
        if (caster.SelectedFluidGraphId != Guid.Empty && IsDecanterFluidGraphCompatible(caster.SelectedFluidGraphId, normalizedFluid))
        {
            graphId = caster.SelectedFluidGraphId;
            return true;
        }

        List<Guid> candidates = GetDecanterAdjacentFluidGraphCandidates(caster.Position);
        for (int i = 0; i < candidates.Count; i++)
        {
            Guid candidate = candidates[i];
            if (!IsDecanterFluidGraphCompatible(candidate, normalizedFluid))
                continue;

            graphId = candidate;
            return true;
        }

        return false;
    }

    private bool TryGetCasterRule(
        CasterSnapshot caster,
        string recipeKey,
        out string normalizedRecipeKey,
        out string fluidType,
        out int fluidAmountMg,
        out MachineRecipeItemOutput primaryOutput,
        out MachineRecipeItemOutput secondaryOutput,
        out int craftTimeTicks)
    {
        normalizedRecipeKey = string.Empty;
        fluidType = string.Empty;
        fluidAmountMg = 0;
        primaryOutput = null;
        secondaryOutput = null;
        craftTimeTicks = Math.Max(1, caster?.CycleTickLength ?? 1);

        if (caster == null)
            return false;

        if (!string.IsNullOrEmpty(recipeKey) &&
            MachineRecipeRegistry.TryGetRecipeByKey(recipeKey, out MachineRecipe selected) &&
            TileEntityCaster.TryReadMachineRecipeAsCasterRule(
                selected,
                Math.Max(1, caster.CycleTickLength),
                out fluidType,
                out fluidAmountMg,
                out primaryOutput,
                out secondaryOutput,
                out craftTimeTicks,
                out _))
        {
            normalizedRecipeKey = selected?.NormalizedKey ?? recipeKey;
            return true;
        }

        List<MachineRecipe> recipes = MachineRecipeRegistry.GetRecipesForMachineGroups(
            GetSnapshotMachineGroupsCsv(caster.MachineRecipeGroupsCsv, "mold"),
            true);

        for (int i = 0; i < recipes.Count; i++)
        {
            MachineRecipe recipe = recipes[i];
            if (TileEntityCaster.TryReadMachineRecipeAsCasterRule(
                    recipe,
                    Math.Max(1, caster.CycleTickLength),
                    out fluidType,
                    out fluidAmountMg,
                    out primaryOutput,
                    out secondaryOutput,
                    out craftTimeTicks,
                    out _))
            {
                normalizedRecipeKey = recipe?.NormalizedKey ?? string.Empty;
                return true;
            }
        }

        return false;
    }

    private static string GetSnapshotMachineGroupsCsv(string configured, string fallback)
    {
        List<string> groups = MachineRecipeRegistry.ParseMachineGroups(configured);
        if (groups.Count == 0)
            groups.Add(fallback);

        return string.Join(",", groups.ToArray());
    }
}

