using System;
using System.Globalization;
using UnityEngine;

public class XUiC_BoilerInfo : XUiController
{
    private Vector3i blockPosition;
    private TileEntityBoiler te;

    public override void Init()
    {
        base.Init();

        var closeBtn = GetChildById("closeButton")?.ViewComponent as XUiV_Button;
        if (closeBtn != null)
            closeBtn.Controller.OnPress += (c, b) => xui.playerUI.windowManager.Close("BoilerInfo");

        var powerBtn = GetChildById("powerbutton")?.ViewComponent as XUiV_Button;
        if (powerBtn != null)
            powerBtn.Controller.OnPress += (c, b) => TogglePower();

        var recipePrevBtn = GetChildById("recipeprevbutton")?.ViewComponent as XUiV_Button;
        if (recipePrevBtn != null)
            recipePrevBtn.Controller.OnPress += (c, b) => Helper.RequestBoilerCycleRecipe(blockPosition, -1);

        var recipeNextBtn = GetChildById("recipenextbutton")?.ViewComponent as XUiV_Button;
        if (recipeNextBtn != null)
            recipeNextBtn.Controller.OnPress += (c, b) => Helper.RequestBoilerCycleRecipe(blockPosition, 1);
    }

    public static void Open(EntityPlayerLocal player, Vector3i pos)
    {
        if (player?.playerUI == null)
            return;

        var ctrl = player.playerUI.xui?.GetChildByType<XUiC_BoilerInfo>();
        if (ctrl != null)
            ctrl.blockPosition = pos;

        player.playerUI.windowManager.Open("BoilerInfo", true, false, true);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        te = GetTileEntity();
        RefreshBindings(true);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (te == null)
            te = GetTileEntity();

        if (te == null || !te.NeedsUiRefresh)
            return;

        te.NeedsUiRefresh = false;
        RefreshBindings(true);
    }

    private TileEntityBoiler GetTileEntity()
    {
        if (blockPosition == default || GameManager.Instance?.World == null)
            return null;

        return GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityBoiler;
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        TileEntityBoiler boiler = GetTileEntity();
        WorldBase world = GameManager.Instance?.World;

        switch (bindingName?.Trim() ?? string.Empty)
        {
            case "machinename":
                value = boiler?.blockValue.Block?.GetLocalizedBlockName() ?? "Boiler";
                return true;
            case "selected_recipe":
                value = boiler?.GetSelectedRecipeDisplayName() ?? "None";
                return true;
            case "powerbutton":
                if (boiler == null)
                {
                    value = "Turn On";
                    return true;
                }

                value = boiler.IsOn ? "Turn Off" : "Turn On";
                return true;
            case "machine_state":
                if (boiler == null)
                {
                    value = "Offline";
                    return true;
                }

                value = boiler.AreAllRequirementsMet(world) ? (boiler.IsProcessing ? "Boiling" : "Ready") : "Waiting";
                return true;
            case "cycle_timer":
                value = boiler == null ? "0/0" : $"{boiler.CycleTickCounter}/{Math.Max(1, boiler.CycleTickLength)}";
                return true;
            case "last_action":
                value = boiler?.LastAction ?? "Idle";
                return true;
            case "block_reason":
                value = boiler?.LastBlockReason ?? string.Empty;
                return true;
            case "pending_fluid_input_a_name":
                value = ToFluidDisplayName(boiler?.PendingFluidInputAType);
                return true;
            case "pending_fluid_input_a":
                value = boiler == null ? "0 gal" : $"{FormatGallons(boiler.PendingFluidInputAAmountMg)} gal";
                return true;
            case "pending_fluid_output_name":
                value = ToFluidDisplayName(boiler?.PendingFluidOutputType);
                return true;
            case "pending_fluid_output":
                value = boiler == null ? "0/0 gal" : $"{FormatGallons(boiler.pendingFluidOutput)}/{FormatGallons(boiler.pendingFluidOutputCapacityMg)} gal";
                return true;
            case "current_heat":
                value = boiler == null ? "0" : boiler.CurrentHeat.ToString(CultureInfo.InvariantCulture);
                return true;
            case "max_heat":
                value = boiler == null ? "0" : boiler.CurrentHeatSourceMax.ToString(CultureInfo.InvariantCulture);
                return true;
            case "required_heat":
                value = boiler == null ? "0" : boiler.GetRequiredHeatForSelectedRecipe().ToString(CultureInfo.InvariantCulture);
                return true;
            case "heat_source_status":
                value = boiler == null ? "No source" : GetHeatSourceStatusText(boiler.GetHeatSourceUiState(world));
                return true;
            case "heat_source_none":
                value = boiler != null && boiler.GetHeatSourceUiState(world) == "none" ? "true" : "false";
                return true;
            case "heat_source_off":
                value = boiler != null && boiler.GetHeatSourceUiState(world) == "off" ? "true" : "false";
                return true;
            case "heat_source_heating":
                value = boiler != null && boiler.GetHeatSourceUiState(world) == "heating" ? "true" : "false";
                return true;
            case "heat_fill":
                value = boiler == null || boiler.CurrentHeatSourceMax <= 0
                    ? "0"
                    : Mathf.Clamp01((float)boiler.CurrentHeat / boiler.CurrentHeatSourceMax).ToString(CultureInfo.InvariantCulture);
                return true;
            case "req_recipe":
                value = boiler != null && boiler.HasSelectedRecipe() ? "true" : "false";
                return true;
            case "req_not_recipe":
                value = boiler != null && boiler.HasSelectedRecipe() ? "false" : "true";
                return true;
            case "req_fluid_input":
                value = boiler != null && boiler.HasFluidInputRequirement(world) ? "true" : "false";
                return true;
            case "req_not_fluid_input":
                value = boiler != null && boiler.HasFluidInputRequirement(world) ? "false" : "true";
                return true;
            case "req_fluid_output":
                value = boiler != null && boiler.HasFluidOutputRequirement(world) ? "true" : "false";
                return true;
            case "req_not_fluid_output":
                value = boiler != null && boiler.HasFluidOutputRequirement(world) ? "false" : "true";
                return true;
            case "req_heat":
                value = boiler != null && boiler.HasRequiredHeat() ? "true" : "false";
                return true;
            case "req_not_heat":
                value = boiler != null && boiler.HasRequiredHeat() ? "false" : "true";
                return true;
        }

        return false;
    }

    private void TogglePower()
    {
        TileEntityBoiler boiler = GetTileEntity();
        if (boiler == null)
            return;

        Helper.RequestMachinePowerToggle(boiler.GetClrIdx(), blockPosition, !boiler.IsOn);
        RefreshBindings(true);
    }

    private static string GetHeatSourceStatusText(string uiState)
    {
        switch (uiState ?? string.Empty)
        {
            case "off":
                return "Heat source idle";
            case "heating":
                return "Heating";
            default:
                return "No heat source";
        }
    }

    private static string ToFluidDisplayName(string fluidType)
    {
        if (string.IsNullOrWhiteSpace(fluidType))
            return "None";

        string normalized = fluidType.Trim().Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string FormatGallons(int milliGallons)
    {
        double gallons = milliGallons / (double)FluidConstants.MilliGallonsPerGallon;
        return gallons.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
