public class TileEntityUniversalWasher : TileEntityMachine
{
    // ─────────────────────────────────────────────
    // CONSTRUCTOR
    // ─────────────────────────────────────────────
    public TileEntityUniversalWasher(Chunk chunk) : base(chunk)
    {
        DevLog("CTOR — TileEntityUniversalWasher CREATED");
    }

    // ─────────────────────────────────────────────
    // LOGGING
    // ─────────────────────────────────────────────
    private void DevLog(string msg)
    {
        if (!IsDevLogging)
            return;

        Log.Out($"[Washer][TE][{ToWorldPos()}] {msg}");
    }

    // ─────────────────────────────────────────────
    // ENGINE REQUIRED
    // ─────────────────────────────────────────────
    public override TileEntityType GetTileEntityType()
    {
        DevLog("GetTileEntityType()");
        return unchecked((TileEntityType)UCTileEntityIDs.UniversalWasher);
    }

    // ─────────────────────────────────────────────
    // HLR STATE FLAG (NO LOGIC YET)
    // ─────────────────────────────────────────────
    public override void SetSimulatedByHLR(bool value)
    {
        DevLog($"SetSimulatedByHLR({value})");
        simulatedByHLR = value;
    }

    // ─────────────────────────────────────────────
    // SAVE / LOAD (EMPTY STATE)
    // ─────────────────────────────────────────────
    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        DevLog($"WRITE BEGIN mode={mode}");

        base.write(bw, mode);

        int version = 1;

        if (mode != StreamModeWrite.Persistency)
            return;

        bw.Write(version); // VERSION

        //Add other write below

        DevLog("WRITE END Persistency");
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        DevLog($"READ BEGIN mode={mode}");

        base.read(br, mode);

        if (mode != StreamModeRead.Persistency)
            return;

        int version = br.ReadInt32();

        //Add other read below

        DevLog($"READ END Persistency version={version}");
    }
}
