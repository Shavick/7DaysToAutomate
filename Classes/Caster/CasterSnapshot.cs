using System;
using System.Collections.Generic;

public class CasterSnapshot : IHLRSnapshot
{
    public string SnapshotKind => "Caster";
    public int SnapshotVersion => 2;

    public Guid MachineId { get; set; }
    public Vector3i Position { get; set; }
    public ulong WorldTime;
    public ulong LastHLRSimTime;

    public bool IsOn;

    public Vector3i SelectedOutputChestPos;
    public OutputTransportMode SelectedOutputMode;
    public Guid SelectedOutputPipeGraphId;
    public Vector3i SelectedOutputPipeAnchorPos;

    public string SelectedRecipeKey;
    public string SelectedFluidType;
    public Guid SelectedFluidGraphId;

    public bool IsProcessing;
    public int CycleTickCounter;
    public int CycleTickLength;
    public string ActiveRecipeKey;
    public string MachineRecipeGroupsCsv;

    public string PendingFluidInputType;
    public int PendingFluidInputAmountMg;
    public Dictionary<string, int> PendingOutputs = new Dictionary<string, int>(StringComparer.Ordinal);

    public string LastAction;
    public string LastBlockReason;
}
