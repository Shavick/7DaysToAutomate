using System;

public class XUiC_MachineRecipeCodexEntry : XUiController
{
    public MachineRecipe Recipe { get; private set; }
    public XUiC_MachineRecipeCodexList RecipeList;

    private bool isSelected;

    public void SetRecipe(MachineRecipe recipe, bool selected)
    {
        Recipe = recipe;
        isSelected = selected;
        IsDirty = true;
        RefreshBindings(true);
    }

    public override void OnPressed(int mouseButton)
    {
        base.OnPressed(mouseButton);

        if (Recipe != null)
            RecipeList?.OnRecipePressed(this);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        switch (bindingName)
        {
            case "recipe_name":
                value = XUiC_MachineRecipeCodex.GetRecipeDisplayName(Recipe);
                return true;

            case "recipe_machine":
                value = GetMachineName();
                return true;

            case "tooltip":
                value = BuildTooltip();
                return true;

            case "isselected":
                value = isSelected ? "true" : "false";
                return true;
        }

        return false;
    }

    private string BuildTooltip()
    {
        if (Recipe == null)
            return string.Empty;

        return $"{XUiC_MachineRecipeCodex.GetRecipeDisplayName(Recipe)} [{GetMachineName()}]";
    }

    private string GetMachineName()
    {
        if (Recipe == null || string.IsNullOrWhiteSpace(Recipe.Machine))
            return string.Empty;

        return XUiC_MachineRecipeCodex.GetDisplayText(Recipe.Machine);
    }
}
