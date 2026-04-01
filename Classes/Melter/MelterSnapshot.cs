using System;

public class MelterSnapshot : DecanterSnapshot
{
    public override string SnapshotKind => "Melter";
    public override int SnapshotVersion => 6;

    public int CurrentHeat;
    public int CurrentHeatSourceMax;
}
