using HarmonyLib;

[HarmonyPatch(typeof(Block))]
[HarmonyPatch("OnBlockActivated",
    new[] {
        typeof(WorldBase),
        typeof(int),
        typeof(Vector3i),
        typeof(BlockValue),
        typeof(EntityPlayerLocal)
    })]
public class Patch_Block_OnBlockActivated
{
    static bool Prefix(Block __instance, WorldBase _world, int _clrIdx, Vector3i _blockPos,
                       BlockValue _blockValue, EntityPlayerLocal _player, ref bool __result)
    {
        if (_world.IsRemote()) return true;

        var te = _world.GetTileEntity(_blockPos);

        if (te == null)
            return true; // Not our block → run vanilla

        if (te is TileEntityUniversalExtractor extractor)
        {
            Log.Out("[ExtractorHarmony] Activation → EXTRACTOR");
            XUiC_IronExtractorInfo.Open(_player, _blockPos);
            __result = true;
            return false;
        }

        if (te is TileEntityUniversalCrafter crafter)
        {
            Log.Out("[ExtractorHarmony] Activation → CRAFTER");
            XUiC_UniversalCrafter.Open(_player, _blockPos);
            __result = true;
            return false;
        }


        return true;
    }
}
