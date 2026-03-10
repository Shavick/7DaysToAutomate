public class UniversalCrafterBlock : MachineBlock<TileEntityUniversalCrafter>
{
    public UniversalCrafterBlock()
    {
        // HasTileEntity enforced by MachineBlock
    }

    private bool IsDevLogging => Properties.GetBool("DevLogs");

    protected override TileEntityUniversalCrafter CreateTileEntity(Chunk chunk)
    {
        return new TileEntityUniversalCrafter(chunk);
    }

    public override void Init()
    {
        base.Init();

        if (IsDevLogging)
            Log.Out("[UniversalCrafterBlock] Initialized with tile entity support.");
    }

    public override bool CanPlaceBlockAt(
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue,
        bool bOmitCollideCheck = false)
    {
        if (IsDevLogging)
            Log.Out($"[UniversalCrafterBlock] Checking placement at {World.toBlock(blockPos)}");

        string source = Properties.GetString("BlockSource");
        if (string.IsNullOrEmpty(source))
            return base.CanPlaceBlockAt(world, clrIdx, blockPos, blockValue, bOmitCollideCheck);

        Vector3i below = blockPos + Vector3i.down;
        BlockValue bvBelow = world.GetBlock(clrIdx, below);
        if (bvBelow.Block.GetBlockName() != source)
        {
            if (IsDevLogging)
                Log.Out($"[UniversalCrafterBlock] Placement blocked: Block below {bvBelow.Block.GetBlockName()} does not match source.");

            return false;
        }

        return base.CanPlaceBlockAt(world, clrIdx, blockPos, blockValue, bOmitCollideCheck);
    }

    public override void OnBlockLoaded(
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue)
    {
        base.OnBlockLoaded(world, clrIdx, blockPos, blockValue);

        var te = world.GetTileEntity(clrIdx, blockPos) as TileEntityUniversalCrafter;
        if (te == null)
        {
            Log.Warning($"[Crafter][BLOCK][{blockPos}] LOAD — NO TILE ENTITY");
            return;
        }

        if (te.IsDevLogging)
            Log.Out($"[Crafter][BLOCK][{blockPos}] LOAD — BEGIN");

        // Clients should not do HLR authority/load-resume work
        if (world.IsRemote())
        {
            te.ResolveRecipeIfNeeded();
            te.ResolveSelectedInputContainer();
            te.ResolveSelectedOutputContainer();

            if (te.IsDevLogging)
                Log.Out($"[Crafter][BLOCK][{blockPos}] LOAD — CLIENT SIDE COMPLETE");

            return;
        }

        // Pull snapshot from HLR
        var hlr = WorldHLR.GetOrCreate((World)world);
        if (hlr != null && hlr.TryUnregisterMachine(te.MachineGuid, out var snapshot))
        {
            if (te.IsDevLogging)
                Log.Out($"[Crafter][BLOCK][{blockPos}] LOAD — Snapshot FOUND, applying");

            te.ApplyHLRSnapshot(snapshot);
        }
        else
        {
            if (te.IsDevLogging)
                Log.Out($"[Crafter][BLOCK][{blockPos}] LOAD — No snapshot (live machine)");
        }

        // Resolve runtime refs after load/apply
        te.ResolveRecipeIfNeeded();
        te.ResolveSelectedInputContainer();
        te.ResolveSelectedOutputContainer();

        // Cache CraftSpeed if needed
        if (te.CraftSpeed <= 0f)
        {
            te.CraftSpeed = te.GetCraftingSpeed();

            if (te.IsDevLogging)
                Log.Out($"[Crafter][BLOCK][{blockPos}] LOAD — CraftSpeed cached: {te.CraftSpeed}");
        }

        // Resume crafting if allowed
        if (!te.disabledByPlayer && !string.IsNullOrEmpty(te.SelectedRecipeName))
        {
            if (te.IsDevLogging)
                Log.Out($"[Crafter][BLOCK][{blockPos}] LOAD — Resuming craft");

            te.StartCraft();
        }

        if (te.IsDevLogging)
            Log.Out($"[Crafter][BLOCK][{blockPos}] LOAD — COMPLETE (HLR → TE)");
    }

    public override void OnBlockUnloaded(
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue)
    {
        base.OnBlockUnloaded(world, clrIdx, blockPos, blockValue);

        // Clients should never hand authority to HLR
        if (world.IsRemote())
            return;

        var te = world.GetTileEntity(clrIdx, blockPos) as TileEntityUniversalCrafter;
        if (te == null)
        {
            Log.Warning($"[Crafter][BLOCK][{blockPos}] UNLOAD — NO TILE ENTITY");
            return;
        }

        if (te.IsDevLogging)
            Log.Warning($"[Crafter][BLOCK][{blockPos}] UNLOAD — BEGIN");

        if (string.IsNullOrEmpty(te.SelectedRecipeName))
        {
            if (te.IsDevLogging)
                Log.Out($"[Crafter][BLOCK][{blockPos}] UNLOAD — SKIP (no recipe selected)");

            return;
        }

        if (te.IsDevLogging)
            Log.Out($"[Crafter][BLOCK][{blockPos}] UNLOAD — Building snapshot");

        var snapshot = te.BuildHLRSnapshot(world);
        if (snapshot == null)
        {
            Log.Warning($"[Crafter][BLOCK][{blockPos}] UNLOAD — SNAPSHOT NULL (skip)");
            return;
        }

        var hlr = WorldHLR.GetOrCreate((World)world);
        if (hlr == null)
        {
            Log.Error($"[Crafter][BLOCK][{blockPos}] UNLOAD — HLR NULL (cannot register)");
            return;
        }

        hlr.RegisterMachine(te.MachineGuid, snapshot);
        te.SetSimulatedByHLR(true);

        if (te.IsDevLogging)
            Log.Warning($"[Crafter][BLOCK][{blockPos}] UNLOAD — COMPLETE (TE → HLR)");
    }

    public override bool OnBlockActivated(
        string commandName,
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue,
        EntityPlayerLocal player)
    {
        return OnBlockActivated(world, clrIdx, blockPos, blockValue, player);
    }

    public override bool OnBlockActivated(
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue,
        EntityPlayerLocal player)
    {
        if (player == null)
            return false;

        if (IsDevLogging)
            Log.Out($"[Crafter][BLOCK][{blockPos}] ACTIVATE — world.IsRemote={world.IsRemote()} player={player.entityId}");

        Helper.RequestMachineUIOpen(clrIdx, blockPos, player.entityId, "CrafterInfo");
        return true;
    }

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
        return crafterCommands;
    }

    private readonly BlockActivationCommand[] crafterCommands =
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
            return "[E] Open Universal Crafter";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} Open {name}";
    }
}