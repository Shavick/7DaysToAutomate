using System;
using System.Globalization;

public class XUiC_CasterInfo : XUiController
{
    private const float PassiveRefreshIntervalSeconds = 0.2f;
    private Vector3i blockPosition;
    private TileEntityCaster te;
    private XUiC_CasterOutputContainerList outputList;
    private float passiveRefreshTimer;

    public override void Init()
    {
        base.Init();

        var closeBtn = GetChildById("closeButton")?.ViewComponent as XUiV_Button;
        if (closeBtn != null)
            closeBtn.Controller.OnPress += (c, b) => xui.playerUI.windowManager.Close("CasterInfo");

        var powerBtn = GetChildById("powerbutton")?.ViewComponent as XUiV_Button;
        if (powerBtn != null)
            powerBtn.Controller.OnPress += (c, b) => TogglePower();

        var prevBtn = GetChildById("recipeprevbutton")?.ViewComponent as XUiV_Button;
        if (prevBtn != null)
            prevBtn.Controller.OnPress += (c, b) => Helper.RequestCasterCycleRecipe(blockPosition, -1);

        var nextBtn = GetChildById("recipenextbutton")?.ViewComponent as XUiV_Button;
        if (nextBtn != null)
            nextBtn.Controller.OnPress += (c, b) => Helper.RequestCasterCycleRecipe(blockPosition, 1);

        outputList = GetChildByType<XUiC_CasterOutputContainerList>();
    }

    public static void Open(EntityPlayerLocal player, Vector3i pos)
    {
        if (player?.playerUI == null)
            return;

        var ctrl = player.playerUI.xui?.GetChildByType<XUiC_CasterInfo>();
        if (ctrl != null)
            ctrl.blockPosition = pos;

        player.playerUI.windowManager.Open("CasterInfo", true, false, true);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        passiveRefreshTimer = 0f;
        te = GetTileEntity();
        EnsureContexts();
        RefreshBindings(true);
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        bool shouldRefresh = false;

        if (te == null)
        {
            te = GetTileEntity();
            EnsureContexts();
            shouldRefresh = te != null;
        }

        if (te == null)
            return;

        passiveRefreshTimer += dt;
        if (passiveRefreshTimer >= PassiveRefreshIntervalSeconds)
        {
            passiveRefreshTimer = 0f;
            shouldRefresh = true;
        }

        if (te.NeedsUiRefresh)
        {
            te.NeedsUiRefresh = false;
            EnsureContexts();
            if (outputList != null)
                outputList.IsDirty = true;
            shouldRefresh = true;
        }

        if (shouldRefresh)
            RefreshBindings(true);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        te = GetTileEntity();
        WorldBase world = GameManager.Instance?.World;

        switch (bindingName)
        {
            case "machinename":
                value = te?.blockValue.Block?.GetLocalizedBlockName() ?? "Universal Caster";
                return true;

            case "selected_recipe":
                value = te?.GetSelectedRecipeDisplayName() ?? "None";
                return true;

            case "recipe_details":
                value = te?.GetSelectedRecipeDetails() ?? "Select a recipe.";
                return true;

            case "powerbutton":
                value = te != null && te.IsOn ? "Turn Off" : "Turn On";
                return true;

            case "machine_state":
                if (te == null)
                {
                    value = "Offline";
                    return true;
                }

                if (!te.IsOn)
                {
                    value = "Off";
                    return true;
                }

                value = te.AreAllRequirementsMet(world) ? "Running" : "Waiting";
                return true;

            case "cycle_timer":
                value = te?.GetCycleTimerText() ?? "0/0";
                return true;

            case "pending_fluid_input_summary":
                if (te == null)
                {
                    value = "0 gal";
                    return true;
                }

                string fluid = GetFluidDisplayName(te.GetPendingFluidInputType());
                value = string.IsNullOrEmpty(fluid)
                    ? $"{FormatGallons(te.GetPendingFluidInputAmountMg())} gal"
                    : $"{FormatGallons(te.GetPendingFluidInputAmountMg())} gal ({fluid})";
                return true;

            case "pending_primary_output_icon":
                value = GetItemIconName(te?.GetPendingPrimaryOutputItemName());
                return true;

            case "pending_primary_output_name":
                value = GetItemDisplayName(te?.GetPendingPrimaryOutputItemName());
                return true;

            case "pending_primary_output_count":
                value = (te?.GetPendingPrimaryOutputItemCount() ?? 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case "pending_primary_output_has_item":
                value = te != null && te.GetPendingPrimaryOutputItemCount() > 0 ? "true" : "false";
                return true;

            case "pending_secondary_output_icon":
                value = GetItemIconName(te?.GetPendingSecondaryOutputItemName());
                return true;

            case "pending_secondary_output_name":
                value = GetItemDisplayName(te?.GetPendingSecondaryOutputItemName());
                return true;

            case "pending_secondary_output_count":
                value = (te?.GetPendingSecondaryOutputItemCount() ?? 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case "pending_secondary_output_has_item":
                value = te != null && te.GetPendingSecondaryOutputItemCount() > 0 ? "true" : "false";
                return true;

            case "selected_output":
                value = te == null || te.SelectedOutputChestPos == Vector3i.zero ? "None" : te.SelectedOutputChestPos.ToString();
                return true;

            case "selected_fluid_graph":
                value = te == null || te.SelectedFluidGraphId == Guid.Empty ? "None" : te.SelectedFluidGraphId.ToString();
                return true;

            case "last_action":
                value = te?.LastAction ?? "Idle";
                return true;

            case "block_reason":
                value = te?.LastBlockReason ?? string.Empty;
                return true;

            case "req_recipe":
                value = te != null && te.HasSelectedRecipe() ? "true" : "false";
                return true;

            case "req_not_recipe":
                value = te != null && te.HasSelectedRecipe() ? "false" : "true";
                return true;

            case "req_item_output":
                value = te != null && te.HasItemOutputRequirement(world) ? "true" : "false";
                return true;

            case "req_not_item_output":
                value = te != null && te.HasItemOutputRequirement(world) ? "false" : "true";
                return true;

            case "req_fluid_input":
                value = te != null && te.HasFluidInputRequirement(world) ? "true" : "false";
                return true;

            case "req_not_fluid_input":
                value = te != null && te.HasFluidInputRequirement(world) ? "false" : "true";
                return true;
        }

        return false;
    }

    private TileEntityCaster GetTileEntity()
    {
        if (blockPosition == default || GameManager.Instance?.World == null)
            return null;

        return GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityCaster;
    }

    private void EnsureContexts()
    {
        if (outputList == null)
            outputList = GetChildByType<XUiC_CasterOutputContainerList>();

        if (te == null)
            te = GetTileEntity();

        if (outputList != null && te != null)
            outputList.SetContext(te, blockPosition);
    }

    private void TogglePower()
    {
        te = GetTileEntity();
        if (te == null)
            return;

        Helper.RequestMachinePowerToggle(te.GetClrIdx(), blockPosition, !te.IsOn);
        RefreshBindings(true);
    }

    private static string GetFluidDisplayName(string fluidType)
    {
        if (string.IsNullOrWhiteSpace(fluidType))
            return string.Empty;

        string normalized = fluidType.Trim().Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
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
}
