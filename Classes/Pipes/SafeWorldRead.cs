using System;
using System.Collections.Generic;

public static class SafeWorldRead
{
    public static bool TryGetTileEntity(WorldBase world, int clrIdx, Vector3i pos, out TileEntity tileEntity)
    {
        tileEntity = null;
        if (world == null)
            return false;

        try
        {
            tileEntity = world.GetTileEntity(clrIdx, pos);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[SafeWorldRead] GetTileEntity(clrIdx={clrIdx}, pos={pos}) failed: {ex.Message}");
            return false;
        }
    }

    public static bool TryGetTileEntity(WorldBase world, Vector3i pos, out TileEntity tileEntity)
    {
        tileEntity = null;
        if (world == null)
            return false;

        try
        {
            tileEntity = world.GetTileEntity(pos);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[SafeWorldRead] GetTileEntity(pos={pos}) failed: {ex.Message}");
            return false;
        }
    }

    public static bool TryGetBlock(WorldBase world, int clrIdx, Vector3i pos, out BlockValue blockValue)
    {
        blockValue = default(BlockValue);
        if (world == null)
            return false;

        try
        {
            blockValue = world.GetBlock(clrIdx, pos);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[SafeWorldRead] GetBlock(clrIdx={clrIdx}, pos={pos}) failed: {ex.Message}");
            return false;
        }
    }

    public static bool TryGetBlock(WorldBase world, Vector3i pos, out BlockValue blockValue)
    {
        blockValue = default(BlockValue);
        if (world == null)
            return false;

        try
        {
            blockValue = world.GetBlock(pos);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[SafeWorldRead] GetBlock(pos={pos}) failed: {ex.Message}");
            return false;
        }
    }

    public static List<Chunk> GetChunkArraySnapshot(WorldBase world)
    {
        var chunks = new List<Chunk>();
        if (world == null || world.ChunkClusters == null || world.ChunkClusters.Count == 0 || world.ChunkClusters[0] == null)
            return chunks;

        try
        {
            foreach (Chunk chunk in world.ChunkClusters[0].GetChunkArray())
            {
                chunks.Add(chunk);
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[SafeWorldRead] GetChunkArray snapshot failed: {ex.Message}");
        }

        return chunks;
    }
}
