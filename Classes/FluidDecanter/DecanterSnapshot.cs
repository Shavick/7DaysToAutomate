using System;

public class DecanterSnapshot : IHLRSnapshot
{
    public string SnapshotKind => "Decanter";
    public int SnapshotVersion => 2;

    public Guid MachineId { get; set; }
    public Vector3i Position { get; set; }
    public ulong WorldTime;
    public ulong LastHLRSimTime;

    public bool IsOn;

    public Vector3i SelectedInputChestPos;
    public Guid SelectedInputPipeGraphId;

    public Vector3i SelectedOutputChestPos;
    public OutputTransportMode SelectedOutputMode;
    public Guid SelectedOutputPipeGraphId;

    public string SelectedFluidType;
    public Guid SelectedFluidGraphId;

    public int PendingItemInput;
    public int PendingItemOutput;
    public int PendingFluidInput;
    public int PendingFluidOutput;

    public string PendingItemInputName;
    public int PendingItemInputFluidAmountMg;
    public string PendingItemInputReturnItemName;
    public string PendingItemOutputName;

    public int CycleTickCounter;
    public int CycleTickLength;
    public int PendingFluidOutputCapacityMg;

    public string LastAction;
    public string LastBlockReason;
}
