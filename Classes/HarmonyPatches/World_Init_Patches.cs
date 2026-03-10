using HarmonyLib;

[HarmonyPatch(typeof(World))]
[HarmonyPatch("Init")]
public static class H_World_Init
{
    public static void Postfix(World __instance)
    {
        var hlr = WorldHLR.GetOrCreate(__instance);

        Log.Out("[HLR] Attached to World via ConditionalWeakTable");
    }
}
