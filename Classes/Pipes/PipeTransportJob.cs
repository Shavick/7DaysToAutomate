using System;
using System.Collections.Generic;

public enum PipeTransportJobDirection
{
    MachineToStorage = 0,
    StorageToMachine = 1
}

public enum PipeTransportJobTargetType
{
    Storage = 0,
    Machine = 1
}

public class PipeTransportJob
{
    public Guid JobId;
    public Guid PipeGraphId;

    public bool HasPickedUpItems;
    public ulong TransitStartWorldTime;

    public PipeTransportJobDirection Direction;
    public PipeTransportJobTargetType TargetType;

    public Vector3i SourcePos;
    public Vector3i TargetPos;

    // Legacy single-item fields are kept for compatibility and machine->storage jobs.
    public string ItemName;
    public int ItemCount;

    // Packet payload for mixed-ingredient jobs.
    public readonly Dictionary<string, int> PacketItemCounts = new Dictionary<string, int>();

    public readonly List<Vector3i> RoutePipePositions = new List<Vector3i>();

    public ulong StartWorldTime;
    public ulong ArrivalWorldTime;

    public bool IsComplete;
    public bool IsFailed;

    public PipeTransportJob()
    {
        JobId = Guid.NewGuid();
    }

    public PipeTransportJob(
        Guid pipeGraphId,
        PipeTransportJobDirection direction,
        PipeTransportJobTargetType targetType,
        Vector3i sourcePos,
        Vector3i targetPos,
        string itemName,
        int itemCount,
        IEnumerable<Vector3i> routePipePositions,
        ulong startWorldTime,
        ulong arrivalWorldTime)
    {
        JobId = Guid.NewGuid();
        PipeGraphId = pipeGraphId;
        Direction = direction;
        TargetType = targetType;
        SourcePos = sourcePos;
        TargetPos = targetPos;
        ItemName = itemName ?? string.Empty;
        ItemCount = itemCount;
        StartWorldTime = startWorldTime;
        ArrivalWorldTime = arrivalWorldTime;

        if (!string.IsNullOrEmpty(ItemName) && ItemCount > 0)
            PacketItemCounts[ItemName] = ItemCount;

        if (routePipePositions != null)
            RoutePipePositions.AddRange(routePipePositions);
    }

    public PipeTransportJob(
        Guid pipeGraphId,
        PipeTransportJobDirection direction,
        PipeTransportJobTargetType targetType,
        Vector3i sourcePos,
        Vector3i targetPos,
        Dictionary<string, int> packetItemCounts,
        IEnumerable<Vector3i> routePipePositions,
        ulong startWorldTime,
        ulong arrivalWorldTime)
    {
        JobId = Guid.NewGuid();
        PipeGraphId = pipeGraphId;
        Direction = direction;
        TargetType = targetType;
        SourcePos = sourcePos;
        TargetPos = targetPos;
        StartWorldTime = startWorldTime;
        ArrivalWorldTime = arrivalWorldTime;

        SetPacketItemCounts(packetItemCounts);

        if (routePipePositions != null)
            RoutePipePositions.AddRange(routePipePositions);
    }

    public bool HasValidGraphId()
    {
        return PipeGraphId != Guid.Empty;
    }

    public bool HasValidItem()
    {
        return GetTotalItemCount() > 0;
    }

    public int GetTotalItemCount()
    {
        int total = 0;

        foreach (var kvp in PacketItemCounts)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            total += kvp.Value;
        }

        if (total > 0)
            return total;

        return !string.IsNullOrEmpty(ItemName) && ItemCount > 0 ? ItemCount : 0;
    }

    public int GetItemCount(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return 0;

        if (PacketItemCounts.TryGetValue(itemName, out int packetCount) && packetCount > 0)
            return packetCount;

        if (string.Equals(ItemName, itemName, StringComparison.Ordinal) && ItemCount > 0)
            return ItemCount;

        return 0;
    }

    public Dictionary<string, int> GetPacketItemCountsCopy()
    {
        Dictionary<string, int> copy = new Dictionary<string, int>();

        foreach (var kvp in PacketItemCounts)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            copy[kvp.Key] = kvp.Value;
        }

        if (copy.Count == 0 && !string.IsNullOrEmpty(ItemName) && ItemCount > 0)
            copy[ItemName] = ItemCount;

        return copy;
    }

    public void SetPacketItemCounts(Dictionary<string, int> packetItemCounts)
    {
        PacketItemCounts.Clear();

        if (packetItemCounts != null)
        {
            foreach (var kvp in packetItemCounts)
            {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                    continue;

                PacketItemCounts[kvp.Key] = kvp.Value;
            }
        }

        ItemName = string.Empty;
        ItemCount = 0;

        foreach (var kvp in PacketItemCounts)
        {
            ItemName = kvp.Key;
            ItemCount = kvp.Value;
            break;
        }
    }

    public bool HasValidEndpoints()
    {
        return SourcePos != Vector3i.zero && TargetPos != Vector3i.zero;
    }

    public bool HasRoute()
    {
        return RoutePipePositions.Count > 0;
    }

    public bool IsReadyToArrive(ulong worldTime)
    {
        return !IsComplete &&
               !IsFailed &&
               HasPickedUpItems &&
               worldTime >= ArrivalWorldTime;
    }

    public bool IsWaitingForPickup()
    {
        return !IsComplete && !IsFailed && !HasPickedUpItems;
    }

    public bool UsesPipe(Vector3i pipePos)
    {
        return RoutePipePositions.Contains(pipePos);
    }

    public bool IsMachineToStorage()
    {
        return Direction == PipeTransportJobDirection.MachineToStorage &&
               TargetType == PipeTransportJobTargetType.Storage;
    }

    public bool IsStorageToMachine()
    {
        return Direction == PipeTransportJobDirection.StorageToMachine &&
               TargetType == PipeTransportJobTargetType.Machine;
    }

    public override string ToString()
    {
        return $"JobId={JobId} PipeGraphId={PipeGraphId} Direction={Direction} TargetType={TargetType} Source={SourcePos} Target={TargetPos} Items={GetTotalItemCount()} Types={PacketItemCounts.Count} RouteLen={RoutePipePositions.Count} Start={StartWorldTime} Arrival={ArrivalWorldTime} Complete={IsComplete} Failed={IsFailed}";
    }
}
