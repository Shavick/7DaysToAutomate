using System;
using System.Collections.Generic;

public class TileEntityNetworkStorageInterface : TileEntity
{
    private const int PersistVersion = 1;

    public Guid NetworkId = Guid.Empty;
    public bool IsNetworkDirty = true;

    // For future remote pairing support.
    public Vector3i LinkedControllerPos = Vector3i.zero;
    public int LastSnapshotRevision = 0;
    public int LastSnapshotStackTypes = 0;
    public int LastSnapshotTotalItems = 0;
    public ulong LastSnapshotBuildWorldTime = 0UL;
    public string LastSnapshotTopSummary = string.Empty;

    public TileEntityNetworkStorageInterface(Chunk chunk) : base(chunk)
    {
    }

    public bool HasValidNetworkId => NetworkId != Guid.Empty;

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.NetworkStorageInterface);
    }

    public void MarkNetworkDirty()
    {
        IsNetworkDirty = true;
    }

    public void SetLinkedControllerPos(Vector3i controllerPos)
    {
        if (controllerPos == LinkedControllerPos)
            return;

        LinkedControllerPos = controllerPos;
        IsNetworkDirty = true;
        setModified();
    }

    public bool ResolveNetworkId(WorldBase world, bool force = false)
    {
        if (world == null || world.IsRemote())
            return false;

        if (!force && !IsNetworkDirty && NetworkId != Guid.Empty)
            return true;

        Guid resolved = Guid.Empty;

        // First choice: explicitly linked controller (future remote pairing path).
        if (LinkedControllerPos != Vector3i.zero &&
            SafeWorldRead.TryGetTileEntity(world, 0, LinkedControllerPos, out TileEntity linkedTe) &&
            linkedTe is TileEntityNetworkController linkedController &&
            linkedController.HasValidNetworkId)
        {
            resolved = linkedController.NetworkId;
        }

        // Fallback: detect adjacent pipe's network id.
        if (resolved == Guid.Empty)
        {
            Vector3i self = ToWorldPos();
            for (int i = 0; i < NeighborOffsets.Length; i++)
            {
                Vector3i pipePos = self + NeighborOffsets[i];
                if (!SafeWorldRead.TryGetTileEntity(world, 0, pipePos, out TileEntity te) || !(te is TileEntityItemPipe pipeTe))
                    continue;

                if (pipeTe.NetworkId == Guid.Empty)
                    continue;

                resolved = pipeTe.NetworkId;
                break;
            }
        }

        bool changed = NetworkId != resolved;
        NetworkId = resolved;
        IsNetworkDirty = false;

        if (changed)
            setModified();

        return NetworkId != Guid.Empty;
    }

    public bool RefreshSnapshotSummary(WorldBase world)
    {
        LastSnapshotRevision = 0;
        LastSnapshotStackTypes = 0;
        LastSnapshotTotalItems = 0;
        LastSnapshotBuildWorldTime = 0UL;
        LastSnapshotTopSummary = string.Empty;

        if (world == null || world.IsRemote() || NetworkId == Guid.Empty)
            return false;

        if (!NetworkStorageService.TryBuildStorageSnapshot(world, NetworkId, out NetworkStorageSnapshot snapshot) || snapshot == null)
            return false;

        LastSnapshotRevision = snapshot.Revision;
        LastSnapshotStackTypes = snapshot.DisplayStacks.Count;
        LastSnapshotBuildWorldTime = snapshot.BuildAtWorldTime;

        int total = 0;
        int topCount = Math.Min(6, snapshot.DisplayStacks.Count);
        var summary = new List<string>(topCount);

        for (int i = 0; i < snapshot.DisplayStacks.Count; i++)
        {
            NetworkDisplayStack stack = snapshot.DisplayStacks[i];
            total += stack.TotalCount;

            if (i < topCount)
            {
                string itemName = NetworkItemIdentity.GetStableDisplayName(stack.DisplayStack);
                summary.Add($"{itemName}:{stack.TotalCount}");
            }
        }

        LastSnapshotTotalItems = total;
        LastSnapshotTopSummary = summary.Count > 0 ? string.Join(" | ", summary) : string.Empty;
        setModified();
        return true;
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        if (mode == StreamModeWrite.ToClient)
        {
            bw.Write(NetworkId.ToString());
            bw.Write(IsNetworkDirty);
            WriteVector3i(bw, LinkedControllerPos);
            bw.Write(LastSnapshotRevision);
            bw.Write(LastSnapshotStackTypes);
            bw.Write(LastSnapshotTotalItems);
            bw.Write((long)LastSnapshotBuildWorldTime);
            bw.Write(LastSnapshotTopSummary ?? string.Empty);
            return;
        }

        if (mode != StreamModeWrite.Persistency)
            return;

        bw.Write(PersistVersion);
        bw.Write(NetworkId.ToString());
        bw.Write(IsNetworkDirty);
        WriteVector3i(bw, LinkedControllerPos);
        bw.Write(LastSnapshotRevision);
        bw.Write(LastSnapshotStackTypes);
        bw.Write(LastSnapshotTotalItems);
        bw.Write((long)LastSnapshotBuildWorldTime);
        bw.Write(LastSnapshotTopSummary ?? string.Empty);
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
            LinkedControllerPos = ReadVector3i(br);
            LastSnapshotRevision = br.ReadInt32();
            LastSnapshotStackTypes = br.ReadInt32();
            LastSnapshotTotalItems = br.ReadInt32();
            LastSnapshotBuildWorldTime = (ulong)br.ReadInt64();
            LastSnapshotTopSummary = br.ReadString();
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
            LinkedControllerPos = ReadVector3i(br);
            LastSnapshotRevision = br.ReadInt32();
            LastSnapshotStackTypes = br.ReadInt32();
            LastSnapshotTotalItems = br.ReadInt32();
            LastSnapshotBuildWorldTime = (ulong)br.ReadInt64();
            LastSnapshotTopSummary = br.ReadString();
        }
    }

    private static void WriteVector3i(PooledBinaryWriter bw, Vector3i value)
    {
        bw.Write(value.x);
        bw.Write(value.y);
        bw.Write(value.z);
    }

    private static Vector3i ReadVector3i(PooledBinaryReader br)
    {
        int x = br.ReadInt32();
        int y = br.ReadInt32();
        int z = br.ReadInt32();
        return new Vector3i(x, y, z);
    }

    private static readonly Vector3i[] NeighborOffsets =
    {
        Vector3i.forward,
        Vector3i.back,
        Vector3i.left,
        Vector3i.right,
        Vector3i.up,
        Vector3i.down
    };
}
