using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

[HarmonyPatch]
public static class XUiC_CraftingListInfo_MachineRecipeButton_Patch
{
    private static readonly HashSet<int> HookedControllers = new HashSet<int>();

    private static MethodBase TargetMethod()
    {
        Type craftingListInfoType = AccessTools.TypeByName("XUiC_CraftingListInfo");
        if (craftingListInfoType == null)
            return null;

        return AccessTools.Method(craftingListInfoType, "Init");
    }

    private static void Postfix(object __instance)
    {
        if (!(__instance is XUiController controller))
            return;

        int controllerId = controller.GetHashCode();
        if (!HookedControllers.Add(controllerId))
            return;

        XUiV_Button button = controller.GetChildById("machineRecipeCodexButton")?.ViewComponent as XUiV_Button;
        if (button == null)
            return;

        button.Controller.OnPress += (c, m) =>
        {
            EntityPlayerLocal player = controller.xui?.playerUI?.entityPlayer as EntityPlayerLocal;
            if (player?.playerUI?.windowManager == null)
            {
                World world = GameManager.Instance?.World;
                player = world?.GetPrimaryPlayer() as EntityPlayerLocal;
            }

            if (player?.playerUI?.windowManager == null)
                return;

            XUiC_MachineRecipeCodex.Open(player);
        };
    }
}
