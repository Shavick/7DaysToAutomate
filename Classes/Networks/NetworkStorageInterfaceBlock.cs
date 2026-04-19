using System;

public class NetworkStorageInterfaceBlock : MachineBlock<TileEntityNetworkStorageInterface>
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

    public NetworkStorageInterfaceBlock()
    {
        HasTileEntity = true;
    }

    protected override TileEntityNetworkStorageInterface CreateTileEntity(Chunk chunk)
    {
        return new TileEntityNetworkStorageInterface(chunk);
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
        var te = world.GetTileEntity(clrIdx, blockPos) as TileEntityNetworkStorageInterface;
        if (te == null)
        {
            Log.Warning($"[NetworkStorageInterface][BLOCK][{blockPos}] Activated but TE was null");
            return true;
        }

        if (player != null)
            Helper.RequestMachineUIOpen(clrIdx, blockPos, player.entityId, "NetworkStorageInterface");

        if (world == null || world.IsRemote())
            return true;

        te.ResolveNetworkId(world, true);

        if (te.NetworkId == Guid.Empty)
        {
            Log.Warning($"[NetworkStorageInterface][BLOCK][{blockPos}] No connected network detected");
            return true;
        }

        if (!te.RefreshSnapshotSummary(world))
        {
            Log.Warning($"[NetworkStorageInterface][BLOCK][{blockPos}] Failed snapshot build NetworkId={te.NetworkId}");
            return true;
        }

        Log.Out($"[NetworkStorageInterface][BLOCK][{blockPos}] Snapshot NetworkId={te.NetworkId} Revision={te.LastSnapshotRevision} Types={te.LastSnapshotStackTypes} TotalItems={te.LastSnapshotTotalItems}");

        if (!string.IsNullOrEmpty(te.LastSnapshotTopSummary))
            Log.Out($"[NetworkStorageInterface][BLOCK][{blockPos}] TopStacks={te.LastSnapshotTopSummary}");

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

        MarkAdjacentPipesDirty(_world, 0, _blockPos);
    }

    public override void OnBlockRemoved(
        WorldBase _world,
        Chunk _chunk,
        Vector3i _blockPos,
        BlockValue _blockValue)
    {
        if (!_world.IsRemote())
            MarkAdjacentPipesDirty(_world, 0, _blockPos);

        base.OnBlockRemoved(_world, _chunk, _blockPos, _blockValue);
    }

    public override string GetActivationText(
        WorldBase world,
        BlockValue blockValue,
        int clrIdx,
        Vector3i blockPos,
        EntityAlive entityFocusing)
    {
        if (!(entityFocusing is EntityPlayerLocal player))
            return "[E] View Storage Interface";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        return $"{key} View Storage Interface";
    }

    private static void MarkAdjacentPipesDirty(WorldBase world, int clrIdx, Vector3i centerPos)
    {
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i neighborPos = centerPos + NeighborOffsets[i];

            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out TileEntity te) || !(te is TileEntityItemPipe pipeTe))
                continue;

            pipeTe.MarkNetworkDirty();
            pipeTe.setModified();
            PipeGraphManager.MarkPipeDirty(neighborPos);
        }
    }
}
