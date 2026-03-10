using System;

public class TileEntityItemPipe : TileEntityMachine
{
    private const int PersistVersion = 2;

    // ─────────────────────────────────────────────
    // STATE
    // ─────────────────────────────────────────────
    public Guid NetworkId = Guid.Empty;
    public bool IsNetworkDirty = true;

    public Guid PipeGraphId = Guid.Empty;
    public bool IsPipeGraphDirty = true;

    // ─────────────────────────────────────────────
    // CONSTRUCTOR
    // ─────────────────────────────────────────────
    public TileEntityItemPipe(Chunk chunk) : base(chunk)
    {
        DevLog("CTOR");
    }

    // ─────────────────────────────────────────────
    // LOGGING
    // ─────────────────────────────────────────────
    private bool IsDevLoggingEnabled()
    {
        if (blockValue.Block?.Properties == null)
            return false;

        string value = blockValue.Block.Properties.GetString("DevLogs");
        return bool.TryParse(value, out bool result) && result;
    }

    private void DevLog(string msg)
    {
        if (!IsDevLoggingEnabled())
            return;

        Log.Out($"[ItemPipe][TE][{ToWorldPos()}] {msg}");
    }

    // ─────────────────────────────────────────────
    // ENGINE REQUIRED
    // ─────────────────────────────────────────────
    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.ItemPipe);
    }

    // ─────────────────────────────────────────────
    // NETWORK HELPERS
    // ─────────────────────────────────────────────
    public bool HasValidNetworkId => NetworkId != Guid.Empty;

    public Guid GetNetworkId()
    {
        return NetworkId;
    }

    public void SetNetworkId(Guid id)
    {
        NetworkId = id;
        IsNetworkDirty = false;
        DevLog($"SetNetworkId={NetworkId}");
    }

    public void ClearNetworkId()
    {
        NetworkId = Guid.Empty;
        IsNetworkDirty = false;
        DevLog("ClearNetworkId()");
    }

    public void MarkNetworkDirty()
    {
        if (IsNetworkDirty)
            return;

        IsNetworkDirty = true;
        DevLog("MarkNetworkDirty()");
    }

    public void RecalculateNetworkId(WorldBase world, bool force = false)
    {
        if (world == null || world.IsRemote())
            return;

        if (!force && !IsNetworkDirty)
            return;

        int clrIdx = 0;
        Vector3i myPos = ToWorldPos();
        BlockValue myValue = world.GetBlock(clrIdx, myPos);

        DevLog($"RecalculateNetworkId BEGIN Dirty={IsNetworkDirty} CurrentNetworkId={NetworkId}");

        if (ItemPipeBlock.TryGetConnectedControllerNetworkId(world, clrIdx, myPos, myValue, out Guid resolvedNetworkId))
        {
            if (NetworkId != resolvedNetworkId || IsNetworkDirty)
            {
                SetNetworkId(resolvedNetworkId);
                setModified();
            }

            DevLog($"RecalculateNetworkId END Resolved={resolvedNetworkId}");
            return;
        }

        if (NetworkId != Guid.Empty || IsNetworkDirty)
        {
            ClearNetworkId();
            setModified();
        }

        DevLog("RecalculateNetworkId END Resolved=Guid.Empty");
    }

    // ─────────────────────────────────────────────
    // PIPE GRAPH HELPERS
    // ─────────────────────────────────────────────
    public bool HasValidPipeGraphId => PipeGraphId != Guid.Empty;

    public Guid GetPipeGraphId()
    {
        return PipeGraphId;
    }

    public void SetPipeGraphId(Guid id)
    {
        PipeGraphId = id;
        IsPipeGraphDirty = false;
        DevLog($"SetPipeGraphId={PipeGraphId}");
    }

    public void ClearPipeGraphId()
    {
        PipeGraphId = Guid.Empty;
        IsPipeGraphDirty = false;
        DevLog("ClearPipeGraphId()");
    }

    public void MarkPipeGraphDirty()
    {
        if (IsPipeGraphDirty)
            return;

        IsPipeGraphDirty = true;
        DevLog("MarkPipeGraphDirty()");
    }

    // ─────────────────────────────────────────────
    // SAVE / LOAD
    // ─────────────────────────────────────────────
    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        if (mode == StreamModeWrite.ToClient)
        {
            bw.Write(NetworkId.ToString());
            bw.Write(IsNetworkDirty);

            bw.Write(PipeGraphId.ToString());
            bw.Write(IsPipeGraphDirty);
            return;
        }

        if (mode != StreamModeWrite.Persistency)
            return;

        bw.Write(PersistVersion);

        bw.Write(NetworkId.ToString());
        bw.Write(IsNetworkDirty);

        bw.Write(PipeGraphId.ToString());
        bw.Write(IsPipeGraphDirty);
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);

        if (mode == StreamModeRead.FromServer)
        {
            string networkId = br.ReadString();
            if (!Guid.TryParse(networkId, out NetworkId))
                NetworkId = Guid.Empty;

            IsNetworkDirty = br.ReadBoolean();

            string pipeGraphId = br.ReadString();
            if (!Guid.TryParse(pipeGraphId, out PipeGraphId))
                PipeGraphId = Guid.Empty;

            IsPipeGraphDirty = br.ReadBoolean();
            return;
        }

        if (mode != StreamModeRead.Persistency)
            return;

        int version = br.ReadInt32();

        if (version >= 1)
        {
            string networkId = br.ReadString();
            if (!Guid.TryParse(networkId, out NetworkId))
                NetworkId = Guid.Empty;

            IsNetworkDirty = br.ReadBoolean();
        }

        if (version >= 2)
        {
            string pipeGraphId = br.ReadString();
            if (!Guid.TryParse(pipeGraphId, out PipeGraphId))
                PipeGraphId = Guid.Empty;

            IsPipeGraphDirty = br.ReadBoolean();
        }
        else
        {
            PipeGraphId = Guid.Empty;
            IsPipeGraphDirty = true;
        }
    }
}