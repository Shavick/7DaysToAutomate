
public class UniversalWasherBlock : MachineBlock<TileEntityUniversalWasher>
{
    private void DevLog(string msg, string type = "Log")
    {
        switch (type)
        {
            case "Warning":
                Log.Warning($"[Washer][Block] " + msg);
                break;

            case "Error":
                Log.Error($"[Washer][Block] " + msg);
                break;

            default:
                Log.Out($"[Washer][Block] " + msg);
                break;
        }
    }

    protected override TileEntityUniversalWasher CreateTileEntity(Chunk chunk)
    {
        DevLog("CreateTileEntity()");
        return new TileEntityUniversalWasher(chunk);
    }

    public override void OnBlockLoaded(
    WorldBase world,
    int clrIdx,
    Vector3i blockPos,
    BlockValue blockValue)
    {
        base.OnBlockLoaded(world, clrIdx, blockPos, blockValue);

        if (blockValue.Block.Properties.GetBool("DevLogs"))
            Log.Out($"[WasherBlock] Block loaded at {blockPos}");
    }

    public override void OnBlockUnloaded(
    WorldBase world,
    int clrIdx,
    Vector3i blockPos,
    BlockValue blockValue)
    {
        base.OnBlockUnloaded(world, clrIdx, blockPos, blockValue);

        if (blockValue.Block.Properties.GetBool("DevLogs"))
            Log.Out($"[WasherBlock] Block unloaded at {blockPos}");
    }

    public override bool HasBlockActivationCommands(WorldBase _world, BlockValue _blockValue, int _clrIdx, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return true;
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase _world, BlockValue _blockValue, int _clrIdx, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return cmds;
    }

    private readonly BlockActivationCommand[] cmds =
    {
        new BlockActivationCommand("open", "campfire", true)
    };

    public override bool OnBlockActivated(string commandName, WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue, EntityPlayerLocal player)
    {
        return OnBlockActivated(world, clrIdx, blockPos, blockValue, player);
    }

    public override bool OnBlockActivated(WorldBase _world, int _clrIdx, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        if (_world.IsRemote())
        {
            var te = _world.GetTileEntity(_blockPos) as TileEntityUniversalWasher;
            if (te == null)
                return false;

            if (te.IsDevLogging)
                Log.Out($"[Washer][Block] Activated by {_player.EntityName}");
        }

        // Server Logic Here Later

        return true;
    }

    public override string GetActivationText(
        WorldBase world,
        BlockValue blockValue,
        int clrIdx,
        Vector3i blockPos,
        EntityAlive entityAlive)
    {
        if (!(entityAlive is EntityPlayerLocal player))
            return "[E] Open Universal Washer";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} open {name}";
    }
}

