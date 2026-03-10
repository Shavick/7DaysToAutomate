public class NetPackageToggleMachinePower : NetPackage
{
    public enum MessageType : byte
    {
        RequestToggle,
        ToggleClient
    }

    private Vector3i BlockPos;
    private int BlockPosx;
    private int BlockPosy;
    private int BlockPosz;
    private int ClrIdx;
    private MessageType Type;
    private bool IsOn;

    public NetPackageToggleMachinePower Setup(int clrIdx, Vector3i blockPos, NetPackageToggleMachinePower.MessageType type, bool isOn = false)
    {
        ClrIdx = clrIdx;
        BlockPos = blockPos;
        BlockPosx = blockPos.x;
        BlockPosy = blockPos.y;
        BlockPosz = blockPos.z;
        Type = type;
        IsOn = isOn;
        Log.Out($"[Setup]BlockPosition = {BlockPos}");
        return this;
    }

    public override void write(PooledBinaryWriter _writer)
    {
        Log.Out("[NetPackage][ToggleMachinePower][Write] Start");
        base.write(_writer);
        _writer.Write(ClrIdx);
        _writer.Write(BlockPosx);
        _writer.Write(BlockPosy);
        _writer.Write(BlockPosz);
        _writer.Write((byte)Type);
        _writer.Write(IsOn);
        Log.Out("[NetPackage][ToggleMachinePower][Write] Finish");
    }

    public override void read(PooledBinaryReader _reader)
    {
        Log.Out("[NetPackage][ToggleMachinePower][Read] Start");
        ClrIdx = _reader.ReadInt32();
        BlockPosx = _reader.ReadInt32();
        BlockPosy = _reader.ReadInt32();
        BlockPosz = _reader.ReadInt32();
        BlockPos = new Vector3i(BlockPosx, BlockPosy, BlockPosz);
        Type = (NetPackageToggleMachinePower.MessageType)_reader.ReadByte();
        IsOn = _reader.ReadBoolean();
        Log.Out("[NetPackage][ToggleMachinePower][Read] Finish");
    }
    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        // Only the server should act on this package.
        if (!ConnectionManager.Instance.IsServer)
            return;

        // Only handle the request message.
        if (Type != MessageType.RequestToggle)
            return;

        // IMPORTANT: ensure BlockPos is valid in case you’re using x/y/z fields in write/read.
        // If you fixed your netpackage to always rebuild BlockPos in read(), this line is redundant but harmless.
        if (BlockPos == Vector3i.zero && (BlockPosx != 0 || BlockPosy != 0 || BlockPosz != 0))
            BlockPos = new Vector3i(BlockPosx, BlockPosy, BlockPosz);

        TileEntity te = _world.GetTileEntity(ClrIdx, BlockPos);
        if (te == null)
        {
            Log.Error($"[NetPkg][Power][SERVER] No TileEntity at {BlockPos} (clrIdx={ClrIdx})");
            return;
        }

        if (!(te is TileEntityMachine machine))
        {
            Log.Error($"[NetPkg][Power][SERVER] TE at {BlockPos} is not TileEntityMachine (actual={te.GetType().Name})");
            return;
        }

        Log.Out($"[NetPkg][Power][SERVER] Toggling power at {BlockPos} IsOn(before)={machine.IsOn}");
        machine.TogglePower(); // MUST cause setModified() and ToClient serialization for clients to update
        Log.Out($"[NetPkg][Power][SERVER] Toggled power at {BlockPos} IsOn(after)={machine.IsOn}");

        // NO ToggleClient response here.
        // The server’s setModified() will trigger NetPackageTileEntity updates to clients.
    }
    public override int GetLength()
    {
        return 18;
    }
}

