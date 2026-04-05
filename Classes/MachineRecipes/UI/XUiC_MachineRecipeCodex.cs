using System;
using System.Collections.Generic;
using System.Globalization;

public class XUiC_MachineRecipeCodex : XUiController
{
    private sealed class OutputDisplayEntry
    {
        public bool IsItem;
        public string ItemIcon = string.Empty;
        public string FluidType = string.Empty;
        public string Label = string.Empty;
    }

    private static readonly Dictionary<string, int> MachineDefaultCraftTimeTicksByGroup =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "fluiddecanter", 10 },
            { "fluidinfuser", 10 },
            { "melter", 5 },
            { "fluid_mixer", 10 },
            { "boiler", 10 },
            { "mold", 10 }
        };

    private readonly List<string> machineGroups = new List<string>();
    private int selectedGroupIndex;
    private string selectedRecipeKey = string.Empty;

    private XUiC_MachineRecipeCodexList recipeList;

    public static void Open(EntityPlayerLocal player)
    {
        if (player?.playerUI?.windowManager == null)
            return;

        player.playerUI.windowManager.Open("MachineRecipeCodex", true, false, true);
    }

    public override void Init()
    {
        base.Init();

        recipeList = GetChildByType<XUiC_MachineRecipeCodexList>();
        recipeList?.SetOwner(this);

        HookButton("closeButton", () => xui.playerUI.windowManager.Close("MachineRecipeCodex"));
        HookButton("groupPrevButton", PrevGroup);
        HookButton("groupNextButton", NextGroup);
        HookButton("pagePrevButton", () => recipeList?.PrevPage());
        HookButton("pageNextButton", () => recipeList?.NextPage());
    }

    public override void OnOpen()
    {
        base.OnOpen();
        BuildGroups();
        selectedRecipeKey = string.Empty;
        recipeList?.RefreshData(true);
        RefreshBindings(true);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        switch (bindingName)
        {
            case "selected_machine_group":
                value = GetSelectedGroupDisplayName();
                return true;

            case "selected_recipe_name":
                value = GetSelectedRecipeDisplayName();
                return true;

            case "selected_recipe_details":
                value = BuildSelectedRecipeDetails();
                return true;

            case "main_output_title":
                value = GetMainOutputTitle();
                return true;

            case "main_output_item_icon":
                value = GetMainOutputItemIcon();
                return true;

            case "main_output_item_visible":
                value = HasMainOutputItem() ? "true" : "false";
                return true;

            case "main_output_fluid_visible":
                value = HasMainOutputFluidOrGas() ? "true" : "false";
                return true;

            case "main_output_none_visible":
                value = (!HasMainOutputItem() && !HasMainOutputFluidOrGas()) ? "true" : "false";
                return true;

            case "main_output_fluid_color":
                value = GetMainOutputFluidColor();
                return true;

            case "recipe_machine_name_header":
                value = GetRecipeMachineNameHeader();
                return true;

            case "recipe_time_text":
                value = GetRecipeTimeText();
                return true;

            case "recipe_output_title":
                value = GetSelectedRecipeDisplayName();
                return true;

            case "heat_badge_visible":
                value = GetHeatBadgeVisible();
                return true;

            case "heat_badge_text":
                value = GetHeatBadgeText();
                return true;
            case "heat_badge_tooltip":
                value = GetHeatBadgeTooltip();
                return true;

            case "input_slot1_visible":
                value = GetInputSlotVisible(0);
                return true;
            case "input_slot1_item_icon":
                value = GetInputSlotItemIcon(0);
                return true;
            case "input_slot1_item_visible":
                value = GetInputSlotItemVisible(0);
                return true;
            case "input_slot1_fluid_visible":
                value = GetInputSlotFluidVisible(0);
                return true;
            case "input_slot1_none_visible":
                value = GetInputSlotNoneVisible(0);
                return true;
            case "input_slot1_fluid_color":
                value = GetInputSlotFluidColor(0);
                return true;
            case "input_slot1_tooltip":
                value = GetInputSlotTooltip(0);
                return true;

            case "input_slot2_visible":
                value = GetInputSlotVisible(1);
                return true;
            case "input_slot2_item_icon":
                value = GetInputSlotItemIcon(1);
                return true;
            case "input_slot2_item_visible":
                value = GetInputSlotItemVisible(1);
                return true;
            case "input_slot2_fluid_visible":
                value = GetInputSlotFluidVisible(1);
                return true;
            case "input_slot2_none_visible":
                value = GetInputSlotNoneVisible(1);
                return true;
            case "input_slot2_fluid_color":
                value = GetInputSlotFluidColor(1);
                return true;
            case "input_slot2_tooltip":
                value = GetInputSlotTooltip(1);
                return true;

            case "input_slot3_visible":
                value = GetInputSlotVisible(2);
                return true;
            case "input_slot3_item_icon":
                value = GetInputSlotItemIcon(2);
                return true;
            case "input_slot3_item_visible":
                value = GetInputSlotItemVisible(2);
                return true;
            case "input_slot3_fluid_visible":
                value = GetInputSlotFluidVisible(2);
                return true;
            case "input_slot3_none_visible":
                value = GetInputSlotNoneVisible(2);
                return true;
            case "input_slot3_fluid_color":
                value = GetInputSlotFluidColor(2);
                return true;
            case "input_slot3_tooltip":
                value = GetInputSlotTooltip(2);
                return true;

            case "input_plus1_visible":
                value = GetInputPlusVisible(0);
                return true;
            case "input_plus2_visible":
                value = GetInputPlusVisible(1);
                return true;

            case "input_more_visible":
                value = GetInputMoreVisible();
                return true;
            case "input_more_text":
                value = GetInputMoreText();
                return true;
            case "input_count_two_visible":
                value = GetInputCountTwoVisible();
                return true;
            case "input_count_not_two_visible":
                value = GetInputCountNotTwoVisible();
                return true;
            case "two_input_left_item_icon":
                value = GetInputEntryItemIcon(0);
                return true;
            case "two_input_left_item_visible":
                value = GetInputEntryItemVisible(0);
                return true;
            case "two_input_left_fluid_visible":
                value = GetInputEntryFluidVisible(0);
                return true;
            case "two_input_left_none_visible":
                value = GetInputEntryNoneVisible(0);
                return true;
            case "two_input_left_fluid_color":
                value = GetInputEntryFluidColor(0);
                return true;
            case "two_input_left_tooltip":
                value = GetInputEntryTooltip(0);
                return true;
            case "two_input_right_item_icon":
                value = GetInputEntryItemIcon(1);
                return true;
            case "two_input_right_item_visible":
                value = GetInputEntryItemVisible(1);
                return true;
            case "two_input_right_fluid_visible":
                value = GetInputEntryFluidVisible(1);
                return true;
            case "two_input_right_none_visible":
                value = GetInputEntryNoneVisible(1);
                return true;
            case "two_input_right_fluid_color":
                value = GetInputEntryFluidColor(1);
                return true;
            case "two_input_right_tooltip":
                value = GetInputEntryTooltip(1);
                return true;

            case "output_slot1_visible":
                value = GetOutputSlotVisible(0);
                return true;
            case "output_slot1_item_icon":
                value = GetOutputSlotItemIcon(0);
                return true;
            case "output_slot1_item_visible":
                value = GetOutputSlotItemVisible(0);
                return true;
            case "output_slot1_fluid_visible":
                value = GetOutputSlotFluidVisible(0);
                return true;
            case "output_slot1_none_visible":
                value = GetOutputSlotNoneVisible(0);
                return true;
            case "output_slot1_fluid_color":
                value = GetOutputSlotFluidColor(0);
                return true;
            case "output_slot1_tooltip":
                value = GetOutputSlotTooltip(0);
                return true;

            case "output_slot2_visible":
                value = GetOutputSlotVisible(1);
                return true;
            case "output_slot2_item_icon":
                value = GetOutputSlotItemIcon(1);
                return true;
            case "output_slot2_item_visible":
                value = GetOutputSlotItemVisible(1);
                return true;
            case "output_slot2_fluid_visible":
                value = GetOutputSlotFluidVisible(1);
                return true;
            case "output_slot2_none_visible":
                value = GetOutputSlotNoneVisible(1);
                return true;
            case "output_slot2_fluid_color":
                value = GetOutputSlotFluidColor(1);
                return true;
            case "output_slot2_tooltip":
                value = GetOutputSlotTooltip(1);
                return true;

            case "output_slot3_visible":
                value = GetOutputSlotVisible(2);
                return true;
            case "output_slot3_item_icon":
                value = GetOutputSlotItemIcon(2);
                return true;
            case "output_slot3_item_visible":
                value = GetOutputSlotItemVisible(2);
                return true;
            case "output_slot3_fluid_visible":
                value = GetOutputSlotFluidVisible(2);
                return true;
            case "output_slot3_none_visible":
                value = GetOutputSlotNoneVisible(2);
                return true;
            case "output_slot3_fluid_color":
                value = GetOutputSlotFluidColor(2);
                return true;
            case "output_slot3_tooltip":
                value = GetOutputSlotTooltip(2);
                return true;

            case "output_slot4_visible":
                value = GetOutputSlotVisible(3);
                return true;
            case "output_slot4_item_icon":
                value = GetOutputSlotItemIcon(3);
                return true;
            case "output_slot4_item_visible":
                value = GetOutputSlotItemVisible(3);
                return true;
            case "output_slot4_fluid_visible":
                value = GetOutputSlotFluidVisible(3);
                return true;
            case "output_slot4_none_visible":
                value = GetOutputSlotNoneVisible(3);
                return true;
            case "output_slot4_fluid_color":
                value = GetOutputSlotFluidColor(3);
                return true;
            case "output_slot4_tooltip":
                value = GetOutputSlotTooltip(3);
                return true;

            case "output_slot5_visible":
                value = GetOutputSlotVisible(4);
                return true;
            case "output_slot5_item_icon":
                value = GetOutputSlotItemIcon(4);
                return true;
            case "output_slot5_item_visible":
                value = GetOutputSlotItemVisible(4);
                return true;
            case "output_slot5_fluid_visible":
                value = GetOutputSlotFluidVisible(4);
                return true;
            case "output_slot5_none_visible":
                value = GetOutputSlotNoneVisible(4);
                return true;
            case "output_slot5_fluid_color":
                value = GetOutputSlotFluidColor(4);
                return true;
            case "output_slot5_tooltip":
                value = GetOutputSlotTooltip(4);
                return true;

            case "output_plus1_visible":
                value = GetOutputPlusVisible(0);
                return true;
            case "output_plus2_visible":
                value = GetOutputPlusVisible(1);
                return true;
            case "output_plus3_visible":
                value = GetOutputPlusVisible(2);
                return true;
            case "output_plus4_visible":
                value = GetOutputPlusVisible(3);
                return true;

            case "output_more_visible":
                value = GetOutputMoreVisible();
                return true;
            case "output_more_text":
                value = GetOutputMoreText();
                return true;

            case "output_count_two_visible":
                value = GetOutputCountTwoVisible();
                return true;

            case "output_count_not_two_visible":
                value = GetOutputCountNotTwoVisible();
                return true;

            case "two_output_left_item_icon":
                value = GetOutputEntryItemIcon(0);
                return true;

            case "two_output_left_item_visible":
                value = GetOutputEntryItemVisible(0);
                return true;

            case "two_output_left_fluid_visible":
                value = GetOutputEntryFluidVisible(0);
                return true;

            case "two_output_left_none_visible":
                value = GetOutputEntryNoneVisible(0);
                return true;

            case "two_output_left_fluid_color":
                value = GetOutputEntryFluidColor(0);
                return true;

            case "two_output_left_tooltip":
                value = GetOutputEntryTooltip(0);
                return true;

            case "two_output_right_item_icon":
                value = GetOutputEntryItemIcon(1);
                return true;

            case "two_output_right_item_visible":
                value = GetOutputEntryItemVisible(1);
                return true;

            case "two_output_right_fluid_visible":
                value = GetOutputEntryFluidVisible(1);
                return true;

            case "two_output_right_none_visible":
                value = GetOutputEntryNoneVisible(1);
                return true;

            case "two_output_right_fluid_color":
                value = GetOutputEntryFluidColor(1);
                return true;

            case "two_output_right_tooltip":
                value = GetOutputEntryTooltip(1);
                return true;

            case "secondary_output_visible":
                value = HasSecondaryOutput() ? "true" : "false";
                return true;

            case "secondary_output_item_icon":
                value = GetSecondaryOutputItemIcon();
                return true;

            case "secondary_output_item_visible":
                value = HasSecondaryOutputItem() ? "true" : "false";
                return true;

            case "secondary_output_fluid_visible":
                value = HasSecondaryOutputFluidOrGas() ? "true" : "false";
                return true;

            case "secondary_output_fluid_color":
                value = GetSecondaryOutputFluidColor();
                return true;

            case "secondary_output_text":
                value = GetSecondaryOutputText();
                return true;

            case "item_input_card_text":
                value = BuildItemInputCardText();
                return true;

            case "fluid_input_card_text":
                value = BuildFluidInputCardText();
                return true;

            case "item_output_card_text":
                value = BuildItemOutputCardText();
                return true;

            case "fluid_output_card_text":
                value = BuildFluidOutputCardText();
                return true;

            case "gas_output_card_text":
                value = BuildGasOutputCardText();
                return true;

            case "item_input_icon_color":
                value = "255,255,255,255";
                return true;

            case "item_input_item_icon":
                value = GetItemInputIcon();
                return true;

            case "item_input_item_visible":
                value = HasItemInput() ? "true" : "false";
                return true;

            case "item_input_none_visible":
                value = HasItemInput() ? "false" : "true";
                return true;

            case "fluid_input_icon_color":
                value = GetFluidInputIconColor();
                return true;

            case "item_output_icon_color":
                value = "255,255,255,255";
                return true;

            case "item_output_item_icon":
                value = GetItemOutputIcon();
                return true;

            case "item_output_item_visible":
                value = HasItemOutput() ? "true" : "false";
                return true;

            case "item_output_none_visible":
                value = HasItemOutput() ? "false" : "true";
                return true;

            case "fluid_output_icon_color":
                value = GetFluidOutputIconColor();
                return true;

            case "gas_output_icon_color":
                value = GetGasOutputIconColor();
                return true;

            case "page_info":
                value = recipeList?.GetPageText() ?? "Page 1/1";
                return true;

            case "recipe_count":
                value = recipeList?.GetCountText() ?? "0 recipes";
                return true;
        }

        return false;
    }

    public string GetMachineGroupFilterCsv()
    {
        if (selectedGroupIndex <= 0 || selectedGroupIndex >= machineGroups.Count)
            return string.Empty;

        return machineGroups[selectedGroupIndex];
    }

    public bool IsSelectedRecipe(string normalizedKey)
    {
        return !string.IsNullOrEmpty(normalizedKey) && string.Equals(selectedRecipeKey, normalizedKey, StringComparison.Ordinal);
    }

    public void SetSelectedRecipe(string normalizedKey)
    {
        selectedRecipeKey = normalizedKey ?? string.Empty;
        RefreshBindings(true);
    }

    public void OnListUpdated()
    {
        if (!string.IsNullOrEmpty(selectedRecipeKey) && !recipeList.HasRecipe(selectedRecipeKey))
            selectedRecipeKey = string.Empty;

        RefreshBindings(true);
    }

    private void PrevGroup()
    {
        if (machineGroups.Count <= 0)
            return;

        selectedGroupIndex--;
        if (selectedGroupIndex < 0)
            selectedGroupIndex = machineGroups.Count - 1;

        selectedRecipeKey = string.Empty;
        recipeList?.RefreshData(true);
        RefreshBindings(true);
    }

    private void NextGroup()
    {
        if (machineGroups.Count <= 0)
            return;

        selectedGroupIndex++;
        if (selectedGroupIndex >= machineGroups.Count)
            selectedGroupIndex = 0;

        selectedRecipeKey = string.Empty;
        recipeList?.RefreshData(true);
        RefreshBindings(true);
    }

    private void BuildGroups()
    {
        machineGroups.Clear();
        machineGroups.Add(string.Empty);

        List<string> groups = MachineRecipeRegistry.GetAllMachineGroups();
        for (int i = 0; i < groups.Count; i++)
        {
            string group = groups[i];
            if (!string.IsNullOrWhiteSpace(group))
                machineGroups.Add(group);
        }

        selectedGroupIndex = Math.Max(0, Math.Min(selectedGroupIndex, machineGroups.Count - 1));
    }

    private string GetSelectedGroupDisplayName()
    {
        if (selectedGroupIndex <= 0 || selectedGroupIndex >= machineGroups.Count)
            return "All Machines";

        return ToDisplayText(machineGroups[selectedGroupIndex]);
    }

    private string GetSelectedRecipeDisplayName()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe == null)
            return "Select a recipe";

        return GetRecipeDisplayName(recipe);
    }

    private string BuildSelectedRecipeDetails()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe == null)
            return "Pick a recipe from the list to view details.";

        string machine = ToDisplayText(recipe.Machine);
        int craftTimeSeconds = recipe.CraftTimeTicks.HasValue
            ? Math.Max(1, recipe.CraftTimeTicks.Value)
            : GetBlockDefaultCraftTimeTicks(recipe.Machine);
        string craftTime = craftTimeSeconds == 1
            ? "1 second"
            : $"{craftTimeSeconds} seconds";

        return
            $"Machine: {machine}\n" +
            $"Time: {craftTime}\n" +
            $"Heat: {recipe.RequiredHeat}";
    }

    private MachineRecipe GetSelectedRecipe()
    {
        if (string.IsNullOrEmpty(selectedRecipeKey))
            return null;

        return MachineRecipeRegistry.TryGetRecipeByKey(selectedRecipeKey, out MachineRecipe recipe) ? recipe : null;
    }

    private void HookButton(string id, Action handler)
    {
        XUiV_Button button = GetChildById(id)?.ViewComponent as XUiV_Button;
        if (button != null)
            button.Controller.OnPress += (c, m) => handler?.Invoke();
    }

    public static string GetRecipeDisplayName(MachineRecipe recipe)
    {
        if (recipe == null)
            return string.Empty;

        if (recipe.ItemOutputs != null && recipe.ItemOutputs.Count > 0)
        {
            MachineRecipeItemOutput output = recipe.ItemOutputs[0];
            ItemValue itemValue = ItemClass.GetItem(output?.ItemName ?? string.Empty, false);
            if (itemValue?.ItemClass != null)
                return itemValue.ItemClass.GetLocalizedItemName();
        }

        if (!string.IsNullOrWhiteSpace(recipe.Name))
            return Localization.Get(recipe.Name);

        if (recipe.FluidOutputs != null && recipe.FluidOutputs.Count > 0)
            return ToDisplayText(recipe.FluidOutputs[0].Type);

        if (recipe.GasOutputs != null && recipe.GasOutputs.Count > 0)
            return ToDisplayText(recipe.GasOutputs[0].Type);

        return recipe.NormalizedKey ?? string.Empty;
    }

    public static string GetSearchableText(MachineRecipe recipe)
    {
        if (recipe == null)
            return string.Empty;

        List<string> parts = new List<string>
        {
            GetRecipeDisplayName(recipe),
            recipe.Name ?? string.Empty,
            recipe.Machine ?? string.Empty
        };

        AppendItemNames(parts, recipe.Inputs);
        AppendFluidInputNames(parts, recipe.FluidInputs);
        AppendItemOutputNames(parts, recipe.ItemOutputs);
        AppendFluidOutputNames(parts, recipe.FluidOutputs);
        AppendGasOutputNames(parts, recipe.GasOutputs);

        return string.Join(" ", parts.ToArray());
    }

    private static void AppendItemNames(List<string> target, IReadOnlyList<MachineRecipeInput> items)
    {
        if (items == null)
            return;

        for (int i = 0; i < items.Count; i++)
            target.Add(GetItemDisplayName(items[i]?.ItemName));
    }

    private static void AppendFluidInputNames(List<string> target, IReadOnlyList<MachineRecipeFluidInput> fluids)
    {
        if (fluids == null)
            return;

        for (int i = 0; i < fluids.Count; i++)
            target.Add(ToDisplayText(fluids[i]?.Type));
    }

    private static void AppendItemOutputNames(List<string> target, IReadOnlyList<MachineRecipeItemOutput> items)
    {
        if (items == null)
            return;

        for (int i = 0; i < items.Count; i++)
            target.Add(GetItemDisplayName(items[i]?.ItemName));
    }

    private static void AppendFluidOutputNames(List<string> target, IReadOnlyList<MachineRecipeFluidOutput> fluids)
    {
        if (fluids == null)
            return;

        for (int i = 0; i < fluids.Count; i++)
            target.Add(ToDisplayText(fluids[i]?.Type));
    }

    private static void AppendGasOutputNames(List<string> target, IReadOnlyList<MachineRecipeGasOutput> gases)
    {
        if (gases == null)
            return;

        for (int i = 0; i < gases.Count; i++)
            target.Add(ToDisplayText(gases[i]?.Type));
    }

    private static string FormatItemInputs(IReadOnlyList<MachineRecipeInput> inputs)
    {
        if (inputs == null || inputs.Count == 0)
            return "None";

        List<string> parts = new List<string>();
        for (int i = 0; i < inputs.Count; i++)
        {
            MachineRecipeInput input = inputs[i];
            parts.Add($"{GetItemDisplayName(input?.ItemName)} x{input?.Count ?? 0}");
        }

        return string.Join(", ", parts.ToArray());
    }

    private static string FormatFluidInputs(IReadOnlyList<MachineRecipeFluidInput> inputs)
    {
        if (inputs == null || inputs.Count == 0)
            return "None";

        List<string> parts = new List<string>();
        for (int i = 0; i < inputs.Count; i++)
        {
            MachineRecipeFluidInput input = inputs[i];
            double gallons = (input?.AmountMg ?? 0) / (double)FluidConstants.MilliGallonsPerGallon;
            parts.Add($"{ToDisplayText(input?.Type)} {gallons.ToString("0.###", CultureInfo.InvariantCulture)} gal");
        }

        return string.Join(", ", parts.ToArray());
    }

    private static string FormatItemOutputs(IReadOnlyList<MachineRecipeItemOutput> outputs)
    {
        if (outputs == null || outputs.Count == 0)
            return "None";

        List<string> parts = new List<string>();
        for (int i = 0; i < outputs.Count; i++)
        {
            MachineRecipeItemOutput output = outputs[i];
            parts.Add($"{GetItemDisplayName(output?.ItemName)} x{output?.Count ?? 0}");
        }

        return string.Join(", ", parts.ToArray());
    }

    private static string FormatFluidOutputs(IReadOnlyList<MachineRecipeFluidOutput> outputs)
    {
        if (outputs == null || outputs.Count == 0)
            return "None";

        List<string> parts = new List<string>();
        for (int i = 0; i < outputs.Count; i++)
        {
            MachineRecipeFluidOutput output = outputs[i];
            double gallons = (output?.AmountMg ?? 0) / (double)FluidConstants.MilliGallonsPerGallon;
            parts.Add($"{ToDisplayText(output?.Type)} {gallons.ToString("0.###", CultureInfo.InvariantCulture)} gal");
        }

        return string.Join(", ", parts.ToArray());
    }

    private static string FormatGasOutputs(IReadOnlyList<MachineRecipeGasOutput> outputs)
    {
        if (outputs == null || outputs.Count == 0)
            return "None";

        List<string> parts = new List<string>();
        for (int i = 0; i < outputs.Count; i++)
        {
            MachineRecipeGasOutput output = outputs[i];
            double gallons = (output?.AmountMg ?? 0) / (double)FluidConstants.MilliGallonsPerGallon;
            parts.Add($"{ToDisplayText(output?.Type)} {gallons.ToString("0.###", CultureInfo.InvariantCulture)} gal");
        }

        return string.Join(", ", parts.ToArray());
    }

    private static string GetItemDisplayName(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return string.Empty;

        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        if (itemValue?.ItemClass != null)
            return itemValue.ItemClass.GetLocalizedItemName();

        return itemName;
    }

    public static string GetDisplayText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string trimmed = raw.Trim();
        switch (trimmed.ToLowerInvariant())
        {
            case "fluidinfuser":
                return "Fluid Infuser";
            case "fluiddecanter":
                return "Fluid Decanter";
            case "fluidmixer":
            case "fluid_mixer":
                return "Fluid Mixer";
        }

        string normalized = trimmed.Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string ToDisplayText(string raw)
    {
        return GetDisplayText(raw);
    }

    private static int GetBlockDefaultCraftTimeTicks(string machineGroup)
    {
        if (!string.IsNullOrWhiteSpace(machineGroup) &&
            MachineDefaultCraftTimeTicksByGroup.TryGetValue(machineGroup.Trim(), out int ticks))
            return Math.Max(1, ticks);

        return 20;
    }

    private string GetMainOutputTitle()
    {
        if (!TryGetOutputEntry(0, out OutputDisplayEntry output))
            return "Main Output: None";

        return $"Main Output: {output.Label}";
    }

    private string GetMainOutputItemIcon()
    {
        if (!TryGetOutputEntry(0, out OutputDisplayEntry output) || !output.IsItem)
            return string.Empty;

        return output.ItemIcon ?? string.Empty;
    }

    private bool HasMainOutputItem()
    {
        return TryGetOutputEntry(0, out OutputDisplayEntry output) && output.IsItem;
    }

    private bool HasMainOutputFluidOrGas()
    {
        return TryGetOutputEntry(0, out OutputDisplayEntry output) && !output.IsItem;
    }

    private string GetMainOutputFluidColor()
    {
        if (TryGetOutputEntry(0, out OutputDisplayEntry output) && !output.IsItem)
            return GetFluidTypeColor(output.FluidType);

        return "255,255,255,255";
    }

    private bool HasSecondaryOutput()
    {
        return TryGetOutputEntry(1, out _);
    }

    private bool HasSecondaryOutputItem()
    {
        return TryGetOutputEntry(1, out OutputDisplayEntry output) && output.IsItem;
    }

    private bool HasSecondaryOutputFluidOrGas()
    {
        return TryGetOutputEntry(1, out OutputDisplayEntry output) && !output.IsItem;
    }

    private string GetSecondaryOutputItemIcon()
    {
        if (!TryGetOutputEntry(1, out OutputDisplayEntry output) || !output.IsItem)
            return string.Empty;

        return output.ItemIcon ?? string.Empty;
    }

    private string GetSecondaryOutputFluidColor()
    {
        if (TryGetOutputEntry(1, out OutputDisplayEntry output) && !output.IsItem)
            return GetFluidTypeColor(output.FluidType);

        return "255,255,255,255";
    }

    private string GetSecondaryOutputText()
    {
        if (!TryGetOutputEntry(1, out OutputDisplayEntry output))
            return string.Empty;

        int total = BuildOutputEntries().Count;
        if (total > 2)
            return $"Also: {output.Label} (+{total - 2} more)";

        return $"Also: {output.Label}";
    }

    private string BuildItemInputCardText()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.Inputs == null || recipe.Inputs.Count == 0)
            return "Item Input\nNone";

        MachineRecipeInput input = recipe.Inputs[0];
        return $"Item Input\n{GetItemDisplayName(input?.ItemName)} x{input?.Count ?? 0}";
    }

    private string BuildFluidInputCardText()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.FluidInputs == null || recipe.FluidInputs.Count == 0)
            return "Fluid Input\nNone";

        MachineRecipeFluidInput input = recipe.FluidInputs[0];
        double gallons = (input?.AmountMg ?? 0) / (double)FluidConstants.MilliGallonsPerGallon;
        return $"Fluid Input\n{ToDisplayText(input?.Type)} {gallons.ToString("0.###", CultureInfo.InvariantCulture)} gal";
    }

    private string BuildItemOutputCardText()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.ItemOutputs == null || recipe.ItemOutputs.Count == 0)
            return "Item Output\nNone";

        MachineRecipeItemOutput output = recipe.ItemOutputs[0];
        return $"Item Output\n{GetItemDisplayName(output?.ItemName)} x{output?.Count ?? 0}";
    }

    private string BuildFluidOutputCardText()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.FluidOutputs == null || recipe.FluidOutputs.Count == 0)
            return "Fluid Output\nNone";

        MachineRecipeFluidOutput output = recipe.FluidOutputs[0];
        double gallons = (output?.AmountMg ?? 0) / (double)FluidConstants.MilliGallonsPerGallon;
        return $"Fluid Output\n{ToDisplayText(output?.Type)} {gallons.ToString("0.###", CultureInfo.InvariantCulture)} gal";
    }

    private string BuildGasOutputCardText()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.GasOutputs == null || recipe.GasOutputs.Count == 0)
            return "Gas Output\nNone";

        MachineRecipeGasOutput output = recipe.GasOutputs[0];
        double gallons = (output?.AmountMg ?? 0) / (double)FluidConstants.MilliGallonsPerGallon;
        return $"Gas Output\n{ToDisplayText(output?.Type)} {gallons.ToString("0.###", CultureInfo.InvariantCulture)} gal";
    }

    private string GetFluidInputIconColor()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.FluidInputs == null || recipe.FluidInputs.Count == 0)
            return "255,255,255,255";

        return GetFluidTypeColor(recipe.FluidInputs[0]?.Type);
    }

    private string GetFluidOutputIconColor()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.FluidOutputs == null || recipe.FluidOutputs.Count == 0)
            return "255,255,255,255";

        return GetFluidTypeColor(recipe.FluidOutputs[0]?.Type);
    }

    private string GetGasOutputIconColor()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.GasOutputs == null || recipe.GasOutputs.Count == 0)
            return "255,255,255,255";

        return GetFluidTypeColor(recipe.GasOutputs[0]?.Type);
    }

    private string GetItemInputIcon()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.Inputs == null || recipe.Inputs.Count == 0)
            return string.Empty;

        string itemName = recipe.Inputs[0]?.ItemName ?? string.Empty;
        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        return itemValue?.ItemClass?.GetIconName() ?? string.Empty;
    }

    private bool HasItemInput()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        return recipe?.Inputs != null && recipe.Inputs.Count > 0;
    }

    private string GetItemOutputIcon()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe?.ItemOutputs == null || recipe.ItemOutputs.Count == 0)
            return string.Empty;

        string itemName = recipe.ItemOutputs[0]?.ItemName ?? string.Empty;
        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        return itemValue?.ItemClass?.GetIconName() ?? string.Empty;
    }

    private bool HasItemOutput()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        return recipe?.ItemOutputs != null && recipe.ItemOutputs.Count > 0;
    }

    private string GetRecipeMachineNameHeader()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe == null)
            return "Select A Recipe";

        return ToDisplayText(recipe.Machine);
    }

    private string GetRecipeTimeText()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe == null)
            return string.Empty;

        int craftTimeSeconds = recipe.CraftTimeTicks.HasValue
            ? Math.Max(1, recipe.CraftTimeTicks.Value)
            : GetBlockDefaultCraftTimeTicks(recipe.Machine);

        return $"({craftTimeSeconds}s)";
    }

    private string GetHeatBadgeVisible()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        return (recipe != null && recipe.RequiredHeat > 0) ? "true" : "false";
    }

    private string GetHeatBadgeText()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe == null || recipe.RequiredHeat <= 0)
            return string.Empty;

        return $"Heat {recipe.RequiredHeat}";
    }

    private string GetHeatBadgeTooltip()
    {
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe == null || recipe.RequiredHeat <= 0)
            return string.Empty;

        return $"Place a block that produces {recipe.RequiredHeat} Heat under the {ToDisplayText(recipe.Machine)}";
    }

    private string GetInputSlotVisible(int visualSlot)
    {
        return TryGetInputEntryForVisualSlot(visualSlot, out _) ? "true" : "false";
    }

    private string GetInputSlotItemIcon(int visualSlot)
    {
        if (!TryGetInputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry) || !entry.IsItem)
            return string.Empty;

        return entry.ItemIcon ?? string.Empty;
    }

    private string GetInputSlotItemVisible(int visualSlot)
    {
        return (TryGetInputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry) && entry.IsItem)
            ? "true"
            : "false";
    }

    private string GetInputSlotFluidVisible(int visualSlot)
    {
        return (TryGetInputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry) && !entry.IsItem && !string.IsNullOrEmpty(entry.FluidType))
            ? "true"
            : "false";
    }

    private string GetInputSlotNoneVisible(int visualSlot)
    {
        if (!TryGetInputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry))
            return "true";

        return (!entry.IsItem && string.IsNullOrEmpty(entry.FluidType)) ? "true" : "false";
    }

    private string GetInputSlotFluidColor(int visualSlot)
    {
        if (!TryGetInputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry) || entry.IsItem)
            return "255,255,255,255";

        return GetFluidTypeColor(entry.FluidType);
    }

    private string GetInputSlotTooltip(int visualSlot)
    {
        if (!TryGetInputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry))
            return string.Empty;

        return entry.Label ?? string.Empty;
    }

    private string GetInputPlusVisible(int plusIndex)
    {
        int left = plusIndex;
        int right = plusIndex + 1;
        bool leftVisible = TryGetInputEntryForVisualSlot(left, out _);
        bool rightVisible = TryGetInputEntryForVisualSlot(right, out _);
        return (leftVisible && rightVisible) ? "true" : "false";
    }

    private string GetInputMoreVisible()
    {
        List<OutputDisplayEntry> all = BuildInputEntries();
        return all.Count > 3 ? "true" : "false";
    }

    private string GetInputMoreText()
    {
        List<OutputDisplayEntry> all = BuildInputEntries();
        if (all.Count <= 3)
            return string.Empty;

        return $"+{all.Count - 3} more";
    }

    private string GetInputCountTwoVisible()
    {
        return BuildInputEntries().Count == 2 ? "true" : "false";
    }

    private string GetInputCountNotTwoVisible()
    {
        return BuildInputEntries().Count == 2 ? "false" : "true";
    }

    private string GetInputEntryItemIcon(int index)
    {
        if (!TryGetInputEntry(index, out OutputDisplayEntry entry) || !entry.IsItem)
            return string.Empty;

        return entry.ItemIcon ?? string.Empty;
    }

    private string GetInputEntryItemVisible(int index)
    {
        return (TryGetInputEntry(index, out OutputDisplayEntry entry) && entry.IsItem) ? "true" : "false";
    }

    private string GetInputEntryFluidVisible(int index)
    {
        return (TryGetInputEntry(index, out OutputDisplayEntry entry) && !entry.IsItem && !string.IsNullOrEmpty(entry.FluidType))
            ? "true"
            : "false";
    }

    private string GetInputEntryNoneVisible(int index)
    {
        if (!TryGetInputEntry(index, out OutputDisplayEntry entry))
            return "true";

        return (!entry.IsItem && string.IsNullOrEmpty(entry.FluidType)) ? "true" : "false";
    }

    private string GetInputEntryFluidColor(int index)
    {
        if (!TryGetInputEntry(index, out OutputDisplayEntry entry) || entry.IsItem)
            return "255,255,255,255";

        return GetFluidTypeColor(entry.FluidType);
    }

    private string GetInputEntryTooltip(int index)
    {
        if (!TryGetInputEntry(index, out OutputDisplayEntry entry))
            return string.Empty;

        return entry.Label ?? string.Empty;
    }

    private bool TryGetInputEntry(int index, out OutputDisplayEntry entry)
    {
        entry = null;
        if (index < 0)
            return false;

        List<OutputDisplayEntry> entries = BuildInputEntries();
        if (entries.Count <= index)
            return false;

        entry = entries[index];
        return entry != null;
    }

    private string GetOutputSlotVisible(int visualSlot)
    {
        return TryGetOutputEntryForVisualSlot(visualSlot, out _, out _, out _, out _) ? "true" : "false";
    }

    private string GetOutputSlotItemIcon(int visualSlot)
    {
        if (!TryGetOutputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry, out _, out _, out _) || !entry.IsItem)
            return string.Empty;

        return entry.ItemIcon ?? string.Empty;
    }

    private string GetOutputSlotItemVisible(int visualSlot)
    {
        return (TryGetOutputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry, out _, out _, out _) && entry.IsItem)
            ? "true"
            : "false";
    }

    private string GetOutputSlotFluidVisible(int visualSlot)
    {
        return (TryGetOutputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry, out _, out _, out _) && !entry.IsItem)
            ? "true"
            : "false";
    }

    private string GetOutputSlotNoneVisible(int visualSlot)
    {
        return TryGetOutputEntryForVisualSlot(visualSlot, out _, out _, out _, out _) ? "false" : "true";
    }

    private string GetOutputSlotFluidColor(int visualSlot)
    {
        if (!TryGetOutputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry, out _, out _, out _) || entry.IsItem)
            return "255,255,255,255";

        return GetFluidTypeColor(entry.FluidType);
    }

    private string GetOutputSlotTooltip(int visualSlot)
    {
        if (!TryGetOutputEntryForVisualSlot(visualSlot, out OutputDisplayEntry entry, out _, out _, out _))
            return string.Empty;

        return entry.Label ?? string.Empty;
    }

    private string GetOutputPlusVisible(int plusIndex)
    {
        int left = plusIndex;
        int right = plusIndex + 1;
        bool leftVisible = TryGetOutputEntryForVisualSlot(left, out _, out _, out _, out _);
        bool rightVisible = TryGetOutputEntryForVisualSlot(right, out _, out _, out _, out _);
        return (leftVisible && rightVisible) ? "true" : "false";
    }

    private string GetOutputMoreVisible()
    {
        List<OutputDisplayEntry> all = BuildOutputEntries();
        return all.Count > 3 ? "true" : "false";
    }

    private string GetOutputMoreText()
    {
        List<OutputDisplayEntry> all = BuildOutputEntries();
        if (all.Count <= 3)
            return string.Empty;

        return $"+{all.Count - 3} more";
    }

    private string GetOutputCountTwoVisible()
    {
        return BuildOutputEntries().Count == 2 ? "true" : "false";
    }

    private string GetOutputCountNotTwoVisible()
    {
        return BuildOutputEntries().Count == 2 ? "false" : "true";
    }

    private string GetOutputEntryItemIcon(int index)
    {
        if (!TryGetOutputEntry(index, out OutputDisplayEntry entry) || !entry.IsItem)
            return string.Empty;

        return entry.ItemIcon ?? string.Empty;
    }

    private string GetOutputEntryItemVisible(int index)
    {
        return (TryGetOutputEntry(index, out OutputDisplayEntry entry) && entry.IsItem) ? "true" : "false";
    }

    private string GetOutputEntryFluidVisible(int index)
    {
        return (TryGetOutputEntry(index, out OutputDisplayEntry entry) && !entry.IsItem) ? "true" : "false";
    }

    private string GetOutputEntryNoneVisible(int index)
    {
        return TryGetOutputEntry(index, out _) ? "false" : "true";
    }

    private string GetOutputEntryFluidColor(int index)
    {
        if (!TryGetOutputEntry(index, out OutputDisplayEntry entry) || entry.IsItem)
            return "255,255,255,255";

        return GetFluidTypeColor(entry.FluidType);
    }

    private string GetOutputEntryTooltip(int index)
    {
        if (!TryGetOutputEntry(index, out OutputDisplayEntry entry))
            return string.Empty;

        return entry.Label ?? string.Empty;
    }

    private bool TryGetOutputEntry(int index, out OutputDisplayEntry entry)
    {
        entry = null;
        if (index < 0)
            return false;

        List<OutputDisplayEntry> entries = BuildOutputEntries();
        if (entries.Count <= index)
            return false;

        entry = entries[index];
        return entry != null;
    }

    private bool TryGetOutputEntryForVisualSlot(
        int visualSlot,
        out OutputDisplayEntry entry,
        out int logicalIndex,
        out int displayCount,
        out int totalCount)
    {
        entry = null;
        logicalIndex = -1;
        displayCount = 0;
        totalCount = 0;

        if (visualSlot < 0 || visualSlot >= 3)
            return false;

        List<OutputDisplayEntry> all = BuildOutputEntries();
        totalCount = all.Count;
        if (all.Count <= 0)
            return false;

        displayCount = Math.Min(3, all.Count);
        int start = (3 - displayCount) / 2;
        int endExclusive = start + displayCount;
        if (visualSlot < start || visualSlot >= endExclusive)
            return false;

        logicalIndex = visualSlot - start;
        entry = all[logicalIndex];
        return entry != null;
    }

    private bool TryGetInputEntryForVisualSlot(int visualSlot, out OutputDisplayEntry entry)
    {
        entry = null;

        if (visualSlot < 0 || visualSlot >= 3)
            return false;

        List<OutputDisplayEntry> all = BuildInputEntries();
        if (all.Count <= 0)
            return false;

        int displayCount = Math.Min(3, all.Count);
        int start = (3 - displayCount) / 2;
        int endExclusive = start + displayCount;
        if (visualSlot < start || visualSlot >= endExclusive)
            return false;

        int logicalIndex = visualSlot - start;
        entry = all[logicalIndex];
        return entry != null;
    }

    private List<OutputDisplayEntry> BuildInputEntries()
    {
        List<OutputDisplayEntry> entries = new List<OutputDisplayEntry>();
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe == null)
            return entries;

        if (recipe.Inputs != null)
        {
            for (int i = 0; i < recipe.Inputs.Count; i++)
            {
                MachineRecipeInput input = recipe.Inputs[i];
                string itemName = input?.ItemName ?? string.Empty;
                string icon = string.Empty;
                ItemValue itemValue = ItemClass.GetItem(itemName, false);
                if (itemValue?.ItemClass != null)
                    icon = itemValue.ItemClass.GetIconName();

                entries.Add(new OutputDisplayEntry
                {
                    IsItem = true,
                    ItemIcon = icon ?? string.Empty,
                    Label = $"{GetItemDisplayName(itemName)} x{input?.Count ?? 0}"
                });
            }
        }

        if (recipe.FluidInputs != null)
        {
            for (int i = 0; i < recipe.FluidInputs.Count; i++)
            {
                MachineRecipeFluidInput input = recipe.FluidInputs[i];
                double gallons = (input?.AmountMg ?? 0) / (double)FluidConstants.MilliGallonsPerGallon;
                entries.Add(new OutputDisplayEntry
                {
                    IsItem = false,
                    FluidType = input?.Type ?? string.Empty,
                    Label = $"{ToDisplayText(input?.Type)} {gallons.ToString("0.###", CultureInfo.InvariantCulture)} gal"
                });
            }
        }

        if (entries.Count == 0)
        {
            entries.Add(new OutputDisplayEntry
            {
                IsItem = false,
                FluidType = string.Empty,
                Label = "None"
            });
        }

        return entries;
    }

    private List<OutputDisplayEntry> BuildOutputEntries()
    {
        List<OutputDisplayEntry> entries = new List<OutputDisplayEntry>();
        MachineRecipe recipe = GetSelectedRecipe();
        if (recipe == null)
            return entries;

        if (recipe.ItemOutputs != null)
        {
            for (int i = 0; i < recipe.ItemOutputs.Count; i++)
            {
                MachineRecipeItemOutput output = recipe.ItemOutputs[i];
                string itemName = output?.ItemName ?? string.Empty;
                string icon = string.Empty;
                ItemValue itemValue = ItemClass.GetItem(itemName, false);
                if (itemValue?.ItemClass != null)
                    icon = itemValue.ItemClass.GetIconName();

                entries.Add(new OutputDisplayEntry
                {
                    IsItem = true,
                    ItemIcon = icon ?? string.Empty,
                    Label = $"{GetItemDisplayName(itemName)} x{output?.Count ?? 0}"
                });
            }
        }

        if (recipe.FluidOutputs != null)
        {
            for (int i = 0; i < recipe.FluidOutputs.Count; i++)
            {
                MachineRecipeFluidOutput output = recipe.FluidOutputs[i];
                double gallons = (output?.AmountMg ?? 0) / (double)FluidConstants.MilliGallonsPerGallon;
                entries.Add(new OutputDisplayEntry
                {
                    IsItem = false,
                    FluidType = output?.Type ?? string.Empty,
                    Label = $"{ToDisplayText(output?.Type)} {gallons.ToString("0.###", CultureInfo.InvariantCulture)} gal"
                });
            }
        }

        if (recipe.GasOutputs != null)
        {
            for (int i = 0; i < recipe.GasOutputs.Count; i++)
            {
                MachineRecipeGasOutput output = recipe.GasOutputs[i];
                double gallons = (output?.AmountMg ?? 0) / (double)FluidConstants.MilliGallonsPerGallon;
                entries.Add(new OutputDisplayEntry
                {
                    IsItem = false,
                    FluidType = output?.Type ?? string.Empty,
                    Label = $"{ToDisplayText(output?.Type)} {gallons.ToString("0.###", CultureInfo.InvariantCulture)} gal"
                });
            }
        }

        return entries;
    }

    private static string GetFluidTypeColor(string fluidType)
    {
        string t = (fluidType ?? string.Empty).Trim().ToLowerInvariant();
        switch (t)
        {
            case "water":
                return "70,170,255,255";
            case "murky_water":
                return "125,150,95,255";
            case "gas":
                return "255,210,70,255";
            case "molten_oil":
            case "oil":
                return "25,25,25,255";
            case "molten_iron":
            case "molten_iron_alloy":
                return "185,185,185,255";
            case "molten_steel":
            case "molten_steel_alloy":
                return "145,145,160,255";
            case "molten_lead":
            case "molten_lead_alloy":
                return "95,105,135,255";
            case "molten_brass":
                return "190,145,60,255";
            case "molten_clay":
            case "molten_stone":
                return "150,120,95,255";
            case "molten_glass":
                return "180,220,220,255";
            case "molten_plastic":
                return "235,235,235,255";
            case "glue":
            case "glue_resin":
                return "220,210,150,255";
            case "collagen":
                return "210,185,155,255";
            default:
                return "255,255,255,255";
        }
    }
}
