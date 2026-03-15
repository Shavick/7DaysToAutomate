using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class TileEntityFluidDecanter : TileEntityMachine
{
    private const int PersistVersion = 105;
    private const int ClientSyncVersion = 4;
    private const int MaxSerializedInputTargets = 64;
    private const int MaxSerializedOutputTargets = 64;
    private const int MaxSerializedFluidOptions = 64;
    private const string DefaultMachineRecipeGroup = "fluiddecanter";

    private static readonly Vector3i[] NeighborOffsets =
    {
        Vector3i.forward,
        Vector3i.back,
        Vector3i.left,
        Vector3i.right,
        Vector3i.up,
        Vector3i.down
    };

    private static readonly object InvalidRecipeWarnSync = new object();
    private static readonly HashSet<string> warnedInvalidRecipeKeys = new HashSet<string>(StringComparer.Ordinal);

    private sealed class FuelConversionRule
    {
        public string RecipeKey;
        public string InputItemName;
        public int InputItemCount;
        public string FluidType;
        public int FluidAmountMg;
        public string ReturnItemName;
        public int ReturnItemAmount;
        public int CraftTimeTicks;
    }

    public List<InputTargetInfo> availableInputTargets = new List<InputTargetInfo>();
    public List<OutputTargetInfo> availableOutputTargets = new List<OutputTargetInfo>();

    public Vector3i SelectedInputChestPos = Vector3i.zero;
    public Guid SelectedInputPipeGraphId = Guid.Empty;

    public Vector3i SelectedOutputChestPos = Vector3i.zero;
    public OutputTransportMode SelectedOutputMode = OutputTransportMode.Adjacent;
    public Guid SelectedOutputPipeGraphId = Guid.Empty;

    public string SelectedFluidType = string.Empty;
    public string SelectedRecipeKey = string.Empty;
    public Guid SelectedFluidGraphId = Guid.Empty;

    public int pendingItemInput;
    public int pendingItemOutput;
    public int pendingFluidInput;
    public int pendingFluidOutput;

    public int cycleTickCounter;
    public int cycleTickLength = 20;
    public int pendingFluidOutputCapacityMg = 5000;

    public string LastAction = "Idle";
    public string LastBlockReason = string.Empty;

    private readonly List<FuelConversionRule> conversionRules = new List<FuelConversionRule>();
    private readonly List<string> fluidOptions = new List<string>();
    private string machineRecipeGroupsCsv = DefaultMachineRecipeGroup;

    private bool configLoaded;
    private int refreshTicker;
    private int lastStateSignature = int.MinValue;
    private ulong lastUiSyncWorldTime;

    private TileEntityComposite selectedInputContainer;
    private TileEntityComposite selectedOutputContainer;

    private string pendingItemInputName = string.Empty;
    private int pendingItemInputFluidAmountMg;
    private string pendingItemInputReturnItemName = string.Empty;
    private int pendingItemInputReturnItemAmount = 1;

    private string pendingItemOutputName = string.Empty;

    public string PendingItemInputName => pendingItemInputName ?? string.Empty;
    public string PendingItemOutputName => pendingItemOutputName ?? string.Empty;

    public TileEntityFluidDecanter(Chunk chunk) : base(chunk)
    {
    }

    private void DevLog(string msg)
    {
        if (!IsDevLogging)
            return;

        Log.Out($"[Decanter][TE][{ToWorldPos()}] {msg}");
    }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.FluidDecanter);
    }

    public override void SetSimulatedByHLR(bool value)
    {
        simulatedByHLR = value;
    }

    public override IHLRSnapshot BuildHLRSnapshot(WorldBase world)
    {
        EnsureConfigLoaded();

        ulong now = world?.GetWorldTime() ?? 0UL;

        return new DecanterSnapshot
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
            SelectedFluidType = SelectedFluidType ?? string.Empty,
            SelectedRecipeKey = SelectedRecipeKey ?? string.Empty,
            MachineRecipeGroupsCsv = machineRecipeGroupsCsv ?? DefaultMachineRecipeGroup,
            SelectedFluidGraphId = SelectedFluidGraphId,
            PendingItemInput = pendingItemInput,
            PendingItemOutput = pendingItemOutput,
            PendingFluidInput = pendingFluidInput,
            PendingFluidOutput = pendingFluidOutput,
            PendingItemInputName = pendingItemInputName ?? string.Empty,
            PendingItemInputFluidAmountMg = pendingItemInputFluidAmountMg,
            PendingItemInputReturnItemName = pendingItemInputReturnItemName ?? string.Empty,
            PendingItemInputReturnItemAmount = pendingItemInputReturnItemAmount,
            PendingItemOutputName = pendingItemOutputName ?? string.Empty,
            CycleTickCounter = cycleTickCounter,
            CycleTickLength = cycleTickLength,
            PendingFluidOutputCapacityMg = pendingFluidOutputCapacityMg,
            LastAction = LastAction ?? string.Empty,
            LastBlockReason = LastBlockReason ?? string.Empty
        };
    }

    public override void ApplyHLRSnapshot(object snapshotObj)
    {
        if (!(snapshotObj is DecanterSnapshot snapshot))
            return;

        EnsureConfigLoaded();

        IsOn = snapshot.IsOn;

        SelectedInputChestPos = snapshot.SelectedInputChestPos;
        SelectedInputPipeGraphId = snapshot.SelectedInputPipeGraphId;

        SelectedOutputChestPos = snapshot.SelectedOutputChestPos;
        SelectedOutputMode = snapshot.SelectedOutputMode;
        SelectedOutputPipeGraphId = snapshot.SelectedOutputPipeGraphId;

        SelectedFluidType = (snapshot.SelectedFluidType ?? string.Empty).Trim().ToLowerInvariant();
        SelectedRecipeKey = snapshot.SelectedRecipeKey ?? string.Empty;
        SelectedFluidGraphId = snapshot.SelectedFluidGraphId;

        pendingItemInput = Math.Max(0, snapshot.PendingItemInput);
        pendingItemOutput = Math.Max(0, snapshot.PendingItemOutput);
        pendingFluidInput = Math.Max(0, snapshot.PendingFluidInput);
        pendingFluidOutput = Math.Max(0, snapshot.PendingFluidOutput);

        pendingItemInputName = snapshot.PendingItemInputName ?? string.Empty;
        pendingItemInputFluidAmountMg = Math.Max(0, snapshot.PendingItemInputFluidAmountMg);
        pendingItemInputReturnItemName = snapshot.PendingItemInputReturnItemName ?? string.Empty;
        pendingItemInputReturnItemAmount = Math.Max(1, snapshot.PendingItemInputReturnItemAmount);
        pendingItemOutputName = snapshot.PendingItemOutputName ?? string.Empty;

        cycleTickCounter = Math.Max(0, snapshot.CycleTickCounter);
        cycleTickLength = Math.Max(1, snapshot.CycleTickLength);
        pendingFluidOutputCapacityMg = Math.Max(0, snapshot.PendingFluidOutputCapacityMg);

        LastAction = snapshot.LastAction ?? string.Empty;
        LastBlockReason = snapshot.LastBlockReason ?? string.Empty;

        SanitizePendingState();
        ResolveSelectedInputContainer();
        ResolveSelectedOutputContainer();

        simulatedByHLR = false;
        NeedsUiRefresh = true;

        World currentWorld = GameManager.Instance?.World;
        if (currentWorld != null && !currentWorld.IsRemote())
            setModified();
    }

    public List<string> GetFluidOptions()
    {
        EnsureConfigLoaded();
        return new List<string>(fluidOptions);
    }

    public bool HasItemInputRequirement(WorldBase world)
    {
        if (world == null)
            return false;

        if (!HasSelectedInputTarget(world))
            return false;

        // Client only has replicated selections/targets; do availability checks on server.
        if (world.IsRemote())
            return !string.IsNullOrEmpty(SelectedRecipeKey);

        if (pendingItemInput > 0)
            return true;

        return TryFindMatchingInputRule(world, out _, out _, out _);
    }

    public bool HasItemOutputRequirement(WorldBase world)
    {
        if (world == null)
            return false;

        return HasSelectedOutputTarget(world);
    }

    public bool HasFluidOutputRequirement(WorldBase world)
    {
        EnsureConfigLoaded();

        if (world == null || string.IsNullOrEmpty(SelectedFluidType))
            return false;

        // Client uses replicated selected graph/type from server.
        if (world.IsRemote())
            return SelectedFluidGraphId != Guid.Empty;

        return TryGetCompatibleFluidGraph(world, SelectedFluidType, out _);
    }

    public bool AreAllRequirementsMet(WorldBase world)
    {
        return HasItemInputRequirement(world) &&
               HasItemOutputRequirement(world) &&
               HasFluidOutputRequirement(world);
    }

    public bool IsWaiting(WorldBase world)
    {
        return IsOn && !AreAllRequirementsMet(world);
    }

    private bool HasSelectedInputTarget(WorldBase world)
    {
        if (world == null || SelectedInputChestPos == Vector3i.zero)
            return false;

        List<InputTargetInfo> inputs = GetAvailableInputTargets(world);
        if (inputs == null || inputs.Count == 0)
            return false;

        bool foundByPosition = false;
        Guid reboundGraphId = Guid.Empty;

        for (int i = 0; i < inputs.Count; i++)
        {
            InputTargetInfo target = inputs[i];
            if (target == null || target.BlockPos != SelectedInputChestPos)
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

        List<OutputTargetInfo> outputs = GetAvailableOutputTargets(world);
        if (outputs == null || outputs.Count == 0)
            return false;

        bool foundByPositionAndMode = false;
        Guid reboundGraphId = Guid.Empty;

        for (int i = 0; i < outputs.Count; i++)
        {
            OutputTargetInfo target = outputs[i];
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

    private bool TryFindMatchingInputRule(WorldBase world, out FuelConversionRule rule, out string matchedItemName, out string blockedReason)
    {
        rule = null;
        matchedItemName = string.Empty;
        blockedReason = string.Empty;

        if (world == null)
        {
            blockedReason = "World unavailable";
            return false;
        }

        if (!HasSelectedInputTarget(world))
        {
            blockedReason = "Missing Item Input";
            return false;
        }

        if (string.IsNullOrEmpty(SelectedRecipeKey))
        {
            blockedReason = "No recipe selected";
            return false;
        }

        if (!TryGetConversionRule(SelectedRecipeKey, out rule) || rule == null)
        {
            blockedReason = "Selected recipe unavailable";
            return false;
        }

        if (string.IsNullOrEmpty(rule.InputItemName) || rule.InputItemCount <= 0)
        {
            blockedReason = "Selected recipe invalid";
            return false;
        }

        HashSet<string> candidates = new HashSet<string>(StringComparer.Ordinal);
        candidates.Add(rule.InputItemName);

        if (candidates.Count == 0)
        {
            blockedReason = "Selected recipe has no valid inputs";
            return false;
        }

        if (!PipeGraphManager.TryFindFirstMatchingStorageItem(
                world,
                0,
                SelectedInputPipeGraphId,
                SelectedInputChestPos,
                candidates,
                out matchedItemName,
                out int availableCount,
                out blockedReason))
        {
            if (string.IsNullOrEmpty(blockedReason))
                blockedReason = "No matching input item";
            return false;
        }

        if (!string.Equals(matchedItemName, rule.InputItemName, StringComparison.Ordinal))
        {
            blockedReason = "Input does not match selected recipe";
            return false;
        }

        if (availableCount < rule.InputItemCount)
        {
            blockedReason = $"Need {rule.InputItemCount}x {rule.InputItemName}";
            return false;
        }

        if (rule.FluidAmountMg <= 0)
        {
            blockedReason = "Invalid conversion rule";
            return false;
        }

        blockedReason = string.Empty;
        return true;
    }

    private bool TryGetConversionRule(string recipeKey, out FuelConversionRule rule)
    {
        rule = null;
        if (string.IsNullOrEmpty(recipeKey))
            return false;

        for (int i = 0; i < conversionRules.Count; i++)
        {
            FuelConversionRule candidate = conversionRules[i];
            if (candidate == null)
                continue;

            if (!string.Equals(candidate.RecipeKey, recipeKey, StringComparison.Ordinal))
                continue;

            rule = candidate;
            return true;
        }

        return false;
    }

    public bool ServerSelectInputContainer(Vector3i chestPos, string pipeGraphId)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            return false;

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
                MarkDirty();

            return true;
        }

        WorldBase world = GameManager.Instance?.World;
        if (world == null)
            return false;

        RefreshAvailableInputTargets(world);

        bool found = false;
        for (int i = 0; i < availableInputTargets.Count; i++)
        {
            InputTargetInfo target = availableInputTargets[i];
            if (target == null)
                continue;

            if (target.BlockPos == chestPos && target.PipeGraphId == parsedPipeGraphId)
            {
                found = true;
                break;
            }
        }

        if (!found)
            return false;

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

        bool found = false;
        for (int i = 0; i < availableOutputTargets.Count; i++)
        {
            OutputTargetInfo target = availableOutputTargets[i];
            if (target == null)
                continue;

            if (target.BlockPos == chestPos &&
                target.TransportMode == mode &&
                target.PipeGraphId == parsedPipeGraphId)
            {
                found = true;
                break;
            }
        }

        if (!found)
            return false;

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

    public bool ServerCycleFluidSelection(int direction)
    {
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            return false;

        EnsureConfigLoaded();

        if (conversionRules.Count == 0)
            return false;

        int index = -1;
        for (int i = 0; i < conversionRules.Count; i++)
        {
            FuelConversionRule candidate = conversionRules[i];
            if (candidate == null)
                continue;

            if (string.Equals(candidate.RecipeKey, SelectedRecipeKey, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            index = 0;

        int step = direction < 0 ? -1 : 1;
        int next = (index + step + conversionRules.Count) % conversionRules.Count;
        FuelConversionRule nextRule = conversionRules[next];
        if (nextRule == null)
            return false;

        if (string.Equals(nextRule.RecipeKey, SelectedRecipeKey, StringComparison.Ordinal))
            return false;

        bool hasPendingBuffers = pendingItemInput > 0 || pendingItemOutput > 0 || pendingFluidOutput > 0;
        if (hasPendingBuffers)
            return false;

        SelectedRecipeKey = nextRule.RecipeKey ?? string.Empty;
        SelectedFluidType = nextRule.FluidType ?? string.Empty;
        cycleTickLength = Math.Max(1, nextRule.CraftTimeTicks);

        WorldBase world = GameManager.Instance?.World;
        if (world != null)
            ResolveFluidOutputGraph(world);

        MarkDirty();
        return true;
    }

    public List<InputTargetInfo> GetAvailableInputTargets(WorldBase world)
    {
        if (world == null)
            return availableInputTargets ?? new List<InputTargetInfo>();

        if (world.IsRemote())
            return availableInputTargets ?? new List<InputTargetInfo>();

        RefreshAvailableInputTargets(world);
        return availableInputTargets;
    }

    public List<OutputTargetInfo> GetAvailableOutputTargets(WorldBase world)
    {
        if (world == null)
            return availableOutputTargets ?? new List<OutputTargetInfo>();

        if (world.IsRemote())
            return availableOutputTargets ?? new List<OutputTargetInfo>();

        RefreshAvailableOutputTargets(world);
        return availableOutputTargets;
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

    private static bool AreInputTargetsEqual(List<InputTargetInfo> left, List<InputTargetInfo> right)
    {
        int leftCount = left?.Count ?? 0;
        int rightCount = right?.Count ?? 0;
        if (leftCount != rightCount)
            return false;

        for (int i = 0; i < leftCount; i++)
        {
            InputTargetInfo leftTarget = left[i];
            InputTargetInfo rightTarget = right[i];

            if (leftTarget == null || rightTarget == null)
            {
                if (!ReferenceEquals(leftTarget, rightTarget))
                    return false;

                continue;
            }

            if (leftTarget.BlockPos != rightTarget.BlockPos ||
                leftTarget.PipeGraphId != rightTarget.PipeGraphId)
            {
                return false;
            }
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
            OutputTargetInfo leftTarget = left[i];
            OutputTargetInfo rightTarget = right[i];

            if (leftTarget == null || rightTarget == null)
            {
                if (!ReferenceEquals(leftTarget, rightTarget))
                    return false;

                continue;
            }

            if (leftTarget.BlockPos != rightTarget.BlockPos ||
                leftTarget.TransportMode != rightTarget.TransportMode ||
                leftTarget.PipeGraphId != rightTarget.PipeGraphId)
            {
                return false;
            }
        }

        return true;
    }

    public bool ResolveSelectedInputContainer()
    {
        WorldBase world = GameManager.Instance?.World;
        if (world == null)
            return false;

        TileEntityComposite resolved = null;
        if (SelectedInputChestPos != Vector3i.zero)
            resolved = world.GetTileEntity(SelectedInputChestPos) as TileEntityComposite;

        if (!object.ReferenceEquals(selectedInputContainer, resolved))
        {
            selectedInputContainer = resolved;
            return true;
        }

        return false;
    }

    public bool ResolveSelectedOutputContainer()
    {
        WorldBase world = GameManager.Instance?.World;
        if (world == null)
            return false;

        TileEntityComposite resolved = null;
        if (SelectedOutputChestPos != Vector3i.zero)
            resolved = world.GetTileEntity(SelectedOutputChestPos) as TileEntityComposite;

        if (!object.ReferenceEquals(selectedOutputContainer, resolved))
        {
            selectedOutputContainer = resolved;
            return true;
        }

        return false;
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

    private bool SanitizePendingState()
    {
        bool changed = false;

        if (pendingItemInput <= 0)
        {
            if (pendingItemInput != 0)
            {
                pendingItemInput = 0;
                changed = true;
            }

            if (!string.IsNullOrEmpty(pendingItemInputName))
            {
                pendingItemInputName = string.Empty;
                changed = true;
            }

            if (pendingItemInputFluidAmountMg != 0)
            {
                pendingItemInputFluidAmountMg = 0;
                changed = true;
            }

            if (!string.IsNullOrEmpty(pendingItemInputReturnItemName))
            {
                pendingItemInputReturnItemName = string.Empty;
                changed = true;
            }

            if (pendingItemInputReturnItemAmount != 1)
            {
                pendingItemInputReturnItemAmount = 1;
                changed = true;
            }
        }
        else
        {
            if (string.IsNullOrEmpty(pendingItemInputName) || pendingItemInputFluidAmountMg <= 0)
            {
                pendingItemInput = 0;
                pendingItemInputName = string.Empty;
                pendingItemInputFluidAmountMg = 0;
                pendingItemInputReturnItemName = string.Empty;
                pendingItemInputReturnItemAmount = 1;
                changed = true;
            }
            else if (pendingItemInputReturnItemAmount <= 0)
            {
                pendingItemInputReturnItemAmount = 1;
                changed = true;
            }
        }

        if (pendingItemOutput <= 0)
        {
            if (pendingItemOutput != 0)
            {
                pendingItemOutput = 0;
                changed = true;
            }

            if (!string.IsNullOrEmpty(pendingItemOutputName))
            {
                pendingItemOutputName = string.Empty;
                changed = true;
            }
        }
        else if (string.IsNullOrEmpty(pendingItemOutputName))
        {
            pendingItemOutput = 0;
            changed = true;
        }

        if (pendingFluidInput != 0)
        {
            pendingFluidInput = 0;
            changed = true;
        }

        if (pendingFluidOutput < 0)
        {
            pendingFluidOutput = 0;
            changed = true;
        }

        return changed;
    }

    private bool TryDepositToAdjacentOutput(WorldBase world, string itemName, int requestedCount, out int depositedCount, out string blockedReason)
    {
        depositedCount = 0;
        blockedReason = string.Empty;

        if (world == null || string.IsNullOrEmpty(itemName) || requestedCount <= 0)
        {
            blockedReason = "Invalid output request";
            return false;
        }

        if (selectedOutputContainer == null || selectedOutputContainer.ToWorldPos() != SelectedOutputChestPos)
            selectedOutputContainer = world.GetTileEntity(SelectedOutputChestPos) as TileEntityComposite;

        if (selectedOutputContainer == null)
        {
            blockedReason = "Output storage unavailable";
            return false;
        }

        TEFeatureStorage storage = selectedOutputContainer.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
        {
            blockedReason = "Output storage unavailable";
            return false;
        }

        if (storage.IsUserAccessing())
        {
            blockedReason = "Output storage busy";
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
            if (slot.IsEmpty() || slot.count <= 0 || slot.itemValue == null)
                continue;

            if (slot.itemValue.type != itemValue.type)
                continue;

            int maxStack = slot.itemValue.ItemClass.Stacknumber.Value;
            if (maxStack <= 0)
                maxStack = 1;

            int space = maxStack - slot.count;
            if (space <= 0)
                continue;

            int move = Math.Min(space, remaining);
            slot.count += move;
            storage.items[i] = slot;
            remaining -= move;
        }

        for (int i = 0; i < storage.items.Length && remaining > 0; i++)
        {
            if (!storage.items[i].IsEmpty())
                continue;

            int maxStack = itemValue.ItemClass.Stacknumber.Value;
            if (maxStack <= 0)
                maxStack = 1;

            int move = Math.Min(maxStack, remaining);
            storage.items[i] = new ItemStack(itemValue.Clone(), move);
            remaining -= move;
        }

        depositedCount = requestedCount - remaining;
        if (depositedCount <= 0)
        {
            blockedReason = "Output storage full";
            return false;
        }

        storage.SetModified();
        blockedReason = string.Empty;
        return true;
    }

    private bool TryFlushPendingItemOutput(WorldBase world, out bool changed, out string blockedReason)
    {
        changed = false;
        blockedReason = string.Empty;

        if (pendingItemOutput <= 0)
            return true;

        if (SelectedOutputChestPos == Vector3i.zero)
        {
            blockedReason = "Missing Item Output";
            DevLog($"ITEM FLUSH BLOCKED -> {blockedReason}");
            return false;
        }

        if (string.IsNullOrEmpty(pendingItemOutputName))
        {
            pendingItemOutput = 0;
            changed = true;
            blockedReason = "Pending item output invalid";
            DevLog($"ITEM FLUSH RESET -> {blockedReason}");
            return false;
        }

        int before = pendingItemOutput;
        int depositedCount = 0;

        if (SelectedOutputMode == OutputTransportMode.Pipe)
        {
            if (SelectedOutputPipeGraphId == Guid.Empty)
            {
                blockedReason = "Missing Item Output";
                DevLog($"PIPE DEPOSIT BLOCKED -> {blockedReason}");
                return false;
            }

            Dictionary<string, int> toDeposit = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { pendingItemOutputName, pendingItemOutput }
            };

            DevLog($"PIPE DEPOSIT ATTEMPT -> graph={SelectedOutputPipeGraphId} pos={SelectedOutputChestPos} request={pendingItemOutputName}:{pendingItemOutput}");
            if (!PipeGraphManager.TryDepositStorageItems(world, 0, SelectedOutputPipeGraphId, SelectedOutputChestPos, toDeposit, out Dictionary<string, int> deposited) ||
                deposited == null ||
                !deposited.TryGetValue(pendingItemOutputName, out depositedCount) ||
                depositedCount <= 0)
            {
                blockedReason = "Item output blocked";
                DevLog($"PIPE DEPOSIT BLOCKED -> graph={SelectedOutputPipeGraphId} pos={SelectedOutputChestPos} item={pendingItemOutputName} reason={blockedReason}");
                return false;
            }

            DevLog($"PIPE DEPOSIT SUCCESS -> graph={SelectedOutputPipeGraphId} pos={SelectedOutputChestPos} item={pendingItemOutputName} deposited={depositedCount}");
        }
        else
        {
            DevLog($"ADJACENT DEPOSIT ATTEMPT -> pos={SelectedOutputChestPos} request={pendingItemOutputName}:{pendingItemOutput}");
            if (!TryDepositToAdjacentOutput(world, pendingItemOutputName, pendingItemOutput, out depositedCount, out blockedReason) || depositedCount <= 0)
            {
                if (string.IsNullOrEmpty(blockedReason))
                    blockedReason = "Item output blocked";
                DevLog($"ADJACENT DEPOSIT BLOCKED -> pos={SelectedOutputChestPos} item={pendingItemOutputName} reason={blockedReason}");
                return false;
            }

            DevLog($"ADJACENT DEPOSIT SUCCESS -> pos={SelectedOutputChestPos} item={pendingItemOutputName} deposited={depositedCount}");
        }

        pendingItemOutput -= depositedCount;
        if (pendingItemOutput < 0)
            pendingItemOutput = 0;

        if (pendingItemOutput == 0)
            pendingItemOutputName = string.Empty;

        changed = pendingItemOutput != before;

        if (pendingItemOutput > 0)
        {
            blockedReason = "Item output blocked";
            DevLog($"ITEM FLUSH PARTIAL -> item={pendingItemOutputName} remaining={pendingItemOutput}");
            return false;
        }

        blockedReason = string.Empty;
        return true;
    }

    private bool TryInjectFluidPartial(WorldBase world, int requestedMg, out int injectedMg, out string blockedReason)
    {
        injectedMg = 0;
        blockedReason = string.Empty;

        if (requestedMg <= 0)
            return true;

        DevLog($"FLUID INJECT ATTEMPT -> graph={SelectedFluidGraphId} fluid={SelectedFluidType} requestMg={requestedMg}");

        if (world == null || SelectedFluidGraphId == Guid.Empty || string.IsNullOrEmpty(SelectedFluidType))
        {
            blockedReason = "Missing/Invalid Fluid Output";
            DevLog($"FLUID INJECT BLOCKED -> {blockedReason} graph={SelectedFluidGraphId} fluid={SelectedFluidType} requestMg={requestedMg}");
            return false;
        }

        if (FluidGraphManager.TryInjectFluid(world, 0, SelectedFluidGraphId, SelectedFluidType, requestedMg, out blockedReason))
        {
            injectedMg = requestedMg;
            blockedReason = string.Empty;
            DevLog($"FLUID INJECT SUCCESS -> graph={SelectedFluidGraphId} fluid={SelectedFluidType} injectedMg={injectedMg}");
            return true;
        }

        if (string.Equals(blockedReason, "Fluid graph unavailable", StringComparison.Ordinal) ||
            string.Equals(blockedReason, "No active pump", StringComparison.Ordinal))
        {
            Guid staleGraphId = SelectedFluidGraphId;
            ResolveFluidOutputGraph(world);
            DevLog($"FLUID INJECT RETRY RESOLVE -> staleGraph={staleGraphId} resolvedGraph={SelectedFluidGraphId} fluid={SelectedFluidType}");

            if (SelectedFluidGraphId != Guid.Empty &&
                FluidGraphManager.TryInjectFluid(world, 0, SelectedFluidGraphId, SelectedFluidType, requestedMg, out blockedReason))
            {
                injectedMg = requestedMg;
                blockedReason = string.Empty;
                DevLog($"FLUID INJECT SUCCESS -> graph={SelectedFluidGraphId} fluid={SelectedFluidType} injectedMg={injectedMg}");
                return true;
            }
        }

        bool retryWithSmallerAmount =
            string.Equals(blockedReason, "Graph throughput full", StringComparison.Ordinal) ||
            string.Equals(blockedReason, "No storage room", StringComparison.Ordinal);

        if (!retryWithSmallerAmount || requestedMg <= 1)
        {
            DevLog($"FLUID INJECT BLOCKED -> graph={SelectedFluidGraphId} fluid={SelectedFluidType} requestMg={requestedMg} reason={blockedReason}");
            return false;
        }

        int attempt = requestedMg / 2;
        while (attempt > 0)
        {
            if (FluidGraphManager.TryInjectFluid(world, 0, SelectedFluidGraphId, SelectedFluidType, attempt, out string smallerReason))
            {
                injectedMg = attempt;
                blockedReason = string.Empty;
                DevLog($"FLUID INJECT PARTIAL -> graph={SelectedFluidGraphId} fluid={SelectedFluidType} injectedMg={injectedMg} requestedMg={requestedMg}");
                return true;
            }

            bool canContinue =
                string.Equals(smallerReason, "Graph throughput full", StringComparison.Ordinal) ||
                string.Equals(smallerReason, "No storage room", StringComparison.Ordinal);

            if (!canContinue)
            {
                blockedReason = smallerReason;
                DevLog($"FLUID INJECT BLOCKED -> graph={SelectedFluidGraphId} fluid={SelectedFluidType} requestMg={attempt} reason={blockedReason}");
                return false;
            }

            attempt /= 2;
        }

        DevLog($"FLUID INJECT BLOCKED -> graph={SelectedFluidGraphId} fluid={SelectedFluidType} requestMg={requestedMg} reason={blockedReason}");
        return false;
    }

    private bool TryFlushPendingFluidOutput(WorldBase world, out bool changed, out string blockedReason)
    {
        changed = false;
        blockedReason = string.Empty;

        if (pendingFluidOutput <= 0)
            return true;

        if (!TryInjectFluidPartial(world, pendingFluidOutput, out int injectedMg, out blockedReason))
        {
            DevLog($"FLUID FLUSH BLOCKED -> pendingMg={pendingFluidOutput} reason={blockedReason}");
            return false;
        }

        if (injectedMg <= 0)
            return false;

        pendingFluidOutput -= injectedMg;
        if (pendingFluidOutput < 0)
            pendingFluidOutput = 0;

        changed = true;
        DevLog($"FLUID FLUSH SUCCESS -> injectedMg={injectedMg} remainingMg={pendingFluidOutput}");

        if (pendingFluidOutput > 0)
        {
            blockedReason = "Graph throughput full";
            DevLog($"FLUID FLUSH PARTIAL -> remainingMg={pendingFluidOutput} reason={blockedReason}");
            return false;
        }

        blockedReason = string.Empty;
        return true;
    }

    private bool TryRunCycle(WorldBase world, out string cycleAction, out string blockedReason)
    {
        cycleAction = "Running";
        blockedReason = string.Empty;

        if (world == null)
        {
            blockedReason = "World unavailable";
            DevLog($"CYCLE BLOCKED -> {blockedReason}");
            return false;
        }

        if (pendingItemInput > 0)
        {
            if (pendingItemOutput > 0)
            {
                blockedReason = "Pending item output full";
                DevLog($"CYCLE BLOCKED -> {blockedReason}");
                return false;
            }

            if (string.IsNullOrEmpty(pendingItemInputName) || pendingItemInputFluidAmountMg <= 0)
            {
                pendingItemInput = 0;
                pendingItemInputName = string.Empty;
                pendingItemInputFluidAmountMg = 0;
                pendingItemInputReturnItemName = string.Empty;
                pendingItemInputReturnItemAmount = 1;
                blockedReason = "Pending input invalid";
                DevLog($"CYCLE RESET -> {blockedReason}");
                return true;
            }

            int freeCapacity = pendingFluidOutputCapacityMg - pendingFluidOutput;
            if (freeCapacity < pendingItemInputFluidAmountMg)
            {
                blockedReason = "Pending fluid output full";
                DevLog($"CYCLE BLOCKED -> {blockedReason} freeMg={freeCapacity} requiredMg={pendingItemInputFluidAmountMg}");
                return false;
            }

            int convertedFluidMg = pendingItemInputFluidAmountMg;
            string returnItem = pendingItemInputReturnItemName;
            int returnItemAmount = Math.Max(1, pendingItemInputReturnItemAmount);

            pendingItemInput = 0;
            pendingItemInputName = string.Empty;
            pendingItemInputFluidAmountMg = 0;
            pendingItemInputReturnItemName = string.Empty;
            pendingItemInputReturnItemAmount = 1;

            pendingFluidOutput += Math.Max(0, convertedFluidMg);

            if (!string.IsNullOrEmpty(returnItem))
            {
                ItemValue returnValue = ItemClass.GetItem(returnItem, false);
                if (returnValue != null && returnValue.type != ItemValue.None.type)
                {
                    pendingItemOutput = returnItemAmount;
                    pendingItemOutputName = returnItem;
                }
            }

            cycleAction = "Converted";
            DevLog($"CYCLE CONVERTED -> fluidMg={convertedFluidMg} returnItem={returnItem} returnItemAmount={returnItemAmount} pendingFluidOutMg={pendingFluidOutput} pendingItemOut={pendingItemOutput}");
            return true;
        }

        if (pendingItemOutput > 0)
        {
            blockedReason = "Pending item output full";
            DevLog($"CYCLE BLOCKED -> {blockedReason}");
            return false;
        }

        if (pendingFluidOutput >= pendingFluidOutputCapacityMg)
        {
            blockedReason = "Pending fluid output full";
            DevLog($"CYCLE BLOCKED -> {blockedReason} currentMg={pendingFluidOutput} capacityMg={pendingFluidOutputCapacityMg}");
            return false;
        }

        if (!TryFindMatchingInputRule(world, out FuelConversionRule rule, out string matchedItemName, out blockedReason) || rule == null)
        {
            if (string.IsNullOrEmpty(blockedReason))
                blockedReason = "No matching input item";
            DevLog($"CYCLE BLOCKED -> {blockedReason} fluid={SelectedFluidType}");
            return false;
        }

        if (pendingItemOutput > 0)
        {
            blockedReason = "Pending item output full";
            DevLog($"CYCLE BLOCKED -> {blockedReason}");
            return false;
        }

        Dictionary<string, int> request = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { matchedItemName, rule.InputItemCount }
        };

        DevLog($"PIPE CONSUME ATTEMPT -> graph={SelectedInputPipeGraphId} pos={SelectedInputChestPos} request={matchedItemName}:{rule.InputItemCount}");
        if (!PipeGraphManager.TryConsumeStorageItems(world, 0, SelectedInputPipeGraphId, SelectedInputChestPos, request, out Dictionary<string, int> consumed) ||
            consumed == null ||
            !consumed.TryGetValue(matchedItemName, out int consumedCount) ||
            consumedCount <= 0)
        {
            blockedReason = "Input item unavailable";
            DevLog($"PIPE CONSUME BLOCKED -> graph={SelectedInputPipeGraphId} pos={SelectedInputChestPos} item={matchedItemName} reason={blockedReason}");
            return false;
        }

        DevLog($"PIPE CONSUME SUCCESS -> graph={SelectedInputPipeGraphId} pos={SelectedInputChestPos} item={matchedItemName} consumed={consumedCount}");

        pendingItemInput = Math.Max(1, consumedCount);
        pendingItemInputName = matchedItemName;
        pendingItemInputFluidAmountMg = Math.Max(0, rule.FluidAmountMg);
        pendingItemInputReturnItemName = rule.ReturnItemName ?? string.Empty;
        pendingItemInputReturnItemAmount = Math.Max(1, rule.ReturnItemAmount);

        cycleAction = "Requested Input";
        blockedReason = string.Empty;
        return true;
    }

    public override void UpdateTick(World world)
    {
        if (world == null)
            return;

        EnsureConfigLoaded();

        if (world.IsRemote())
            return;

        if (simulatedByHLR)
            return;

        bool changed = false;

        refreshTicker++;
        if (refreshTicker >= 10)
        {
            refreshTicker = 0;
            RefreshAvailableInputTargets(world);
            RefreshAvailableOutputTargets(world);
            changed |= ResolveSelectedInputContainer();
            changed |= ResolveSelectedOutputContainer();
            changed |= ResolveFluidOutputGraph(world);
        }

        changed |= SanitizePendingState();

        string runtimeBlockReason = string.Empty;

        if (IsOn)
        {
            if (!TryFlushPendingItemOutput(world, out bool itemOutputChanged, out string itemOutputBlockedReason))
            {
                if (string.IsNullOrEmpty(runtimeBlockReason))
                    runtimeBlockReason = itemOutputBlockedReason;
            }
            changed |= itemOutputChanged;

            if (!TryFlushPendingFluidOutput(world, out bool fluidOutputChanged, out string fluidOutputBlockedReason))
            {
                if (string.IsNullOrEmpty(runtimeBlockReason))
                    runtimeBlockReason = fluidOutputBlockedReason;
            }
            changed |= fluidOutputChanged;
        }

        bool requirementsMet = AreAllRequirementsMet(world);

        string nextAction;
        string nextReason = string.Empty;

        if (!IsOn)
        {
            nextAction = "Idle";
            cycleTickCounter = 0;
        }
        else if (!requirementsMet)
        {
            nextAction = "Waiting";
            nextReason = GetFirstMissingRequirementReason(world);
            cycleTickCounter = 0;
        }
        else
        {
            nextAction = "Running";

            cycleTickCounter++;
            if (cycleTickCounter >= cycleTickLength)
            {
                cycleTickCounter = 0;
                changed |= TryRunCycle(world, out string cycleAction, out string cycleBlockedReason);

                if (!string.IsNullOrEmpty(cycleAction))
                    nextAction = cycleAction;

                if (!string.IsNullOrEmpty(cycleBlockedReason))
                    nextReason = cycleBlockedReason;
            }

            if (string.IsNullOrEmpty(nextReason) && !string.IsNullOrEmpty(runtimeBlockReason))
                nextReason = runtimeBlockReason;
        }

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

        int nextSignature = BuildStateSignature(requirementsMet);
        if (nextSignature != lastStateSignature)
        {
            lastStateSignature = nextSignature;
            changed = true;
        }

        ulong now = (ulong)world.worldTime;
        bool periodicUiSync = now >= lastUiSyncWorldTime + 20UL;
        if (periodicUiSync)
            lastUiSyncWorldTime = now;

        if (changed || periodicUiSync)
            MarkDirty();
    }
    protected override void OnPowerStateChanged(bool state)
    {
        if (state)
        {
            LastAction = "Waiting";
        }
        else
        {
            LastAction = "Idle";
            LastBlockReason = string.Empty;
            cycleTickCounter = 0;
        }
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        if (mode == StreamModeWrite.ToClient)
        {
            bw.Write(ClientSyncVersion);

            bw.Write(IsOn);

            bw.Write(SelectedInputChestPos.x);
            bw.Write(SelectedInputChestPos.y);
            bw.Write(SelectedInputChestPos.z);
            bw.Write(SelectedInputPipeGraphId.ToString());

            bw.Write(SelectedOutputChestPos.x);
            bw.Write(SelectedOutputChestPos.y);
            bw.Write(SelectedOutputChestPos.z);
            bw.Write((int)SelectedOutputMode);
            bw.Write(SelectedOutputPipeGraphId.ToString());

            bw.Write(SelectedFluidType ?? string.Empty);
            bw.Write(SelectedRecipeKey ?? string.Empty);
            bw.Write(SelectedFluidGraphId.ToString());

            int inputCount = Math.Min(MaxSerializedInputTargets, availableInputTargets?.Count ?? 0);
            bw.Write(inputCount);
            for (int i = 0; i < inputCount; i++)
            {
                InputTargetInfo target = availableInputTargets[i];
                bw.Write(target.BlockPos.x);
                bw.Write(target.BlockPos.y);
                bw.Write(target.BlockPos.z);
                bw.Write(target.PipeGraphId.ToString());
            }

            int outputCount = Math.Min(MaxSerializedOutputTargets, availableOutputTargets?.Count ?? 0);
            bw.Write(outputCount);
            for (int i = 0; i < outputCount; i++)
            {
                OutputTargetInfo target = availableOutputTargets[i];
                bw.Write(target.BlockPos.x);
                bw.Write(target.BlockPos.y);
                bw.Write(target.BlockPos.z);
                bw.Write((int)target.TransportMode);
                bw.Write(target.PipeGraphId.ToString());
            }

            int fluidCount = Math.Min(MaxSerializedFluidOptions, fluidOptions.Count);
            bw.Write(fluidCount);
            for (int i = 0; i < fluidCount; i++)
                bw.Write(fluidOptions[i] ?? string.Empty);

            bw.Write(pendingItemInput);
            bw.Write(pendingItemOutput);
            bw.Write(pendingFluidInput);
            bw.Write(pendingFluidOutput);
            bw.Write(pendingItemInputName ?? string.Empty);
            bw.Write(pendingItemOutputName ?? string.Empty);

            bw.Write(cycleTickCounter);
            bw.Write(cycleTickLength);
            bw.Write(pendingFluidOutputCapacityMg);

            bw.Write(LastAction ?? string.Empty);
            bw.Write(LastBlockReason ?? string.Empty);
            return;
        }

        if (mode != StreamModeWrite.Persistency)
            return;

        bw.Write(PersistVersion);

        bw.Write(IsOn);

        bw.Write(SelectedInputChestPos.x);
        bw.Write(SelectedInputChestPos.y);
        bw.Write(SelectedInputChestPos.z);
        bw.Write(SelectedInputPipeGraphId.ToString());

        bw.Write(SelectedOutputChestPos.x);
        bw.Write(SelectedOutputChestPos.y);
        bw.Write(SelectedOutputChestPos.z);
        bw.Write((int)SelectedOutputMode);
        bw.Write(SelectedOutputPipeGraphId.ToString());

        bw.Write(SelectedFluidType ?? string.Empty);
        bw.Write(SelectedRecipeKey ?? string.Empty);
        bw.Write(SelectedFluidGraphId.ToString());

        bw.Write(pendingItemInput);
        bw.Write(pendingItemOutput);
        bw.Write(pendingFluidInput);
        bw.Write(pendingFluidOutput);

        bw.Write(pendingItemInputName ?? string.Empty);
        bw.Write(pendingItemInputFluidAmountMg);
        bw.Write(pendingItemInputReturnItemName ?? string.Empty);
        bw.Write(pendingItemInputReturnItemAmount);
        bw.Write(pendingItemOutputName ?? string.Empty);

        bw.Write(cycleTickCounter);
        bw.Write(cycleTickLength);
        bw.Write(pendingFluidOutputCapacityMg);

        bw.Write(LastAction ?? string.Empty);
        bw.Write(LastBlockReason ?? string.Empty);
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);

        try
        {
            if (mode == StreamModeRead.FromServer)
            {
                int clientSyncVersion = br.ReadInt32();

                IsOn = br.ReadBoolean();

                int inX = br.ReadInt32();
                int inY = br.ReadInt32();
                int inZ = br.ReadInt32();
                SelectedInputChestPos = new Vector3i(inX, inY, inZ);

                string inputGraphId = br.ReadString();
                if (!Guid.TryParse(inputGraphId, out SelectedInputPipeGraphId))
                    SelectedInputPipeGraphId = Guid.Empty;

                int outX = br.ReadInt32();
                int outY = br.ReadInt32();
                int outZ = br.ReadInt32();
                SelectedOutputChestPos = new Vector3i(outX, outY, outZ);

                SelectedOutputMode = (OutputTransportMode)br.ReadInt32();
                string outputGraphId = br.ReadString();
                if (!Guid.TryParse(outputGraphId, out SelectedOutputPipeGraphId))
                    SelectedOutputPipeGraphId = Guid.Empty;

                SelectedFluidType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
                if (clientSyncVersion >= 4)
                    SelectedRecipeKey = br.ReadString() ?? string.Empty;
                else
                    SelectedRecipeKey = string.Empty;

                string fluidGraphId = br.ReadString();
                if (!Guid.TryParse(fluidGraphId, out SelectedFluidGraphId))
                    SelectedFluidGraphId = Guid.Empty;

                int inputCount = br.ReadInt32();
                if (inputCount < 0 || inputCount > MaxSerializedInputTargets)
                    throw new InvalidOperationException($"Invalid fluid decanter input target count: {inputCount}");

                availableInputTargets = new List<InputTargetInfo>(inputCount);
                for (int i = 0; i < inputCount; i++)
                {
                    int tx = br.ReadInt32();
                    int ty = br.ReadInt32();
                    int tz = br.ReadInt32();
                    string graph = br.ReadString();

                    Guid parsed;
                    if (!Guid.TryParse(graph, out parsed))
                        parsed = Guid.Empty;

                    availableInputTargets.Add(new InputTargetInfo(new Vector3i(tx, ty, tz), parsed));
                }

                int outputCount = br.ReadInt32();
                if (outputCount < 0 || outputCount > MaxSerializedOutputTargets)
                    throw new InvalidOperationException($"Invalid fluid decanter output target count: {outputCount}");

                availableOutputTargets = new List<OutputTargetInfo>(outputCount);
                for (int i = 0; i < outputCount; i++)
                {
                    int tx = br.ReadInt32();
                    int ty = br.ReadInt32();
                    int tz = br.ReadInt32();
                    OutputTransportMode modeValue = (OutputTransportMode)br.ReadInt32();
                    string graph = br.ReadString();

                    Guid parsed;
                    if (!Guid.TryParse(graph, out parsed))
                        parsed = Guid.Empty;

                    availableOutputTargets.Add(new OutputTargetInfo(new Vector3i(tx, ty, tz), modeValue, parsed));
                }

                int fluidCount = br.ReadInt32();
                if (fluidCount < 0 || fluidCount > MaxSerializedFluidOptions)
                    throw new InvalidOperationException($"Invalid fluid options count: {fluidCount}");

                fluidOptions.Clear();
                for (int i = 0; i < fluidCount; i++)
                {
                    string fluid = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(fluid) && !fluidOptions.Contains(fluid))
                        fluidOptions.Add(fluid);
                }

                pendingItemInput = Math.Max(0, br.ReadInt32());
                pendingItemOutput = Math.Max(0, br.ReadInt32());
                pendingFluidInput = Math.Max(0, br.ReadInt32());
                pendingFluidOutput = Math.Max(0, br.ReadInt32());

                if (clientSyncVersion >= 2)
                {
                    pendingItemInputName = br.ReadString() ?? string.Empty;
                    pendingItemOutputName = br.ReadString() ?? string.Empty;
                }
                else
                {
                    pendingItemInputName = string.Empty;
                    pendingItemOutputName = string.Empty;
                }

                if (pendingItemInput <= 0)
                    pendingItemInputName = string.Empty;

                if (pendingItemOutput <= 0)
                    pendingItemOutputName = string.Empty;

                pendingItemInputFluidAmountMg = 0;
                pendingItemInputReturnItemName = string.Empty;
                pendingItemInputReturnItemAmount = 1;

                cycleTickCounter = Math.Max(0, br.ReadInt32());
                cycleTickLength = Math.Max(1, br.ReadInt32());
                pendingFluidOutputCapacityMg = Math.Max(0, br.ReadInt32());

                LastAction = br.ReadString() ?? string.Empty;
                LastBlockReason = br.ReadString() ?? string.Empty;

                NeedsUiRefresh = true;
                return;
            }

            if (mode != StreamModeRead.Persistency)
                return;

            int version = br.ReadInt32();

            if (version >= 101)
            {
                IsOn = br.ReadBoolean();

                int inX = br.ReadInt32();
                int inY = br.ReadInt32();
                int inZ = br.ReadInt32();
                SelectedInputChestPos = new Vector3i(inX, inY, inZ);
                string inputGraph = br.ReadString();
                if (!Guid.TryParse(inputGraph, out SelectedInputPipeGraphId))
                    SelectedInputPipeGraphId = Guid.Empty;

                int outX = br.ReadInt32();
                int outY = br.ReadInt32();
                int outZ = br.ReadInt32();
                SelectedOutputChestPos = new Vector3i(outX, outY, outZ);
                SelectedOutputMode = (OutputTransportMode)br.ReadInt32();
                string outputGraph = br.ReadString();
                if (!Guid.TryParse(outputGraph, out SelectedOutputPipeGraphId))
                    SelectedOutputPipeGraphId = Guid.Empty;

                SelectedFluidType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
                if (version >= 105)
                    SelectedRecipeKey = br.ReadString() ?? string.Empty;
                else
                    SelectedRecipeKey = string.Empty;
                string fluidGraph = br.ReadString();
                if (!Guid.TryParse(fluidGraph, out SelectedFluidGraphId))
                    SelectedFluidGraphId = Guid.Empty;

                pendingItemInput = Math.Max(0, br.ReadInt32());
                pendingItemOutput = Math.Max(0, br.ReadInt32());
                pendingFluidInput = Math.Max(0, br.ReadInt32());
                pendingFluidOutput = Math.Max(0, br.ReadInt32());

                if (version >= 102)
                {
                    pendingItemInputName = br.ReadString() ?? string.Empty;
                    pendingItemInputFluidAmountMg = Math.Max(0, br.ReadInt32());
                    pendingItemInputReturnItemName = br.ReadString() ?? string.Empty;
                    if (version >= 103)
                        pendingItemInputReturnItemAmount = Math.Max(1, br.ReadInt32());
                    else
                        pendingItemInputReturnItemAmount = 1;
                    pendingItemOutputName = br.ReadString() ?? string.Empty;
                }
                else
                {
                    pendingItemInputName = string.Empty;
                    pendingItemInputFluidAmountMg = 0;
                    pendingItemInputReturnItemName = string.Empty;
                    pendingItemInputReturnItemAmount = 1;
                    pendingItemOutputName = string.Empty;

                    // v101 did not persist pending item metadata needed to continue processing safely.
                    pendingItemInput = 0;
                    pendingItemOutput = 0;
                }

                cycleTickCounter = Math.Max(0, br.ReadInt32());
                cycleTickLength = Math.Max(1, br.ReadInt32());
                pendingFluidOutputCapacityMg = Math.Max(0, br.ReadInt32());

                LastAction = br.ReadString() ?? string.Empty;
                LastBlockReason = br.ReadString() ?? string.Empty;
            }
            else
            {
                if (version >= 1)
                {
                    br.ReadString();
                    br.ReadInt32();
                }

                if (version >= 3)
                {
                    br.ReadString();
                    br.ReadBoolean();
                    br.ReadBoolean();
                }

                ResetState();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[FluidDecanter][READ] Failed at {ToWorldPos()} mode={mode}: {ex.Message}");
            ResetState();
        }
    }

    private void ResetState()
    {
        IsOn = false;

        SelectedInputChestPos = Vector3i.zero;
        SelectedInputPipeGraphId = Guid.Empty;
        SelectedOutputChestPos = Vector3i.zero;
        SelectedOutputMode = OutputTransportMode.Adjacent;
        SelectedOutputPipeGraphId = Guid.Empty;
        SelectedFluidType = string.Empty;
        SelectedRecipeKey = string.Empty;
        machineRecipeGroupsCsv = DefaultMachineRecipeGroup;
        SelectedFluidGraphId = Guid.Empty;

        availableInputTargets = new List<InputTargetInfo>();
        availableOutputTargets = new List<OutputTargetInfo>();

        pendingItemInput = 0;
        pendingItemOutput = 0;
        pendingFluidInput = 0;
        pendingFluidOutput = 0;

        pendingItemInputName = string.Empty;
        pendingItemInputFluidAmountMg = 0;
        pendingItemInputReturnItemName = string.Empty;
        pendingItemInputReturnItemAmount = 1;
        pendingItemOutputName = string.Empty;

        cycleTickCounter = 0;
        cycleTickLength = 20;
        pendingFluidOutputCapacityMg = 5000;

        LastAction = "Idle";
        LastBlockReason = string.Empty;
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

        conversionRules.Clear();
        fluidOptions.Clear();

        List<MachineRecipe> machineRecipes = MachineRecipeRegistry.GetRecipesForMachineGroups(machineRecipeGroupsCsv, true);
        for (int i = 0; i < machineRecipes.Count; i++)
        {
            MachineRecipe machineRecipe = machineRecipes[i];
            if (!TryReadMachineRecipeAsDecanterRule(
                    machineRecipe,
                    defaultCycleTickLength,
                    out string inputItemName,
                    out int inputItemCount,
                    out string fluidType,
                    out int fluidAmountMg,
                    out string returnItemName,
                    out int returnItemAmount,
                    out int craftTimeTicks,
                    out string error))
            {
                WarnInvalidRecipeOnce(machineRecipe, error);
                continue;
            }

            conversionRules.Add(
                new FuelConversionRule
                {
                    RecipeKey = machineRecipe.NormalizedKey,
                    InputItemName = inputItemName,
                    InputItemCount = inputItemCount,
                    FluidType = fluidType,
                    FluidAmountMg = fluidAmountMg,
                    ReturnItemName = returnItemName,
                    ReturnItemAmount = returnItemAmount,
                    CraftTimeTicks = craftTimeTicks
                });
        }

        for (int i = 0; i < conversionRules.Count; i++)
        {
            FuelConversionRule rule = conversionRules[i];
            if (rule == null || string.IsNullOrEmpty(rule.FluidType))
                continue;

            if (!fluidOptions.Contains(rule.FluidType))
                fluidOptions.Add(rule.FluidType);
        }

        FuelConversionRule selectedRule = null;
        if (!TryGetConversionRule(SelectedRecipeKey, out selectedRule) || selectedRule == null)
        {
            if (!string.IsNullOrEmpty(SelectedFluidType))
            {
                string normalizedSelectedFluid = SelectedFluidType.Trim().ToLowerInvariant();
                for (int i = 0; i < conversionRules.Count; i++)
                {
                    FuelConversionRule candidate = conversionRules[i];
                    if (candidate == null)
                        continue;

                    if (!string.Equals(candidate.FluidType, normalizedSelectedFluid, StringComparison.Ordinal))
                        continue;

                    selectedRule = candidate;
                    break;
                }
            }

            if (selectedRule == null && conversionRules.Count > 0)
                selectedRule = conversionRules[0];
        }

        if (selectedRule != null)
        {
            SelectedRecipeKey = selectedRule.RecipeKey ?? string.Empty;
            SelectedFluidType = selectedRule.FluidType ?? string.Empty;
            cycleTickLength = Math.Max(1, selectedRule.CraftTimeTicks);
        }
        else
        {
            SelectedRecipeKey = string.Empty;
            SelectedFluidType = string.Empty;
            cycleTickLength = defaultCycleTickLength;
        }
    }

    private static void WarnInvalidRecipeOnce(MachineRecipe recipe, string error)
    {
        string key = recipe?.NormalizedKey ?? string.Empty;
        if (string.IsNullOrEmpty(key))
            key = $"{recipe?.Machine}|{recipe?.SourceContext}";

        lock (InvalidRecipeWarnSync)
        {
            if (!warnedInvalidRecipeKeys.Add(key))
                return;
        }

        Log.Warning($"[FluidDecanter] Skipped machineRecipe key='{recipe?.NormalizedKey}' machine='{recipe?.Machine}': {error}");
    }

    private string GetMachineRecipeGroupsCsv()
    {
        string configured = blockValue.Block?.Properties?.GetString("MachineRecipes");
        if (string.IsNullOrWhiteSpace(configured))
            return DefaultMachineRecipeGroup;

        return configured.Trim();
    }

    public static bool TryReadMachineRecipeAsDecanterRule(
        MachineRecipe recipe,
        int defaultCraftTimeTicks,
        out string inputItemName,
        out int inputItemCount,
        out string fluidType,
        out int fluidAmountMg,
        out string returnItemName,
        out int returnItemAmount,
        out int craftTimeTicks,
        out string error)
    {
        inputItemName = string.Empty;
        inputItemCount = 0;
        fluidType = string.Empty;
        fluidAmountMg = 0;
        returnItemName = string.Empty;
        returnItemAmount = 0;
        craftTimeTicks = Math.Max(1, defaultCraftTimeTicks);
        error = string.Empty;

        if (recipe == null)
        {
            error = "Recipe is null";
            return false;
        }

        if (recipe.Inputs == null || recipe.Inputs.Count != 1)
        {
            error = "Decanter recipe requires exactly one <input>";
            return false;
        }

        if (recipe.FluidOutputs == null || recipe.FluidOutputs.Count != 1)
        {
            error = "Decanter recipe requires exactly one <fluid_output>";
            return false;
        }

        if (recipe.GasOutputs != null && recipe.GasOutputs.Count > 0)
        {
            error = "Decanter recipe does not support <gas_output>";
            return false;
        }

        if (recipe.ItemOutputs != null && recipe.ItemOutputs.Count > 1)
        {
            error = "Decanter recipe supports at most one distinct <output>";
            return false;
        }

        MachineRecipeInput input = recipe.Inputs[0];
        MachineRecipeFluidOutput fluidOutput = recipe.FluidOutputs[0];
        if (input == null || fluidOutput == null)
        {
            error = "Decanter recipe input/output invalid";
            return false;
        }

        inputItemName = (input.ItemName ?? string.Empty).Trim();
        inputItemCount = Math.Max(1, input.Count);
        fluidType = (fluidOutput.Type ?? string.Empty).Trim().ToLowerInvariant();
        fluidAmountMg = Math.Max(0, fluidOutput.AmountMg);
        craftTimeTicks = recipe.CraftTimeTicks.HasValue
            ? Math.Max(1, recipe.CraftTimeTicks.Value)
            : Math.Max(1, defaultCraftTimeTicks);

        if (string.IsNullOrEmpty(inputItemName))
        {
            error = "Decanter input item is empty";
            return false;
        }

        if (string.IsNullOrEmpty(fluidType))
        {
            error = "Decanter fluid type is empty";
            return false;
        }

        if (fluidAmountMg <= 0)
        {
            error = "Decanter fluid output amount is invalid";
            return false;
        }

        if (craftTimeTicks <= 0)
        {
            error = "Decanter craft time is invalid";
            return false;
        }

        if (recipe.ItemOutputs != null && recipe.ItemOutputs.Count > 0)
        {
            MachineRecipeItemOutput itemOutput = recipe.ItemOutputs[0];
            returnItemName = (itemOutput?.ItemName ?? string.Empty).Trim();
            returnItemAmount = Math.Max(1, itemOutput?.Count ?? 1);
        }
        else
        {
            returnItemName = string.Empty;
            returnItemAmount = 0;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsRecipeAllowedForMachineGroups(MachineRecipe recipe, string machineGroupsCsv)
    {
        if (recipe == null || string.IsNullOrEmpty(recipe.Machine))
            return false;

        List<string> groups = MachineRecipeRegistry.ParseMachineGroups(machineGroupsCsv);
        for (int i = 0; i < groups.Count; i++)
        {
            if (string.Equals(groups[i], recipe.Machine, StringComparison.Ordinal))
                return true;
        }

        return false;
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
                string dedupeKey = $"{storagePos}|{pipeTe.PipeGraphId}";
                if (!seen.Add(dedupeKey))
                    continue;

                TileEntityComposite comp = world.GetTileEntity(0, storagePos) as TileEntityComposite;
                if (comp == null)
                    continue;

                results.Add(new InputTargetInfo(storagePos, pipeTe.PipeGraphId));
            }
        }

        return results;
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

    private string GetFluidOutputRequirementFailureReason(WorldBase world)
    {
        if (world == null)
            return "Missing/Invalid Fluid Output";

        if (string.IsNullOrEmpty(SelectedFluidType))
            return "No fluid selected";

        List<Guid> candidates = GetAdjacentFluidGraphCandidates(world);
        if (candidates == null || candidates.Count == 0)
            return "Missing/Invalid Fluid Output";

        string normalizedSelected = SelectedFluidType.Trim().ToLowerInvariant();
        bool sawDifferentFluidType = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            Guid graphId = candidates[i];
            if (graphId == Guid.Empty)
                continue;

            if (!FluidGraphManager.TryGetGraph(graphId, out FluidGraphData graph) || graph == null)
                continue;

            string graphFluid = graph.FluidType;
            if (string.IsNullOrEmpty(graphFluid))
                continue;

            if (!string.Equals(graphFluid, normalizedSelected, StringComparison.Ordinal))
                sawDifferentFluidType = true;
        }

        if (sawDifferentFluidType)
            return "Output fluid network contains another fluid type";

        return "Missing/Invalid Fluid Output";
    }

    private string GetFirstMissingRequirementReason(WorldBase world)
    {
        if (world == null)
            return "World unavailable";

        if (!HasSelectedInputTarget(world))
            return "Missing Item Input";

        if (string.IsNullOrEmpty(SelectedRecipeKey))
            return "No recipe selected";

        if (string.IsNullOrEmpty(SelectedFluidType))
            return "No fluid selected";

        if (!world.IsRemote() && pendingItemInput <= 0)
        {
            if (!TryFindMatchingInputRule(world, out _, out _, out string inputReason))
                return string.IsNullOrEmpty(inputReason) ? "No matching input item" : inputReason;
        }

        if (!HasItemOutputRequirement(world))
            return "Missing Item Output";

        if (!HasFluidOutputRequirement(world))
            return GetFluidOutputRequirementFailureReason(world);

        return string.Empty;
    }
    private int BuildStateSignature(bool requirementsMet)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + (IsOn ? 1 : 0);
            hash = (hash * 31) + (requirementsMet ? 1 : 0);
            hash = (hash * 31) + SelectedInputChestPos.x;
            hash = (hash * 31) + SelectedInputChestPos.y;
            hash = (hash * 31) + SelectedInputChestPos.z;
            hash = (hash * 31) + SelectedInputPipeGraphId.GetHashCode();
            hash = (hash * 31) + SelectedOutputChestPos.x;
            hash = (hash * 31) + SelectedOutputChestPos.y;
            hash = (hash * 31) + SelectedOutputChestPos.z;
            hash = (hash * 31) + (int)SelectedOutputMode;
            hash = (hash * 31) + SelectedOutputPipeGraphId.GetHashCode();
            hash = (hash * 31) + (SelectedFluidType?.GetHashCode() ?? 0);
            hash = (hash * 31) + (SelectedRecipeKey?.GetHashCode() ?? 0);
            hash = (hash * 31) + (machineRecipeGroupsCsv?.GetHashCode() ?? 0);
            hash = (hash * 31) + SelectedFluidGraphId.GetHashCode();
            hash = (hash * 31) + pendingItemInput;
            hash = (hash * 31) + pendingItemOutput;
            hash = (hash * 31) + pendingFluidInput;
            hash = (hash * 31) + pendingFluidOutput;
            hash = (hash * 31) + (pendingItemInputName?.GetHashCode() ?? 0);
            hash = (hash * 31) + pendingItemInputFluidAmountMg;
            hash = (hash * 31) + (pendingItemInputReturnItemName?.GetHashCode() ?? 0);
            hash = (hash * 31) + pendingItemInputReturnItemAmount;
            hash = (hash * 31) + (pendingItemOutputName?.GetHashCode() ?? 0);
            hash = (hash * 31) + (LastAction?.GetHashCode() ?? 0);
            hash = (hash * 31) + (LastBlockReason?.GetHashCode() ?? 0);
            hash = (hash * 31) + availableInputTargets.Count;
            hash = (hash * 31) + availableOutputTargets.Count;
            hash = (hash * 31) + fluidOptions.Count;
            return hash;
        }
    }
    private void MarkDirty()
    {
        NeedsUiRefresh = true;

        World world = GameManager.Instance?.World;
        if (world != null && !world.IsRemote())
            setModified();
    }
}












