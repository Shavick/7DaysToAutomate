public class FluidStorageBlock : MachineBlock<TileEntityFluidStorage>
{
    private static readonly Vector3i[] NeighborOffsets =
    {
        Vector3i.forward,
        Vector3i.back,
        Vector3i.left,
        Vector3i.right,
        Vector3i.up,
        Vector3i.down
    };

    private readonly BlockActivationCommand[] cmds =
    {
        new BlockActivationCommand("open", "campfire", true, false, null)
    };

    protected override TileEntityFluidStorage CreateTileEntity(Chunk chunk)
    {
        return new TileEntityFluidStorage(chunk);
    }

    public override void OnBlockAdded(
        WorldBase world,
        Chunk chunk,
        Vector3i blockPos,
        BlockValue blockValue,
        PlatformUserIdentifierAbs addedByPlayer)
    {
        base.OnBlockAdded(world, chunk, blockPos, blockValue, addedByPlayer);
        if (world.IsRemote() || blockValue.ischild)
            return;

        MarkAdjacentPipesDirty(world, 0, blockPos);
    }

    public override void OnBlockRemoved(
        WorldBase world,
        Chunk chunk,
        Vector3i blockPos,
        BlockValue blockValue)
    {
        if (!world.IsRemote())
            MarkAdjacentPipesDirty(world, 0, blockPos);

        base.OnBlockRemoved(world, chunk, blockPos, blockValue);
    }

    public override bool HasBlockActivationCommands(WorldBase world, BlockValue blockValue, int clrIdx, Vector3i blockPos, EntityAlive entityFocusing)
    {
        return true;
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase world, BlockValue blockValue, int clrIdx, Vector3i blockPos, EntityAlive entityFocusing)
    {
        return cmds;
    }

    public override bool OnBlockActivated(string commandName, WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue, EntityPlayerLocal player)
    {
        return OnBlockActivated(world, clrIdx, blockPos, blockValue, player);
    }

    public override bool OnBlockActivated(WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue, EntityPlayerLocal player)
    {
        return false;
    }

    public override string GetActivationText(
        WorldBase world,
        BlockValue blockValue,
        int clrIdx,
        Vector3i blockPos,
        EntityAlive entityFocusing)
    {
        if (!(entityFocusing is EntityPlayerLocal player))
            return "[E] Probe Fluid Storage";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} Probe {name}";
    }

    private static void MarkAdjacentPipesDirty(WorldBase world, int clrIdx, Vector3i centerPos)
    {
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i neighborPos = centerPos + NeighborOffsets[i];
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out TileEntity te) || !(te is TileEntityLiquidPipe pipe))
                continue;

            pipe.MarkFluidGraphDirty();
            pipe.setModified();
            FluidGraphManager.MarkPipeDirty(neighborPos);
        }
    }
}




