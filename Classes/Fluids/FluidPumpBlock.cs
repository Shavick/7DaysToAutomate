using System;
using System.Collections.Generic;

public class FluidPumpBlock : MachineBlock<TileEntityFluidPump>
{
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

    protected override TileEntityFluidPump CreateTileEntity(Chunk chunk)
    {
        return new TileEntityFluidPump(chunk);
    }

    public override void OnBlockLoaded(
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue)
    {
        base.OnBlockLoaded(world, clrIdx, blockPos, blockValue);
        if (world.IsRemote() || blockValue.ischild)
            return;

        FluidGraphManager.TryApplyPumpSnapshotForPosition(world, clrIdx, blockPos);
        MarkAdjacentPipesDirty(world, clrIdx, blockPos);
    }

    public override void OnBlockUnloaded(
        WorldBase world,
        int clrIdx,
        Vector3i blockPos,
        BlockValue blockValue)
    {
        if (!world.IsRemote() && !blockValue.ischild)
        {
            FluidGraphManager.CapturePumpSnapshotForPosition(world, clrIdx, blockPos);
            MarkAdjacentPipesDirty(world, clrIdx, blockPos);
        }

        base.OnBlockUnloaded(world, clrIdx, blockPos, blockValue);
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

        MarkAdjacentPipesDirty(world, 0, blockPos);
    }

    public override void OnBlockRemoved(
        WorldBase world,
        Chunk chunk,
        Vector3i blockPos,
        BlockValue blockValue)
    {
        if (!world.IsRemote())
        {
            FluidGraphManager.RemovePumpSnapshotAtPosition(blockPos, true);
            MarkAdjacentPipesDirty(world, 0, blockPos);
        }

        base.OnBlockRemoved(world, chunk, blockPos, blockValue);
    }

    public override bool HasBlockActivationCommands(WorldBase world, BlockValue blockValue, int clrIdx, Vector3i blockPos, EntityAlive entityFocusing)
    {
        return true;
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase world, BlockValue blockValue, int clrIdx, Vector3i blockPos, EntityAlive entityFocusing)
    {
        return cmds;
    }

    public override bool OnBlockActivated(string commandName, WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue, EntityPlayerLocal player)
    {
        return OnBlockActivated(world, clrIdx, blockPos, blockValue, player);
    }

    public override bool OnBlockActivated(WorldBase world, int clrIdx, Vector3i blockPos, BlockValue blockValue, EntityPlayerLocal player)
    {
        LogGraphFromPumpActivation(world, clrIdx, blockPos);
        return true;
    }

    private static void LogGraphFromPumpActivation(WorldBase world, int clrIdx, Vector3i pumpPos)
    {
        if (world == null)
            return;

        Guid graphId = Guid.Empty;
        Vector3i seedPipePos = Vector3i.zero;
        bool hasSeedPipe = false;

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i neighbor = pumpPos + NeighborOffsets[i];
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighbor, out TileEntity te) || !(te is TileEntityLiquidPipe pipe))
                continue;

            if (!hasSeedPipe)
            {
                seedPipePos = neighbor;
                hasSeedPipe = true;
            }

            if (pipe.FluidGraphId != Guid.Empty)
            {
                graphId = pipe.FluidGraphId;
                seedPipePos = neighbor;
                hasSeedPipe = true;
                break;
            }
        }

        if (graphId == Guid.Empty)
        {
            if (!hasSeedPipe)
            {
                Log.Out($"[FluidGraph][PumpActivate] Pump {pumpPos} is unlinked (no adjacent pipe)");
                return;
            }

            if (!FluidGraphManager.TryEnsureGraphForPipe(world, clrIdx, seedPipePos, out FluidGraphData rebuiltGraph) || rebuiltGraph == null)
            {
                Log.Out($"[FluidGraph][PumpActivate] Pump {pumpPos} is unlinked (no adjacent graph)");
                return;
            }

            graphId = rebuiltGraph.FluidGraphId;
        }

        if (!FluidGraphManager.TryGetGraph(graphId, out FluidGraphData graph) || graph == null)
        {
            if (!hasSeedPipe || !FluidGraphManager.TryEnsureGraphForPipe(world, clrIdx, seedPipePos, out graph) || graph == null)
            {
                Log.Out($"[FluidGraph][PumpActivate] Pump {pumpPos} references missing graph {graphId} (rebuild failed)");
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

        Log.Out($"[FluidGraph][PumpActivate] Pos={pumpPos} GraphId={graph.FluidGraphId} Fluid={fluid} Pipes={graph.PipePositions.Count} Pumps={graph.PumpEndpoints.Count} Storage={graph.StorageEndpoints.Count} Blocked={graph.LastBlockedReason}({graph.LastBlockedCount})");
        Log.Out($"[FluidGraph][PumpActivate] Totals Fluid={ToWholeGallons(totalAmountMg)}g/{ToWholeGallons(totalCapMg)}g InputCap={ToWholeGps(totalInputCapMgPerTick)}g/s OutputCap={ToWholeGps(totalOutputCapMgPerTick)}g/s ResolvedStorage={storageCountResolved}/{graph.StorageEndpoints.Count}");

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
        foreach (Vector3i pipe in graph.PipePositions)
            pipeLines.Add(pipe.ToString());

        pipeLines.Sort(StringComparer.Ordinal);
        Log.Out($"[FluidGraph][PumpActivate] PipeList={string.Join(", ", pipeLines)}");
        Log.Out($"[FluidGraph][PumpActivate] PumpDetails={string.Join(" || ", pumpLines)}");
        Log.Out($"[FluidGraph][PumpActivate] StorageDetails={string.Join(" || ", storageLines)}");
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
            return "[E] Probe Fluid Pump";

        string key =
            player.playerInput.Activate.GetBindingXuiMarkupString() +
            player.playerInput.PermanentActions.Activate.GetBindingXuiMarkupString();

        string name = blockValue.Block.GetLocalizedBlockName();
        return $"{key} Probe {name}";
    }

    private static void MarkAdjacentPipesDirty(WorldBase world, int clrIdx, Vector3i centerPos)
    {
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i neighborPos = centerPos + NeighborOffsets[i];
            if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, neighborPos, out TileEntity te) || !(te is TileEntityLiquidPipe pipe))
                continue;

            pipe.MarkFluidGraphDirty();
            pipe.setModified();
            FluidGraphManager.MarkPipeDirty(neighborPos);
        }
    }
}
