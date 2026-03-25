using System;
using System.Globalization;

public class XUiC_FluidInfuserInfo : XUiController
{
    private Vector3i blockPosition;
    private TileEntityFluidInfuser te;
    private XUiC_FluidInfuserInputContainerList inputList;
    private XUiC_FluidInfuserOutputContainerList outputList;
    private XUiC_FluidInfuserRecipeList recipeList;
    private bool recipeOverlayVisible;
    private string pendingRecipeKey = string.Empty;
    public override void Init()
    {
        base.Init();

        var closeBtn = GetChildById("closeButton")?.ViewComponent as XUiV_Button;
        if (closeBtn != null)
            closeBtn.Controller.OnPress += (c, b) => xui.playerUI.windowManager.Close("FluidInfuserInfo");

        var powerBtn = GetChildById("powerbutton")?.ViewComponent as XUiV_Button;
        if (powerBtn != null)
            powerBtn.Controller.OnPress += (c, b) => TogglePower();

        var recipesBtn = GetChildById("recipesButton")?.ViewComponent as XUiV_Button;
        if (recipesBtn != null)
            recipesBtn.Controller.OnPress += (c, b) => ShowRecipeOverlay(true);

        var cancelBtn = GetChildById("recipeCancelButton")?.ViewComponent as XUiV_Button;
        if (cancelBtn != null)
            cancelBtn.Controller.OnPress += (c, b) => CancelRecipeSelection();

        var applyBtn = GetChildById("recipeApplyButton")?.ViewComponent as XUiV_Button;
        if (applyBtn != null)
            applyBtn.Controller.OnPress += (c, b) => ApplyRecipeSelection();

        inputList = GetChildByType<XUiC_FluidInfuserInputContainerList>();
        outputList = GetChildByType<XUiC_FluidInfuserOutputContainerList>();
        recipeList = GetChildByType<XUiC_FluidInfuserRecipeList>();
        if (recipeList != null)
            recipeList.SetOwner(this);
    }

    public static void Open(EntityPlayerLocal player, Vector3i pos)
    {
        if (player?.playerUI == null)
            return;

        var ctrl = player.playerUI.xui?.GetChildByType<XUiC_FluidInfuserInfo>();
        if (ctrl != null)
            ctrl.blockPosition = pos;

        player.playerUI.windowManager.Open("FluidInfuserInfo", true, false, true);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        te = GetTileEntity();
        pendingRecipeKey = te?.SelectedRecipeKey ?? string.Empty;
        EnsureContexts();
        ShowRecipeOverlay(false);
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
        if (!recipeOverlayVisible)
            pendingRecipeKey = te.SelectedRecipeKey ?? string.Empty;

        if (inputList != null)
            inputList.IsDirty = true;

        if (outputList != null)
            outputList.IsDirty = true;

        if (recipeList != null)
            recipeList.IsDirty = true;

        RefreshBindings(true);
    }

    public void SetPendingRecipe(string recipeKey)
    {
        pendingRecipeKey = recipeKey ?? string.Empty;
        RefreshBindings(true);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        te = GetTileEntity();
        WorldBase world = GameManager.Instance?.World;

        switch (bindingName)
        {
            case "machinename":
                value = te?.blockValue.Block?.GetLocalizedBlockName() ?? "Fluid Infuser";
                return true;

            case "selected_recipe":
                value = recipeOverlayVisible ? GetRecipeDisplayName(pendingRecipeKey) : (te?.GetSelectedRecipeDisplayName() ?? "None");
                return true;

            case "selected_fluid":
                value = te?.GetSelectedFluidDisplayName() ?? "None";
                return true;

            case "recipe_details":
                value = recipeOverlayVisible ? GetPendingRecipeDetails() : (te?.GetSelectedRecipeDetails() ?? "Select a recipe.");
                return true;

            case "powerbutton":
                if (te == null)
                {
                    value = "Turn On";
                    return true;
                }

                value = te.IsOn ? "Turn Off" : "Turn On";
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

            case "pending_item_input":
                value = (te?.GetPendingInputItemCount() ?? 0).ToString();
                return true;

            case "pending_item_output":
                value = (te?.GetPendingOutputItemCount() ?? 0).ToString();
                return true;

            case "pending_item_input_icon":
                value = GetItemIconName(te?.GetPendingInputItemName());
                return true;

            case "pending_item_output_icon":
                value = GetItemIconName(te?.GetPendingOutputItemName());
                return true;

            case "pending_item_input_has_item":
                value = te != null && te.GetPendingInputItemCount() > 0 && !string.IsNullOrEmpty(te.GetPendingInputItemName())
                    ? "true"
                    : "false";
                return true;

            case "pending_item_output_has_item":
                value = te != null && te.GetPendingOutputItemCount() > 0 && !string.IsNullOrEmpty(te.GetPendingOutputItemName())
                    ? "true"
                    : "false";
                return true;

            case "pending_item_input_name":
                value = GetItemDisplayName(te?.GetPendingInputItemName());
                return true;

            case "pending_item_output_name":
                value = GetItemDisplayName(te?.GetPendingOutputItemName());
                return true;

            case "pending_fluid_input_name":
                value = GetFluidDisplayName(te?.GetPendingFluidInputType());
                return true;

            case "pending_fluid_output_name":
                value = string.Empty;
                return true;

            case "pending_fluid_input":
                value = te == null
                    ? "0 gal"
                    : $"{FormatGallons(te.GetPendingFluidInputAmountMg())} gal";
                return true;

            case "pending_fluid_output":
                value = "0 gal";
                return true;

            case "pending_fluid_input_summary":
                value = FormatFluidSummary(te?.GetPendingFluidInputAmountMg() ?? 0, te?.GetPendingFluidInputType());
                return true;

            case "pending_fluid_output_summary":
                value = FormatFluidSummary(0, null);
                return true;

            case "selected_input":
                value = te == null || te.SelectedInputChestPos == Vector3i.zero ? "None" : te.SelectedInputChestPos.ToString();
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

            case "req_item_input":
                value = te != null && te.HasItemInputRequirement(world) ? "true" : "false";
                return true;

            case "req_not_item_input":
                value = te != null && te.HasItemInputRequirement(world) ? "false" : "true";
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

    private static string FormatFluidSummary(int milliGallons, string fluidType)
    {
        string amountText = $"{FormatGallons(milliGallons)} gal";
        string fluidDisplay = GetFluidDisplayName(fluidType);
        return string.IsNullOrEmpty(fluidDisplay)
            ? amountText
            : $"{amountText} ({fluidDisplay})";
    }

    private TileEntityFluidInfuser GetTileEntity()
    {
        if (blockPosition == default || GameManager.Instance?.World == null)
            return null;

        return GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityFluidInfuser;
    }

    private void TogglePower()
    {
        te = GetTileEntity();
        if (te == null)
            return;

        Helper.RequestMachinePowerToggle(te.GetClrIdx(), blockPosition, !te.IsOn);
        RefreshBindings(true);
    }

    private void CancelRecipeSelection()
    {
        te = GetTileEntity();
        pendingRecipeKey = te?.SelectedRecipeKey ?? string.Empty;
        ShowRecipeOverlay(false);
        RefreshBindings(true);
    }

    private void ApplyRecipeSelection()
    {
        if (!string.IsNullOrEmpty(pendingRecipeKey))
            Helper.RequestFluidInfuserSelectRecipe(blockPosition, pendingRecipeKey);

        ShowRecipeOverlay(false);
    }

    private void EnsureContexts()
    {
        if (inputList == null)
            inputList = GetChildByType<XUiC_FluidInfuserInputContainerList>();
        if (outputList == null)
            outputList = GetChildByType<XUiC_FluidInfuserOutputContainerList>();
        if (recipeList == null)
            recipeList = GetChildByType<XUiC_FluidInfuserRecipeList>();

        if (te == null)
            te = GetTileEntity();

        if (te == null)
            return;

        inputList?.SetContext(te, blockPosition);
        outputList?.SetContext(te, blockPosition);
        recipeList?.SetContext(te, blockPosition);
    }

    private void ShowRecipeOverlay(bool visible)
    {
        recipeOverlayVisible = visible;
        SetVisibility("recipeOverlay", visible);
        SetVisibility("recipeSearchPanel", visible);
        SetVisibility("recipeSearchInput", visible);
        SetVisibility("recipeGrid", visible);
        SetVisibility("recipeSummary", visible);
        SetVisibility("recipeSummaryTitle", visible);
        SetVisibility("recipeCancelButton", visible);
        SetVisibility("recipeCancelLabel", visible);
        SetVisibility("recipeApplyButton", visible);
        SetVisibility("recipeApplyLabel", visible);
    }

    private void SetVisibility(string id, bool visible)
    {
        XUiController child = GetChildById(id);
        if (child?.ViewComponent != null)
            child.ViewComponent.IsVisible = visible;
    }

    private string GetRecipeDisplayName(string recipeKey)
    {
        if (string.IsNullOrEmpty(recipeKey))
            return "None";

        if (!MachineRecipeRegistry.TryGetRecipeByKey(recipeKey, out MachineRecipe recipe) || recipe == null)
            return "None";

        if (recipe.ItemOutputs != null && recipe.ItemOutputs.Count > 0)
        {
            MachineRecipeItemOutput output = recipe.ItemOutputs[0];
            ItemValue itemValue = ItemClass.GetItem(output?.ItemName ?? string.Empty, false);
            if (itemValue?.ItemClass != null)
                return itemValue.ItemClass.GetLocalizedItemName();
        }

        return !string.IsNullOrWhiteSpace(recipe.Name) ? Localization.Get(recipe.Name) : recipeKey;
    }

    private string GetPendingRecipeDetails()
    {
        if (string.IsNullOrEmpty(pendingRecipeKey))
            return "Select a recipe.";

        if (!MachineRecipeRegistry.TryGetRecipeByKey(pendingRecipeKey, out MachineRecipe recipe) || recipe == null)
            return "Select a recipe.";

        System.Collections.Generic.List<string> inputs = new System.Collections.Generic.List<string>();
        for (int i = 0; i < recipe.Inputs.Count; i++)
        {
            MachineRecipeInput input = recipe.Inputs[i];
            ItemValue itemValue = ItemClass.GetItem(input?.ItemName ?? string.Empty, false);
            string label = itemValue?.ItemClass != null ? itemValue.ItemClass.GetLocalizedItemName() : input?.ItemName ?? string.Empty;
            inputs.Add($"{label} x{input?.Count ?? 0}");
        }

        System.Collections.Generic.List<string> outputs = new System.Collections.Generic.List<string>();
        for (int i = 0; i < recipe.ItemOutputs.Count; i++)
        {
            MachineRecipeItemOutput output = recipe.ItemOutputs[i];
            ItemValue itemValue = ItemClass.GetItem(output?.ItemName ?? string.Empty, false);
            string label = itemValue?.ItemClass != null ? itemValue.ItemClass.GetLocalizedItemName() : output?.ItemName ?? string.Empty;
            outputs.Add($"{label} x{output?.Count ?? 0}");
        }

        MachineRecipeFluidInput fluidInput = recipe.FluidInputs != null && recipe.FluidInputs.Count > 0 ? recipe.FluidInputs[0] : null;
        return $"Inputs: {string.Join(", ", inputs.ToArray())}\nFluid: {ToFluidLabel(fluidInput)}\nOutputs: {string.Join(", ", outputs.ToArray())}\nTime: {Math.Max(1, recipe.CraftTimeTicks ?? 20)} ticks";
    }

    private static string ToFluidLabel(MachineRecipeFluidInput fluidInput)
    {
        if (fluidInput == null || string.IsNullOrWhiteSpace(fluidInput.Type))
            return "None";

        string normalized = fluidInput.Type.Trim().Replace('_', ' ');
        string display = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
        double gallons = fluidInput.AmountMg / (double)FluidConstants.MilliGallonsPerGallon;
        return $"{display} {gallons:0.###} gal";
    }
}
