using System;
using System.Collections.Generic;

public class GrinderSnapshot : IHLRSnapshot
{
    public string SnapshotKind => "UniversalGrinder";
    public int SnapshotVersion => 1;

    public Guid MachineId { get; set; }
    public Vector3i Position { get; set; }

    public ulong WorldTime;
    public ulong LastHLRSimTime;

    public bool IsOn;

    public Vector3i SelectedInputChestPos;
    public Guid SelectedInputPipeGraphId;
    public Vector3i SelectedInputPipeAnchorPos;

    public Vector3i SelectedOutputChestPos;
    public OutputTransportMode SelectedOutputMode;
    public Guid SelectedOutputPipeGraphId;
    public Vector3i SelectedOutputPipeAnchorPos;

    public bool ProcessItemArmorMods;
    public float EffectiveReturnRate;
    public int BaseBatchSize;
    public int MaxPendingOutput;
    public string AcceptedRecipeBenchesCsv;
    public string BlockedRecipeBenchesCsv;

    public bool IsProcessing;
    public int CycleTickCounter;
    public int CycleTickLength;
    public int ActiveBatchSize;
    public string ActiveItemName;

    public long ItemsProcessed;

    public Dictionary<string, int> PendingOutputs = new Dictionary<string, int>(StringComparer.Ordinal);
    public Dictionary<string, int> ActiveCycleOutputs = new Dictionary<string, int>(StringComparer.Ordinal);

    public bool IsFuelEnabled;
    public string FuelType;
    public int FuelBufferMg;
    public int FuelCapacityMg;
    public int FuelUsePerSecondMg;
    public int FuelPullPerSecondMg;
    public Guid SelectedFuelGraphId;
    public int FuelUseRemainder;
    public int FuelPullRemainder;

    public string LastAction;
    public string LastBlockReason;
}
