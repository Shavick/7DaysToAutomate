using System;

public interface IHLRSnapshot
{

    // Identifies which simulator / machine type owns this snapshot
    string SnapshotKind { get; }

    // Versioning for safe future upgrades
    int SnapshotVersion { get; }

    Guid MachineId { get; set; }
    Vector3i Position { get; set; }
}