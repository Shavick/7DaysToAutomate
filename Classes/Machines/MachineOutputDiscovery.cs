using System;
using System.Collections.Generic;

public static class MachineOutputDiscovery
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

    public static List<OutputTargetInfo> GetAvailableOutputs(WorldBase world, int clrIdx, Vector3i machinePos, int maxResults = 8)
    {
        var results = new List<OutputTargetInfo>();
        var targetIndexByPos = new Dictionary<Vector3i, int>();

        if (world == null || maxResults <= 0)
            return results;

        for (int i = 0; i < NeighborOffsets.Length && results.Count < maxResults; i++)
        {
            Vector3i neighborPos = machinePos + NeighborOffsets[i];

            // Adjacent storage
            if (TryAddAdjacentStorage(world, clrIdx, neighborPos, results, targetIndexByPos, maxResults))
                continue;

            // Adjacent pipe -> graph endpoints
            TryAddPipeEndpoints(world, clrIdx, neighborPos, results, targetIndexByPos, maxResults);
        }

        return results;
    }

    private static bool TryAddAdjacentStorage(
        WorldBase world,
        int clrIdx,
        Vector3i pos,
        List<OutputTargetInfo> results,
        Dictionary<Vector3i, int> targetIndexByPos,
        int maxResults)
    {
        if (!(world.GetTileEntity(clrIdx, pos) is TileEntityComposite composite))
            return false;

        TEFeatureStorage storage = composite.GetFeature<TEFeatureStorage>();
        if (storage == null || storage.items == null)
            return false;

        if (targetIndexByPos.TryGetValue(pos, out int existingIndex))
        {
            if (existingIndex >= 0 &&
                existingIndex < results.Count &&
                results[existingIndex] != null &&
                results[existingIndex].TransportMode != OutputTransportMode.Adjacent)
            {
                results[existingIndex] = new OutputTargetInfo(pos, OutputTransportMode.Adjacent);
            }

            return true;
        }

        if (results.Count >= maxResults)
            return true;

        targetIndexByPos[pos] = results.Count;
        results.Add(new OutputTargetInfo(pos, OutputTransportMode.Adjacent));
        return true;
    }

    private static void TryAddPipeEndpoints(
    WorldBase world,
    int clrIdx,
    Vector3i pipePos,
    List<OutputTargetInfo> results,
    Dictionary<Vector3i, int> targetIndexByPos,
    int maxResults)
    {
        if (results.Count >= maxResults)
            return;

        if (!(world.GetTileEntity(clrIdx, pipePos) is TileEntityItemPipe pipeTe))
            return;


        if (!PipeGraphManager.TryGetStorageEndpointsForPipe(world, clrIdx, pipePos, out List<Vector3i> endpoints))
        {
            return;
        }


        Guid pipeGraphId = pipeTe.PipeGraphId;

        for (int i = 0; i < endpoints.Count && results.Count < maxResults; i++)
        {
            Vector3i endpointPos = endpoints[i];
            if (targetIndexByPos.ContainsKey(endpointPos))
                continue;

            if (!(world.GetTileEntity(clrIdx, endpointPos) is TileEntityComposite endpointComposite))
                continue;

            TEFeatureStorage endpointStorage = endpointComposite.GetFeature<TEFeatureStorage>();
            if (endpointStorage == null || endpointStorage.items == null)
                continue;

            targetIndexByPos[endpointPos] = results.Count;
            results.Add(new OutputTargetInfo(endpointPos, OutputTransportMode.Pipe, pipeGraphId));
        }
    }
}

