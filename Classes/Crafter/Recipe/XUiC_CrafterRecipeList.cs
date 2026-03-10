using System;
using System.Collections.Generic;

public class XUiC_CrafterRecipeList : XUiController
{
    public Recipe CurrentRecipe { get; private set; }

    public XUiC_CrafterRecipeEntry[] recipeControls;
    public XUiC_CrafterIngredientsList ingredientsList;
    public XUiC_UniversalCrafter crafterUI;

    private XUiC_TextInput txtInput;
    private string searchTerm = string.Empty;

    private bool hasRestoredRecipe = false;

    public int length;

    public struct RecipeInfo
    {
        public Recipe recipe;
        public string name;
    }

    public List<RecipeInfo> recipeInfos = new List<RecipeInfo>();

    public TileEntityUniversalCrafter te;
    public Vector3i blockPos;
    public XUiC_CrafterRecipeEntry SelectedEntry;

    public void ClearSelection()
    {
        CurrentRecipe = null;
        SelectedEntry = null;

        for (int i = 0; i < recipeControls.Length; i++)
        {
            recipeControls[i].SetRecipe(null);
        }
    }

    public void SetContext(Vector3i pos)
    {
        blockPos = pos;
        IsDirty = true;
        hasRestoredRecipe = false;
    }

    private TileEntityUniversalCrafter GetCrafter()
    {
        return crafterUI?.GetCrafter();
    }

    public override void Init()
    {
        base.Init();

        recipeControls = GetChildrenByType<XUiC_CrafterRecipeEntry>();
        for (int i = 0; i < recipeControls.Length; i++)
        {
            var entry = recipeControls[i];
            entry.OnPress += OnPressRecipe;
            entry.RecipeList = this;
        }

        var grid = ViewComponent as XUiV_Grid;
        if (grid != null)
        {
            length = grid.Columns = grid.Rows;
        }

        txtInput = windowGroup.Controller.GetChildById("searchInput") as XUiC_TextInput;
        if (txtInput != null)
        {
            txtInput.OnChangeHandler += HandleSearchChanged;
            txtInput.OnSubmitHandler += HandleSearchSubmitted;
        }

        IsDirty = true;
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);

        if (!IsDirty)
            return;

        var te = GetCrafter();
        if (te == null)
        {
            IsDirty = false;
            return;
        }

        for (int i = 0; i < recipeControls.Length; i++)
        {
            var entry = recipeControls[i];

            if (i < recipeInfos.Count)
            {
                var info = recipeInfos[i];
                entry.SetRecipe(info.recipe);
                entry.ViewComponent.Enabled = true;
                entry.ViewComponent.IsVisible = true;
            }
            else
            {
                entry.SetRecipe(null);
                entry.ViewComponent.Enabled = false;
                entry.ViewComponent.IsVisible = false;
            }
        }

        // Restore local UI selection from replicated TE state only
        if (!hasRestoredRecipe && !string.IsNullOrEmpty(te.SelectedRecipeName))
        {
            hasRestoredRecipe = true;
            SelectRecipeByName(te.SelectedRecipeName, false);
        }

        IsDirty = false;
    }

    private void BuildRecipeList(string searchText = "")
    {
        recipeInfos.Clear();

        bool hasSearch = !string.IsNullOrEmpty(searchText);

        foreach (var recipe in XUiM_Recipes.GetRecipes())
        {
            if (recipe == null)
                continue;

            if (!string.IsNullOrEmpty(recipe.craftingArea) &&
                !string.Equals(recipe.craftingArea, "workbench", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!recipe.IsUnlocked(xui?.playerUI.entityPlayer))
                continue;

            if (hasSearch &&
                recipe.GetName().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            recipeInfos.Add(new RecipeInfo
            {
                recipe = recipe,
                name = recipe.GetName()
            });
        }

        IsDirty = true;
    }

    private void HandleSearchChanged(XUiController sender, string text, bool fromCode = false)
    {
        searchTerm = text ?? string.Empty;
        BuildRecipeList(searchTerm);
    }

    private void HandleSearchSubmitted(XUiController sender, string text)
    {
        searchTerm = text ?? string.Empty;
        BuildRecipeList(searchTerm);
    }

    public void SelectRecipeByName(string recipeName)
    {
        SelectRecipeByName(recipeName, false);
    }

    private void SelectRecipeByName(string recipeName, bool sendRequest)
    {
        if (string.IsNullOrEmpty(recipeName))
            return;

        var te = GetCrafter();
        if (te == null)
        {
            Log.Warning("[Crafter][RecipeList] TE is null in SelectRecipeByName");
            return;
        }

        for (int i = 0; i < recipeInfos.Count; i++)
        {
            if (recipeInfos[i].name != recipeName)
                continue;

            CurrentRecipe = recipeInfos[i].recipe;

            if (ingredientsList != null)
                ingredientsList.ShowIngredients(CurrentRecipe);

            LogRecipeIngredients(CurrentRecipe);

            if (te.IsDevLogging)
                Log.Out($"[Crafter][RecipeList] Selected recipe in UI: {recipeName} (sendRequest={sendRequest})");

            if (sendRequest)
            {
                Helper.RequestCrafterSelectRecipe(blockPos, recipeName);
            }

            return;
        }

        if (te.IsDevLogging)
            Log.Warning($"[Crafter][RecipeList] Could not select recipe '{recipeName}'");
    }

    public override void OnOpen()
    {
        if (ViewComponent != null && !ViewComponent.IsVisible)
            ViewComponent.IsVisible = true;

        BuildRecipeList(searchTerm);
        IsDirty = true;
        hasRestoredRecipe = false;
    }

    public override void OnClose()
    {
        if (ViewComponent != null && ViewComponent.IsVisible)
            ViewComponent.IsVisible = false;
    }

    public void OnRecipePressed(XUiC_CrafterRecipeEntry entry)
    {
        if (entry == null || entry.Recipe == null)
            return;

        SelectedEntry = entry;
        CurrentRecipe = entry.Recipe;

        if (ingredientsList != null)
            ingredientsList.ShowIngredients(CurrentRecipe);

        LogRecipeIngredients(entry.Recipe);

        Helper.RequestCrafterSelectRecipe(blockPos, entry.Recipe.GetName());
    }

    public void LogRecipeIngredients(Recipe recipe)
    {
        if (recipe == null)
        {
            Log.Out("[Crafter] No recipe selected");
            return;
        }

        Log.Out($"[Crafter] Ingredients for {recipe.GetName()}");

        foreach (var ing in recipe.ingredients)
        {
            string itemName = ing.itemValue.ItemClass.GetItemName();
            int qty = ing.count;
            Log.Out($"[Crafter]  • {itemName} x{qty}");
        }
    }

    private void OnPressRecipe(XUiController _sender, int _mouseButton)
    {
        Log.Out("[Crafter][UI] OnPressRecipe fired");

        var entry = _sender as XUiC_CrafterRecipeEntry;
        if (entry == null || entry.Recipe == null)
        {
            Log.Warning("[Crafter][UI] OnPressRecipe entry null or recipe null");
            return;
        }

        SelectedEntry = entry;
        CurrentRecipe = entry.Recipe;

        if (ingredientsList != null)
            ingredientsList.ShowIngredients(CurrentRecipe);

        Log.Out($"[Crafter][UI] Requesting recipe select: {CurrentRecipe.GetName()}");
        Helper.RequestCrafterSelectRecipe(blockPos, CurrentRecipe.GetName());
    }
}