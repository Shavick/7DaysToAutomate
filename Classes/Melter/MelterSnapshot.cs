using System;

public class MelterSnapshot : DecanterSnapshot
{
    public override string SnapshotKind => "Melter";
    public override int SnapshotVersion => 1;

    public int CurrentHeat;
    public int CurrentHeatSourceMax;
}
