using System;

public class TileEntityLiquidPipe : TileEntityMachine
{
    private const int PersistVersion = 3;

    public Guid FluidGraphId = Guid.Empty;
    public bool IsFluidGraphDirty = true;
    public bool IsValveOpen = true;
    public string RememberedFluidType = string.Empty;

    public TileEntityLiquidPipe(Chunk chunk) : base(chunk)
    {
    }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.LiquidPipe);
    }

    public void SetFluidGraphId(Guid id)
    {
        FluidGraphId = id;
        IsFluidGraphDirty = false;
    }

    public void ClearFluidGraphId()
    {
        FluidGraphId = Guid.Empty;
        IsFluidGraphDirty = false;
    }

    public void MarkFluidGraphDirty()
    {
        IsFluidGraphDirty = true;
    }

    public void SetRememberedFluidType(string fluidType)
    {
        if (string.IsNullOrWhiteSpace(fluidType))
        {
            RememberedFluidType = string.Empty;
            return;
        }

        RememberedFluidType = fluidType.Trim().ToLowerInvariant();
    }

    public void ClearRememberedFluidType()
    {
        RememberedFluidType = string.Empty;
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        if (mode == StreamModeWrite.Persistency)
            bw.Write(PersistVersion);

        bw.Write(FluidGraphId.ToString());
        bw.Write(IsFluidGraphDirty);
        bw.Write(IsValveOpen);
        bw.Write(RememberedFluidType ?? string.Empty);
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);

        int persistVersion = PersistVersion;
        if (mode == StreamModeRead.Persistency)
            persistVersion = br.ReadInt32();

        string graphId = br.ReadString();
        if (!Guid.TryParse(graphId, out FluidGraphId))
            FluidGraphId = Guid.Empty;

        IsFluidGraphDirty = br.ReadBoolean();

        if (mode == StreamModeRead.Persistency && persistVersion < 2)
            IsValveOpen = true;
        else
            IsValveOpen = br.ReadBoolean();

        if (mode == StreamModeRead.Persistency && persistVersion < 3)
            RememberedFluidType = string.Empty;
        else
            SetRememberedFluidType(br.ReadString() ?? string.Empty);
    }
}
