public class XUiC_FluidInfuserRecipeEntry : XUiController
{
    public MachineRecipe Recipe { get; private set; }
    public XUiC_FluidInfuserRecipeList RecipeList;

    public void SetRecipe(MachineRecipe recipe)
    {
        Recipe = recipe;
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
            case "recipeicon":
                value = GetRecipeIcon();
                return true;

            case "tooltip":
                value = GetRecipeName();
                return true;
        }

        return false;
    }

    private string GetRecipeName()
    {
        if (Recipe == null)
            return string.Empty;

        if (Recipe.ItemOutputs != null && Recipe.ItemOutputs.Count > 0)
        {
            MachineRecipeItemOutput output = Recipe.ItemOutputs[0];
            ItemValue itemValue = ItemClass.GetItem(output?.ItemName ?? string.Empty, false);
            if (itemValue?.ItemClass != null)
                return itemValue.ItemClass.GetLocalizedItemName();
        }

        return !string.IsNullOrWhiteSpace(Recipe.Name) ? Localization.Get(Recipe.Name) : Recipe.NormalizedKey ?? string.Empty;
    }

    private string GetRecipeIcon()
    {
        if (Recipe?.ItemOutputs == null || Recipe.ItemOutputs.Count == 0)
            return string.Empty;

        MachineRecipeItemOutput output = Recipe.ItemOutputs[0];
        ItemValue itemValue = ItemClass.GetItem(output?.ItemName ?? string.Empty, false);
        return itemValue?.ItemClass?.GetIconName() ?? string.Empty;
    }
}
