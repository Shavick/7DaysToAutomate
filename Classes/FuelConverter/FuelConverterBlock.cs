public class FuelConverterBlock : MachineBlock<TileEntityFuelConverter>
{
    protected override TileEntityFuelConverter CreateTileEntity(Chunk chunk)
    {
        return new TileEntityFuelConverter(chunk);
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
        Helper.RequestMachineUIOpen(clrIdx, blockPos, player.entityId, "FuelConverterInfo");
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
            return "[E] Open Fluid Decanter";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} Open {name}";
    }
}






