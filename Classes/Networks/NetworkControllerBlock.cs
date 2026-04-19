using System.Text;

public class NetworkControllerBlock
    : MachineBlock<TileEntityNetworkController>
{
    // ---------------------------------------------
    // CONSTRUCTOR
    // ---------------------------------------------
    public NetworkControllerBlock()
    {
        HasTileEntity = true;
    }

    // ---------------------------------------------
    // TILE ENTITY CREATION
    // ---------------------------------------------
    protected override TileEntityNetworkController CreateTileEntity(Chunk chunk)
    {
        Log.Out("[NetworkController][BLOCK] CreateTileEntity()");
        return new TileEntityNetworkController(chunk);
    }

    // ---------------------------------------------
    // INIT
    // ---------------------------------------------
    public override void Init()
    {
        base.Init();
        Log.Out("[NetworkController][BLOCK] Init()");
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

    private static bool IsPipeConnectedToControllerSide(
        WorldBase world,
        int clrIdx,
        Vector3i controllerPos,
        Vector3i pipePos)
    {
        var pipeTe = world.GetTileEntity(clrIdx, pipePos) as TileEntityItemPipe;
        if (pipeTe == null)
            return false;

        BlockValue pipeValue = world.GetBlock(clrIdx, pipePos);
        ItemPipeBlock.PipeAxis pipeAxis = ItemPipeBlock.GetStraightPipeAxis(pipeValue);

        if (pipeAxis == ItemPipeBlock.PipeAxis.None)
            return false;

        Vector3i delta = pipePos - controllerPos;

        if (delta == Vector3i.left || delta == Vector3i.right)
            return pipeAxis == ItemPipeBlock.PipeAxis.AxisX;

        if (delta == Vector3i.forward || delta == Vector3i.back)
            return pipeAxis == ItemPipeBlock.PipeAxis.AxisZ;

        if (delta == Vector3i.up || delta == Vector3i.down)
            return pipeAxis == ItemPipeBlock.PipeAxis.AxisY;

        return false;
    }

    private static void MarkConnectedAdjacentPipesDirty(
        WorldBase world,
        int clrIdx,
        Vector3i controllerPos)
    {
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i pipePos = controllerPos + NeighborOffsets[i];

            if (!IsPipeConnectedToControllerSide(world, clrIdx, controllerPos, pipePos))
                continue;

            var pipeTe = world.GetTileEntity(clrIdx, pipePos) as TileEntityItemPipe;
            if (pipeTe == null)
                continue;

            pipeTe.MarkNetworkDirty();
            pipeTe.setModified();

            Log.Out($"[NetworkController][BLOCK][{controllerPos}] Marked connected pipe dirty at {pipePos}");
        }
    }

    // ---------------------------------------------
    // ACTIVATION / UI
    // ---------------------------------------------
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
        return OnBlockActivated(world, clrIdx, blockPos, blockValue, player);
    }

    public override bool OnBlockActivated(
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue,
        EntityPlayerLocal player)
    {
        var te = world.GetTileEntity(clrIdx, blockPos) as TileEntityNetworkController;

        if (te == null)
        {
            Log.Warning($"[NetworkController][BLOCK][{blockPos}] Activated but TE was null");
            return true;
        }

        Log.Out($"[NetworkController][BLOCK][{blockPos}] Activated NetworkId={te.NetworkId}");

        if (world == null || world.IsRemote())
            return true;

        if (!NetworkStorageService.TryBuildStorageSnapshot(world, te.NetworkId, out NetworkStorageSnapshot snapshot) || snapshot == null)
        {
            Log.Warning($"[NetworkController][BLOCK][{blockPos}] Failed to build storage snapshot for network {te.NetworkId}");
            return true;
        }

        int stackTypes = snapshot.DisplayStacks.Count;
        int totalItems = 0;
        for (int i = 0; i < snapshot.DisplayStacks.Count; i++)
            totalItems += snapshot.DisplayStacks[i].TotalCount;

        Log.Out($"[NetworkController][BLOCK][{blockPos}] Snapshot built NetworkId={te.NetworkId} Revision={snapshot.Revision} Types={stackTypes} TotalItems={totalItems}");

        int topCount = System.Math.Min(6, snapshot.DisplayStacks.Count);
        if (topCount > 0)
        {
            var sb = new StringBuilder();
            sb.Append("[NetworkController][BLOCK][");
            sb.Append(blockPos);
            sb.Append("] TopStacks=");

            for (int i = 0; i < topCount; i++)
            {
                NetworkDisplayStack display = snapshot.DisplayStacks[i];
                string itemName = NetworkItemIdentity.GetStableDisplayName(display.DisplayStack);
                if (i > 0)
                    sb.Append(" | ");

                sb.Append(itemName);
                sb.Append(":");
                sb.Append(display.TotalCount);
            }

            Log.Out(sb.ToString());
        }

        return true;
    }

    public override void OnBlockAdded(
        WorldBase _world,
        Chunk _chunk,
        Vector3i _blockPos,
        BlockValue _blockValue,
        PlatformUserIdentifierAbs _addedByPlayer)
    {
        base.OnBlockAdded(_world, _chunk, _blockPos, _blockValue, _addedByPlayer);

        if (_world.IsRemote() || _blockValue.ischild)
            return;

        MarkConnectedAdjacentPipesDirty(_world, 0, _blockPos);
    }

    public override void OnBlockRemoved(
        WorldBase _world,
        Chunk _chunk,
        Vector3i _blockPos,
        BlockValue _blockValue)
    {
        if (!_world.IsRemote())
            MarkConnectedAdjacentPipesDirty(_world, 0, _blockPos);

        base.OnBlockRemoved(_world, _chunk, _blockPos, _blockValue);
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
            return "[E] Open Network Controller";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} Open {name}";
    }
}
