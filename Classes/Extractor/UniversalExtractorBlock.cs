using System.Collections.Generic;

public class UniversalExtractorBlock
    : MachineBlock<TileEntityUniversalExtractor>
{
    // ─────────────────────────────────────────────
    // CONSTRUCTOR
    // ─────────────────────────────────────────────
    public UniversalExtractorBlock()
    {
        // HasTileEntity enforced by MachineBlock
    }

    // ─────────────────────────────────────────────
    // TILE ENTITY CREATION
    // ─────────────────────────────────────────────
    protected override TileEntityUniversalExtractor CreateTileEntity(Chunk chunk)
    {
        Log.Out("[Extractor][BLOCK] CreateTileEntity()");
        return new TileEntityUniversalExtractor(chunk);
    }

    // ─────────────────────────────────────────────
    // INIT
    // ─────────────────────────────────────────────
    public override void Init()
    {
        base.Init();
        Log.Out("[Extractor][BLOCK] Init()");
    }

    // ─────────────────────────────────────────────
    // PLACEMENT VALIDATION
    // ─────────────────────────────────────────────
    public override bool CanPlaceBlockAt(
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue,
        bool bOmitCollideCheck = false)
    {
        string source = Properties.GetString("BlockSource");
        if (string.IsNullOrEmpty(source))
            return base.CanPlaceBlockAt(world, clrIdx, blockPos, blockValue, bOmitCollideCheck);

        Vector3i below = blockPos + Vector3i.down;
        BlockValue bvBelow = world.GetBlock(clrIdx, below);

        bool valid = bvBelow.Block.GetBlockName() == source;

        return valid && base.CanPlaceBlockAt(world, clrIdx, blockPos, blockValue, bOmitCollideCheck);
    }

    // ─────────────────────────────────────────────
    // BLOCK LOADED (HLR → TE HANDOFF)
    // ─────────────────────────────────────────────
    public override void OnBlockLoaded(
    WorldBase world,
    int clrIdx,
    Vector3i blockPos,
    BlockValue blockValue)
    {
        base.OnBlockLoaded(world, clrIdx, blockPos, blockValue);

        var te = world.GetTileEntity(clrIdx, blockPos) as TileEntityUniversalExtractor;
        if (te == null)
        {
            Log.Warning($"[Extractor][BLOCK][{blockPos}] LOAD — NO TILE ENTITY");
            return;
        }

        if (te.IsDevLogging)
        {
            Log.Warning($"[Extractor][BLOCK][{blockPos}] LOAD — BEGIN");
        }

        var hlr = WorldHLR.GetOrCreate((World)world);

        if (te.IsDevLogging)
        {
            Log.Warning($"[Extractor][BLOCK][{blockPos}] TRY CLAIM — TE MachineGuid={te.MachineGuid}");
        }

        if (hlr.TryUnregisterMachine(te.MachineGuid, out var snapshot))
        {
            if (te.IsDevLogging)
            {
                Log.Warning($"[Extractor][BLOCK][{blockPos}] LOAD — Snapshot FOUND, applying");
            }

            te.ApplyHLRSnapshot(snapshot);
        }
        else
        {
            if (te.IsDevLogging)
            {
                Log.Warning($"[Extractor][BLOCK][{blockPos}] LOAD — No snapshot (live machine)");
            }
        }

        te.SetSimulatedByHLR(false);

        if (te.IsDevLogging)
        {
            Log.Warning($"[Extractor][BLOCK][{blockPos}] LOAD — COMPLETE (HLR → TE)");
        }
    }


    // ─────────────────────────────────────────────
    // BLOCK UNLOADED (TE → HLR HANDOFF)
    // ─────────────────────────────────────────────
    public override void OnBlockUnloaded(
    WorldBase world,
    int clrIdx,
    Vector3i blockPos,
    BlockValue blockValue)
    {
        base.OnBlockUnloaded(world, clrIdx, blockPos, blockValue);

        var te = world.GetTileEntity(clrIdx, blockPos) as TileEntityUniversalExtractor;
        if (te == null)
        {
            Log.Warning($"[Extractor][BLOCK][{blockPos}] UNLOAD — NO TILE ENTITY");
            return;
        }

        if (te.IsDevLogging)
        {
            Log.Warning($"[Extractor][BLOCK][{blockPos}] UNLOAD — BEGIN");
            Log.Out($"[Extractor][BLOCK][{blockPos}] UNLOAD — Building snapshot");
        }

        var snapshot = te.BuildHLRSnapshot(world);
        if (snapshot == null)
        {
            Log.Error($"[Extractor][BLOCK][{blockPos}] UNLOAD — Snapshot FAILED");
            return;
        }

        var hlr = WorldHLR.GetOrCreate((World)world);
        hlr.RegisterMachine(te.MachineGuid, snapshot);

        te.SetSimulatedByHLR(true);

        if (te.IsDevLogging)
        {
            Log.Warning($"[Extractor][BLOCK][{blockPos}] UNLOAD — COMPLETE (TE → HLR)");
        }
    }


    // ─────────────────────────────────────────────
    // ACTIVATION / UI
    // ─────────────────────────────────────────────
    public override bool HasBlockActivationCommands(
        WorldBase world,
        BlockValue blockValue,
        int clrIdx,
        Vector3i blockPos,
        EntityAlive entityFocusing)
    {
        return true;
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(
        WorldBase world,
        BlockValue blockValue,
        int clrIdx,
        Vector3i blockPos,
        EntityAlive entityFocusing)
    {
        return cmds;
    }

    public override bool OnBlockActivated(
        string commandName,
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue,
        EntityPlayerLocal player)
    {
        Log.Out($"First pass Block Position = {blockPos}");
        return OnBlockActivated(world, clrIdx, blockPos, blockValue, player);
    }
    public override bool OnBlockActivated(
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue,
        EntityPlayerLocal player)
    {
        var te = world.GetTileEntity(clrIdx, blockPos) as TileEntityUniversalExtractor;
        if (te != null)
        {
            List<OutputTargetInfo> outputs = te.GetAvailableOutputTargets(world);
            Log.Out($"[Extractor][TE][{te.ToWorldPos()}] OutputTargetCount={outputs.Count}");

            for (int i = 0; i < outputs.Count; i++)
                Log.Out($"[Extractor][TE][{te.ToWorldPos()}] OutputTarget[{i}] {outputs[i]}");
        }

        Helper.RequestMachineUIOpen(clrIdx, blockPos, player.entityId, "ExtractorInfo");
        return true;
    }



    private readonly BlockActivationCommand[] cmds =
    {
        new BlockActivationCommand("open", "campfire", true, false, null)
    };

    public override string GetActivationText(
        WorldBase world,
        BlockValue blockValue,
        int clrIdx,
        Vector3i blockPos,
        EntityAlive entityFocusing)
    {
        if (!(entityFocusing is EntityPlayerLocal player))
            return "[E] Open Universal Extractor";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} Open {name}";
    }
}
