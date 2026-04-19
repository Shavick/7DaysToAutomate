public class UniversalGrinderBlock : MachineBlock<TileEntityGrinder>
{
    protected override TileEntityGrinder CreateTileEntity(Chunk chunk)
    {
        return new TileEntityGrinder(chunk);
    }

    public override void OnBlockLoaded(WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue)
    {
        base.OnBlockLoaded(world, clrIdx, blockPos, blockValue);

        if (world.IsRemote()) return;

        TileEntityGrinder te = world.GetTileEntity(clrIdx, blockPos) as TileEntityGrinder;
        if (te == null) return;

        HigherLogicRegistry hlr = WorldHLR.GetOrCreate((World)world);
        if (hlr != null && hlr.TryUnregisterMachine(te.MachineGuid, out IHLRSnapshot snapshot)) te.ApplyHLRSnapshot(snapshot);
        te.SetSimulatedByHLR(false);
    }

    public override void OnBlockUnloaded(WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue)
    {
        base.OnBlockUnloaded(world, clrIdx, blockPos, blockValue);

        if (world.IsRemote()) return;

        TileEntityGrinder te = world.GetTileEntity(clrIdx, blockPos) as TileEntityGrinder;
        if (te == null) return;

        IHLRSnapshot snapshot = te.BuildHLRSnapshot(world);
        if (snapshot == null) return;

        HigherLogicRegistry hlr = WorldHLR.GetOrCreate((World)world);
        if (hlr != null) hlr.RegisterMachine(te.MachineGuid, te.BuildHLRSnapshot(world));
        te.SetSimulatedByHLR(true);
    }

    public override bool HasBlockActivationCommands(WorldBase world, BlockValue blockValue, int clrIdx, Vector3i blockPos, EntityAlive entityFocusing)
    {
        return true;
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase world, BlockValue blockValue, int clrIdx, Vector3i blockPos, EntityAlive entityFocusing)
    {
        return cmds;
    }

    public override bool OnBlockActivated(string _commandName, WorldBase _world, int _cIdx, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        return base.OnBlockActivated(_commandName, _world, _cIdx, _blockPos, _blockValue, _player);
    }

    public override bool OnBlockActivated(WorldBase _world, int _clrIdx, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        if (_player == null) return false;
        Helper.RequestMachineUIOpen(_clrIdx, _blockPos, _player.entityId, "GrinderInfo");
        return true;
    }

    public override string GetActivationText(WorldBase _world, BlockValue _blockValue, int _clrIdx, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        if (!(_entityFocusing is EntityPlayerLocal player)) return "[E] Open Grinder";

        string key = player.playerInput.Activate.GetBindingXuiMarkupString();
        player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = _blockValue.Block.GetLocalizedBlockName();
        return $"[{key}] Open {name}";
    }

    private readonly BlockActivationCommand[] cmds =
    {
        new BlockActivationCommand("open", "campfire", true, false, null)
    };
}