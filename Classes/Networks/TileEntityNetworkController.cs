using System;

public class TileEntityNetworkController : TileEntity
{
    // ─────────────────────────────────────────────
    // STATE
    // ─────────────────────────────────────────────
    public Guid NetworkId = Guid.Empty;

    // ─────────────────────────────────────────────
    // CONSTRUCTOR
    // ─────────────────────────────────────────────
    public TileEntityNetworkController(Chunk chunk) : base(chunk)
    {
        if (NetworkId == Guid.Empty)
        {
            NetworkId = Guid.NewGuid();
            Log.Out($"[NetworkController][TE] CTOR — Generated NetworkId={NetworkId}");
        }
        else
        {
            Log.Out($"[NetworkController][TE] CTOR — Existing NetworkId={NetworkId}");
        }
    }

    public bool HasValidNetworkId => NetworkId != Guid.Empty;

    public Guid GetNetworkId()
    {
        return NetworkId;
    }

    // ─────────────────────────────────────────────
    // ENGINE REQUIRED
    // ─────────────────────────────────────────────
    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.NetworkController);
    }

    // ─────────────────────────────────────────────
    // SAVE / LOAD
    // ─────────────────────────────────────────────
    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        //Log.Out($"[NetworkController][TE][{ToWorldPos()}] WRITE BEGIN mode={mode}");

        base.write(bw, mode);

        if (mode == StreamModeWrite.ToClient)
        {
            bw.Write(NetworkId.ToString());
            return;
        }

        if (mode != StreamModeWrite.Persistency)
        {
            //Log.Out($"[NetworkController][TE][{ToWorldPos()}] WRITE SKIP custom (mode={mode})");
            return;
        }

        bw.Write(1); // VERSION
        bw.Write(NetworkId.ToString());

        //Log.Out($"[NetworkController][TE][{ToWorldPos()}] WRITE END Persistency NetworkId={NetworkId}");
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);

        if (mode == StreamModeRead.FromServer)
        {
            string id = br.ReadString();
            if (!Guid.TryParse(id, out NetworkId))
                NetworkId = Guid.Empty;

            Log.Out($"[NetworkController][TE][{ToWorldPos()}] READ FromServer NetworkId={NetworkId}");
            return;
        }

        if (mode != StreamModeRead.Persistency)
        {
            Log.Out($"[NetworkController][TE][{ToWorldPos()}] READ SKIP custom (mode={mode})");
            return;
        }

        int version = br.ReadInt32();

        if (version >= 1)
        {
            string id = br.ReadString();
            if (!Guid.TryParse(id, out NetworkId))
                NetworkId = Guid.Empty;
        }

        Log.Out($"[NetworkController][TE][{ToWorldPos()}] READ END Persistency NetworkId={NetworkId}");
    }
}