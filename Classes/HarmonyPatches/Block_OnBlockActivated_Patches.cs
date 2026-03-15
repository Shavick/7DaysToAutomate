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
        if (_world == null || _world.IsRemote() || _player == null)
            return true;

        var te = _world.GetTileEntity(_clrIdx, _blockPos);

        if (te == null)
            return true; // Not our block → run vanilla

        if (te is TileEntityUniversalExtractor)
        {
            Log.Out("[ExtractorHarmony] Activation → EXTRACTOR");
            Helper.RequestMachineUIOpen(_clrIdx, _blockPos, _player.entityId, "ExtractorInfo");
            __result = true;
            return false;
        }

        if (te is TileEntityUniversalCrafter)
        {
            Log.Out("[ExtractorHarmony] Activation → CRAFTER");
            Helper.RequestMachineUIOpen(_clrIdx, _blockPos, _player.entityId, "CrafterInfo");
            __result = true;
            return false;
        }


        return true;
    }
}
