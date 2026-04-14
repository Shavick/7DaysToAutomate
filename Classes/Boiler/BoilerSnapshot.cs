using System;

public class BoilerSnapshot : IHLRSnapshot
{
    public string SnapshotKind => "Boiler";
    public int SnapshotVersion => 1;

    public Guid MachineId { get; set; }
    public Vector3i Position { get; set; }
    public ulong WorldTime;
    public ulong LastHLRSimTime;

    public bool IsOn;
    public string SelectedRecipeKey;
    public string SelectedFluidType;
    public Guid SelectedFluidGraphId;

    public bool IsProcessing;
    public int CycleTickCounter;
    public int CycleTickLength;
    public string ActiveRecipeKey;

    public string PendingFluidInputAType;
    public int PendingFluidInputAAmountMg;
    public string PendingFluidInputBType;
    public int PendingFluidInputBAmountMg;

    public string PendingFluidOutputType;
    public int PendingFluidOutput;
    public int PendingFluidOutputCapacityMg;
    public int CurrentHeat;
    public int CurrentHeatSourceMax;

    public string MachineRecipeGroupsCsv;
    public string LastAction;
    public string LastBlockReason;
}
