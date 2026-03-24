using System;
using System.Globalization;
using UnityEngine;

public class XUiC_MelterInfo : XUiController
{
    private Vector3i blockPosition;
    private TileEntityMelter te;
    private XUiC_MelterInputContainerList inputList;

    public override void Init()
    {
        base.Init();

        var closeBtn = GetChildById("closeButton")?.ViewComponent as XUiV_Button;
        if (closeBtn != null)
            closeBtn.Controller.OnPress += (c, b) => xui.playerUI.windowManager.Close("MelterInfo");

        var recipePrevBtn = GetChildById("recipeprevbutton")?.ViewComponent as XUiV_Button;
        if (recipePrevBtn != null)
            recipePrevBtn.Controller.OnPress += (c, b) => Helper.RequestMelterCycleRecipe(blockPosition, -1);

        var recipeNextBtn = GetChildById("recipenextbutton")?.ViewComponent as XUiV_Button;
        if (recipeNextBtn != null)
            recipeNextBtn.Controller.OnPress += (c, b) => Helper.RequestMelterCycleRecipe(blockPosition, 1);

        inputList = GetChildByType<XUiC_MelterInputContainerList>();
    }

    public static void Open(EntityPlayerLocal player, Vector3i pos)
    {
        if (player?.playerUI == null)
            return;

        var ctrl = player.playerUI.xui?.GetChildByType<XUiC_MelterInfo>();
        if (ctrl != null)
            ctrl.blockPosition = pos;

        player.playerUI.windowManager.Open("MelterInfo", true, false, true);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        te = GetTileEntity();
        EnsureListContext();
        RefreshBindings(true);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (te == null)
        {
            te = GetTileEntity();
            EnsureListContext();
        }

        if (te == null || !te.NeedsUiRefresh)
            return;

        te.NeedsUiRefresh = false;
        EnsureListContext();

        if (inputList != null)
            inputList.IsDirty = true;

        RefreshBindings(true);
    }

    private TileEntityMelter GetTileEntity()
    {
        if (blockPosition == default || GameManager.Instance?.World == null)
            return null;

        return GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityMelter;
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        TileEntityMelter melter = GetTileEntity();
        WorldBase world = GameManager.Instance?.World;

        switch (bindingName?.Trim() ?? string.Empty)
        {
            case "machinename":
                value = melter?.blockValue.Block?.GetLocalizedBlockName() ?? "Melter";
                return true;
            case "selectedfluid":
                value = string.IsNullOrEmpty(melter?.SelectedFluidType) ? "None" : melter.SelectedFluidType;
                return true;
            case "req_item_input":
                value = melter != null && melter.HasItemInputRequirement(world) ? "true" : "false";
                return true;
            case "req_not_item_input":
                value = melter != null && melter.HasItemInputRequirement(world) ? "false" : "true";
                return true;
            case "req_fluid_output":
                value = melter != null && melter.HasFluidOutputRequirement(world) ? "true" : "false";
                return true;
            case "req_not_fluid_output":
                value = melter != null && melter.HasFluidOutputRequirement(world) ? "false" : "true";
                return true;
            case "req_heat":
                value = melter != null && melter.HasRequiredHeat() ? "true" : "false";
                return true;
            case "req_not_heat":
                value = melter != null && melter.HasRequiredHeat() ? "false" : "true";
                return true;
            case "pending_item_input":
                value = (melter?.pendingItemInput ?? 0).ToString(CultureInfo.InvariantCulture);
                return true;
            case "pending_item_input_icon":
                value = GetItemIconName(melter?.PendingItemInputName);
                return true;
            case "pending_item_input_has_item":
                value = melter != null && melter.pendingItemInput > 0 && !string.IsNullOrEmpty(melter.PendingItemInputName) ? "true" : "false";
                return true;
            case "pending_item_input_name":
                value = GetItemDisplayName(melter?.PendingItemInputName);
                return true;
            case "pending_fluid_output":
                value = melter == null ? "0/0 gal" : $"{FormatGallons(melter.pendingFluidOutput)}/{FormatGallons(melter.pendingFluidOutputCapacityMg)} gal";
                return true;
            case "pending_fluid_output_name":
                value = GetFluidDisplayName(melter?.SelectedFluidType);
                return true;
            case "machine_state":
                if (melter == null)
                {
                    value = "Offline";
                    return true;
                }
                value = melter.AreAllRequirementsMet(world) ? "Running" : "Waiting";
                return true;
            case "cycle_timer":
                value = melter == null ? "0/0" : $"{melter.cycleTickCounter}/{melter.cycleTickLength}";
                return true;
            case "last_action":
                value = melter?.LastAction ?? "Idle";
                return true;
            case "block_reason":
                value = melter?.LastBlockReason ?? string.Empty;
                return true;
            case "selected_input":
                value = melter == null || melter.SelectedInputChestPos == Vector3i.zero ? "None" : melter.SelectedInputChestPos.ToString();
                return true;
            case "selected_fluid_graph":
                value = melter == null || melter.SelectedFluidGraphId == Guid.Empty ? "None" : melter.SelectedFluidGraphId.ToString();
                return true;
            case "current_heat":
                value = (melter?.CurrentHeat ?? 0).ToString(CultureInfo.InvariantCulture);
                return true;
            case "max_heat":
                value = (melter?.CurrentHeatSourceMax ?? 0).ToString(CultureInfo.InvariantCulture);
                return true;
            case "required_heat":
                value = melter == null ? "0" : GetRequiredHeat(melter).ToString(CultureInfo.InvariantCulture);
                return true;
            case "heat_fill":
                int max = melter?.CurrentHeatSourceMax ?? 0;
                float fill = max > 0 ? Mathf.Clamp01((float)melter.CurrentHeat / max) : 0f;
                value = fill.ToString("0.###", CultureInfo.InvariantCulture);
                return true;
        }

        return false;
    }

    private static int GetRequiredHeat(TileEntityMelter melter)
    {
        if (melter == null || string.IsNullOrEmpty(melter.SelectedRecipeKey))
            return 0;

        if (!MachineRecipeRegistry.TryGetRecipeByKey(melter.SelectedRecipeKey, out MachineRecipe recipe) || recipe == null)
            return 0;

        return Math.Max(0, recipe.RequiredHeat);
    }

    private static string FormatGallons(int milliGallons)
    {
        double gallons = milliGallons / (double)FluidConstants.MilliGallonsPerGallon;
        return gallons.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string GetItemIconName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return string.Empty;

        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        if (itemValue?.ItemClass == null)
            return string.Empty;

        return itemValue.ItemClass.GetIconName();
    }

    private static string GetItemDisplayName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return string.Empty;

        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        if (itemValue?.ItemClass != null)
            return itemValue.ItemClass.GetLocalizedItemName();

        return itemName;
    }

    private static string GetFluidDisplayName(string fluidType)
    {
        if (string.IsNullOrWhiteSpace(fluidType))
            return string.Empty;

        string normalized = fluidType.Trim().Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private void EnsureListContext()
    {
        if (inputList == null)
            inputList = GetChildByType<XUiC_MelterInputContainerList>();

        if (te == null)
            te = GetTileEntity();

        if (inputList != null && te != null)
            inputList.SetContext(te, blockPosition);
    }
}
