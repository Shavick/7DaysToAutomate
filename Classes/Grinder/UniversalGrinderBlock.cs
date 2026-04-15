public class UniversalGrinderBlock : MachineBlock<TileEntityUniversalGrinder>
{
    protected override TileEntityUniversalGrinder CreateTileEntity(Chunk chunk)
    {
        return new TileEntityUniversalGrinder(chunk);
    }

    public override void OnBlockLoaded(WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue)
    {
        base.OnBlockLoaded(world, clrIdx, blockPos, blockValue);

        if (world.IsRemote())
            return;

        TileEntityUniversalGrinder te = world.GetTileEntity(clrIdx, blockPos) as TileEntityUniversalGrinder;
        if (te == null)
            return;

        HigherLogicRegistry hlr = WorldHLR.GetOrCreate((World)world);
        if (hlr != null && hlr.TryUnregisterMachine(te.MachineGuid, out IHLRSnapshot snapshot))
            te.ApplyHLRSnapshot(snapshot);

        te.RefreshAvailableInputTargets(world);
        te.RefreshAvailableOutputTargets(world);
        te.ResolveSelectedInputContainer();
        te.ResolveSelectedOutputContainer();
        te.ResolveFuelGraph(world);
        te.SetSimulatedByHLR(false);
    }

    public override void OnBlockUnloaded(WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue)
    {
        base.OnBlockUnloaded(world, clrIdx, blockPos, blockValue);

        if (world.IsRemote())
            return;

        TileEntityUniversalGrinder te = world.GetTileEntity(clrIdx, blockPos) as TileEntityUniversalGrinder;
        if (te == null)
            return;

        IHLRSnapshot snapshot = te.BuildHLRSnapshot(world);
        if (snapshot == null)
            return;

        HigherLogicRegistry hlr = WorldHLR.GetOrCreate((World)world);
        if (hlr == null)
            return;

        hlr.RegisterMachine(te.MachineGuid, snapshot);
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

    public override bool OnBlockActivated(string commandName, WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue, EntityPlayerLocal player)
    {
        return OnBlockActivated(world, clrIdx, blockPos, blockValue, player);
    }

    public override bool OnBlockActivated(WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue, EntityPlayerLocal player)
    {
        if (player == null)
            return false;

        Helper.RequestMachineUIOpen(clrIdx, blockPos, player.entityId, "GrinderInfo");
        return true;
    }

    public override string GetActivationText(WorldBase world, BlockValue blockValue, int clrIdx, Vector3i blockPos, EntityAlive entityFocusing)
    {
        if (!(entityFocusing is EntityPlayerLocal player))
            return "[E] Open Universal Grinder";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} Open {name}";
    }

    private readonly BlockActivationCommand[] cmds =
    {
        new BlockActivationCommand("open", "campfire", true, false, null)
    };
}
