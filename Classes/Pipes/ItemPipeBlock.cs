using System;
using System.Collections.Generic;

public class ItemPipeBlock : MachineBlock<TileEntityItemPipe>
{
    public enum PipeAxis
    {
        None,
        AxisX,
        AxisY,
        AxisZ
    }

    private enum PipeShape
    {
        None,
        Straight,
        Elbow,
        TJunction
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

    private readonly BlockActivationCommand[] cmds =
    {
        new BlockActivationCommand("open", "campfire", true, false, null)
    };

    // ─────────────────────────────────────────────
    // CONSTRUCTOR
    // ─────────────────────────────────────────────
    public ItemPipeBlock()
    {
        // HasTileEntity enforced by MachineBlock
    }

    // ─────────────────────────────────────────────
    // LOGGING
    // ─────────────────────────────────────────────
    private static bool IsDevLoggingEnabled(BlockValue blockValue)
    {
        if (blockValue.Block?.Properties == null)
            return false;

        string value = blockValue.Block.Properties.GetString("DevLogs");
        return bool.TryParse(value, out bool result) && result;
    }

    private static void DevLog(BlockValue blockValue, Vector3i pos, string msg)
    {
        if (!IsDevLoggingEnabled(blockValue))
            return;

        Log.Out($"[ItemPipe][BLOCK][{pos}] {msg}");
    }

    private static PipeShape GetPipeShape(BlockValue blockValue)
    {
        string blockName = blockValue.Block?.GetBlockName();
        if (string.IsNullOrEmpty(blockName))
            return PipeShape.None;

        if (blockName.StartsWith("itemPipeElbow", StringComparison.OrdinalIgnoreCase))
            return PipeShape.Elbow;

        if (blockName.StartsWith("itemPipeSmallJoint", StringComparison.OrdinalIgnoreCase))
            return PipeShape.TJunction;

        if (blockName.StartsWith("itemPipe", StringComparison.OrdinalIgnoreCase))
            return PipeShape.Straight;

        return PipeShape.None;
    }

    // ─────────────────────────────────────────────
    // TILE ENTITY CREATION
    // ─────────────────────────────────────────────
    protected override TileEntityItemPipe CreateTileEntity(Chunk chunk)
    {
        Log.Out("[ItemPipe][BLOCK] CreateTileEntity()");
        return new TileEntityItemPipe(chunk);
    }

    // ─────────────────────────────────────────────
    // ROTATION / OPEN SIDE HELPERS
    // ─────────────────────────────────────────────
    public static PipeAxis GetStraightPipeAxis(BlockValue blockValue)
    {
        return GetStraightPipeAxis(blockValue.rotation);
    }

    public static PipeAxis GetStraightPipeAxis(byte rawRotation)
    {
        switch (rawRotation)
        {
            case 0:
            case 2:
                return PipeAxis.AxisZ;

            case 1:
            case 3:
                return PipeAxis.AxisX;

            case 8:
            case 10:
            case 13:
            case 15:
            case 16:
            case 18:
            case 21:
            case 23:
                return PipeAxis.AxisY;

            default:
                return PipeAxis.None;
        }
    }

    private static int NormalizeElbowRotation(int rotation)
    {
        switch (rotation)
        {
            case 8: return 6;
            case 10: return 2;
            case 13: return 7;
            case 14: return 11;
            case 15: return 1;
            case 16: return 4;
            case 17: return 12;
            case 18: return 0;
            case 20: return 19;
            case 21: return 3;
            case 22: return 9;
            case 23: return 5;
            default: return rotation;
        }
    }

    private static HashSet<Vector3i> GetElbowOpenSides(int rotation)
    {
        rotation = NormalizeElbowRotation(rotation);

        var sides = new HashSet<Vector3i>();

        switch (rotation)
        {
            case 0:
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.down);
                break;

            case 1:
                sides.Add(Vector3i.right);
                sides.Add(Vector3i.down);
                break;

            case 2:
                sides.Add(Vector3i.back);
                sides.Add(Vector3i.down);
                break;

            case 3:
                sides.Add(Vector3i.left);
                sides.Add(Vector3i.down);
                break;

            case 4:
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.up);
                break;

            case 5:
                sides.Add(Vector3i.left);
                sides.Add(Vector3i.up);
                break;

            case 6:
                sides.Add(Vector3i.back);
                sides.Add(Vector3i.up);
                break;

            case 7:
                sides.Add(Vector3i.right);
                sides.Add(Vector3i.up);
                break;

            case 9:
                sides.Add(Vector3i.left);
                sides.Add(Vector3i.back);
                break;

            case 11:
                sides.Add(Vector3i.back);
                sides.Add(Vector3i.right);
                break;

            case 12:
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.right);
                break;

            case 19:
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.left);
                break;
        }

        return sides;
    }

    private static int NormalizeTJunctionRotation(int rotation)
    {
        switch (rotation)
        {
            case 2: return 0;
            case 3: return 1;
            case 6: return 4;
            case 7: return 5;
            case 10: return 8;
            case 11: return 9;
            case 14: return 12;
            case 15: return 13;
            case 18: return 16;
            case 19: return 17;
            case 22: return 20;
            case 23: return 21;
            default: return rotation;
        }
    }

    private static HashSet<Vector3i> GetTJunctionOpenSides(int rotation)
    {
        rotation = NormalizeTJunctionRotation(rotation);

        var sides = new HashSet<Vector3i>();

        switch (rotation)
        {
            case 0: // NSU
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.back);
                sides.Add(Vector3i.up);
                break;

            case 1: // EWU
                sides.Add(Vector3i.left);
                sides.Add(Vector3i.right);
                sides.Add(Vector3i.up);
                break;

            case 4: // NSD
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.back);
                sides.Add(Vector3i.down);
                break;

            case 5: // EWD
                sides.Add(Vector3i.left);
                sides.Add(Vector3i.right);
                sides.Add(Vector3i.down);
                break;

            case 8: // UDN
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.up);
                sides.Add(Vector3i.down);
                break;

            case 9: // NEW
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.right);
                sides.Add(Vector3i.left);
                break;

            case 12: // NSW
                sides.Add(Vector3i.back);
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.left);
                break;

            case 13: // UDW
                sides.Add(Vector3i.up);
                sides.Add(Vector3i.down);
                sides.Add(Vector3i.left);
                break;

            case 16: // UDS
                sides.Add(Vector3i.back);
                sides.Add(Vector3i.up);
                sides.Add(Vector3i.down);
                break;

            case 17: // EWN
                sides.Add(Vector3i.right);
                sides.Add(Vector3i.left);
                sides.Add(Vector3i.back);
                break;

            case 20: // NSE
                sides.Add(Vector3i.back);
                sides.Add(Vector3i.forward);
                sides.Add(Vector3i.right);
                break;

            case 21: // UDE
                sides.Add(Vector3i.up);
                sides.Add(Vector3i.down);
                sides.Add(Vector3i.right);
                break;
        }

        return sides;
    }

    private static HashSet<Vector3i> GetStraightOpenSides(int rotation)
    {
        var sides = new HashSet<Vector3i>();

        PipeAxis axis = GetStraightPipeAxis((byte)rotation);

        switch (axis)
        {
            case PipeAxis.AxisX:
                sides.Add(Vector3i.left);
                sides.Add(Vector3i.right);
                break;

            case PipeAxis.AxisY:
                sides.Add(Vector3i.up);
                sides.Add(Vector3i.down);
                break;

            case PipeAxis.AxisZ:
                sides.Add(Vector3i.back);
                sides.Add(Vector3i.forward);
                break;
        }

        return sides;
    }

    public static HashSet<Vector3i> GetOpenSides(BlockValue blockValue)
    {
        switch (GetPipeShape(blockValue))
        {
            case PipeShape.Straight:
                return GetStraightOpenSides(blockValue.rotation);

            case PipeShape.Elbow:
                return GetElbowOpenSides(blockValue.rotation);

            case PipeShape.TJunction:
                return GetTJunctionOpenSides(blockValue.rotation);

            default:
                return new HashSet<Vector3i>();
        }
    }

    public static bool IsPipeBlock(WorldBase world, int clrIdx, Vector3i blockPos)
    {
        return SafeWorldRead.TryGetTileEntity(world, clrIdx, blockPos, out TileEntity tileEntity) && tileEntity is TileEntityItemPipe;
    }

    public static bool IsConnectedPipeNeighbor(
        WorldBase world,
        int clrIdx,
        Vector3i pipePos,
        BlockValue pipeValue,
        Vector3i neighborPos)
    {
        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out TileEntity neighborTileEntity) || !(neighborTileEntity is TileEntityItemPipe))
            return false;

        if (!SafeWorldRead.TryGetBlock(world, clrIdx, neighborPos, out BlockValue neighborValue))
            return false;

        HashSet<Vector3i> myOpenSides = GetOpenSides(pipeValue);
        HashSet<Vector3i> neighborOpenSides = GetOpenSides(neighborValue);

        Vector3i delta = neighborPos - pipePos;
        Vector3i opposite = new Vector3i(-delta.x, -delta.y, -delta.z);

        return myOpenSides.Contains(delta) && neighborOpenSides.Contains(opposite);
    }

    public static List<Vector3i> GetConnectedPipeNeighbors(
        WorldBase world,
        int clrIdx,
        Vector3i pipePos,
        BlockValue pipeValue)
    {
        var results = new List<Vector3i>();

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i neighborPos = pipePos + NeighborOffsets[i];

            if (IsConnectedPipeNeighbor(world, clrIdx, pipePos, pipeValue, neighborPos))
                results.Add(neighborPos);
        }

        return results;
    }

    public static bool IsControllerConnectedNeighbor(
        WorldBase world,
        int clrIdx,
        Vector3i pipePos,
        BlockValue pipeValue,
        Vector3i neighborPos)
    {
        TileEntity controllerEntity;
        var controllerTe = SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out controllerEntity) ? controllerEntity as TileEntityNetworkController : null;
        if (controllerTe == null || !controllerTe.HasValidNetworkId)
            return false;

        HashSet<Vector3i> myOpenSides = GetOpenSides(pipeValue);
        Vector3i delta = neighborPos - pipePos;

        return myOpenSides.Contains(delta);
    }

    public static bool TryGetConnectedControllerNetworkId(
        WorldBase world,
        int clrIdx,
        Vector3i pipePos,
        BlockValue pipeValue,
        out Guid networkId)
    {
        networkId = Guid.Empty;

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i neighborPos = pipePos + NeighborOffsets[i];

            if (!IsControllerConnectedNeighbor(world, clrIdx, pipePos, pipeValue, neighborPos))
                continue;

            TileEntity controllerEntity;
        var controllerTe = SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out controllerEntity) ? controllerEntity as TileEntityNetworkController : null;
            if (controllerTe == null || !controllerTe.HasValidNetworkId)
                continue;

            networkId = controllerTe.GetNetworkId();
            return true;
        }

        return false;
    }

    private static void MarkPipeDirtyAt(WorldBase world, int clrIdx, Vector3i pos)
    {
        TileEntity pipeEntity;
        var pipeTe = SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out pipeEntity) ? pipeEntity as TileEntityItemPipe : null;
        if (pipeTe == null)
            return;

        pipeTe.MarkPipeGraphDirty();
        pipeTe.MarkNetworkDirty();
        pipeTe.setModified();

        PipeGraphManager.MarkPipeDirty(pos);

        if (!SafeWorldRead.TryGetBlock(world, clrIdx, pos, out BlockValue blockValue))
            return;
        DevLog(blockValue, pos, "Marked pipe dirty");
    }

    private static void MarkSelfAndAdjacentPipesDirty(WorldBase world, int clrIdx, Vector3i centerPos)
    {
        MarkPipeDirtyAt(world, clrIdx, centerPos);

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i neighborPos = centerPos + NeighborOffsets[i];
            MarkPipeDirtyAt(world, clrIdx, neighborPos);
        }
    }

    // ─────────────────────────────────────────────
    // BLOCK LIFECYCLE
    // ─────────────────────────────────────────────
    public override void OnBlockAdded(
        WorldBase world,
        Chunk chunk,
        Vector3i blockPos,
        BlockValue blockValue,
        PlatformUserIdentifierAbs addedByPlayer)
    {
        base.OnBlockAdded(world, chunk, blockPos, blockValue, addedByPlayer);

        if (world.IsRemote() || blockValue.ischild)
            return;

        DevLog(blockValue, blockPos, "OnBlockAdded()");
        MarkSelfAndAdjacentPipesDirty(world, 0, blockPos);
    }

    public override void OnBlockRemoved(
        WorldBase world,
        Chunk chunk,
        Vector3i blockPos,
        BlockValue blockValue)
    {
        if (world.IsRemote())
        {
            base.OnBlockRemoved(world, chunk, blockPos, blockValue);
            return;
        }

        DevLog(blockValue, blockPos, "OnBlockRemoved()");

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i neighborPos = blockPos + NeighborOffsets[i];
            MarkPipeDirtyAt(world, 0, neighborPos);
        }

        base.OnBlockRemoved(world, chunk, blockPos, blockValue);
    }

    // ─────────────────────────────────────────────
    // ACTIVATION / UI
    // ─────────────────────────────────────────────
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
        TileEntity pipeEntity;
        var te = SafeWorldRead.TryGetTileEntity(world, clrIdx, blockPos, out pipeEntity) ? pipeEntity as TileEntityItemPipe : null;

        if (te == null)
        {
            DevLog(blockValue, blockPos, "Activated but TE was null");
            return true;
        }

        string blockName = blockValue.Block?.GetBlockName() ?? "null";
        PipeShape shape = GetPipeShape(blockValue);
        HashSet<Vector3i> openSides = GetOpenSides(blockValue);

        Log.Out($"[ItemPipe][DEBUG][{blockPos}] blockName={blockName} shape={shape} rotation={blockValue.rotation}");

        foreach (Vector3i side in openSides)
            DevLog(blockValue, blockPos, $"OpenSide={side}");

        te.RecalculateNetworkId(world);

        List<Vector3i> connected = GetConnectedPipeNeighbors(world, clrIdx, blockPos, blockValue);

        DevLog(
            blockValue,
            blockPos,
            $"NetworkId={te.NetworkId} NetworkDirty={te.IsNetworkDirty} " +
            $"PipeGraphId={te.PipeGraphId} PipeGraphDirty={te.IsPipeGraphDirty} " +
            $"connectedCount={connected.Count}");

        for (int i = 0; i < connected.Count; i++)
            DevLog(blockValue, blockPos, $"ConnectedPipe[{i}]={connected[i]}");

        if (PipeGraphManager.TryGetStorageEndpointsForPipe(world, clrIdx, blockPos, out List<Vector3i> storageEndpoints))
        {
            DevLog(blockValue, blockPos, $"StorageEndpointCount={storageEndpoints.Count}");

            for (int i = 0; i < storageEndpoints.Count; i++)
                DevLog(blockValue, blockPos, $"StorageEndpoint[{i}]={storageEndpoints[i]}");
        }
        else
        {
            DevLog(blockValue, blockPos, "StorageEndpointCount=0");
        }

        return true;
    }

    public override string GetActivationText(
        WorldBase world,
        BlockValue blockValue,
        int clrIdx,
        Vector3i blockPos,
        EntityAlive entityFocusing)
    {
        if (!(entityFocusing is EntityPlayerLocal player))
            return "[E] Inspect Item Pipe";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} Inspect {name}";
    }
}