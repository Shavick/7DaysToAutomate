using System;

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
        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, blockPos, out TileEntity te) || !(te is TileEntityFluidStorage storage))
            return "Unassigned - 0/0 Gallons";

        string fluidType = FormatFluidName(storage.FluidType);
        int amountGallons = ToWholeGallons(storage.FluidAmountMg);
        int capacityGallons = ToWholeGallons(storage.GetCapacityMg());

        return $"{fluidType} - {amountGallons}/{capacityGallons} Gallons";
    }

    private static string FormatFluidName(string fluidType)
    {
        if (string.IsNullOrEmpty(fluidType))
            return "Unassigned";

        if (fluidType.Length == 1)
            return fluidType.ToUpperInvariant();

        return char.ToUpperInvariant(fluidType[0]) + fluidType.Substring(1);
    }

    private static int ToWholeGallons(int mg)
    {
        return (mg + (FluidConstants.MilliGallonsPerGallon / 2)) / FluidConstants.MilliGallonsPerGallon;
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
