using System;

public sealed class NetworkStorageContainerRef
{
    public Vector3i StoragePos;
    public Guid PipeGraphId;
    public int StoragePriority;
    public int StorageListIndex;

    public NetworkStorageContainerRef(Guid pipeGraphId, Vector3i storagePos, int storagePriority, int storageListIndex)
    {
        PipeGraphId = pipeGraphId;
        StoragePos = storagePos;
        StoragePriority = storagePriority;
        StorageListIndex = storageListIndex;
    }
}