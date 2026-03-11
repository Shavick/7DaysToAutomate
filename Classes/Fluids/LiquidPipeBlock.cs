using System;
using System.Collections.Generic;

public class LiquidPipeBlock : MachineBlock<TileEntityLiquidPipe>
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

    protected override TileEntityLiquidPipe CreateTileEntity(Chunk chunk)
    {
        return new TileEntityLiquidPipe(chunk);
    }

    private static PipeShape GetPipeShape(BlockValue blockValue)
    {
        string blockName = blockValue.Block?.GetBlockName();
        if (string.IsNullOrEmpty(blockName))
            return PipeShape.None;

        if (blockName.StartsWith("liquidPipeElbow", StringComparison.OrdinalIgnoreCase))
            return PipeShape.Elbow;

        if (blockName.StartsWith("liquidPipeSmallJoint", StringComparison.OrdinalIgnoreCase))
            return PipeShape.TJunction;

        if (blockName.StartsWith("liquidPipe", StringComparison.OrdinalIgnoreCase))
            return PipeShape.Straight;

        return PipeShape.None;
    }

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
            case 0: sides.Add(Vector3i.forward); sides.Add(Vector3i.down); break;
            case 1: sides.Add(Vector3i.right); sides.Add(Vector3i.down); break;
            case 2: sides.Add(Vector3i.back); sides.Add(Vector3i.down); break;
            case 3: sides.Add(Vector3i.left); sides.Add(Vector3i.down); break;
            case 4: sides.Add(Vector3i.forward); sides.Add(Vector3i.up); break;
            case 5: sides.Add(Vector3i.left); sides.Add(Vector3i.up); break;
            case 6: sides.Add(Vector3i.back); sides.Add(Vector3i.up); break;
            case 7: sides.Add(Vector3i.right); sides.Add(Vector3i.up); break;
            case 9: sides.Add(Vector3i.left); sides.Add(Vector3i.back); break;
            case 11: sides.Add(Vector3i.back); sides.Add(Vector3i.right); break;
            case 12: sides.Add(Vector3i.forward); sides.Add(Vector3i.right); break;
            case 19: sides.Add(Vector3i.forward); sides.Add(Vector3i.left); break;
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
            case 0: sides.Add(Vector3i.forward); sides.Add(Vector3i.back); sides.Add(Vector3i.up); break;
            case 1: sides.Add(Vector3i.left); sides.Add(Vector3i.right); sides.Add(Vector3i.up); break;
            case 4: sides.Add(Vector3i.forward); sides.Add(Vector3i.back); sides.Add(Vector3i.down); break;
            case 5: sides.Add(Vector3i.left); sides.Add(Vector3i.right); sides.Add(Vector3i.down); break;
            case 8: sides.Add(Vector3i.forward); sides.Add(Vector3i.up); sides.Add(Vector3i.down); break;
            case 9: sides.Add(Vector3i.forward); sides.Add(Vector3i.right); sides.Add(Vector3i.left); break;
            case 12: sides.Add(Vector3i.back); sides.Add(Vector3i.forward); sides.Add(Vector3i.left); break;
            case 13: sides.Add(Vector3i.up); sides.Add(Vector3i.down); sides.Add(Vector3i.left); break;
            case 16: sides.Add(Vector3i.back); sides.Add(Vector3i.up); sides.Add(Vector3i.down); break;
            case 17: sides.Add(Vector3i.right); sides.Add(Vector3i.left); sides.Add(Vector3i.back); break;
            case 20: sides.Add(Vector3i.back); sides.Add(Vector3i.forward); sides.Add(Vector3i.right); break;
            case 21: sides.Add(Vector3i.up); sides.Add(Vector3i.down); sides.Add(Vector3i.right); break;
        }

        return sides;
    }

    private static HashSet<Vector3i> GetStraightOpenSides(int rotation)
    {
        var sides = new HashSet<Vector3i>();
        PipeAxis axis = GetStraightPipeAxis((byte)rotation);

        switch (axis)
        {
            case PipeAxis.AxisX: sides.Add(Vector3i.left); sides.Add(Vector3i.right); break;
            case PipeAxis.AxisY: sides.Add(Vector3i.up); sides.Add(Vector3i.down); break;
            case PipeAxis.AxisZ: sides.Add(Vector3i.back); sides.Add(Vector3i.forward); break;
        }

        return sides;
    }

    public static HashSet<Vector3i> GetOpenSides(BlockValue blockValue)
    {
        switch (GetPipeShape(blockValue))
        {
            case PipeShape.Straight: return GetStraightOpenSides(blockValue.rotation);
            case PipeShape.Elbow: return GetElbowOpenSides(blockValue.rotation);
            case PipeShape.TJunction: return GetTJunctionOpenSides(blockValue.rotation);
            default: return new HashSet<Vector3i>();
        }
    }

    public static bool IsConnectedPipeNeighbor(
        WorldBase world,
        int clrIdx,
        Vector3i pipePos,
        BlockValue pipeValue,
        Vector3i neighborPos)
    {
        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out TileEntity neighborTileEntity) || !(neighborTileEntity is TileEntityLiquidPipe))
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

    private static void MarkPipeDirtyAt(WorldBase world, int clrIdx, Vector3i pos)
    {
        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pos, out TileEntity tileEntity) || !(tileEntity is TileEntityLiquidPipe pipeTe))
            return;

        pipeTe.MarkFluidGraphDirty();
        pipeTe.setModified();
        FluidGraphManager.MarkPipeDirty(pos);
    }

    private static void MarkSelfAndAdjacentPipesDirty(WorldBase world, int clrIdx, Vector3i centerPos)
    {
        MarkPipeDirtyAt(world, clrIdx, centerPos);

        for (int i = 0; i < NeighborOffsets.Length; i++)
            MarkPipeDirtyAt(world, clrIdx, centerPos + NeighborOffsets[i]);
    }

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

        MarkSelfAndAdjacentPipesDirty(world, 0, blockPos);
    }

    public override void OnBlockRemoved(
        WorldBase world,
        Chunk chunk,
        Vector3i blockPos,
        BlockValue blockValue)
    {
        if (!world.IsRemote())
        {
            for (int i = 0; i < NeighborOffsets.Length; i++)
                MarkPipeDirtyAt(world, 0, blockPos + NeighborOffsets[i]);
        }

        base.OnBlockRemoved(world, chunk, blockPos, blockValue);
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
        LogGraphFromPipeActivation(world, clrIdx, blockPos);
        return true;
    }

        private static void LogGraphFromPipeActivation(WorldBase world, int clrIdx, Vector3i pipePos)
    {
        if (world == null)
            return;

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, pipePos, out TileEntity te) || !(te is TileEntityLiquidPipe pipe))
        {
            Log.Out($"[FluidGraph][PipeActivate] No liquid pipe tile entity at {pipePos}");
            return;
        }

        Guid graphId = pipe.FluidGraphId;
        if (graphId == Guid.Empty)
        {
            Log.Out($"[FluidGraph][PipeActivate] Pipe {pipePos} is unlinked (GraphId=Empty)");
            return;
        }

        if (!FluidGraphManager.TryGetGraph(graphId, out FluidGraphData graph) || graph == null)
        {
            if (!FluidGraphManager.TryEnsureGraphForPipe(world, clrIdx, pipePos, out graph) || graph == null)
            {
                Log.Out($"[FluidGraph][PipeActivate] Pipe {pipePos} references missing graph {graphId} (rebuild failed)");
                return;
            }

            graphId = graph.FluidGraphId;
        }

        string fluid = string.IsNullOrEmpty(graph.FluidType) ? "Unassigned" : graph.FluidType;
        int storageCountResolved = 0;
        int totalAmountMg = 0;
        int totalCapMg = 0;
        int totalInputCapMgPerTick = 0;
        int totalOutputCapMgPerTick = 0;

        foreach (Vector3i storagePos in graph.StorageEndpoints)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, storagePos, out TileEntity storageTe) || !(storageTe is TileEntityFluidStorage storage))
                continue;

            storageCountResolved++;
            totalAmountMg += storage.FluidAmountMg;
            totalCapMg += storage.GetCapacityMg();
            totalInputCapMgPerTick += storage.GetInputCapMgPerTick();
            totalOutputCapMgPerTick += storage.GetOutputCapMgPerTick();
        }

        Log.Out($"[FluidGraph][PipeActivate] Pos={pipePos} GraphId={graph.FluidGraphId} Fluid={fluid} Pipes={graph.PipePositions.Count} Pumps={graph.PumpEndpoints.Count} Storage={graph.StorageEndpoints.Count} Blocked={graph.LastBlockedReason}({graph.LastBlockedCount})");
        Log.Out($"[FluidGraph][PipeActivate] Totals Fluid={ToWholeGallons(totalAmountMg)}g/{ToWholeGallons(totalCapMg)}g InputCap={ToWholeGps(totalInputCapMgPerTick)}g/s OutputCap={ToWholeGps(totalOutputCapMgPerTick)}g/s ResolvedStorage={storageCountResolved}/{graph.StorageEndpoints.Count}");

        List<string> pumpLines = new List<string>();
        foreach (Vector3i endpoint in graph.PumpEndpoints)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, endpoint, out TileEntity pumpTe) || !(pumpTe is TileEntityFluidPump pump))
            {
                pumpLines.Add($"{endpoint} missing");
                continue;
            }

            string events = string.Join(" | ", pump.GetRecentEventsSummary());
            if (string.IsNullOrEmpty(events))
                events = "none";

            pumpLines.Add($"{endpoint} enabled={pump.PumpEnabled} outputCap={ToWholeGps(pump.GetOutputCapMgPerTick())}g/s events={events}");
        }

        List<string> storageLines = new List<string>();
        foreach (Vector3i endpoint in graph.StorageEndpoints)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, endpoint, out TileEntity storageTe) || !(storageTe is TileEntityFluidStorage storage))
            {
                storageLines.Add($"{endpoint} missing");
                continue;
            }

            string sFluid = string.IsNullOrEmpty(storage.FluidType) ? "Unassigned" : storage.FluidType;
            storageLines.Add($"{endpoint} fluid={sFluid} amt={ToWholeGallons(storage.FluidAmountMg)}g/{ToWholeGallons(storage.GetCapacityMg())}g inCap={ToWholeGps(storage.GetInputCapMgPerTick())}g/s outCap={ToWholeGps(storage.GetOutputCapMgPerTick())}g/s free={ToWholeGallons(storage.GetFreeSpaceMg())}g");
        }

        List<string> pipeLines = new List<string>();
        foreach (Vector3i p in graph.PipePositions)
            pipeLines.Add(p.ToString());

        pipeLines.Sort(StringComparer.Ordinal);
        Log.Out($"[FluidGraph][PipeActivate] PipeList={string.Join(", ", pipeLines)}");
        Log.Out($"[FluidGraph][PipeActivate] PumpDetails={string.Join(" || ", pumpLines)}");
        Log.Out($"[FluidGraph][PipeActivate] StorageDetails={string.Join(" || ", storageLines)}");
    }

    private static int ToWholeGallons(int mg)
    {
        return (mg + (FluidConstants.MilliGallonsPerGallon / 2)) / FluidConstants.MilliGallonsPerGallon;
    }

    private static int ToWholeGps(int mgPerTick)
    {
        int mgPerSec = mgPerTick * FluidConstants.SimulationTicksPerSecond;
        return ToWholeGallons(mgPerSec);
    }

    public override string GetActivationText(
        WorldBase world,
        BlockValue blockValue,
        int clrIdx,
        Vector3i blockPos,
        EntityAlive entityFocusing)
    {
        if (!(entityFocusing is EntityPlayerLocal player))
            return "[E] Probe Liquid Pipe";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} Probe {name}";
    }
}










