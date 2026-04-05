using System;
using System.Collections.Generic;

public class XUiC_MachineRecipeCodexList : XUiController
{
    private XUiC_MachineRecipeCodex owner;
    private XUiC_TextInput searchInput;
    private string searchTerm = string.Empty;

    private readonly List<MachineRecipe> filteredRecipes = new List<MachineRecipe>();
    private int pageIndex;

    public XUiC_MachineRecipeCodexEntry[] recipeControls;

    public void SetOwner(XUiC_MachineRecipeCodex controller)
    {
        owner = controller;
    }

    public override void Init()
    {
        base.Init();

        recipeControls = GetChildrenByType<XUiC_MachineRecipeCodexEntry>();
        for (int i = 0; i < recipeControls.Length; i++)
            recipeControls[i].RecipeList = this;

        searchInput = windowGroup?.Controller?.GetChildById("codexSearchInput") as XUiC_TextInput;
        if (searchInput != null)
        {
            searchInput.OnChangeHandler += OnSearchChanged;
            searchInput.OnSubmitHandler += OnSearchSubmitted;
        }

        IsDirty = true;
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (!IsDirty)
            return;

        if (recipeControls == null || recipeControls.Length == 0)
        {
            IsDirty = false;
            return;
        }

        int pageSize = Math.Max(1, recipeControls?.Length ?? 1);
        int start = pageIndex * pageSize;

        for (int i = 0; i < recipeControls.Length; i++)
        {
            XUiC_MachineRecipeCodexEntry entry = recipeControls[i];
            MachineRecipe recipe = (start + i) < filteredRecipes.Count ? filteredRecipes[start + i] : null;
            bool selected = recipe != null && owner != null && owner.IsSelectedRecipe(recipe.NormalizedKey);

            entry.SetRecipe(recipe, selected);

            bool visible = recipe != null;
            if (entry.ViewComponent != null)
            {
                entry.ViewComponent.Enabled = visible;
                entry.ViewComponent.IsVisible = visible;
            }
        }

        IsDirty = false;
    }

    public void RefreshData(bool resetPage)
    {
        BuildFilteredList();

        if (resetPage)
            pageIndex = 0;

        ClampPageIndex();
        IsDirty = true;
        owner?.OnListUpdated();
    }

    public void PrevPage()
    {
        if (GetPageCount() <= 1)
            return;

        pageIndex--;
        if (pageIndex < 0)
            pageIndex = GetPageCount() - 1;

        IsDirty = true;
        owner?.OnListUpdated();
    }

    public void NextPage()
    {
        if (GetPageCount() <= 1)
            return;

        pageIndex++;
        if (pageIndex >= GetPageCount())
            pageIndex = 0;

        IsDirty = true;
        owner?.OnListUpdated();
    }

    public string GetPageText()
    {
        int pageCount = GetPageCount();
        int oneBased = pageCount > 0 ? pageIndex + 1 : 1;
        return $"Page {oneBased}/{pageCount}";
    }

    public string GetCountText()
    {
        return $"{filteredRecipes.Count} recipes";
    }

    public bool HasRecipe(string normalizedKey)
    {
        for (int i = 0; i < filteredRecipes.Count; i++)
        {
            if (string.Equals(filteredRecipes[i].NormalizedKey, normalizedKey, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public void OnRecipePressed(XUiC_MachineRecipeCodexEntry entry)
    {
        if (entry?.Recipe == null || owner == null)
            return;

        owner.SetSelectedRecipe(entry.Recipe.NormalizedKey ?? string.Empty);
        IsDirty = true;
    }


    private void OnSearchChanged(XUiController sender, string text, bool fromCode = false)
    {
        searchTerm = text ?? string.Empty;
        RefreshData(true);
    }

    private void OnSearchSubmitted(XUiController sender, string text)
    {
        searchTerm = text ?? string.Empty;
        RefreshData(true);
    }

    private void BuildFilteredList()
    {
        filteredRecipes.Clear();

        string groupFilter = owner?.GetMachineGroupFilterCsv() ?? string.Empty;
        List<MachineRecipe> source = MachineRecipeRegistry.GetRecipesForMachineGroups(groupFilter, false);
        bool hasSearch = !string.IsNullOrWhiteSpace(searchTerm);

        for (int i = 0; i < source.Count; i++)
        {
            MachineRecipe recipe = source[i];
            if (recipe == null)
                continue;

            if (hasSearch)
            {
                string searchable = XUiC_MachineRecipeCodex.GetSearchableText(recipe);
                if (searchable.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }

            filteredRecipes.Add(recipe);
        }

        filteredRecipes.Sort(CompareRecipes);
    }

    private void ClampPageIndex()
    {
        int pageCount = GetPageCount();
        pageIndex = Math.Max(0, Math.Min(pageIndex, pageCount - 1));
    }

    private int GetPageCount()
    {
        int pageSize = Math.Max(1, recipeControls?.Length ?? 1);
        if (filteredRecipes.Count <= 0)
            return 1;

        return (filteredRecipes.Count + pageSize - 1) / pageSize;
    }

    private static int CompareRecipes(MachineRecipe a, MachineRecipe b)
    {
        int machine = string.Compare(a?.Machine, b?.Machine, StringComparison.OrdinalIgnoreCase);
        if (machine != 0)
            return machine;

        string aLabel = XUiC_MachineRecipeCodex.GetRecipeDisplayName(a);
        string bLabel = XUiC_MachineRecipeCodex.GetRecipeDisplayName(b);
        return string.Compare(aLabel, bLabel, StringComparison.OrdinalIgnoreCase);
    }

}
