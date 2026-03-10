public abstract class MachineBlock<TTileEntity> : Block
    where TTileEntity : TileEntity
{
    protected MachineBlock()
    {
        HasTileEntity = true;
    }

    private void Devlogs(string message, string priority = "Log")
    {
        switch (priority)
        {
            case "Warning":
                Log.Warning(message);
                break;

            case "Error":
                Log.Error(message);
                break;

            default:
                Log.Out(message);
                break;
        }
    }

    protected abstract TTileEntity CreateTileEntity(Chunk chunk);

    public override void OnBlockAdded(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue, PlatformUserIdentifierAbs _addedByPlayer)
    {
        base.OnBlockAdded(_world, _chunk, _blockPos, _blockValue, _addedByPlayer);

        if (_blockValue.ischild)
            return;
        Devlogs($"OnBlockAdded: IsRemote={_world.IsRemote()} ischild={_blockValue.ischild}");

        var te = CreateTileEntity(_chunk);
        if (te == null)
            return;

        te.localChunkPos = World.toBlock(_blockPos);
        _chunk.AddTileEntity(te);

        var verify = _world.GetTileEntity(0, _blockPos);
        Devlogs($"VERIFY GetTileEntity after AddTileEntity -> {(verify == null ? "NULL" : verify.GetType().Name)}");

        if (te != null)
        {
            Devlogs("Tile Entity added successfully to chunk");
        }
        else
        {
            Devlogs("Error adding tile entity to chunk!", "Error");
        }

    }

    public override void OnBlockRemoved(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue)
    {
        base.OnBlockRemoved(_world, _chunk, _blockPos, _blockValue);

        _chunk.RemoveTileEntity((World)_world, _world.GetTileEntity(_blockPos));
    }
}