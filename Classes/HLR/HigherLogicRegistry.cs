using System;
using System.Collections.Generic;
using System.IO;
using static TileEntityUniversalExtractor;

public class HigherLogicRegistry
{
    private readonly World world;
    private readonly Dictionary<Guid, IHLRSnapshot> snapshots;
    private ulong lastUpdateTime;
    private const ulong UPDATE_INTERVAL = 12; // About 2 seconds (world time ticks)
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
            var ingredientCounts = new Dictionary<string, int>();
            var usedIngredients = new Dictionary<string, int>();
            var owedResources = new Dictionary<string, int>();

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
                UsedIngredients = usedIngredients,
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

        HLRDevLog($"[HLR][Extractor] Simulate BEGIN @ {extractor.Position} ticks={hlrTicksToSimulate}");

        foreach (var timer in extractor.Timers)
        {
            HLRDevLog(
                $"[HLR][Extractor] Timer BEFORE — {timer.Resource} " +
                $"counter={timer.Counter}/{timer.Speed}"
            );

            timer.Counter += hlrTicksToSimulate;

            HLRDevLog(
                $"[HLR][Extractor] Timer ADVANCE — {timer.Resource} " +
                $"counter={timer.Counter}/{timer.Speed}"
            );

            if (timer.Speed <= 0)
            {
                HLRDevLog($"[HLR][Extractor] Timer SKIP — invalid speed for {timer.Resource}");
                continue;
            }

            int completedCycles = timer.Counter / timer.Speed;
            timer.Counter = timer.Counter % timer.Speed;

            HLRDevLog(
                $"[HLR][Extractor] Timer AFTER — {timer.Resource} " +
                $"counter={timer.Counter}/{timer.Speed} cycles={completedCycles}"
            );

            if (completedCycles <= 0)
            {
                HLRDevLog(
                    $"[HLR][Extractor] Timer WAIT — {timer.Resource} " +
                    $"({timer.Speed - timer.Counter} ticks remaining)"
                );
                continue;
            }

            int totalProduced = 0;

            for (int i = 0; i < completedCycles; i++)
            {
                int amount = timer.MinCount;
                if (timer.MinCount < timer.MaxCount)
                {
                    amount = UnityEngine.Random.Range(timer.MinCount, timer.MaxCount + 1);
                }

                totalProduced += amount;
            }

            if (!extractor.OwedResources.TryGetValue(timer.Resource, out int existing))
                extractor.OwedResources[timer.Resource] = totalProduced;
            else
                extractor.OwedResources[timer.Resource] = existing + totalProduced;

            HLRDevLog(
                $"[HLR][Extractor] PRODUCED — {totalProduced}x {timer.Resource} " +
                $"cycles={completedCycles} owed={extractor.OwedResources[timer.Resource]}"
            );
        }

        extractor.WorldTime = worldTime;

        HLRDevLog($"[HLR][Extractor] Simulate END @ {extractor.Position}");
    }

    private void SimulateCrafter(CrafterSnapshot crafter, ulong worldTime, int hlrTicksToSimulate)
    {
        HLRDevLog($"[HLR][Crafter] ========================================");
        HLRDevLog($"[HLR][Crafter] SIMULATE BEGIN @ {crafter.Position} ticks={hlrTicksToSimulate}");
        HLRDevLog($"[HLR][Crafter] Recipe='{crafter.RecipeName}' IsCrafting={crafter.IsCrafting} Disabled={crafter.DisabledByPlayer}");

        // ─────────────────────────
        // Gates
        // ─────────────────────────
        if (!crafter.IsCrafting)
        {
            HLRDevLog("[HLR][Crafter] ABORT — IsCrafting == false");
            return;
        }

        if (crafter.DisabledByPlayer)
        {
            HLRDevLog("[HLR][Crafter] ABORT — DisabledByPlayer == true");
            return;
        }

        if (string.IsNullOrEmpty(crafter.RecipeName))
        {
            HLRDevLog("[HLR][Crafter] ABORT — RecipeName is NULL/EMPTY");
            return;
        }

        Recipe recipe = CraftingManager.GetRecipe(crafter.RecipeName);
        if (recipe == null)
        {
            HLRDevLog($"[HLR][Crafter] ABORT — Recipe '{crafter.RecipeName}' not found");
            return;
        }

        HLRDevLog($"[HLR][Crafter] Recipe resolved: {recipe.GetName()}");

        // ─────────────────────────
        // Batch-aware elapsed-time math
        // ─────────────────────────
        float simulatedSeconds = 2f * hlrTicksToSimulate;

        if (crafter.CraftSpeed <= 0f)
        {
            HLRDevLog("[HLR][Crafter] ABORT — CraftSpeed <= 0");
            return;
        }

        float effectiveCraftTime = crafter.BaseRecipeDuration / crafter.CraftSpeed;
        if (effectiveCraftTime <= 0f)
        {
            HLRDevLog("[HLR][Crafter] ABORT — effectiveCraftTime <= 0");
            return;
        }

        crafter.CraftProgressSeconds += simulatedSeconds;

        HLRDevLog(
            $"[HLR][Crafter] Timing — Base={crafter.BaseRecipeDuration}s " +
            $"Speed={crafter.CraftSpeed} Effective={effectiveCraftTime:0.000}s " +
            $"AddedSeconds={simulatedSeconds:0.000}s ProgressNow={crafter.CraftProgressSeconds:0.000}s"
        );

        int craftsThisTick = (int)(crafter.CraftProgressSeconds / effectiveCraftTime);
        HLRDevLog(
            $"[HLR][Crafter] CraftsThisTick initial = floor({crafter.CraftProgressSeconds:0.000} / {effectiveCraftTime:0.000}) = {craftsThisTick}"
        );

        if (craftsThisTick <= 0)
        {
            HLRDevLog(
                $"[HLR][Crafter] WAIT — insufficient progress " +
                $"({crafter.CraftProgressSeconds:0.000}/{effectiveCraftTime:0.000}s)"
            );
            return;
        }

        // ─────────────────────────
        // Clamp by ingredients
        // ─────────────────────────
        foreach (var ingredient in recipe.ingredients)
        {
            if (ingredient.count <= 0)
                continue;

            string itemName = ingredient.itemValue.ItemClass.GetItemName();

            if (!crafter.IngredientCount.TryGetValue(itemName, out int available))
            {
                HLRDevLog($"[HLR][Crafter] STOP — Missing ingredient '{itemName}'");
                crafter.IsCrafting = false;
                return;
            }

            int maxForIngredient = available / ingredient.count;

            HLRDevLog(
                $"[HLR][Crafter] Ingredient '{itemName}' — " +
                $"available={available} perCraft={ingredient.count} " +
                $"maxCrafts={maxForIngredient}"
            );

            craftsThisTick = System.Math.Min(craftsThisTick, maxForIngredient);

            if (craftsThisTick <= 0)
            {
                HLRDevLog($"[HLR][Crafter] STOP — Ingredient '{itemName}' limits crafts to 0");
                crafter.IsCrafting = false;
                return;
            }
        }

        HLRDevLog($"[HLR][Crafter] CraftsThisTick FINAL = {craftsThisTick}");

        // Consume only the progress actually used after ingredient clamp
        crafter.CraftProgressSeconds -= craftsThisTick * effectiveCraftTime;

        HLRDevLog(
            $"[HLR][Crafter] Progress AFTER craft = {crafter.CraftProgressSeconds:0.000}s"
        );

        // ─────────────────────────
        // Consume ingredients (logical)
        // ─────────────────────────
        foreach (var ingredient in recipe.ingredients)
        {
            if (ingredient.count <= 0)
                continue;

            string itemName = ingredient.itemValue.ItemClass.GetItemName();
            int used = ingredient.count * craftsThisTick;

            int before = crafter.IngredientCount[itemName];
            crafter.IngredientCount[itemName] -= used;
            int after = crafter.IngredientCount[itemName];

            if (!crafter.UsedIngredients.ContainsKey(itemName))
                crafter.UsedIngredients[itemName] = used;
            else
                crafter.UsedIngredients[itemName] += used;

            HLRDevLog(
                $"[HLR][Crafter] CONSUME — {itemName}: " +
                $"used={used} before={before} after={after}"
            );
        }

        // ─────────────────────────
        // Produce output (owed only)
        // ─────────────────────────
        ItemClass outputClass = recipe.GetOutputItemClass();
        if (outputClass != null)
        {
            string outputName = outputClass.GetItemName();
            int produced = recipe.count * craftsThisTick;

            if (!crafter.OwedResources.ContainsKey(outputName))
                crafter.OwedResources[outputName] = produced;
            else
                crafter.OwedResources[outputName] += produced;

            HLRDevLog(
                $"[HLR][Crafter] PRODUCE — {outputName}: +" +
                $"{produced} (total owed={crafter.OwedResources[outputName]})"
            );
        }
        else
        {
            HLRDevLog("[HLR][Crafter] WARNING — Recipe output class is NULL");
        }

        HLRDevLog($"[HLR][Crafter] SIMULATE END @ {crafter.Position}");
        HLRDevLog($"[HLR][Crafter] ========================================");
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
            IngredientCount = new Dictionary<string, int>(),
            UsedIngredients = new Dictionary<string, int>(),
            OwedResources = new Dictionary<string, int>()
        };

        if (source.IngredientCount != null)
        {
            foreach (var kvp in source.IngredientCount)
            {
                clone.IngredientCount[kvp.Key] = kvp.Value;
            }
        }

        if (source.UsedIngredients != null)
        {
            foreach (var kvp in source.UsedIngredients)
            {
                clone.UsedIngredients[kvp.Key] = kvp.Value;
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

    // ─────────────────────────────────────────────
    // REGISTRATION
    // ─────────────────────────────────────────────
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

    // ─────────────────────────────────────────────
    // UNREGISTRATION
    // ─────────────────────────────────────────────
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
                if (version == 1)
                {
                    return new ExtractorSnapshotV1();
                }
                Log.Error($"[HLR][Factory] Unsupported Extractor version {version}");
                return null;

            case "Crafter":
                if (version == 1)
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

        // Used Ingredients (dictionary)

        int usedCount = crafter.UsedIngredients?.Count ?? 0;
        bw.Write(usedCount);

        if (crafter.UsedIngredients != null)
        {
            foreach (var kvp in crafter.UsedIngredients)
            {
                bw.Write(kvp.Key);
                bw.Write(kvp.Value);

                HLRDevLog($"[HLR][Crafter][Save] Used {kvp.Key} x{kvp.Value}");
            }
        }

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
                        LoadExtractorSnapshot(br, extractor);

                    if (snapshot is CrafterSnapshot crafter)
                        LoadCrafterSnapshot(br, crafter);

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

    private void LoadExtractorSnapshot(BinaryReader br, ExtractorSnapshotV1 extractor)
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

        HLRDevLog($"[HLR][Extractor][Load] END");
    }

    private void LoadCrafterSnapshot(BinaryReader br, CrafterSnapshot crafter)
    {
        HLRDevLog($"[HLR][Crafter][Load] BEGIN MachineId={crafter.MachineId}");

        // ─────────────
        // Core state
        // ─────────────
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

        // ─────────────
        // IngredientCount
        // ─────────────
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

        // ─────────────
        // UsedIngredients
        // ─────────────
        int usedCount = br.ReadInt32();
        crafter.UsedIngredients = new Dictionary<string, int>(usedCount);

        HLRDevLog($"[HLR][Crafter][Load] UsedIngredients COUNT={usedCount}");

        for (int i = 0; i < usedCount; i++)
        {
            string key = br.ReadString();
            int value = br.ReadInt32();
            crafter.UsedIngredients[key] = value;

            HLRDevLog($"[HLR][Crafter][Load] Used {key} x{value}");
        }

        // ─────────────
        // OwedResources
        // ─────────────
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

        HLRDevLog($"[HLR][Crafter][Load] END");
    }

}
