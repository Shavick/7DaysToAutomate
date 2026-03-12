using System;

public class TileEntityLiquidPipe : TileEntityMachine
{
    private const int PersistVersion = 2;

    public Guid FluidGraphId = Guid.Empty;
    public bool IsFluidGraphDirty = true;
    public bool IsValveOpen = true;

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

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        if (mode == StreamModeWrite.Persistency)
            bw.Write(PersistVersion);

        bw.Write(FluidGraphId.ToString());
        bw.Write(IsFluidGraphDirty);
        bw.Write(IsValveOpen);
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
    }
}
