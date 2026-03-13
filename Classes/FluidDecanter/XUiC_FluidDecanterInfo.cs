using System;
using System.Globalization;
using UnityEngine;

public class XUiC_FluidDecanterInfo : XUiController
{
    private Vector3i blockPosition;
    private TileEntityFluidDecanter te;

    private XUiC_FluidDecanterInputContainerList inputList;
    private XUiC_FluidDecanterOutputContainerList outputList;

    public Vector3i BlockPosition => blockPosition;

    public override void Init()
    {
        base.Init();

        var closeBtn = GetChildById("closeButton")?.ViewComponent as XUiV_Button;
        if (closeBtn != null)
            closeBtn.Controller.OnPress += (c, b) => xui.playerUI.windowManager.Close("FluidDecanterInfo");

        var powerBtn = GetChildById("powerbutton")?.ViewComponent as XUiV_Button;
        if (powerBtn != null)
            powerBtn.Controller.OnPress += (c, b) => TogglePower();

        var fluidPrevBtn = GetChildById("fluidprevbutton")?.ViewComponent as XUiV_Button;
        if (fluidPrevBtn != null)
            fluidPrevBtn.Controller.OnPress += (c, b) => Helper.RequestFluidDecanterCycleFluid(blockPosition, -1);

        var fluidNextBtn = GetChildById("fluidnextbutton")?.ViewComponent as XUiV_Button;
        if (fluidNextBtn != null)
            fluidNextBtn.Controller.OnPress += (c, b) => Helper.RequestFluidDecanterCycleFluid(blockPosition, 1);

        inputList = GetChildByType<XUiC_FluidDecanterInputContainerList>();
        outputList = GetChildByType<XUiC_FluidDecanterOutputContainerList>();
    }

    public static void Open(EntityPlayerLocal player, Vector3i pos)
    {
        if (player?.playerUI == null)
            return;

        var ctrl = player.playerUI.xui?.GetChildByType<XUiC_FluidDecanterInfo>();
        if (ctrl != null)
            ctrl.blockPosition = pos;

        player.playerUI.windowManager.Open("FluidDecanterInfo", true, false, true);
    }

    public override void OnOpen()
    {
        base.OnOpen();

        te = GetTileEntity();
        EnsureListContexts();
        RefreshBindings(true);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (te == null)
        {
            te = GetTileEntity();
            EnsureListContexts();
        }

        if (te == null)
            return;

        if (!te.NeedsUiRefresh)
            return;

        te.NeedsUiRefresh = false;

        EnsureListContexts();

        if (inputList != null)
            inputList.IsDirty = true;

        if (outputList != null)
            outputList.IsDirty = true;

        RefreshBindings(true);
    }

    public TileEntityFluidDecanter GetTileEntity()
    {
        if (blockPosition == default || GameManager.Instance?.World == null)
            return null;

        return GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityFluidDecanter;
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        TileEntityFluidDecanter converter = GetTileEntity();
        WorldBase world = GameManager.Instance?.World;

        string key = bindingName?.Trim() ?? string.Empty;

        switch (key)
        {
            case "machinename":
                value = converter?.blockValue.Block?.GetLocalizedBlockName() ?? "Fluid Decanter";
                return true;

            case "selectedfluid":
                value = string.IsNullOrEmpty(converter?.SelectedFluidType) ? "None" : converter.SelectedFluidType;
                return true;

            case "powerbutton":
                if (converter == null)
                {
                    value = "Start";
                    return true;
                }

                if (!converter.IsOn)
                {
                    value = "Start";
                    return true;
                }

                value = converter.AreAllRequirementsMet(world) ? "Turn Off" : "Waiting...";
                return true;

            case "req_item_input":
                value = converter != null && converter.HasItemInputRequirement(world) ? "true" : "false";
                return true;

            case "req_not_item_input":
                value = converter != null && converter.HasItemInputRequirement(world) ? "false" : "true";
                return true;

            case "req_item_output":
                value = converter != null && converter.HasItemOutputRequirement(world) ? "true" : "false";
                return true;

            case "req_not_item_output":
                value = converter != null && converter.HasItemOutputRequirement(world) ? "false" : "true";
                return true;

            case "req_fluid_output":
                value = converter != null && converter.HasFluidOutputRequirement(world) ? "true" : "false";
                return true;

            case "req_not_fluid_output":
                value = converter != null && converter.HasFluidOutputRequirement(world) ? "false" : "true";
                return true;

            case "pending_item_input":
                value = (converter?.pendingItemInput ?? 0).ToString();
                return true;

            case "pending_item_output":
                value = (converter?.pendingItemOutput ?? 0).ToString();
                return true;

            case "pending_item_input_icon":
                value = GetItemIconName(converter?.PendingItemInputName);
                return true;

            case "pending_item_output_icon":
                value = GetItemIconName(converter?.PendingItemOutputName);
                return true;

            case "pending_item_input_has_item":
                value = converter != null && converter.pendingItemInput > 0 && !string.IsNullOrEmpty(converter.PendingItemInputName)
                    ? "true"
                    : "false";
                return true;

            case "pending_item_output_has_item":
                value = converter != null && converter.pendingItemOutput > 0 && !string.IsNullOrEmpty(converter.PendingItemOutputName)
                    ? "true"
                    : "false";
                return true;

            case "pending_item_input_name":
                value = GetItemDisplayName(converter?.PendingItemInputName);
                return true;

            case "pending_item_output_name":
                value = GetItemDisplayName(converter?.PendingItemOutputName);
                return true;

            case "pending_fluid_input_name":
                value = GetFluidDisplayName(converter?.SelectedFluidType);
                return true;

            case "pending_fluid_output_name":
                value = GetFluidDisplayName(converter?.SelectedFluidType);
                return true;

            case "pending_fluid_input":
                value = converter == null
                    ? "0 gal"
                    : $"{FormatGallons(converter.pendingFluidInput)} gal";
                return true;

            case "pending_fluid_output":
                value = converter == null
                    ? "0/0 gal"
                    : $"{FormatGallons(converter.pendingFluidOutput)}/{FormatGallons(converter.pendingFluidOutputCapacityMg)} gal";
                return true;

            case "cycle_timer":
                value = converter == null
                    ? "0/0"
                    : $"{converter.cycleTickCounter}/{converter.cycleTickLength}";
                return true;

            case "machine_state":
                if (converter == null)
                {
                    value = "Offline";
                    return true;
                }

                if (!converter.IsOn)
                {
                    value = "Off";
                    return true;
                }

                value = converter.AreAllRequirementsMet(world) ? "Running" : "Waiting";
                return true;

            case "last_action":
                value = converter?.LastAction ?? "Idle";
                return true;

            case "block_reason":
                value = converter?.LastBlockReason ?? string.Empty;
                return true;

            case "selected_input":
                value = converter == null || converter.SelectedInputChestPos == Vector3i.zero
                    ? "None"
                    : converter.SelectedInputChestPos.ToString();
                return true;

            case "selected_output":
                value = converter == null || converter.SelectedOutputChestPos == Vector3i.zero
                    ? "None"
                    : converter.SelectedOutputChestPos.ToString();
                return true;

            case "selected_fluid_graph":
                value = converter == null || converter.SelectedFluidGraphId == Guid.Empty
                    ? "None"
                    : converter.SelectedFluidGraphId.ToString();
                return true;
        }

        return false;
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

    private void TogglePower()
    {
        te = GetTileEntity();
        if (te == null)
            return;

        Helper.RequestMachinePowerToggle(te.GetClrIdx(), blockPosition, !te.IsOn);
        RefreshBindings(true);
    }

    private void EnsureListContexts()
    {
        if (inputList == null)
            inputList = GetChildByType<XUiC_FluidDecanterInputContainerList>();

        if (outputList == null)
            outputList = GetChildByType<XUiC_FluidDecanterOutputContainerList>();

        if (te == null)
            te = GetTileEntity();

        if (te == null)
            return;

        if (inputList != null)
            inputList.SetContext(te, blockPosition);

        if (outputList != null)
            outputList.SetContext(te, blockPosition);
    }
}


