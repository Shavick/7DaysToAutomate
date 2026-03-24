using HarmonyLib;
using System;
using System.Reflection;

[HarmonyPatch]
public static class UCPatch_TileEntity_Instantiate
{
    static MethodBase TargetMethod()
    {

        var m = AccessTools.Method(
            typeof(TileEntity),
            "Instantiate",
            new Type[] { typeof(TileEntityType), typeof(Chunk) }
        );

        return m;
    }

    static bool Prefix(TileEntityType type, Chunk _chunk, ref TileEntity __result)
    {
        int tid = (int)type;

        if (tid == 132)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=132 -> TileEntityUniversalExtractor");
            __result = new TileEntityUniversalExtractor(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 133)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=133 -> TileEntityUniversalCrafter");
            __result = new TileEntityUniversalCrafter(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 134)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=134 -> TileEntityUniversalWasher");
            __result = new TileEntityUniversalWasher(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 135)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=135 -> TileEntityNetworkController");
            __result = new TileEntityNetworkController(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 136)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=136 -> TileEntityItemPipe");
            __result = new TileEntityItemPipe(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }


        if (tid == 137)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=137 -> TileEntityLiquidPipe");
            __result = new TileEntityLiquidPipe(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 138)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=138 -> TileEntityFluidPump");
            __result = new TileEntityFluidPump(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 139)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=139 -> TileEntityFluidStorage");
            __result = new TileEntityFluidStorage(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 140)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=140 -> TileEntityFluidDecanter");
            __result = new TileEntityFluidDecanter(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 141)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=141 -> TileEntityFluidInfuser");
            __result = new TileEntityFluidInfuser(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 142)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=142 -> TileEntityMelter");
            __result = new TileEntityMelter(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }

        if (tid == 143)
        {
            Log.Out("[UCPatch][TE.Instantiate] Handling tid=143 -> TileEntityFluidMixer");
            __result = new TileEntityFluidMixer(_chunk);
            Log.Out($"[UCPatch][TE.Instantiate] Created __result={(__result == null ? "NULL" : __result.GetType().Name)} | returning false (skip vanilla)");
            return false;
        }
        return true;
    }
}


