using System;
using System.Collections.Generic;
using System.IO;
using static TileEntityUniversalExtractor;

public class HigherLogicRegistry
{
    private readonly World world;
    private readonly Dictionary<Guid, IHLRSnapshot> snapshots;
    private ulong lastUpdateTime;
    private const ulong UPDATE_INTERVAL = 12; // 12 world ticks ~= 0.6s at 20 TPS
    private const ulong SAVE_INTERVAL = 360;
    private ulong lastSaveTime;
    private bool isDirty;

    private Dictionary<Guid, IHLRSnapshot> stagedSaveSnapshots;
    private bool saveCycleActive;
    private int saveBatchIndex;
    private HashSet<int> savedBatches;

    private const int BATCH_COUNT = 4;
    private int currentBatchIndex = 0;

    public static bool DevLogs = false;

    // Save and Load
    private string hlrDir;
    private string hlrFile;

    private const string HLR_FOLDER = "HLR";
    private const string HLR_FILE = "hlr_snapshots.dat";
    private const int HLR_VERSION = 2;

    public HigherLogicRegistry(World world)
    {
        this.world = world;
        snapshots = new Dictionary<Guid, IHLRSnapshot>();
        stagedSaveSnapshots = new Dictionary<Guid, IHLRSnapshot>();
        savedBatches = new HashSet<int>();
        saveCycleActive = false;
        saveBatchIndex = 0;

        HLRDevLog($"[HLR] CTOR — Snapshot dictionary initialized");
    }

    private static void HLRDevLog(string msg)
    {
        if (!DevLogs)
            return;

        Log.Out(msg);
    }

    public void Init()
    {
        HLRDevLog("[HLR] Init — Higher Logic Registry initialized");
        HLRDevLog($"[HLR] Init — Current snapshot count = {snapshots.Count}");
    }

    public void Update(ulong worldTime)
    {
        if (lastUpdateTime == 0)
        {
            lastUpdateTime = worldTime;
            return;
        }

        if (worldTime - lastUpdateTime < UPDATE_INTERVAL)
        {
            return;
        }

        lastUpdateTime = worldTime;

        HLRDevLog($"[HLR] Update tick — worldTime={worldTime}, lastSaveTime={lastSaveTime}, saveInterval={SAVE_INTERVAL}, snapshots={snapshots.Count}");

        if (snapshots.Count == 0)
        {
            return;
        }

        HLRDevLog($"[HLR] Simulating batch {currentBatchIndex + 1}/{BATCH_COUNT} — total snapshots={snapshots.Count}");

        int snapshotIndex = 0;
        int simulatedThisTick = 0;

        foreach (var snapshot in snapshots.Values)
        {
            if ((snapshotIndex % BATCH_COUNT) != currentBatchIndex)
            {
                snapshotIndex++;
                continue;
            }

            int missedTicks = GetMissedHLRTicks(snapshot, worldTime);
            SimulateSnapshot(snapshot, worldTime, missedTicks);
            simulatedThisTick++;
            snapshotIndex++;
        }

        HLRDevLog($"[HLR] Batch complete — simulated {simulatedThisTick} snapshot(s)");

        currentBatchIndex++;
        if (currentBatchIndex >= BATCH_COUNT)
            currentBatchIndex = 0;

        if (worldTime - lastSaveTime >= SAVE_INTERVAL)
        {
            HLRDevLog($"[HLR] Periodic save check @ worldTime={worldTime}");

            if (isDirty)
            {
                HLRDevLog($"[HLR] Periodic round-robin save step triggered @ worldTime={worldTime}");
                ProcessRoundRobinSave();
            }
            else
            {
                HLRDevLog("[HLR] Periodic save skipped — no dirty changes");
            }

            lastSaveTime = worldTime;
        }
    }

    public int AddPhantomExtractors(int count)
    {
        if (count <= 0)
        {
            HLRDevLog("[HLR][Phantom] AddPhantomExtractors ABORT — count <= 0");
            return 0;
        }

        int added = 0;
        ulong worldTime = world.GetWorldTime();

        for (int i = 0; i < count; i++)
        {
            var snapshot = new ExtractorSnapshotV1
            {
                MachineId = Guid.NewGuid(),
                Position = new Vector3i(-9999, -9999, -9999),
                IsOn = true,
                IsEnabledByPlayer = true,
                IsPhantom = true,
                WorldTime = worldTime,
                LastHLRSimTime = worldTime,
                Timers = new List<ResourceTimer>
            {
                new ResourceTimer
                {
                    Resource = "resourceScrapIron",
                    Counter = 0,
                    Speed = 10,
                    MinCount = 1,
                    MaxCount = 3
                }
            },
                OwedResources = new Dictionary<string, int>()
            };

            RegisterMachine(snapshot.MachineId, snapshot);
            added++;
        }

        HLRDevLog($"[HLR][Phantom] AddPhantomExtractors SUCCESS — added={added}");
        return added;
    }

    public int AddPhantomCrafters(int count, string recipeName, int ingredientMultiplier = 1000)
    {
        if (count <= 0)
        {
            HLRDevLog("[HLR][Phantom] AddPhantomCrafters ABORT — count <= 0");
            return 0;
        }

        if (string.IsNullOrEmpty(recipeName))
        {
            HLRDevLog("[HLR][Phantom] AddPhantomCrafters ABORT — recipeName is null/empty");
            return 0;
        }

        Recipe recipe = CraftingManager.GetRecipe(recipeName);
        if (recipe == null)
        {
            Log.Error($"[HLR][Phantom] AddPhantomCrafters FAIL — recipe '{recipeName}' not found");
            return 0;
        }

        int added = 0;
        ulong worldTime = world.GetWorldTime();

        for (int i = 0; i < count; i++)
        {
            var ingredientCounts = new Dictionary<string, int>();            var owedResources = new Dictionary<string, int>();

            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient.count <= 0)
                    continue;

                string itemName = ingredient.itemValue.ItemClass.GetItemName();
                ingredientCounts[itemName] = ingredient.count * ingredientMultiplier;
            }

            var snapshot = new CrafterSnapshot
            {
                MachineId = Guid.NewGuid(),
                Position = new Vector3i(-9998 - i, -9998, -9998),
                IsPhantom = true,
                IsCrafting = true,
                DisabledByPlayer = false,
                RecipeName = recipeName,
                BaseRecipeDuration = recipe.craftingTime,
                CraftSpeed = 1f,
                IngredientCount = ingredientCounts,
                OwedResources = owedResources,
                LastHLRSimTime = worldTime
            };

            RegisterMachine(snapshot.MachineId, snapshot);
            added++;
        }

        HLRDevLog($"[HLR][Phantom] AddPhantomCrafters SUCCESS — added={added} recipe='{recipeName}'");
        return added;
    }

    public int ClearPhantomMachines()
    {
        List<Guid> toRemove = new List<Guid>();

        foreach (var kvp in snapshots)
        {
            if (kvp.Value is ExtractorSnapshotV1 extractor && extractor.IsPhantom)
            {
                toRemove.Add(kvp.Key);
                continue;
            }

            if (kvp.Value is CrafterSnapshot crafter && crafter.IsPhantom)
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (Guid id in toRemove)
        {
            snapshots.Remove(id);
        }

        if (toRemove.Count > 0)
        {
            isDirty = true;
        }

        HLRDevLog($"[HLR][Phantom] ClearPhantomMachines SUCCESS — removed={toRemove.Count}");
        return toRemove.Count;
    }

    private bool IsPhantomSnapshot(IHLRSnapshot snapshot)
    {
        if (snapshot is ExtractorSnapshotV1 extractor)
            return extractor.IsPhantom;

        if (snapshot is CrafterSnapshot crafter)
            return crafter.IsPhantom;

        return false;
    }

    private void SimulateSnapshot(IHLRSnapshot snapshot, ulong worldTime, int hlrTicksToSimulate)
    {
        switch (snapshot)
        {
            case ExtractorSnapshotV1 extractor:
                HLRDevLog($"[HLR][Extractor] Simulate @ {extractor.Position} ticks={hlrTicksToSimulate}");
                SimulateExtractor(extractor, worldTime, hlrTicksToSimulate);
                extractor.LastHLRSimTime = worldTime;
                isDirty = true;
                break;

            case CrafterSnapshot crafter:
                HLRDevLog($"[HLR][Crafter] Simulate @ {crafter.Position} ticks={hlrTicksToSimulate}");
                SimulateCrafter(crafter, worldTime, hlrTicksToSimulate);
                crafter.LastHLRSimTime = worldTime;
                isDirty = true;
                break;

            default:
                HLRDevLog($"[HLR] Unknown snapshot type '{snapshot.SnapshotKind}' — skipping");
                break;
        }
    }

    private void SimulateExtractor(ExtractorSnapshotV1 extractor, ulong worldTime, int hlrTicksToSimulate)
    {
        if (!extractor.IsOn)
        {
            HLRDevLog($"[HLR][Extractor] SKIP — extractor OFF @ {extractor.Position}");
            return;
        }

        if (!HasValidGraphStorageEndpoint(extractor.SelectedOutputPipeGraphId, extractor.SelectedOutputChestPos))
        {
            extractor.IsOn = false;
            extractor.IsEnabledByPlayer = false;
            HLRDevLog("[HLR][Extractor] STOP — Missing output graph/storage endpoint; shutting down extractor");
            return;
        }

        HLRDevLog($"[HLR][Extractor] Simulate BEGIN @ {extractor.Position} ticks={hlrTicksToSimulate}");

        FlushOwedResourcesToGraph(extractor.SelectedOutputPipeGraphId, extractor.SelectedOutputChestPos, extractor.OwedResources, "Extractor");

        foreach (var timer in extractor.Timers)
        {
            timer.Counter += hlrTicksToSimulate;

            if (timer.Speed <= 0)
                continue;

            int completedCycles = timer.Counter / timer.Speed;
            timer.Counter = timer.Counter % timer.Speed;

            if (completedCycles <= 0)
                continue;

            int totalProduced = 0;

            for (int i = 0; i < completedCycles; i++)
            {
                int amount = timer.MinCount;
                if (timer.MinCount < timer.MaxCount)
                    amount = UnityEngine.Random.Range(timer.MinCount, timer.MaxCount + 1);

                totalProduced += amount;
            }

            if (totalProduced <= 0)
                continue;

            var produced = new Dictionary<string, int>
            {
                [timer.Resource] = totalProduced
            };

            TryDepositSnapshotOutput(extractor.SelectedOutputPipeGraphId, extractor.SelectedOutputChestPos, produced, out Dictionary<string, int> deposited);

            int accepted = 0;
            if (deposited != null && deposited.TryGetValue(timer.Resource, out int acceptedCount))
                accepted = acceptedCount;

            int remaining = totalProduced - accepted;
            if (remaining > 0)
                AddToOwedDictionary(extractor.OwedResources, timer.Resource, remaining);

            int nowOwed = extractor.OwedResources.TryGetValue(timer.Resource, out int owedValue) ? owedValue : 0;
            HLRDevLog($"[HLR][Extractor] PRODUCED — {totalProduced}x {timer.Resource} deposited={accepted} owed={nowOwed}");
        }

        extractor.WorldTime = worldTime;
        HLRDevLog($"[HLR][Extractor] Simulate END @ {extractor.Position}");
    }

    private void SimulateCrafter(CrafterSnapshot crafter, ulong worldTime, int hlrTicksToSimulate)
    {
        HLRDevLog($"[HLR][Crafter] ========================================");
        HLRDevLog($"[HLR][Crafter] SIMULATE BEGIN @ {crafter.Position} ticks={hlrTicksToSimulate}");

        if (!crafter.IsCrafting || crafter.DisabledByPlayer || string.IsNullOrEmpty(crafter.RecipeName))
            return;

        Recipe recipe = CraftingManager.GetRecipe(crafter.RecipeName);
        if (recipe == null)
            return;

        if (crafter.CraftSpeed <= 0f)
            return;

        float effectiveCraftTime = crafter.BaseRecipeDuration / crafter.CraftSpeed;
        if (effectiveCraftTime <= 0f)
            return;

        if (crafter.SelectedInputPipeGraphId == Guid.Empty || crafter.SelectedInputChestPos == Vector3i.zero)
        {
            HLRDevLog("[HLR][Crafter] STOP — Missing input graph/chest context");
            crafter.IsCrafting = false;
            crafter.DisabledByPlayer = true;
            return;
        }

        if (!HasValidGraphStorageEndpoint(crafter.SelectedInputPipeGraphId, crafter.SelectedInputChestPos))
        {
            HLRDevLog("[HLR][Crafter] STOP — Input graph/storage endpoint unavailable");
            crafter.IsCrafting = false;
            crafter.DisabledByPlayer = true;
            return;
        }

        if (crafter.SelectedOutputPipeGraphId == Guid.Empty || crafter.SelectedOutputChestPos == Vector3i.zero)
        {
            HLRDevLog("[HLR][Crafter] STOP — Missing output graph/chest context");
            crafter.IsCrafting = false;
            crafter.DisabledByPlayer = true;
            return;
        }

        if (!HasValidGraphStorageEndpoint(crafter.SelectedOutputPipeGraphId, crafter.SelectedOutputChestPos))
        {
            HLRDevLog("[HLR][Crafter] STOP — Output graph/storage endpoint unavailable");
            crafter.IsCrafting = false;
            crafter.DisabledByPlayer = true;
            return;
        }

        FlushOwedResourcesToGraph(crafter.SelectedOutputPipeGraphId, crafter.SelectedOutputChestPos, crafter.OwedResources, "Crafter");

        float simulatedSeconds = ((float)UPDATE_INTERVAL * hlrTicksToSimulate) / 20f;
        crafter.CraftProgressSeconds += simulatedSeconds;

        int craftsThisTick = (int)(crafter.CraftProgressSeconds / effectiveCraftTime);
        if (craftsThisTick <= 0)
            return;


        if (!PipeGraphManager.TryGetStorageItemCounts(world, 0, crafter.SelectedInputPipeGraphId, crafter.SelectedInputChestPos, out Dictionary<string, int> availableCounts))
        {
            HLRDevLog("[HLR][Crafter] STOP — Input storage snapshot unavailable");
            crafter.IsCrafting = false;
            crafter.DisabledByPlayer = true;
            return;
        }

        Dictionary<string, int> requiredForCrafts = new Dictionary<string, int>();

        foreach (var ingredient in recipe.ingredients)
        {
            if (ingredient.count <= 0 || ingredient.itemValue?.ItemClass == null)
                continue;

            string itemName = ingredient.itemValue.ItemClass.GetItemName();
            int available = availableCounts.TryGetValue(itemName, out int found) ? found : 0;
            int maxForIngredient = available / ingredient.count;

            craftsThisTick = Math.Min(craftsThisTick, maxForIngredient);
            if (craftsThisTick <= 0)
            {
                crafter.IsCrafting = false;
                HLRDevLog($"[HLR][Crafter] STOP — Ingredient '{itemName}' unavailable");
                return;
            }
        }

        foreach (var ingredient in recipe.ingredients)
        {
            if (ingredient.count <= 0 || ingredient.itemValue?.ItemClass == null)
                continue;

            string itemName = ingredient.itemValue.ItemClass.GetItemName();
            int requiredCount = ingredient.count * craftsThisTick;
            if (requiredCount > 0)
                requiredForCrafts[itemName] = requiredCount;
        }

        if (!PipeGraphManager.TryConsumeStorageItems(world, 0, crafter.SelectedInputPipeGraphId, crafter.SelectedInputChestPos, requiredForCrafts, out Dictionary<string, int> consumed))
        {
            HLRDevLog("[HLR][Crafter] STOP — Failed to consume required ingredients from graph snapshot");
            crafter.IsCrafting = false;
            return;
        }

        crafter.CraftProgressSeconds -= craftsThisTick * effectiveCraftTime;

        ItemClass outputClass = recipe.GetOutputItemClass();
        if (outputClass != null)
        {
            string outputName = outputClass.GetItemName();
            int produced = recipe.count * craftsThisTick;

            var producedMap = new Dictionary<string, int>
            {
                [outputName] = produced
            };

            TryDepositSnapshotOutput(crafter.SelectedOutputPipeGraphId, crafter.SelectedOutputChestPos, producedMap, out Dictionary<string, int> deposited);

            int depositedCount = 0;
            if (deposited != null && deposited.TryGetValue(outputName, out int d))
                depositedCount = d;

            int remaining = produced - depositedCount;
            if (remaining > 0)
                AddToOwedDictionary(crafter.OwedResources, outputName, remaining);

            int owedNow = crafter.OwedResources.TryGetValue(outputName, out int owed) ? owed : 0;
            HLRDevLog($"[HLR][Crafter] PRODUCE — {produced}x {outputName} deposited={depositedCount} owed={owedNow}");
        }

        HLRDevLog($"[HLR][Crafter] SIMULATE END @ {crafter.Position}");
    }

    private void FlushOwedResourcesToGraph(Guid pipeGraphId, Vector3i storagePos, Dictionary<string, int> owedResources, string snapshotKind)
    {
        if (owedResources == null || owedResources.Count == 0)
            return;

        if (pipeGraphId == Guid.Empty || storagePos == Vector3i.zero)
            return;

        Dictionary<string, int> request = new Dictionary<string, int>();
        foreach (var kvp in owedResources)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            request[kvp.Key] = kvp.Value;
        }

        if (request.Count == 0)
            return;

        if (!TryDepositSnapshotOutput(pipeGraphId, storagePos, request, out Dictionary<string, int> deposited) || deposited == null || deposited.Count == 0)
            return;

        List<string> toRemove = null;

        foreach (var kvp in deposited)
        {
            if (!owedResources.TryGetValue(kvp.Key, out int existing))
                continue;

            int remaining = existing - kvp.Value;
            if (remaining > 0)
                owedResources[kvp.Key] = remaining;
            else
            {
                if (toRemove == null)
                    toRemove = new List<string>();

                toRemove.Add(kvp.Key);
            }
        }

        if (toRemove != null)
        {
            for (int i = 0; i < toRemove.Count; i++)
                owedResources.Remove(toRemove[i]);
        }

        HLRDevLog($"[HLR][{snapshotKind}] Flushed owed resources to graph={pipeGraphId} pos={storagePos} depositedTypes={deposited.Count}");
    }

    private bool TryDepositSnapshotOutput(
        Guid pipeGraphId,
        Vector3i storagePos,
        Dictionary<string, int> toDeposit,
        out Dictionary<string, int> deposited)
    {
        deposited = new Dictionary<string, int>();

        if (pipeGraphId == Guid.Empty || storagePos == Vector3i.zero)
            return false;

        return PipeGraphManager.TryDepositStorageItems(world, 0, pipeGraphId, storagePos, toDeposit, out deposited);
    }

    private bool HasValidGraphStorageEndpoint(Guid pipeGraphId, Vector3i storagePos)
    {
        if (pipeGraphId == Guid.Empty || storagePos == Vector3i.zero)
            return false;

        return PipeGraphManager.TryGetStorageItemCounts(world, 0, pipeGraphId, storagePos, out _);
    }

    private static void AddToOwedDictionary(Dictionary<string, int> map, string itemName, int amount)
    {
        if (map == null || string.IsNullOrEmpty(itemName) || amount <= 0)
            return;

        if (map.TryGetValue(itemName, out int existing))
            map[itemName] = existing + amount;
        else
            map[itemName] = amount;
    }

    private int GetMissedHLRTicks(IHLRSnapshot snapshot, ulong worldTime)
    {
        ulong lastSimTime = 0;

        if (snapshot is ExtractorSnapshotV1 extractor)
            lastSimTime = extractor.LastHLRSimTime;
        else if (snapshot is CrafterSnapshot crafter)
            lastSimTime = crafter.LastHLRSimTime;
        else
            return 0;

        if (lastSimTime == 0 || worldTime <= lastSimTime)
            return 1;

        ulong elapsed = worldTime - lastSimTime;
        int ticks = (int)(elapsed / UPDATE_INTERVAL);

        if (ticks <= 0)
            ticks = 1;

        return ticks;
    }

    private void StageSaveBatch(int batchIndex)
    {
        HLRDevLog($"[HLR][Save] StageSaveBatch BEGIN — batch {batchIndex + 1}/{BATCH_COUNT}");

        int snapshotIndex = 0;
        int stagedCount = 0;

        foreach (var kvp in snapshots)
        {
            IHLRSnapshot snapshot = kvp.Value;

            if ((snapshotIndex % BATCH_COUNT) != batchIndex)
            {
                snapshotIndex++;
                continue;
            }

            if (IsPhantomSnapshot(snapshot))
            {
                snapshotIndex++;
                continue;
            }

            IHLRSnapshot cloned = CloneSnapshotForSave(snapshot);
            if (cloned != null)
            {
                stagedSaveSnapshots[kvp.Key] = cloned;
                stagedCount++;
            }
            snapshotIndex++;
        }

        savedBatches.Add(batchIndex);

        HLRDevLog($"[HLR][Save] StageSaveBatch END — staged {stagedCount} snapshot(s)");
    }

    private void BeginSaveCycle()
    {
        stagedSaveSnapshots.Clear();
        savedBatches.Clear();
        saveBatchIndex = 0;
        saveCycleActive = true;

        HLRDevLog("[HLR][Save] BeginSaveCycle — round-robin save started");
    }

    private bool IsSaveCycleComplete()
    {
        return savedBatches.Count >= BATCH_COUNT;
    }

    private void ProcessRoundRobinSave()
    {
        if (!saveCycleActive)
            BeginSaveCycle();

        StageSaveBatch(saveBatchIndex);

        saveBatchIndex++;
        if (saveBatchIndex >= BATCH_COUNT)
            saveBatchIndex = 0;

        if (IsSaveCycleComplete())
        {
            HLRDevLog("[HLR][Save] Round-robin cycle complete — finalizing full save");
            SaveSnapshotSet(stagedSaveSnapshots);
            stagedSaveSnapshots.Clear();
            savedBatches.Clear();
            saveCycleActive = false;
            isDirty = false;
        }
    }

    private IHLRSnapshot CloneSnapshotForSave(IHLRSnapshot snapshot)
    {
        switch (snapshot)
        {
            case ExtractorSnapshotV1 extractor:
                return CloneExtractorSnapshot(extractor);

            case CrafterSnapshot crafter:
                return CloneCrafterSnapshot(crafter);

            default:
                Log.Error($"[HLR][Save] CloneSnapshotForSave FAIL — unknown snapshot kind '{snapshot?.SnapshotKind}'");
                return null;
        }
    }

    private ExtractorSnapshotV1 CloneExtractorSnapshot(ExtractorSnapshotV1 source)
    {
        var clone = new ExtractorSnapshotV1
        {
            MachineId = source.MachineId,
            Position = source.Position,
            IsOn = source.IsOn,
            IsEnabledByPlayer = source.IsEnabledByPlayer,
            IsPhantom = source.IsPhantom,
            WorldTime = source.WorldTime,
            LastHLRSimTime = source.LastHLRSimTime,
            SelectedOutputChestPos = source.SelectedOutputChestPos,
            SelectedOutputPipeGraphId = source.SelectedOutputPipeGraphId,
            Timers = new List<ResourceTimer>(),
            OwedResources = new Dictionary<string, int>()
        };

        if (source.Timers != null)
        {
            foreach (var timer in source.Timers)
            {
                clone.Timers.Add(new ResourceTimer
                {
                    Resource = timer.Resource,
                    Counter = timer.Counter,
                    Speed = timer.Speed,
                    MinCount = timer.MinCount,
                    MaxCount = timer.MaxCount
                });
            }
        }

        if (source.OwedResources != null)
        {
            foreach (var kvp in source.OwedResources)
            {
                clone.OwedResources[kvp.Key] = kvp.Value;
            }
        }

        return clone;
    }

    private CrafterSnapshot CloneCrafterSnapshot(CrafterSnapshot source)
    {
        var clone = new CrafterSnapshot
        {
            MachineId = source.MachineId,
            Position = source.Position,
            IsPhantom = source.IsPhantom,
            IsCrafting = source.IsCrafting,
            DisabledByPlayer = source.DisabledByPlayer,
            RecipeName = source.RecipeName,
            BaseRecipeDuration = source.BaseRecipeDuration,
            CraftSpeed = source.CraftSpeed,
            CraftProgressSeconds = source.CraftProgressSeconds,
            LastHLRSimTime = source.LastHLRSimTime,
            SelectedInputChestPos = source.SelectedInputChestPos,
            SelectedInputPipeGraphId = source.SelectedInputPipeGraphId,
            SelectedOutputChestPos = source.SelectedOutputChestPos,
            SelectedOutputPipeGraphId = source.SelectedOutputPipeGraphId,
            IngredientCount = new Dictionary<string, int>(),
            OwedResources = new Dictionary<string, int>()
        };

        if (source.IngredientCount != null)
        {
            foreach (var kvp in source.IngredientCount)
            {
                clone.IngredientCount[kvp.Key] = kvp.Value;
            }
        }

        if (source.OwedResources != null)
        {
            foreach (var kvp in source.OwedResources)
            {
                clone.OwedResources[kvp.Key] = kvp.Value;
            }
        }

        return clone;
    }

    public Dictionary<string, int> GetSnapshotCountsByType()
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in snapshots.Values)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.SnapshotKind))
                continue;

            if (!counts.ContainsKey(snapshot.SnapshotKind))
                counts[snapshot.SnapshotKind] = 1;
            else
                counts[snapshot.SnapshotKind]++;
        }

        return counts;
    }

    public int GetSnapshotCount(string snapshotKind)
    {
        if (string.IsNullOrEmpty(snapshotKind))
            return 0;

        int count = 0;

        foreach (var snapshot in snapshots.Values)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.SnapshotKind))
                continue;

            if (string.Equals(snapshot.SnapshotKind, snapshotKind, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    public int GetTotalSnapshotCount()
    {
        return snapshots.Count;
    }

    public int GetPhantomSnapshotCount()
    {
        int count = 0;

        foreach (var snapshot in snapshots.Values)
        {
            if (snapshot != null && IsPhantomSnapshot(snapshot))
                count++;
        }

        return count;
    }

    public int GetRealSnapshotCount()
    {
        int count = 0;

        foreach (var snapshot in snapshots.Values)
        {
            if (snapshot != null && !IsPhantomSnapshot(snapshot))
                count++;
        }

        return count;
    }

    public int GetPhantomSnapshotCount(string snapshotKind)
    {
        if (string.IsNullOrEmpty(snapshotKind))
            return 0;

        int count = 0;

        foreach (var snapshot in snapshots.Values)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.SnapshotKind))
                continue;

            if (!string.Equals(snapshot.SnapshotKind, snapshotKind, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsPhantomSnapshot(snapshot))
                count++;
        }

        return count;
    }

    public int GetRealSnapshotCount(string snapshotKind)
    {
        if (string.IsNullOrEmpty(snapshotKind))
            return 0;

        int count = 0;

        foreach (var snapshot in snapshots.Values)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.SnapshotKind))
                continue;

            if (!string.Equals(snapshot.SnapshotKind, snapshotKind, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsPhantomSnapshot(snapshot))
                count++;
        }

        return count;
    }

    // ---------------------------------------------
    // REGISTRATION
    // ---------------------------------------------
    public void RegisterMachine(Guid machineId, IHLRSnapshot snapshot)
    {
        HLRDevLog($"[HLR] RegisterMachine — BEGIN id={machineId}");

        if (snapshot == null)
        {
            Log.Error("[HLR] RegisterMachine FAIL — snapshot is null");
            return;
        }

        if (string.IsNullOrEmpty(snapshot.SnapshotKind))
        {
            Log.Error("[HLR] RegisterMachine FAIL — SnapshotKind is null or empty");
            return;
        }

        if (machineId == Guid.Empty)
        {
            Log.Error("[HLR] RegisterMachine FAIL — machineId is Guid.Empty");
            return;
        }

        bool replacing = snapshots.ContainsKey(machineId);

        snapshots[machineId] = snapshot;
        isDirty = true;

        HLRDevLog(
            $"[HLR] RegisterMachine — SUCCESS " +
            $"id={machineId} " +
            $"kind={snapshot.SnapshotKind} " +
            $"version={snapshot.SnapshotVersion} " +
            $"replacedExisting={replacing}"
        );

        HLRDevLog($"[HLR] RegisterMachine — Active snapshots = {snapshots.Count}");
    }

    // ---------------------------------------------
    // UNREGISTRATION
    // ---------------------------------------------
    public bool TryUnregisterMachine(Guid machineId, out IHLRSnapshot snapshot)
    {
        HLRDevLog($"[HLR] TryUnregisterMachine — BEGIN id={machineId}");

        snapshot = null;

        if (machineId == Guid.Empty)
        {
            Log.Error("[HLR] TryUnregisterMachine FAIL — machineId is Guid.Empty");
            return false;
        }

        if (!snapshots.TryGetValue(machineId, out snapshot))
        {
            HLRDevLog($"[HLR] TryUnregisterMachine — MISS id={machineId}");
            HLRDevLog($"[HLR] TryUnregisterMachine — Active snapshots = {snapshots.Count}");
            return false;
        }

        snapshots.Remove(machineId);
        isDirty = true;

        HLRDevLog(
            $"[HLR] TryUnregisterMachine — SUCCESS " +
            $"id={machineId} " +
            $"kind={snapshot.SnapshotKind} " +
            $"version={snapshot.SnapshotVersion}"
        );

        HLRDevLog($"[HLR] TryUnregisterMachine — Active snapshots = {snapshots.Count}");
        return true;
    }

    private IHLRSnapshot CreateSnapshot(string kind, int version)
    {
        switch (kind)
        {
            case "Extractor":
                if (version == 1 || version == 2)
                {
                    return new ExtractorSnapshotV1();
                }
                Log.Error($"[HLR][Factory] Unsupported Extractor version {version}");
                return null;

            case "Crafter":
                if (version == 1 || version == 2)
                {
                    return new CrafterSnapshot();
                }
                HLRDevLog($"[HLR][Factory] Unsupported Crafter version {version}");
                return null;

            default:
                Log.Error($"[HLR][Factory] Unknown snapshot kind '{kind}'");
                return null;
        }
    }

    private void EnsureSavePaths()
    {
        if (!string.IsNullOrEmpty(hlrFile))
            return;
        string saveRoot = GameIO.GetSaveGameDir();

        hlrDir = Path.Combine(saveRoot, HLR_FOLDER);
        hlrFile = Path.Combine(hlrDir, HLR_FILE);

        try
        {
            if (!Directory.Exists(hlrDir))
            {
                Directory.CreateDirectory(hlrDir);
                HLRDevLog($"[HLR][IO] Created directory: {hlrDir}");
            }

            HLRDevLog($"[HLR][IO] Save root: {saveRoot}");
            HLRDevLog($"[HLR][IO] File path: {hlrFile}");
        }

        catch (Exception ex)
        {
            Log.Error($"[HLR][IO] EnsureSavePaths FAILED: {ex}");
        }
    }

    public void Save()
    {
        Dictionary<Guid, IHLRSnapshot> realSnapshots = new Dictionary<Guid, IHLRSnapshot>();

        foreach (var kvp in snapshots)
        {
            if (IsPhantomSnapshot(kvp.Value))
                continue;

            realSnapshots[kvp.Key] = kvp.Value;
        }

        SaveSnapshotSet(realSnapshots);
        isDirty = false;

        HLRDevLog($"[HLR] Save Called! file={hlrFile}");
    }

    private void SaveSnapshotSet(Dictionary<Guid, IHLRSnapshot> sourceSnapshots)
    {
        EnsureSavePaths();

        string tempFile = hlrFile + ".tmp";

        try
        {
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(new char[] { 'H', 'L', 'R' });
                bw.Write(HLR_VERSION);

                int snapshotCount = sourceSnapshots.Count;
                bw.Write(snapshotCount);
                HLRDevLog($"[HLR][IO] Save — snapshotCount={snapshotCount}");

                foreach (var snapshot in sourceSnapshots.Values)
                {
                    bw.Write(snapshot.SnapshotKind);
                    bw.Write(snapshot.SnapshotVersion);

                    bw.Write(snapshot.MachineId.ToByteArray());
                    bw.Write(snapshot.Position.x);
                    bw.Write(snapshot.Position.y);
                    bw.Write(snapshot.Position.z);

                    HLRDevLog($"[HLR][IO] Save — snapshot kind={snapshot.SnapshotKind} version={snapshot.SnapshotVersion} machineid={snapshot.MachineId}");

                    if (snapshot is ExtractorSnapshotV1 extractor)
                        SaveExtractorSnapshot(bw, extractor);

                    if (snapshot is CrafterSnapshot crafter)
                        SaveCrafterSnapshot(bw, crafter);
                }
            }

            if (File.Exists(hlrFile))
                File.Delete(hlrFile);

            File.Move(tempFile, hlrFile);

            HLRDevLog($"[HLR][IO] Save OK — wrote header v{HLR_VERSION}");
        }
        catch (Exception ex)
        {
            Log.Error($"[HLR][IO] Save FAILED: {ex}");

            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch { }
        }
    }

    private void SaveExtractorSnapshot(BinaryWriter bw, ExtractorSnapshotV1 extractor)
    {
        HLRDevLog($"[HLR][Extractor][Save] BEGIN MachineId={extractor.MachineId}");
        HLRDevLog($"[HLR][Extractor][Save] IsOn={extractor.IsOn}");
        HLRDevLog($"[HLR][Extractor][Save] WorldTime={extractor.WorldTime}");

        // IsOn
        bw.Write(extractor.IsOn);
        bw.Write(extractor.IsEnabledByPlayer);

        // WorldTime
        bw.Write(extractor.WorldTime);

        // Timers
        int timerCount = extractor.Timers?.Count ?? 0;
        bw.Write(timerCount);
        HLRDevLog($"[HLR][Extractor][Save] Timers COUNT={timerCount}");

        if (extractor.Timers != null)
        {
            for (int i = 0; i < extractor.Timers.Count; i++)
            {
                var t = extractor.Timers[i];
                HLRDevLog(
                    $"[HLR][Extractor][Save] Timer[{i}] " +
                    $"res={t.Resource} counter={t.Counter}/{t.Speed} " +
                    $"min={t.MinCount} max={t.MaxCount}"
                );

                bw.Write(t.Resource);
                bw.Write(t.Counter);
                bw.Write(t.Speed);
                bw.Write(t.MinCount);
                bw.Write(t.MaxCount);
            }
        }

        // OwedResources
        int owedCount = extractor.OwedResources?.Count ?? 0;
        bw.Write(owedCount);
        HLRDevLog($"[HLR][Extractor][Save] OwedResources COUNT={owedCount}");

        if (extractor.OwedResources != null)
        {
            foreach (var kvp in extractor.OwedResources)
            {
                HLRDevLog($"[HLR][Extractor][Save] Owed {kvp.Key} x{kvp.Value}");
                bw.Write(kvp.Key);
                bw.Write(kvp.Value);
            }
        }

        bw.Write(extractor.SelectedOutputChestPos.x);
        bw.Write(extractor.SelectedOutputChestPos.y);
        bw.Write(extractor.SelectedOutputChestPos.z);
        bw.Write(extractor.SelectedOutputPipeGraphId.ToString());

        HLRDevLog($"[HLR][Extractor][Save] PipeGraph={extractor.SelectedOutputPipeGraphId} OutputPos={extractor.SelectedOutputChestPos}");
        HLRDevLog($"[HLR][Extractor][Save] END");
    }

    private void SaveCrafterSnapshot(BinaryWriter bw, CrafterSnapshot crafter)
    {
        HLRDevLog($"[HLR][Crafter][Save] Begin for MachineId={crafter.MachineId}");

        bw.Write(crafter.IsCrafting);
        bw.Write(crafter.DisabledByPlayer);
        bw.Write(crafter.RecipeName);
        bw.Write(crafter.BaseRecipeDuration);
        bw.Write(crafter.CraftSpeed);

        HLRDevLog(
        $"[HLR][Crafter][Save] IsCrafting={crafter.IsCrafting} " +
        $"Disabled={crafter.DisabledByPlayer} " +
        $"Recipe='{crafter.RecipeName}' " +
        $"Base={crafter.BaseRecipeDuration} Speed={crafter.CraftSpeed}");

        // Ingredient count (dictionary)

        int ingredientCount = crafter.IngredientCount?.Count ?? 0;
        bw.Write(ingredientCount);

        HLRDevLog($"[HLR][Crafter][Save] IngredientCount COUNT={ingredientCount}");

        if (crafter.IngredientCount != null)
        {
            foreach (var kvp in crafter.IngredientCount)
            {
                bw.Write(kvp.Key);
                bw.Write(kvp.Value);

                HLRDevLog($"[HLR][Crafter][Save] Ingredient {kvp.Key} x{kvp.Value}");
            }
        }
        // UsedIngredients removed from active simulation state, keep a zero count for compatibility with existing save layout.
        bw.Write(0);

        // Owed Resources (dictionary)

        int owedCount = crafter.OwedResources?.Count ?? 0;
        bw.Write(owedCount);
        if (crafter.OwedResources != null)
        {
            foreach (var kvp in crafter.OwedResources)
            {
                bw.Write(kvp.Key);
                bw.Write(kvp.Value);

                HLRDevLog($"[HLR][Crafter][Save] Owed {kvp.Key} x{kvp.Value}");
            }
        }

        bw.Write(crafter.SelectedInputChestPos.x);
        bw.Write(crafter.SelectedInputChestPos.y);
        bw.Write(crafter.SelectedInputChestPos.z);
        bw.Write(crafter.SelectedInputPipeGraphId.ToString());

        bw.Write(crafter.SelectedOutputChestPos.x);
        bw.Write(crafter.SelectedOutputChestPos.y);
        bw.Write(crafter.SelectedOutputChestPos.z);
        bw.Write(crafter.SelectedOutputPipeGraphId.ToString());

        HLRDevLog($"[HLR][Crafter][Save] InputGraph={crafter.SelectedInputPipeGraphId} InputPos={crafter.SelectedInputChestPos}");
        HLRDevLog($"[HLR][Crafter][Save] OutputGraph={crafter.SelectedOutputPipeGraphId} OutputPos={crafter.SelectedOutputChestPos}");
        HLRDevLog($"[HLR][Crafter][Save] END");
    }

    public void Load()
    {
        EnsureSavePaths();

        if (!File.Exists(hlrFile))
        {
            HLRDevLog("[HLR][IO] Load — no file found, starting fresh");
            return;
        }

        try
        {
            using (var fs = new FileStream(hlrFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                char[] magicChars = br.ReadChars(3);
                string magic = new string(magicChars);

                if (magic != "HLR")
                {
                    Log.Error($"[HLR][IO] Load FAILED — bad magic '{magic}'");
                    return;
                }

                int fileVersion = br.ReadInt32();

                if (fileVersion != HLR_VERSION)
                {
                    Log.Error($"[HLR][IO] Load FAILED — unsupported version {fileVersion}");
                    return;
                }

                HLRDevLog($"[HLR][IO] Load OK — header valid (v{fileVersion})");

                // Load Snapshot Count
                int snapshotCount = br.ReadInt32();
                HLRDevLog($"[HLR][IO] Load — snapshotCount={snapshotCount}");

                // Snapshot Information
                for (int i = 0; i < snapshotCount; i++)
                {
                    string kind = br.ReadString();
                    int snapshotVersion = br.ReadInt32();

                    HLRDevLog($"[HLR][IO] Load — snapshot[{i}] kind={kind} version={snapshotVersion}");

                    var snapshot = CreateSnapshot(kind, snapshotVersion);
                    if (snapshot == null)
                        continue;

                    HLRDevLog($"[HLR][Factory] Created snapshot {kind} v{snapshotVersion}");

                    // Snapshot Construction
                    Guid machineId = new Guid(br.ReadBytes(16));
                    int x = br.ReadInt32();
                    int y = br.ReadInt32();
                    int z = br.ReadInt32();

                    snapshot.MachineId = machineId;
                    snapshot.Position = new Vector3i(x, y, z);

                    HLRDevLog($"[HLR][IO] Loaded snapshot id={machineId} pos={snapshot.Position} kind={kind}");

                    if (snapshot is ExtractorSnapshotV1 extractor)
                        LoadExtractorSnapshot(br, extractor, snapshotVersion);
                    if (snapshot is CrafterSnapshot crafter)
                        LoadCrafterSnapshot(br, crafter, snapshotVersion);
                    snapshots[machineId] = snapshot;
                }
            }
        }

        catch (EndOfStreamException)
        {
            Log.Error("[HLR][IO] Load FAILED — file truncated");
        }
        catch (Exception ex)
        {
            Log.Error($"[HLR][IO] Load FAILED — exception: {ex}");
        }
        HLRDevLog($"[HLR] Load Called! file={hlrFile}");
    }

    private void LoadExtractorSnapshot(BinaryReader br, ExtractorSnapshotV1 extractor, int snapshotVersion)
    {
        HLRDevLog($"[HLR][Extractor][Load] BEGIN MachineId={extractor.MachineId}");

        extractor.IsOn = br.ReadBoolean();
        extractor.IsEnabledByPlayer = br.ReadBoolean();

        extractor.WorldTime = br.ReadUInt64();

        HLRDevLog($"[HLR][Extractor][Load] IsOn={extractor.IsOn}");
        HLRDevLog($"[HLR][Extractor][Load] WorldTime={extractor.WorldTime}");

        // Timers
        int timerCount = br.ReadInt32();
        extractor.Timers = new List<ResourceTimer>(timerCount);

        HLRDevLog($"[HLR][Extractor][Load] Timers COUNT={timerCount}");

        for (int i = 0; i < timerCount; i++)
        {
            var timer = new ResourceTimer
            {
                Resource = br.ReadString(),
                Counter = br.ReadInt32(),
                Speed = br.ReadInt32(),
                MinCount = br.ReadInt32(),
                MaxCount = br.ReadInt32()
            };

            extractor.Timers.Add(timer);

            HLRDevLog(
                $"[HLR][Extractor][Load] Timer[{i}] " +
                $"res={timer.Resource} counter={timer.Counter}/{timer.Speed} " +
                $"min={timer.MinCount} max={timer.MaxCount}"
            );
        }

        // OwedResources
        int owedCount = br.ReadInt32();
        extractor.OwedResources = new Dictionary<string, int>(owedCount);

        HLRDevLog($"[HLR][Extractor][Load] OwedResources COUNT={owedCount}");

        for (int i = 0; i < owedCount; i++)
        {
            string key = br.ReadString();
            int value = br.ReadInt32();
            extractor.OwedResources[key] = value;

            HLRDevLog($"[HLR][Extractor][Load] Owed {key} x{value}");
        }

        extractor.SelectedOutputChestPos = Vector3i.zero;
        extractor.SelectedOutputPipeGraphId = Guid.Empty;

        if (snapshotVersion >= 2)
        {
            int outX = br.ReadInt32();
            int outY = br.ReadInt32();
            int outZ = br.ReadInt32();
            extractor.SelectedOutputChestPos = new Vector3i(outX, outY, outZ);

            string outputGraph = br.ReadString();
            if (!Guid.TryParse(outputGraph, out extractor.SelectedOutputPipeGraphId))
                extractor.SelectedOutputPipeGraphId = Guid.Empty;
        }

        HLRDevLog($"[HLR][Extractor][Load] PipeGraph={extractor.SelectedOutputPipeGraphId} OutputPos={extractor.SelectedOutputChestPos}");
        HLRDevLog($"[HLR][Extractor][Load] END");
    }

    private void LoadCrafterSnapshot(BinaryReader br, CrafterSnapshot crafter, int snapshotVersion)
    {
        HLRDevLog($"[HLR][Crafter][Load] BEGIN MachineId={crafter.MachineId}");

        // -------------
        // Core state
        // -------------
        crafter.IsCrafting = br.ReadBoolean();
        crafter.DisabledByPlayer = br.ReadBoolean();

        crafter.RecipeName = br.ReadString();
        crafter.BaseRecipeDuration = br.ReadSingle();
        crafter.CraftSpeed = br.ReadSingle();

        HLRDevLog(
            $"[HLR][Crafter][Load] IsCrafting={crafter.IsCrafting} " +
            $"Disabled={crafter.DisabledByPlayer} " +
            $"Recipe='{crafter.RecipeName}' " +
            $"Base={crafter.BaseRecipeDuration} Speed={crafter.CraftSpeed}"
        );

        // -------------
        // IngredientCount
        // -------------
        int ingredientCount = br.ReadInt32();
        crafter.IngredientCount = new Dictionary<string, int>(ingredientCount);

        HLRDevLog($"[HLR][Crafter][Load] IngredientCount COUNT={ingredientCount}");

        for (int i = 0; i < ingredientCount; i++)
        {
            string key = br.ReadString();
            int value = br.ReadInt32();
            crafter.IngredientCount[key] = value;

            HLRDevLog($"[HLR][Crafter][Load] Ingredient {key} x{value}");
        }
        // -------------
        // UsedIngredients removed from active simulation state; consume and discard legacy data.
        int usedCount = br.ReadInt32();

        HLRDevLog($"[HLR][Crafter][Load] UsedIngredients COUNT={usedCount} (discarded)");

        for (int i = 0; i < usedCount; i++)
        {
            br.ReadString();
            br.ReadInt32();
        }

        // -------------
        // OwedResources
        // -------------
        int owedCount = br.ReadInt32();
        crafter.OwedResources = new Dictionary<string, int>(owedCount);

        HLRDevLog($"[HLR][Crafter][Load] OwedResources COUNT={owedCount}");

        for (int i = 0; i < owedCount; i++)
        {
            string key = br.ReadString();
            int value = br.ReadInt32();
            crafter.OwedResources[key] = value;

            HLRDevLog($"[HLR][Crafter][Load] Owed {key} x{value}");
        }

        crafter.SelectedInputChestPos = Vector3i.zero;
        crafter.SelectedInputPipeGraphId = Guid.Empty;
        crafter.SelectedOutputChestPos = Vector3i.zero;
        crafter.SelectedOutputPipeGraphId = Guid.Empty;

        if (snapshotVersion >= 2)
        {
            int inX = br.ReadInt32();
            int inY = br.ReadInt32();
            int inZ = br.ReadInt32();
            crafter.SelectedInputChestPos = new Vector3i(inX, inY, inZ);

            string inputGraph = br.ReadString();
            if (!Guid.TryParse(inputGraph, out crafter.SelectedInputPipeGraphId))
                crafter.SelectedInputPipeGraphId = Guid.Empty;

            int outX = br.ReadInt32();
            int outY = br.ReadInt32();
            int outZ = br.ReadInt32();
            crafter.SelectedOutputChestPos = new Vector3i(outX, outY, outZ);

            string outputGraph = br.ReadString();
            if (!Guid.TryParse(outputGraph, out crafter.SelectedOutputPipeGraphId))
                crafter.SelectedOutputPipeGraphId = Guid.Empty;
        }

        HLRDevLog($"[HLR][Crafter][Load] InputGraph={crafter.SelectedInputPipeGraphId} InputPos={crafter.SelectedInputChestPos}");
        HLRDevLog($"[HLR][Crafter][Load] OutputGraph={crafter.SelectedOutputPipeGraphId} OutputPos={crafter.SelectedOutputChestPos}");
        HLRDevLog($"[HLR][Crafter][Load] END");
    }

}





















