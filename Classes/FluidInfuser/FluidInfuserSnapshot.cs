using System;
using System.Collections.Generic;

public class FluidInfuserSnapshot : IHLRSnapshot
{
    public string SnapshotKind => "FluidInfuser";
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

    public string SelectedRecipeKey;
    public string SelectedFluidType;
    public Guid SelectedFluidGraphId;

    public bool IsProcessing;
    public int CycleTickCounter;
    public int CycleTickLength;
    public string ActiveRecipeKey;
    public string MachineRecipeGroupsCsv;

    public Dictionary<string, int> PendingInputs = new Dictionary<string, int>();
    public string PendingFluidInputType;
    public int PendingFluidInputAmountMg;
    public Dictionary<string, int> PendingOutputs = new Dictionary<string, int>();

    public string LastAction;
    public string LastBlockReason;
}
