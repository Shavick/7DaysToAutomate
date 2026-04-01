using System;

public class DecanterSnapshot : IHLRSnapshot
{
    public virtual string SnapshotKind => "Decanter";
    public virtual int SnapshotVersion => 6;

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

    public string SelectedFluidType;
    public string SelectedRecipeKey;
    public string MachineRecipeGroupsCsv;
    public Guid SelectedFluidGraphId;

    public int PendingItemInput;
    public int PendingItemOutput;
    public int PendingFluidInput;
    public int PendingFluidOutput;

    public string PendingItemInputName;
    public int PendingItemInputFluidAmountMg;
    public string PendingItemInputReturnItemName;
    public int PendingItemInputReturnItemAmount = 1;
    public string PendingItemOutputName;

    public int CycleTickCounter;
    public int CycleTickLength;
    public int PendingFluidOutputCapacityMg;

    public string LastAction;
    public string LastBlockReason;
}
