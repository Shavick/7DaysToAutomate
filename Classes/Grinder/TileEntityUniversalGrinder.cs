using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

public class TileEntityUniversalGrinder : TileEntityMachine
{
    private const int PersistVersion = 2;
    private static readonly Vector3i[] NeighborOffsets =
    {
        new Vector3i(1, 0, 0),
        new Vector3i(-1, 0, 0),
        new Vector3i(0, 1, 0),
        new Vector3i(0, -1, 0),
        new Vector3i(0, 0, 1),
        new Vector3i(0, 0, -1)
    };

    private sealed class RecipeCandidate
    {
        public Recipe Recipe;
        public string Bench;
    }

    public Vector3i SelectedInputChestPos = Vector3i.zero;
    public Guid SelectedInputPipeGraphId = Guid.Empty;
    public Vector3i SelectedOutputChestPos = Vector3i.zero;
    public OutputTransportMode SelectedOutputMode = OutputTransportMode.Adjacent;
    public Guid SelectedOutputPipeGraphId = Guid.Empty;

    private TileEntityComposite selectedInputContainer;
    private List<InputTargetInfo> availableInputTargets = new List<InputTargetInfo>();
    private List<OutputTargetInfo> availableOutputTargets = new List<OutputTargetInfo>();

    public bool ProcessItemArmorMods = true;
    public long ItemsProcessed;
    public bool IsProcessing;
    public int CycleTickCounter;
    public int CycleTickLength = 20;
    public int ActiveBatchSize;
    public string ActiveItemName = string.Empty;
    public string LastAction = "Idle";
    public string LastBlockReason = string.Empty;

    private bool configLoaded;
    private float baseReturnRate = 0.5f;
    private int baseBatchSize = 1;
    private Dictionary<string, List<RecipeCandidate>> recipesByOutputName;

    private bool fuelConfigLoaded;
    private bool fuelConfigured;
    private string fuelType = string.Empty;
    private int fuelBufferCapacityMg;
    private int fuelUsePerSecondMg;
    private int fuelPullPerSecondMg;
    private int fuelBufferMg;
    private int fuelUseRemainder;
    private int fuelPullRemainder;
    private ulong lastFuelUpdateWorldTime;
    private Guid selectedFuelGraphId = Guid.Empty;
    public string LastFuelStatus = string.Empty;
    public float EffectiveReturnRate => baseReturnRate;
    private readonly Dictionary<string, int> activeCycleOutput = new Dictionary<string, int>(StringComparer.Ordinal);

    public TileEntityUniversalGrinder(Chunk chunk) : base(chunk)
    {
    }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.UniversalGrinder);
    }

    public override void SetSimulatedByHLR(bool value)
    {
        simulatedByHLR = value;
    }

    public override IHLRSnapshot BuildHLRSnapshot(WorldBase world)
    {
        EnsureConfigLoaded();
        LoadFuelConfig();

        ulong now = world?.GetWorldTime() ?? 0UL;

        Dictionary<string, int> pendingOutputs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> kvp in pendingOutput)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            pendingOutputs[kvp.Key] = kvp.Value;
        }

        Dictionary<string, int> activeCycleOutputs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> kvp in activeCycleOutput)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            activeCycleOutputs[kvp.Key] = kvp.Value;
        }

        GrinderSnapshot snapshot = new GrinderSnapshot
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
            ProcessItemArmorMods = ProcessItemArmorMods,
            EffectiveReturnRate = Math.Max(0f, baseReturnRate),
            BaseBatchSize = Math.Max(1, baseBatchSize),
            MaxPendingOutput = Math.Max(1, GetMaxPendingOutput()),
            AcceptedRecipeBenchesCsv = blockValue.Block?.Properties?.GetString("AcceptedRecipeBenches") ?? string.Empty,
            BlockedRecipeBenchesCsv = blockValue.Block?.Properties?.GetString("BlockedRecipeBenches") ?? string.Empty,
            IsProcessing = IsProcessing,
            CycleTickCounter = Math.Max(0, CycleTickCounter),
            CycleTickLength = Math.Max(1, CycleTickLength),
            ActiveBatchSize = Math.Max(0, ActiveBatchSize),
            ActiveItemName = ActiveItemName ?? string.Empty,
            ItemsProcessed = Math.Max(0L, ItemsProcessed),
            PendingOutputs = pendingOutputs,
            ActiveCycleOutputs = activeCycleOutputs,
            IsFuelEnabled = fuelConfigured,
            FuelType = fuelType ?? string.Empty,
            FuelBufferMg = Math.Max(0, fuelBufferMg),
            FuelCapacityMg = Math.Max(0, fuelBufferCapacityMg),
            FuelUsePerSecondMg = Math.Max(0, fuelUsePerSecondMg),
            FuelPullPerSecondMg = Math.Max(0, fuelPullPerSecondMg),
            SelectedFuelGraphId = selectedFuelGraphId,
            FuelUseRemainder = Math.Max(0, fuelUseRemainder),
            FuelPullRemainder = Math.Max(0, fuelPullRemainder),
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
        if (!(snapshotObj is GrinderSnapshot snapshot))
            return;

        IsOn = snapshot.IsOn;
        SelectedInputChestPos = snapshot.SelectedInputChestPos;
        SelectedInputPipeGraphId = snapshot.SelectedInputPipeGraphId;
        SelectedOutputChestPos = snapshot.SelectedOutputChestPos;
        SelectedOutputMode = snapshot.SelectedOutputMode;
        SelectedOutputPipeGraphId = snapshot.SelectedOutputPipeGraphId;
        ProcessItemArmorMods = snapshot.ProcessItemArmorMods;
        baseReturnRate = Math.Max(0f, snapshot.EffectiveReturnRate);
        baseBatchSize = Math.Max(1, snapshot.BaseBatchSize);
        IsProcessing = snapshot.IsProcessing;
        CycleTickCounter = Math.Max(0, snapshot.CycleTickCounter);
        CycleTickLength = Math.Max(1, snapshot.CycleTickLength);
        ActiveBatchSize = Math.Max(0, snapshot.ActiveBatchSize);
        ActiveItemName = snapshot.ActiveItemName ?? string.Empty;
        ItemsProcessed = Math.Max(0L, snapshot.ItemsProcessed);
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

        activeCycleOutput.Clear();
        if (snapshot.ActiveCycleOutputs != null)
        {
            foreach (KeyValuePair<string, int> kvp in snapshot.ActiveCycleOutputs)
            {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                    continue;

                activeCycleOutput[kvp.Key] = kvp.Value;
            }
        }

        fuelConfigured = snapshot.IsFuelEnabled;
        fuelType = snapshot.FuelType ?? string.Empty;
        fuelBufferMg = Math.Max(0, snapshot.FuelBufferMg);
        fuelBufferCapacityMg = Math.Max(0, snapshot.FuelCapacityMg);
        fuelUsePerSecondMg = Math.Max(0, snapshot.FuelUsePerSecondMg);
        fuelPullPerSecondMg = Math.Max(0, snapshot.FuelPullPerSecondMg);
        selectedFuelGraphId = snapshot.SelectedFuelGraphId;
        fuelUseRemainder = Math.Max(0, snapshot.FuelUseRemainder);
        fuelPullRemainder = Math.Max(0, snapshot.FuelPullRemainder);
        lastFuelUpdateWorldTime = 0UL;
        LastFuelStatus = fuelConfigured
            ? $"Fuel {FormatGallons(fuelBufferMg)}/{FormatGallons(fuelBufferCapacityMg)}g"
            : string.Empty;

        configLoaded = true;
        fuelConfigLoaded = true;
        recipesByOutputName = null;
        ResolveSelectedInputContainer();
        ResolveSelectedOutputContainer();

        simulatedByHLR = false;
        NeedsUiRefresh = true;

        World currentWorld = GameManager.Instance?.World;
        if (currentWorld != null && !currentWorld.IsRemote())
            setModified();
    }

    public override void UpdateTick(World world)
    {
        base.UpdateTick(world);
        if (world == null || world.IsRemote() || IsSimulatingHLR())
            return;

        EnsureConfigLoaded();
        LoadFuelConfig();
        ResolveSelectedInputContainer();
        TryFlushPendingOutput(world);

        if (!IsOn)
        {
            LastAction = "Off";
            LastBlockReason = string.Empty;
            NeedsUiRefresh = true;
            return;
        }

        if (!HasSelectedInputTarget(world))
        {
            LastAction = "Waiting";
            LastBlockReason = "Missing Input";
            NeedsUiRefresh = true;
            return;
        }

        if (!HasSelectedOutputTarget(world))
        {
            LastAction = "Waiting";
            LastBlockReason = "Missing Output";
            NeedsUiRefresh = true;
            return;
        }

        if (!UpdateFuel(world))
        {
            LastAction = "Waiting";
            if (string.IsNullOrEmpty(LastBlockReason))
                LastBlockReason = "Waiting for fuel";
            NeedsUiRefresh = true;
            return;
        }

        if (!IsProcessing)
        {
            if (!TryBeginCycle(out string blockedReason))
            {
                LastAction = "Waiting";
                LastBlockReason = blockedReason;
                NeedsUiRefresh = true;
                return;
            }
        }

        CycleTickCounter++;
        if (CycleTickCounter < Math.Max(1, CycleTickLength))
        {
            LastAction = "Grinding";
            LastBlockReason = string.Empty;
            NeedsUiRefresh = true;
            return;
        }

        IsProcessing = false;
        CycleTickCounter = 0;
        CommitActiveCycleOutputToPending();
        ItemsProcessed += Math.Max(0, ActiveBatchSize);
        ActiveBatchSize = 0;
        ActiveItemName = string.Empty;
        LastAction = "Grind complete";
        LastBlockReason = string.Empty;
        NeedsUiRefresh = true;
        setModified();
    }

    private bool TryBeginCycle(out string blockedReason)
    {
        blockedReason = "No valid items";
        if (selectedInputContainer == null)
        {
            blockedReason = "Input container unavailable";
            return false;
        }

        TEFeatureStorage storage = selectedInputContainer.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
        {
            blockedReason = "Input has no storage";
            return false;
        }

        for (int i = 0; i < storage.items.Length; i++)
        {
            ItemStack slot = storage.items[i];
            if (slot.IsEmpty() || slot.itemValue == null || slot.itemValue.ItemClass == null || slot.count <= 0)
                continue;

            ItemValue itemValue = slot.itemValue;
            string itemName = itemValue.ItemClass.GetItemName();
            if (string.IsNullOrEmpty(itemName))
                continue;

            bool hasAttachments = TryCollectAttachments(itemValue, out Dictionary<string, int> attachmentsPerItem);
            if (hasAttachments && !ProcessItemArmorMods)
                continue;

            if (!TryBuildPerItemOutputs(itemValue, out Dictionary<string, int> materialPerItem))
                continue;

            int batch = Math.Min(Math.Max(1, baseBatchSize), slot.count);
            batch = FindBatchThatFits(materialPerItem, attachmentsPerItem, batch);
            if (batch <= 0)
            {
                blockedReason = "Output full";
                return false;
            }

            Dictionary<string, int> materialTotals = Multiply(materialPerItem, batch);
            Dictionary<string, int> attachmentTotals = (ProcessItemArmorMods && attachmentsPerItem != null) ? Multiply(attachmentsPerItem, batch) : new Dictionary<string, int>(StringComparer.Ordinal);

            activeCycleOutput.Clear();
            foreach (KeyValuePair<string, int> kvp in materialTotals)
                AddCycleOutput(kvp.Key, kvp.Value);
            foreach (KeyValuePair<string, int> kvp in attachmentTotals)
                AddCycleOutput(kvp.Key, kvp.Value);

            slot.count -= batch;
            storage.items[i] = slot.count > 0 ? slot : ItemStack.Empty;
            storage.SetModified();

            IsProcessing = true;
            CycleTickCounter = 0;
            ActiveBatchSize = batch;
            ActiveItemName = itemName;
            blockedReason = string.Empty;
            setModified();
            return true;
        }

        return false;
    }

    private int FindBatchThatFits(Dictionary<string, int> materialPerItem, Dictionary<string, int> attachmentPerItem, int maxBatch)
    {
        for (int batch = maxBatch; batch >= 1; batch--)
        {
            int toAdd = SumCounts(materialPerItem, batch) + SumCounts(attachmentPerItem, batch);
            int remainingCapacity = Math.Max(0, GetMaxPendingOutput() - GetPendingOutputTotal());
            if (toAdd <= remainingCapacity)
                return batch;
        }

        return 0;
    }

    private static int SumCounts(Dictionary<string, int> perItem, int batch)
    {
        if (perItem == null)
            return 0;

        int total = 0;
        foreach (KeyValuePair<string, int> kvp in perItem)
            total += Math.Max(0, kvp.Value * batch);
        return total;
    }

    private int GetPendingOutputTotal()
    {
        int total = 0;
        foreach (KeyValuePair<string, int> kvp in pendingOutput)
            total += Math.Max(0, kvp.Value);
        return total;
    }

    private static Dictionary<string, int> Multiply(Dictionary<string, int> values, int factor)
    {
        Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (values == null)
            return result;

        foreach (KeyValuePair<string, int> kvp in values)
        {
            int value = kvp.Value * factor;
            if (!string.IsNullOrEmpty(kvp.Key) && value > 0)
                result[kvp.Key] = value;
        }

        return result;
    }

    private void AddCycleOutput(string itemName, int count)
    {
        if (string.IsNullOrEmpty(itemName) || count <= 0)
            return;

        if (activeCycleOutput.TryGetValue(itemName, out int existing))
            activeCycleOutput[itemName] = existing + count;
        else
            activeCycleOutput[itemName] = count;
    }

    private void CommitActiveCycleOutputToPending()
    {
        if (activeCycleOutput.Count == 0)
            return;

        foreach (KeyValuePair<string, int> kvp in activeCycleOutput)
            AddPendingOutput(kvp.Key, kvp.Value);

        activeCycleOutput.Clear();
    }

    private bool TryBuildPerItemOutputs(ItemValue itemValue, out Dictionary<string, int> materialPerItem)
    {
        materialPerItem = new Dictionary<string, int>(StringComparer.Ordinal);
        string outputItemName = itemValue?.ItemClass?.GetItemName();
        if (string.IsNullOrEmpty(outputItemName))
            return false;

        if (TryGetRecipeReverseOutputsPerItem(outputItemName, out materialPerItem))
            return materialPerItem.Count > 0;

        if (TryGetScrapOutputsPerItem(itemValue, out materialPerItem))
            return materialPerItem.Count > 0;

        return false;
    }

    private bool TryGetRecipeReverseOutputsPerItem(string outputItemName, out Dictionary<string, int> outputsPerItem)
    {
        outputsPerItem = new Dictionary<string, int>(StringComparer.Ordinal);
        EnsureRecipeCache();

        if (recipesByOutputName == null || !recipesByOutputName.TryGetValue(outputItemName, out List<RecipeCandidate> candidates) || candidates == null)
            return false;

        int bestTotal = int.MaxValue;
        Dictionary<string, int> best = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            RecipeCandidate candidate = candidates[i];
            if (candidate?.Recipe == null || !IsBenchAllowed(candidate.Bench))
                continue;

            if (!TryBuildRecipeOutputsForSingleItem(candidate.Recipe, out Dictionary<string, int> candidateOutputs))
                continue;

            int total = 0;
            foreach (KeyValuePair<string, int> kvp in candidateOutputs)
                total += kvp.Value;

            if (best == null || total < bestTotal)
            {
                best = candidateOutputs;
                bestTotal = total;
            }
        }

        if (best == null)
            return false;

        outputsPerItem = best;
        return true;
    }

    private bool TryBuildRecipeOutputsForSingleItem(Recipe recipe, out Dictionary<string, int> outputs)
    {
        outputs = new Dictionary<string, int>(StringComparer.Ordinal);
        if (recipe == null || recipe.ingredients == null)
            return false;

        int recipeOutputCount = Math.Max(1, recipe.count);
        for (int i = 0; i < recipe.ingredients.Count; i++)
        {
            ItemStack ingredient = recipe.ingredients[i];
            if (ingredient.itemValue?.ItemClass == null)
                continue;

            int ingredientCount = Math.Max(0, ingredient.count);
            if (ingredientCount <= 1)
                continue;

            string ingredientName = ingredient.itemValue.ItemClass.GetItemName();
            if (string.IsNullOrEmpty(ingredientName))
                continue;

            int recovered = (int)Math.Floor((ingredientCount * baseReturnRate) / recipeOutputCount);
            if (recovered <= 0)
                continue;

            outputs[ingredientName] = recovered;
        }

        return outputs.Count > 0;
    }

    private bool TryGetScrapOutputsPerItem(ItemValue itemValue, out Dictionary<string, int> outputsPerItem)
    {
        outputsPerItem = new Dictionary<string, int>(StringComparer.Ordinal);
        if (itemValue?.ItemClass == null)
            return false;

        int inputCount = 1;
        Recipe scrapRecipe = CraftingManager.GetScrapableRecipe(itemValue, inputCount);
        if (scrapRecipe == null || scrapRecipe.count <= 0)
            return false;

        ItemClass sourceClass = ItemClass.GetForId(itemValue.type);
        ItemClass scrapOutputClass = ItemClass.GetForId(scrapRecipe.itemValueType);
        if (sourceClass == null || scrapOutputClass == null)
            return false;

        string scrapOutputName = scrapOutputClass?.GetItemName();
        if (string.IsNullOrEmpty(scrapOutputName))
            return false;

        int sourceWeight = Math.Max(0, sourceClass.GetWeight());
        int outputWeight = Math.Max(0, scrapOutputClass.GetWeight());
        if (sourceWeight <= 0 || outputWeight <= 0)
            return false;

        int unitsRaw = (sourceWeight * inputCount) / outputWeight;
        if (unitsRaw <= 0)
            return false;

        int vanillaScrapCount = (int)(unitsRaw * 0.75f);
        if (vanillaScrapCount <= 0)
            vanillaScrapCount = 1;

        int recovered = (int)Math.Floor(vanillaScrapCount * baseReturnRate);
        if (recovered <= 0)
            return false;

        outputsPerItem[scrapOutputName] = recovered;
        return true;
    }

    private void EnsureRecipeCache()
    {
        if (recipesByOutputName != null)
            return;

        recipesByOutputName = new Dictionary<string, List<RecipeCandidate>>(StringComparer.Ordinal);
        var all = XUiM_Recipes.GetRecipes();
        if (all == null)
            return;

        for (int i = 0; i < all.Count; i++)
        {
            Recipe recipe = all[i];
            if (recipe == null || recipe.GetOutputItemClass() == null)
                continue;

            string outputName = recipe.GetOutputItemClass().GetItemName();
            if (string.IsNullOrEmpty(outputName))
                continue;

            if (!recipesByOutputName.TryGetValue(outputName, out List<RecipeCandidate> list))
            {
                list = new List<RecipeCandidate>();
                recipesByOutputName[outputName] = list;
            }

            list.Add(new RecipeCandidate { Recipe = recipe, Bench = NormalizeBench(recipe.craftingArea) });
        }
    }

    private bool TryCollectAttachments(ItemValue itemValue, out Dictionary<string, int> attachments)
    {
        attachments = new Dictionary<string, int>(StringComparer.Ordinal);
        if (itemValue == null)
            return false;

        CollectAttachmentMember(itemValue, "Modifications", attachments);
        CollectAttachmentMember(itemValue, "modifications", attachments);
        CollectAttachmentMember(itemValue, "CosmeticMods", attachments);
        CollectAttachmentMember(itemValue, "cosmeticMods", attachments);
        CollectAttachmentMember(itemValue, "Mods", attachments);
        CollectAttachmentMember(itemValue, "mods", attachments);
        return attachments.Count > 0;
    }

    private static void CollectAttachmentMember(object source, string memberName, Dictionary<string, int> output)
    {
        object value = GetMemberValue(source, memberName);
        if (value == null)
            return;

        if (value is IEnumerable enumerable && !(value is string))
        {
            foreach (object entry in enumerable)
                TryCollectItemName(entry, output);
            return;
        }

        TryCollectItemName(value, output);
    }

    private static void TryCollectItemName(object entry, Dictionary<string, int> output)
    {
        if (entry is ItemValue itemValue)
        {
            string name = itemValue.ItemClass?.GetItemName();
            if (!string.IsNullOrEmpty(name) && itemValue.type != ItemValue.None.type)
                AddCount(output, name, 1);
            return;
        }

        if (entry is ItemStack stack)
        {
            string name = stack.itemValue?.ItemClass?.GetItemName();
            if (!string.IsNullOrEmpty(name) && stack.count > 0)
                AddCount(output, name, stack.count);
            return;
        }

        object maybeItemValue = GetMemberValue(entry, "itemValue") ?? GetMemberValue(entry, "ItemValue");
        if (maybeItemValue is ItemValue iv)
        {
            string name = iv.ItemClass?.GetItemName();
            if (!string.IsNullOrEmpty(name) && iv.type != ItemValue.None.type)
                AddCount(output, name, 1);
        }
    }

    private static void AddCount(Dictionary<string, int> dict, string key, int amount)
    {
        if (string.IsNullOrEmpty(key) || amount <= 0)
            return;

        if (dict.TryGetValue(key, out int existing))
            dict[key] = existing + amount;
        else
            dict[key] = amount;
    }

    private static string NormalizeBench(string bench)
    {
        if (string.IsNullOrWhiteSpace(bench))
            return "player";

        return bench.Trim().ToLowerInvariant();
    }

    private bool IsBenchAllowed(string bench)
    {
        string normalized = NormalizeBench(bench);
        HashSet<string> blocked = ParseCsvSet(blockValue.Block?.Properties?.GetString("BlockedRecipeBenches"));
        if (blocked.Contains(normalized))
            return false;

        HashSet<string> accepted = ParseCsvSet(blockValue.Block?.Properties?.GetString("AcceptedRecipeBenches"));
        return accepted.Count == 0 || accepted.Contains(normalized);
    }

    private static HashSet<string> ParseCsvSet(string csv)
    {
        HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv))
            return set;

        string[] parts = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
            set.Add(NormalizeBench(parts[i]));

        return set;
    }

    private void EnsureConfigLoaded()
    {
        if (configLoaded)
            return;

        configLoaded = true;
        CycleTickLength = ReadIntProperty("InputSpeed", 20, 1, 2000);
        baseBatchSize = ReadIntProperty("BatchSize", 1, 1, 4096);

        string returnRaw = blockValue.Block?.Properties?.GetString("BaseReturnRate");
        if (string.IsNullOrWhiteSpace(returnRaw) || !float.TryParse(returnRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out baseReturnRate))
            baseReturnRate = 0.5f;

        if (baseReturnRate < 0f)
            baseReturnRate = 0f;
    }

    private void LoadFuelConfig()
    {
        if (fuelConfigLoaded)
            return;

        fuelConfigLoaded = true;
        if (!TryGetValidFluidFuelConfig(out string requestedFluid, out int bufferGallons, out int usePerSecond, out int pullPerSecond))
        {
            fuelConfigured = false;
            return;
        }

        fuelConfigured = true;
        fuelType = requestedFluid;
        fuelBufferCapacityMg = bufferGallons * FluidConstants.MilliGallonsPerGallon;
        fuelUsePerSecondMg = usePerSecond * FluidConstants.MilliGallonsPerGallon;
        fuelPullPerSecondMg = pullPerSecond * FluidConstants.MilliGallonsPerGallon;
    }

    private bool UpdateFuel(WorldBase world)
    {
        if (!fuelConfigured)
            return true;

        ulong now = world.GetWorldTime();
        if (lastFuelUpdateWorldTime == 0UL)
        {
            lastFuelUpdateWorldTime = now;
            return true;
        }

        ulong delta = now > lastFuelUpdateWorldTime ? now - lastFuelUpdateWorldTime : 0UL;
        lastFuelUpdateWorldTime = now;
        if (delta == 0UL)
            return true;

        int use = ComputePerTickAmount(fuelUsePerSecondMg, ref fuelUseRemainder, delta);
        if (use > 0)
        {
            if (fuelBufferMg < use)
            {
                LastFuelStatus = "Blocked: Fuel empty";
                LastBlockReason = "Waiting for fuel";
                return false;
            }

            fuelBufferMg -= use;
        }

        int pull = ComputePerTickAmount(fuelPullPerSecondMg, ref fuelPullRemainder, delta);
        int request = Math.Min(Math.Max(0, fuelBufferCapacityMg - fuelBufferMg), pull);
        if (request > 0)
        {
            if (selectedFuelGraphId == Guid.Empty && !TryGetCompatibleFluidGraph(world, fuelType, out selectedFuelGraphId))
            {
                LastFuelStatus = "Blocked: No connected fuel graph";
            }
            else if (FluidGraphManager.TryConsumeFluid(world, 0, selectedFuelGraphId, fuelType, request, out int consumedMg) && consumedMg > 0)
            {
                fuelBufferMg = Math.Min(fuelBufferCapacityMg, fuelBufferMg + consumedMg);
            }
        }

        LastFuelStatus = $"Fuel {FormatGallons(fuelBufferMg)}/{FormatGallons(fuelBufferCapacityMg)}g";
        return true;
    }

    public bool ResolveFuelGraph(WorldBase world)
    {
        selectedFuelGraphId = Guid.Empty;
        return TryGetCompatibleFluidGraph(world, fuelType, out selectedFuelGraphId);
    }

    private bool TryGetCompatibleFluidGraph(WorldBase world, string fluidType, out Guid graphId)
    {
        graphId = Guid.Empty;
        if (world == null || string.IsNullOrWhiteSpace(fluidType))
            return false;

        string normalized = fluidType.Trim().ToLowerInvariant();
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i pipePos = ToWorldPos() + NeighborOffsets[i];
            TileEntityLiquidPipe pipe = world.GetTileEntity(0, pipePos) as TileEntityLiquidPipe;
            if (pipe == null)
                continue;

            Guid candidate = pipe.FluidGraphId;
            if (candidate == Guid.Empty)
                continue;

            if (!FluidGraphManager.TryGetGraph(candidate, out FluidGraphData graph) || graph == null)
                continue;

            if (!string.IsNullOrEmpty(graph.FluidType) && !string.Equals(graph.FluidType, normalized, StringComparison.Ordinal))
                continue;

            graphId = candidate;
            return true;
        }

        return false;
    }

    private static int ComputePerTickAmount(int perSecondMg, ref int remainder, ulong deltaTicks)
    {
        long numerator = (long)perSecondMg * (long)deltaTicks + remainder;
        int amount = (int)(numerator / 20L);
        remainder = (int)(numerator % 20L);
        return Math.Max(0, amount);
    }

    public void ResolveSelectedInputContainer()
    {
        selectedInputContainer = null;
        if (SelectedInputChestPos == Vector3i.zero)
            return;

        WorldBase world = GameManager.Instance?.World;
        if (world == null)
            return;

        selectedInputContainer = world.GetTileEntity(0, SelectedInputChestPos) as TileEntityComposite;
    }

    public void ResolveSelectedOutputContainer()
    {
        // Output target is resolved on flush using target position.
    }

    public List<InputTargetInfo> GetAvailableInputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return availableInputTargets ?? new List<InputTargetInfo>();

        RefreshAvailableInputTargets(world);
        return availableInputTargets;
    }

    public List<OutputTargetInfo> GetAvailableOutputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return availableOutputTargets ?? new List<OutputTargetInfo>();

        RefreshAvailableOutputTargets(world);
        return availableOutputTargets;
    }

    public void RefreshAvailableInputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        availableInputTargets = DiscoverInputTargets(world);
    }

    public void RefreshAvailableOutputTargets(WorldBase world)
    {
        if (world == null || world.IsRemote())
            return;

        availableOutputTargets = MachineOutputDiscovery.GetAvailableOutputs(world, 0, ToWorldPos(), 8);
    }

    private List<InputTargetInfo> DiscoverInputTargets(WorldBase world)
    {
        List<InputTargetInfo> results = new List<InputTargetInfo>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i pipePos = ToWorldPos() + NeighborOffsets[i];
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
                if (!(world.GetTileEntity(0, storagePos) is TileEntityComposite))
                    continue;

                string key = $"{storagePos}|{pipeTe.PipeGraphId}";
                if (!seen.Add(key))
                    continue;

                results.Add(new InputTargetInfo(storagePos, pipeTe.PipeGraphId));
            }
        }

        return results;
    }

    private bool HasSelectedInputTarget(WorldBase world)
    {
        if (world == null || SelectedInputChestPos == Vector3i.zero)
            return false;

        List<InputTargetInfo> targets = GetAvailableInputTargets(world);
        for (int i = 0; i < targets.Count; i++)
        {
            InputTargetInfo target = targets[i];
            if (target != null && target.BlockPos == SelectedInputChestPos && target.PipeGraphId == SelectedInputPipeGraphId)
                return true;
        }

        return false;
    }

    private bool HasSelectedOutputTarget(WorldBase world)
    {
        if (world == null || SelectedOutputChestPos == Vector3i.zero)
            return false;

        List<OutputTargetInfo> targets = GetAvailableOutputTargets(world);
        for (int i = 0; i < targets.Count; i++)
        {
            OutputTargetInfo target = targets[i];
            if (target != null && target.BlockPos == SelectedOutputChestPos && target.TransportMode == SelectedOutputMode && target.PipeGraphId == SelectedOutputPipeGraphId)
                return true;
        }

        return false;
    }

    private void TryFlushPendingOutput(WorldBase world)
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

            Dictionary<string, int> request = new Dictionary<string, int>(StringComparer.Ordinal) { [kvp.Key] = kvp.Value };
            int moved = 0;
            if (SelectedOutputMode == OutputTransportMode.Pipe)
            {
                if (PipeGraphManager.TryDepositStorageItems(world, 0, SelectedOutputPipeGraphId, SelectedOutputChestPos, request, out Dictionary<string, int> deposited) &&
                    deposited != null &&
                    deposited.TryGetValue(kvp.Key, out moved))
                {
                }
            }
            else
            {
                moved = TryDepositToAdjacentOutput(world, kvp.Key, kvp.Value);
            }

            if (moved <= 0)
            {
                LastBlockReason = "Output blocked";
                break;
            }

            int remaining = kvp.Value - moved;
            if (remaining > 0)
                pendingOutput[kvp.Key] = remaining;
            else
                pendingOutput.Remove(kvp.Key);
        }
    }

    private int TryDepositToAdjacentOutput(WorldBase world, string itemName, int requestedCount)
    {
        if (!(world.GetTileEntity(0, SelectedOutputChestPos) is TileEntityComposite comp))
            return 0;

        TEFeatureStorage storage = comp.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
            return 0;

        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        if (itemValue?.ItemClass == null)
            return 0;

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

        int movedCount = requestedCount - remaining;
        if (movedCount > 0)
            storage.SetModified();

        return movedCount;
    }

    private static object GetMemberValue(object instance, string memberName)
    {
        if (instance == null || string.IsNullOrEmpty(memberName))
            return null;

        Type type = instance.GetType();
        PropertyInfo property = AccessTools.Property(type, memberName);
        if (property != null)
            return property.GetValue(instance, null);

        FieldInfo field = AccessTools.Field(type, memberName);
        if (field != null)
            return field.GetValue(instance);

        return null;
    }

    private int ReadIntProperty(string propertyName, int fallback, int min, int max)
    {
        string raw = blockValue.Block?.Properties?.GetString(propertyName);
        if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out int value))
            value = fallback;

        if (value < min) value = min;
        if (value > max) value = max;
        return value;
    }

    private static string FormatGallons(int milliGallons)
    {
        double gallons = milliGallons / (double)FluidConstants.MilliGallonsPerGallon;
        return gallons.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public string GetCycleTimerText() => IsProcessing ? $"{CycleTickCounter}/{Math.Max(1, CycleTickLength)}" : $"0/{Math.Max(1, CycleTickLength)}";
    public string GetPrimaryStatusText(WorldBase world) => !IsOn ? "Off" : (IsProcessing ? "Running" : "Waiting");
    public string GetSecondaryStatusText()
    {
        string secondary = !string.IsNullOrEmpty(LastBlockReason) ? LastBlockReason : (LastAction ?? string.Empty);
        string primary = IsOn ? (IsProcessing ? "Running" : "Waiting") : "Off";
        if (string.Equals(secondary, primary, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return secondary;
    }
    public string GetPendingOutputSummary() => pendingOutput.Count == 0 ? "(empty)" : string.Join(" | ", pendingOutput);
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

    public string GetPendingInputItemName() => IsProcessing ? (ActiveItemName ?? string.Empty) : string.Empty;
    public int GetPendingInputItemCount() => IsProcessing ? Math.Max(0, ActiveBatchSize) : 0;

    public bool ServerSelectInputContainer(Vector3i chestPos, string pipeGraphId)
    {
        Guid parsed = Guid.Empty;
        if (!string.IsNullOrEmpty(pipeGraphId))
            Guid.TryParse(pipeGraphId, out parsed);

        SelectedInputChestPos = chestPos;
        SelectedInputPipeGraphId = parsed;
        NeedsUiRefresh = true;
        setModified();
        return true;
    }

    public bool ServerSelectOutputContainer(Vector3i chestPos, OutputTransportMode mode, string pipeGraphId)
    {
        Guid parsed = Guid.Empty;
        if (!string.IsNullOrEmpty(pipeGraphId))
            Guid.TryParse(pipeGraphId, out parsed);

        SelectedOutputChestPos = chestPos;
        SelectedOutputMode = mode;
        SelectedOutputPipeGraphId = parsed;
        NeedsUiRefresh = true;
        setModified();
        return true;
    }

    public bool ServerToggleProcessMods()
    {
        ProcessItemArmorMods = !ProcessItemArmorMods;
        NeedsUiRefresh = true;
        setModified();
        return true;
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);
        if (mode != StreamModeWrite.Persistency)
            return;

        bw.Write(PersistVersion);
        bw.Write(SelectedInputChestPos.x); bw.Write(SelectedInputChestPos.y); bw.Write(SelectedInputChestPos.z);
        bw.Write(SelectedInputPipeGraphId.ToString());
        bw.Write(SelectedOutputChestPos.x); bw.Write(SelectedOutputChestPos.y); bw.Write(SelectedOutputChestPos.z);
        bw.Write((int)SelectedOutputMode);
        bw.Write(SelectedOutputPipeGraphId.ToString());
        bw.Write(ProcessItemArmorMods);
        bw.Write(baseReturnRate);
        bw.Write(baseBatchSize);
        bw.Write(IsProcessing);
        bw.Write(CycleTickCounter);
        bw.Write(CycleTickLength);
        bw.Write(ActiveBatchSize);
        bw.Write(ActiveItemName ?? string.Empty);
        bw.Write(ItemsProcessed);
        bw.Write(LastAction ?? string.Empty);
        bw.Write(LastBlockReason ?? string.Empty);
        bw.Write(fuelConfigured);
        bw.Write(fuelType ?? string.Empty);
        bw.Write(fuelBufferMg);
        bw.Write(fuelBufferCapacityMg);
        bw.Write(fuelUsePerSecondMg);
        bw.Write(fuelPullPerSecondMg);
        bw.Write(selectedFuelGraphId.ToString());
        bw.Write(LastFuelStatus ?? string.Empty);
        bw.Write(activeCycleOutput.Count);
        foreach (KeyValuePair<string, int> kvp in activeCycleOutput)
        {
            bw.Write(kvp.Key ?? string.Empty);
            bw.Write(Math.Max(0, kvp.Value));
        }
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);
        if (mode != StreamModeRead.Persistency)
            return;

        int version = br.ReadInt32();
        _ = version;
        SelectedInputChestPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
        Guid.TryParse(br.ReadString(), out SelectedInputPipeGraphId);
        SelectedOutputChestPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
        SelectedOutputMode = (OutputTransportMode)br.ReadInt32();
        Guid.TryParse(br.ReadString(), out SelectedOutputPipeGraphId);
        ProcessItemArmorMods = br.ReadBoolean();
        baseReturnRate = Math.Max(0f, br.ReadSingle());
        baseBatchSize = Math.Max(1, br.ReadInt32());
        IsProcessing = br.ReadBoolean();
        CycleTickCounter = Math.Max(0, br.ReadInt32());
        CycleTickLength = Math.Max(1, br.ReadInt32());
        ActiveBatchSize = Math.Max(0, br.ReadInt32());
        ActiveItemName = br.ReadString() ?? string.Empty;
        ItemsProcessed = Math.Max(0L, br.ReadInt64());
        LastAction = br.ReadString() ?? "Idle";
        LastBlockReason = br.ReadString() ?? string.Empty;
        fuelConfigured = br.ReadBoolean();
        fuelType = br.ReadString() ?? string.Empty;
        fuelBufferMg = Math.Max(0, br.ReadInt32());
        fuelBufferCapacityMg = Math.Max(0, br.ReadInt32());
        fuelUsePerSecondMg = Math.Max(0, br.ReadInt32());
        fuelPullPerSecondMg = Math.Max(0, br.ReadInt32());
        Guid.TryParse(br.ReadString(), out selectedFuelGraphId);
        LastFuelStatus = br.ReadString() ?? string.Empty;
        activeCycleOutput.Clear();
        if (version >= 2)
        {
            int activeCycleCount = Math.Max(0, br.ReadInt32());
            for (int i = 0; i < activeCycleCount; i++)
            {
                string itemName = br.ReadString();
                int count = Math.Max(0, br.ReadInt32());
                if (string.IsNullOrEmpty(itemName) || count <= 0)
                    continue;

                activeCycleOutput[itemName] = count;
            }
        }
        NeedsUiRefresh = true;
    }
}
