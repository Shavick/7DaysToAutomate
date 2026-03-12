using System;
using System.Collections.Generic;
using System.Reflection;

public static class PipeProbeHudManager
{
    private const string WindowGroupName = "PipeProbeHud";
    private const int AutoProbeRequestIntervalMs = 250;

    private static bool isVisible;
    private static string activeTargetType = string.Empty;
    private static Vector3i activePos = Vector3i.zero;

    private static string title = string.Empty;
    private static readonly string[] lines = new string[6];
    private static long nextAutoProbeRequestAtMs;
    private static Vector3i lastAutoProbePos = Vector3i.zero;
    private static int lastAutoProbeClrIdx;
    private static bool hasAutoProbeTarget;

    public static bool IsVisible => isVisible;
    public static void UpdateAutoProbe(World world)
    {
        if (world == null)
            return;

        EntityPlayerLocal player = world.GetPrimaryPlayer() as EntityPlayerLocal;
        if (player == null)
            return;

        if (!IsHoldingPipeWrench(player))
        {
            hasAutoProbeTarget = false;
            Close(player);
            return;
        }

        if (!TryGetCurrentHitInfo(player, out WorldRayHitInfo hitInfo) || hitInfo == null || !hitInfo.bHitValid)
        {
            hasAutoProbeTarget = false;
            Close(player);
            return;
        }

        int clrIdx = hitInfo.hit.clrIdx;
        Vector3i blockPos = hitInfo.hit.blockPos;
        long nowMs = Environment.TickCount;
        bool changedTarget = !hasAutoProbeTarget || clrIdx != lastAutoProbeClrIdx || blockPos != lastAutoProbePos;
        if (!changedTarget && nowMs < nextAutoProbeRequestAtMs)
            return;

        hasAutoProbeTarget = true;
        lastAutoProbeClrIdx = clrIdx;
        lastAutoProbePos = blockPos;
        nextAutoProbeRequestAtMs = nowMs + AutoProbeRequestIntervalMs;
        Helper.RequestPipeProbeSnapshot(clrIdx, blockPos, player.entityId);
    }

    public static bool TryHandleUseAction(World world, EntityPlayerLocal player, WorldRayHitInfo hitInfo)
    {
        if (world == null || player == null)
            return false;

        if (!IsHoldingPipeWrench(player))
            return false;

        if (hitInfo == null || !hitInfo.bHitValid)
        {
            Close(player);
            return true;
        }

        Helper.RequestPipeProbeSnapshot(hitInfo.hit.clrIdx, hitInfo.hit.blockPos, player.entityId);
        return true;
    }

    public static bool TryHandleBlockProbe(WorldBase world, EntityPlayerLocal player, Vector3i blockPos, string targetType)
    {
        if (world == null || player == null || !world.IsRemote())
            return false;

        if (!IsHoldingPipeWrench(player))
            return false;

        if (isVisible && string.Equals(activeTargetType, targetType, StringComparison.OrdinalIgnoreCase))
        {
            Close(player);
            return true;
        }

        if (!BuildSnapshot(world, blockPos, targetType, out string snapshotTitle, out string[] snapshotLines))
            return true;

        title = snapshotTitle;
        for (int i = 0; i < lines.Length; i++)
            lines[i] = i < snapshotLines.Length ? snapshotLines[i] : string.Empty;

        activeTargetType = targetType ?? string.Empty;
        activePos = blockPos;

        Open(player);
        return true;
    }
    public static void ApplyServerSnapshot(EntityPlayerLocal player, string targetType, Vector3i blockPos, string snapshotTitle, string[] snapshotLines)
    {
        if (player == null)
            return;

        title = snapshotTitle ?? string.Empty;
        for (int i = 0; i < lines.Length; i++)
            lines[i] = (snapshotLines != null && i < snapshotLines.Length) ? (snapshotLines[i] ?? string.Empty) : string.Empty;

        activeTargetType = targetType ?? string.Empty;
        activePos = blockPos;

        Open(player);
    }

    public static bool TryBuildSnapshotServer(World world, int clrIdx, Vector3i blockPos, out string targetType, out string snapshotTitle, out string[] snapshotLines)
    {
        targetType = string.Empty;
        snapshotTitle = string.Empty;
        snapshotLines = new string[6];

        if (world == null)
            return false;

        if (!SafeWorldRead.TryGetTileEntity(world, clrIdx, blockPos, out TileEntity te) || te == null)
            return false;

        if (te is TileEntityLiquidPipe pipe)
        {
            targetType = "liquidpipe";
            return BuildPipeSnapshot(world, pipe, snapshotLines, out snapshotTitle);
        }

        if (te is TileEntityFluidPump pump)
        {
            targetType = "fluidpump";
            return BuildPumpSnapshot(world, blockPos, pump, snapshotLines, out snapshotTitle);
        }

        if (te is TileEntityFluidStorage storage)
        {
            targetType = "fluidstorage";
            return BuildStorageSnapshot(storage, snapshotLines, out snapshotTitle);
        }

        return false;
    }
    public static bool TryGetBindingValue(string bindingName, out string value)
    {
        value = string.Empty;

        if (string.IsNullOrEmpty(bindingName))
            return false;

        switch (bindingName)
        {
            case "probe_title":
                value = title;
                return true;
            case "probe_line1":
                value = lines[0] ?? string.Empty;
                return true;
            case "probe_line2":
                value = lines[1] ?? string.Empty;
                return true;
            case "probe_line3":
                value = lines[2] ?? string.Empty;
                return true;
            case "probe_line4":
                value = lines[3] ?? string.Empty;
                return true;
            case "probe_line5":
                value = lines[4] ?? string.Empty;
                return true;
            case "probe_line6":
                value = lines[5] ?? string.Empty;
                return true;
            default:
                return false;
        }
    }

    private static bool BuildSnapshot(WorldBase world, Vector3i pos, string targetType, out string snapshotTitle, out string[] snapshotLines)
    {
        snapshotTitle = "Pipe Probe";
        snapshotLines = new string[6];

        if (!SafeWorldRead.TryGetTileEntity(world, 0, pos, out TileEntity te) || te == null)
            return false;

        if (string.Equals(targetType, "liquidpipe", StringComparison.OrdinalIgnoreCase) && te is TileEntityLiquidPipe pipe)
            return BuildPipeSnapshot(world, pipe, snapshotLines, out snapshotTitle);

        if (string.Equals(targetType, "fluidpump", StringComparison.OrdinalIgnoreCase) && te is TileEntityFluidPump pump)
            return BuildPumpSnapshot(world, pos, pump, snapshotLines, out snapshotTitle);

        if (string.Equals(targetType, "fluidstorage", StringComparison.OrdinalIgnoreCase) && te is TileEntityFluidStorage storage)
            return BuildStorageSnapshot(storage, snapshotLines, out snapshotTitle);

        return false;
    }

    private static bool BuildPipeSnapshot(WorldBase world, TileEntityLiquidPipe pipe, string[] snapshotLines, out string snapshotTitle)
    {
        snapshotTitle = "Liquid Network";

        Guid graphId = pipe.FluidGraphId;
        if (graphId == Guid.Empty || !FluidGraphManager.TryGetGraph(graphId, out FluidGraphData graph) || graph == null)
        {
            snapshotLines[0] = "Graph: Unlinked";
            return true;
        }

        string fluid = string.IsNullOrEmpty(graph.FluidType) ? "Unassigned" : graph.FluidType;
        int totalMg = 0;
        int capMg = 0;

        foreach (Vector3i storagePos in graph.StorageEndpoints)
        {
            if (!SafeWorldRead.TryGetTileEntity(world, 0, storagePos, out TileEntity storageTe) || !(storageTe is TileEntityFluidStorage storage))
                continue;

            totalMg += storage.FluidAmountMg;
            capMg += storage.GetCapacityMg();
        }

        snapshotLines[0] = $"Fluid: {fluid}";
        snapshotLines[1] = $"Amount: {ToWholeGallons(totalMg)}g / {ToWholeGallons(capMg)}g";
        snapshotLines[2] = $"Pipes: {graph.PipePositions.Count}";
        snapshotLines[3] = $"Pumps: {graph.PumpEndpoints.Count}";
        snapshotLines[4] = $"Storage: {graph.StorageEndpoints.Count}";
        snapshotLines[5] = $"Blocked: {graph.LastBlockedReason} ({graph.LastBlockedCount})";
        return true;
    }

    private static bool BuildPumpSnapshot(WorldBase world, Vector3i pumpPos, TileEntityFluidPump pump, string[] snapshotLines, out string snapshotTitle)
    {
        snapshotTitle = "Fluid Pump";

        Guid graphId = FindAdjacentGraphId(world, pumpPos);
        string graphText = graphId == Guid.Empty ? "Unlinked" : graphId.ToString();

        snapshotLines[0] = $"State: {(pump.PumpEnabled ? "Active" : "Disabled")}";
        snapshotLines[1] = $"Output Cap: {ToWholeGallonsPerSecond(pump.GetOutputCapMgPerTick())} g/s";
        snapshotLines[2] = $"Graph: {graphText}";

        int line = 3;
        foreach (string ev in pump.GetRecentEventsSummary())
        {
            if (line >= snapshotLines.Length)
                break;

            snapshotLines[line++] = ev;
        }

        return true;
    }

    private static bool BuildStorageSnapshot(TileEntityFluidStorage storage, string[] snapshotLines, out string snapshotTitle)
    {
        snapshotTitle = "Fluid Storage";

        string fluid = string.IsNullOrEmpty(storage.FluidType) ? "Unassigned" : storage.FluidType;
        int amount = storage.FluidAmountMg;
        int cap = storage.GetCapacityMg();

        snapshotLines[0] = $"Fluid: {fluid}";
        snapshotLines[1] = $"Amount: {ToWholeGallons(amount)}g / {ToWholeGallons(cap)}g";
        snapshotLines[2] = $"Input Cap: {ToWholeGallonsPerSecond(storage.GetInputCapMgPerTick())} g/s";
        snapshotLines[3] = $"Output Cap: {ToWholeGallonsPerSecond(storage.GetOutputCapMgPerTick())} g/s";
        snapshotLines[4] = $"Free: {ToWholeGallons(storage.GetFreeSpaceMg())}g";
        snapshotLines[5] = string.Empty;
        return true;
    }

    private static Guid FindAdjacentGraphId(WorldBase world, Vector3i center)
    {
        Vector3i[] dirs =
        {
            Vector3i.forward,
            Vector3i.back,
            Vector3i.left,
            Vector3i.right,
            Vector3i.up,
            Vector3i.down
        };

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3i pos = center + dirs[i];
            if (!SafeWorldRead.TryGetTileEntity(world, 0, pos, out TileEntity te) || !(te is TileEntityLiquidPipe pipe))
                continue;

            if (pipe.FluidGraphId != Guid.Empty)
                return pipe.FluidGraphId;
        }

        return Guid.Empty;
    }

    private static int ToWholeGallons(int mg)
    {
        return (mg + (FluidConstants.MilliGallonsPerGallon / 2)) / FluidConstants.MilliGallonsPerGallon;
    }

    private static int ToWholeGallonsPerSecond(int mgPerTick)
    {
        int mgPerSec = mgPerTick * FluidConstants.SimulationTicksPerSecond;
        return ToWholeGallons(mgPerSec);
    }

    private static void Open(EntityPlayerLocal player)
    {
        if (player?.playerUI?.windowManager == null)
            return;

        if (!player.playerUI.windowManager.IsWindowOpen(WindowGroupName))
            player.playerUI.windowManager.Open(WindowGroupName, false, false, false);

        isVisible = true;
    }

    public static void Close(EntityPlayerLocal player)
    {
        if (player?.playerUI?.windowManager != null && player.playerUI.windowManager.IsWindowOpen(WindowGroupName))
            player.playerUI.windowManager.Close(WindowGroupName);

        isVisible = false;
        activeTargetType = string.Empty;
        activePos = Vector3i.zero;
    }

    private static bool TryGetCurrentHitInfo(EntityPlayerLocal player, out WorldRayHitInfo hitInfo)
    {
        hitInfo = null;
        if (player == null)
            return false;

        Type playerType = player.GetType();

        MethodInfo getterMethod = playerType.GetMethod("GetHitInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (getterMethod != null)
        {
            object methodValue = getterMethod.Invoke(player, null);
            if (methodValue is WorldRayHitInfo byMethod)
            {
                hitInfo = byMethod;
                return true;
            }
        }

        PropertyInfo hitInfoProperty = playerType.GetProperty("HitInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (hitInfoProperty != null)
        {
            object propertyValue = hitInfoProperty.GetValue(player, null);
            if (propertyValue is WorldRayHitInfo byProperty)
            {
                hitInfo = byProperty;
                return true;
            }
        }

        FieldInfo hitInfoField = playerType.GetField("hitInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (hitInfoField != null)
        {
            object fieldValue = hitInfoField.GetValue(player);
            if (fieldValue is WorldRayHitInfo byField)
            {
                hitInfo = byField;
                return true;
            }
        }

        return false;
    }

    public static bool IsHoldingPipeWrench(EntityPlayerLocal player)
    {
        if (player == null)
            return false;

        string itemName = GetHeldItemName(player);
        if (string.IsNullOrEmpty(itemName))
            return false;

        return string.Equals(itemName, "pipeWrench", StringComparison.OrdinalIgnoreCase)
            || string.Equals(itemName, "meleeToolWrench", StringComparison.OrdinalIgnoreCase)
            || string.Equals(itemName, "itemPipeProbe", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetHeldItemName(EntityPlayerLocal player)
    {
        object inventory = player.inventory;
        if (inventory == null)
            return string.Empty;

        ItemValue heldItem = default;
        bool found = false;

        MethodInfo getHoldingItem = inventory.GetType().GetMethod("GetHoldingItemItemValue", BindingFlags.Public | BindingFlags.Instance);
        if (getHoldingItem != null)
        {
            object result = getHoldingItem.Invoke(inventory, null);
            if (result is ItemValue itemValue)
            {
                heldItem = itemValue;
                found = true;
            }
        }

        if (!found)
        {
            FieldInfo field = inventory.GetType().GetField("holdingItemItemValue", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                object result = field.GetValue(inventory);
                if (result is ItemValue itemValue)
                {
                    heldItem = itemValue;
                    found = true;
                }
            }
        }

        if (!found || heldItem.ItemClass == null)
            return string.Empty;

        return heldItem.ItemClass.GetItemName() ?? string.Empty;
    }
}












