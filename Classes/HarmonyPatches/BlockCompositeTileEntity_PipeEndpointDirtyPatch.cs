using HarmonyLib;

[HarmonyPatch(typeof(BlockCompositeTileEntity))]
public static class BlockCompositeTileEntity_PipeEndpointDirtyPatch
{
    private static readonly Vector3i[] NeighborOffsets =
    {
        Vector3i.forward,
        Vector3i.back,
        Vector3i.left,
        Vector3i.right,
        Vector3i.up,
        Vector3i.down
    };

    [HarmonyPostfix]
    [HarmonyPatch("OnBlockAdded")]
    public static void OnBlockAdded_Postfix(
        WorldBase _world,
        Chunk _chunk,
        Vector3i _blockPos,
        BlockValue _blockValue,
        PlatformUserIdentifierAbs _addedByPlayer)
    {
        if (_world == null || _world.IsRemote() || _blockValue.ischild)
            return;

        if (!HasStorageFeature(_world, _chunk.ClrIdx, _blockPos))
            return;

        MarkAdjacentPipesDirty(_world, _chunk.ClrIdx, _blockPos);
    }

    [HarmonyPrefix]
    [HarmonyPatch("OnBlockRemoved")]
    public static void OnBlockRemoved_Prefix(
        WorldBase _world,
        Chunk _chunk,
        Vector3i _blockPos,
        BlockValue _blockValue)
    {
        if (_world == null || _world.IsRemote() || _blockValue.ischild)
            return;

        if (!HasStorageFeature(_world, _chunk.ClrIdx, _blockPos))
            return;

        MarkAdjacentPipesDirty(_world, _chunk.ClrIdx, _blockPos);
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnBlockLoaded")]
    public static void OnBlockLoaded_Postfix(
        WorldBase _world,
        int _clrIdx,
        Vector3i _blockPos,
        BlockValue _blockValue)
    {
        if (_world == null || _world.IsRemote() || _blockValue.ischild)
            return;

        if (!HasStorageFeature(_world, _clrIdx, _blockPos))
            return;

        PipeGraphManager.TryApplyStorageSnapshotForPosition(_world, _clrIdx, _blockPos);
    }

    [HarmonyPrefix]
    [HarmonyPatch("OnBlockUnloaded")]
    public static void OnBlockUnloaded_Prefix(
        WorldBase _world,
        int _clrIdx,
        Vector3i _blockPos,
        BlockValue _blockValue)
    {
        if (_world == null || _world.IsRemote() || _blockValue.ischild)
            return;

        if (!HasStorageFeature(_world, _clrIdx, _blockPos))
            return;

        PipeGraphManager.CaptureStorageSnapshotForPosition(_world, _clrIdx, _blockPos);
    }

    private static bool HasStorageFeature(WorldBase world, int clrIdx, Vector3i blockPos)
    {
        var composite = world.GetTileEntity(clrIdx, blockPos) as TileEntityComposite;
        if (composite == null)
            return false;

        TEFeatureStorage storage = composite.GetFeature<TEFeatureStorage>();
        return storage != null && storage.items != null;
    }

    private static void MarkAdjacentPipesDirty(WorldBase world, int clrIdx, Vector3i centerPos)
    {
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            Vector3i neighborPos = centerPos + NeighborOffsets[i];
            var pipeTe = world.GetTileEntity(clrIdx, neighborPos) as TileEntityItemPipe;
            if (pipeTe == null)
                continue;

            pipeTe.MarkPipeGraphDirty();
            pipeTe.MarkNetworkDirty();
            pipeTe.setModified();

            PipeGraphManager.MarkPipeDirty(neighborPos);
        }
    }
}
