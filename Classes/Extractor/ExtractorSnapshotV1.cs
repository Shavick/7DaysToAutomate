using System;
using System.Collections.Generic;

public class ExtractorSnapshotV1 : IHLRSnapshot
{
    public string SnapshotKind => "Extractor";
    public bool IsPhantom;
    public ulong LastHLRSimTime;
    int IHLRSnapshot.SnapshotVersion => 5;
    Guid IHLRSnapshot.MachineId
    {
        get => MachineId;
        set => MachineId = value;
    }

    Vector3i IHLRSnapshot.Position
    {
        get => Position;
        set => Position = value;
    }

    public ulong WorldTime;
    public bool IsOn;
    public bool IsEnabledByPlayer;
    public Guid MachineId;
    public Vector3i Position;

    // Pipe graph context for HLR push planning.
    public Vector3i SelectedOutputChestPos;
    public OutputTransportMode SelectedOutputMode;
    public Guid SelectedOutputPipeGraphId;
    public Vector3i SelectedOutputPipeAnchorPos;

    // Production State
    public List<TileEntityUniversalExtractor.ResourceTimer> Timers;

    // Production Accounting
    public Dictionary<string, int> OwedResources;
}
