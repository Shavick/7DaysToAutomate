using System;

public class XUiC_UniversalGrinderInfo : XUiController
{
    private Vector3i blockPosition;
    private TileEntityUniversalGrinder te;
    private XUiC_GrinderInputContainerList inputList;
    private XUiC_GrinderOutputContainerList outputList;

    public override void Init()
    {
        base.Init();

        XUiV_Button closeBtn = GetChildById("closeButton")?.ViewComponent as XUiV_Button;
        if (closeBtn != null)
            closeBtn.Controller.OnPress += (c, b) => xui.playerUI.windowManager.Close("GrinderInfo");

        XUiV_Button powerBtn = GetChildById("powerbutton")?.ViewComponent as XUiV_Button;
        if (powerBtn != null)
            powerBtn.Controller.OnPress += (c, b) => TogglePower();

        XUiV_Button modsBtn = GetChildById("processModsButton")?.ViewComponent as XUiV_Button;
        if (modsBtn != null)
            modsBtn.Controller.OnPress += (c, b) => ToggleMods();

        inputList = GetChildByType<XUiC_GrinderInputContainerList>();
        outputList = GetChildByType<XUiC_GrinderOutputContainerList>();
    }

    public static void Open(EntityPlayerLocal player, Vector3i pos)
    {
        if (player?.playerUI == null)
            return;

        XUiC_UniversalGrinderInfo ctrl = player.playerUI.xui?.GetChildByType<XUiC_UniversalGrinderInfo>();
        if (ctrl != null)
            ctrl.blockPosition = pos;

        player.playerUI.windowManager.Open("GrinderInfo", true, false, true);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        te = GetTileEntity();
        EnsureContexts();
        RefreshBindings(true);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (te == null)
        {
            te = GetTileEntity();
            EnsureContexts();
        }

        if (te == null || !te.NeedsUiRefresh)
            return;

        te.NeedsUiRefresh = false;
        EnsureContexts();
        if (inputList != null)
            inputList.IsDirty = true;

        if (outputList != null)
            outputList.IsDirty = true;

        RefreshBindings(true);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        te = GetTileEntity();
        WorldBase world = GameManager.Instance?.World;

        switch (bindingName)
        {
            case "machinename":
                value = te?.blockValue.Block?.GetLocalizedBlockName() ?? "Universal Grinder";
                return true;
            case "powerbutton":
                value = te == null ? "Turn On" : (te.IsOn ? "Turn Off" : "Turn On");
                return true;
            case "machine_state":
                value = te?.GetPrimaryStatusText(world) ?? "Offline";
                return true;
            case "machine_secondary":
                value = te?.GetSecondaryStatusText() ?? string.Empty;
                return true;
            case "cycle_timer":
                value = te?.GetCycleTimerText() ?? "0/0";
                return true;
            case "selected_input":
                value = te == null || te.SelectedInputChestPos == Vector3i.zero ? "None" : te.SelectedInputChestPos.ToString();
                return true;
            case "selected_output":
                value = te == null || te.SelectedOutputChestPos == Vector3i.zero ? "None" : te.SelectedOutputChestPos.ToString();
                return true;
            case "effective_return_rate":
                value = te == null ? "50%" : $"{(te.EffectiveReturnRate * 100f):0.##}%";
                return true;
            case "items_processed":
                value = te == null ? "0" : te.ItemsProcessed.ToString();
                return true;
            case "mods_button_text":
                value = te == null || te.ProcessItemArmorMods
                    ? "Process Item/Armor Mods: ON"
                    : "Process Item/Armor Mods: OFF";
                return true;
            case "pending_output":
                value = te?.GetPendingOutputSummary() ?? "(empty)";
                return true;
            case "pending_item_input":
                value = te == null ? "0" : te.GetPendingInputItemCount().ToString();
                return true;
            case "pending_item_input_name":
                value = te == null ? string.Empty : GetItemDisplayName(te.GetPendingInputItemName());
                return true;
            case "pending_item_input_icon":
                value = te == null ? string.Empty : GetItemIconName(te.GetPendingInputItemName());
                return true;
            case "pending_item_input_has_item":
                value = te != null && te.GetPendingInputItemCount() > 0 ? "true" : "false";
                return true;
            case "pending_item_output":
                value = te == null ? "0" : te.GetPendingOutputItemCount().ToString();
                return true;
            case "pending_item_output_name":
                value = te == null ? string.Empty : GetItemDisplayName(te.GetPendingOutputItemName());
                return true;
            case "pending_item_output_icon":
                value = te == null ? string.Empty : GetItemIconName(te.GetPendingOutputItemName());
                return true;
            case "pending_item_output_has_item":
                value = te != null && te.GetPendingOutputItemCount() > 0 ? "true" : "false";
                return true;
            case "last_action":
                value = te?.LastAction ?? "Idle";
                return true;
            case "block_reason":
                value = te?.LastBlockReason ?? string.Empty;
                return true;
        }

        return false;
    }

    private TileEntityUniversalGrinder GetTileEntity()
    {
        if (blockPosition == default || GameManager.Instance?.World == null)
            return null;

        return GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityUniversalGrinder;
    }

    private void TogglePower()
    {
        te = GetTileEntity();
        if (te == null)
            return;

        Helper.RequestMachinePowerToggle(te.GetClrIdx(), blockPosition, !te.IsOn);
        RefreshBindings(true);
    }

    private void ToggleMods()
    {
        Helper.RequestGrinderToggleMods(blockPosition);
        RefreshBindings(true);
    }

    private void EnsureContexts()
    {
        if (inputList == null)
            inputList = GetChildByType<XUiC_GrinderInputContainerList>();
        if (outputList == null)
            outputList = GetChildByType<XUiC_GrinderOutputContainerList>();
        if (te == null)
            te = GetTileEntity();

        if (te == null)
            return;

        inputList?.SetContext(te, blockPosition);
        outputList?.SetContext(te, blockPosition);
    }

    private static string GetItemIconName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return string.Empty;

        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        return itemValue?.ItemClass != null ? itemValue.ItemClass.GetIconName() : string.Empty;
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
