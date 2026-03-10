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

    public string ItemName;
    public int ItemCount;

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

        if (routePipePositions != null)
            RoutePipePositions.AddRange(routePipePositions);
    }

    public bool HasValidGraphId()
    {
        return PipeGraphId != Guid.Empty;
    }

    public bool HasValidItem()
    {
        return !string.IsNullOrEmpty(ItemName) && ItemCount > 0;
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
        return $"JobId={JobId} PipeGraphId={PipeGraphId} Direction={Direction} TargetType={TargetType} Source={SourcePos} Target={TargetPos} Item={ItemName} Count={ItemCount} RouteLen={RoutePipePositions.Count} Start={StartWorldTime} Arrival={ArrivalWorldTime} Complete={IsComplete} Failed={IsFailed}";
    }
}