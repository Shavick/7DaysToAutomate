using System;
using System.Collections.Generic;

public class XUiC_FluidInfuserRecipeList : XUiController
{
    private TileEntityFluidInfuser te;
    private Vector3i blockPos;
    private XUiC_TextInput txtInput;
    private string searchTerm = string.Empty;
    private XUiC_FluidInfuserInfo owner;

    public XUiC_FluidInfuserRecipeEntry[] recipeControls;
    public List<MachineRecipe> filteredRecipes = new List<MachineRecipe>();

    public void SetOwner(XUiC_FluidInfuserInfo controller)
    {
        owner = controller;
    }

    public void SetContext(TileEntityFluidInfuser infuser, Vector3i pos)
    {
        te = infuser;
        blockPos = pos;
        BuildRecipeList();
        IsDirty = true;
    }

    public override void Init()
    {
        base.Init();
        recipeControls = GetChildrenByType<XUiC_FluidInfuserRecipeEntry>();
        for (int i = 0; i < recipeControls.Length; i++)
        {
            recipeControls[i].RecipeList = this;
        }

        txtInput = windowGroup.Controller.GetChildById("recipeSearchInput") as XUiC_TextInput;
        if (txtInput != null)
        {
            txtInput.OnChangeHandler += HandleSearchChanged;
            txtInput.OnSubmitHandler += HandleSearchSubmitted;
        }

        IsDirty = true;
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        if (!IsDirty)
            return;

        for (int i = 0; i < recipeControls.Length; i++)
        {
            if (i < filteredRecipes.Count)
            {
                recipeControls[i].SetRecipe(filteredRecipes[i]);
                recipeControls[i].ViewComponent.Enabled = true;
                recipeControls[i].ViewComponent.IsVisible = true;
            }
            else
            {
                recipeControls[i].SetRecipe(null);
                recipeControls[i].ViewComponent.Enabled = false;
                recipeControls[i].ViewComponent.IsVisible = false;
            }
        }

        IsDirty = false;
    }

    public void SelectRecipeByKey(string recipeKey, bool notifyOwner)
    {
        if (!notifyOwner || owner == null)
            return;

        owner.SetPendingRecipe(recipeKey);
    }

    public void OnRecipePressed(XUiC_FluidInfuserRecipeEntry entry)
    {
        if (entry?.Recipe == null || owner == null)
            return;

        owner.SetPendingRecipe(entry.Recipe.NormalizedKey ?? string.Empty);
    }

    private void HandleSearchChanged(XUiController sender, string text, bool fromCode = false)
    {
        searchTerm = text ?? string.Empty;
        BuildRecipeList();
    }

    private void HandleSearchSubmitted(XUiController sender, string text)
    {
        searchTerm = text ?? string.Empty;
        BuildRecipeList();
    }

    private void BuildRecipeList()
    {
        filteredRecipes.Clear();
        if (te == null)
            return;

        List<MachineRecipe> recipes = te.GetAvailableRecipes();
        bool hasSearch = !string.IsNullOrWhiteSpace(searchTerm);
        for (int i = 0; i < recipes.Count; i++)
        {
            MachineRecipe recipe = recipes[i];
            if (recipe == null)
                continue;

            if (hasSearch)
            {
                string label = GetRecipeLabel(recipe);
                if (label.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }

            filteredRecipes.Add(recipe);
        }

        IsDirty = true;
    }

    private static string GetRecipeLabel(MachineRecipe recipe)
    {
        if (recipe?.ItemOutputs != null && recipe.ItemOutputs.Count > 0)
        {
            MachineRecipeItemOutput output = recipe.ItemOutputs[0];
            ItemValue itemValue = ItemClass.GetItem(output?.ItemName ?? string.Empty, false);
            if (itemValue?.ItemClass != null)
                return itemValue.ItemClass.GetLocalizedItemName();
        }

        return recipe?.Name ?? recipe?.NormalizedKey ?? string.Empty;
    }
}
