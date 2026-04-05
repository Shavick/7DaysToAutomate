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
        switch ((int)type)
        {
            case 132:
                __result = new TileEntityUniversalExtractor(_chunk);
                return false;
            case 133:
                __result = new TileEntityUniversalCrafter(_chunk);
                return false;
            case 134:
                __result = new TileEntityUniversalWasher(_chunk);
                return false;
            case 135:
                __result = new TileEntityNetworkController(_chunk);
                return false;
            case 136:
                __result = new TileEntityItemPipe(_chunk);
                return false;
            case 137:
                __result = new TileEntityLiquidPipe(_chunk);
                return false;
            case 138:
                __result = new TileEntityFluidPump(_chunk);
                return false;
            case 139:
                __result = new TileEntityFluidStorage(_chunk);
                return false;
            case 140:
                __result = new TileEntityFluidDecanter(_chunk);
                return false;
            case 141:
                __result = new TileEntityFluidInfuser(_chunk);
                return false;
            case 142:
                __result = new TileEntityMelter(_chunk);
                return false;
            case 143:
                __result = new TileEntityFluidMixer(_chunk);
                return false;
            case 144:
                __result = new TileEntityCaster(_chunk);
                return false;
            case 145:
                __result = new TileEntityBoiler(_chunk);
                return false;
            default:
                return true;
        }
    }
}
