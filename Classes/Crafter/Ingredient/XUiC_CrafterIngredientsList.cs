public class XUiC_CrafterIngredientsList : XUiController
{
    public Recipe CurrentRecipe;
    public XUiC_UniversalCrafter crafterUI;   // Assigned from the parent UI

    private XUiC_CrafterIngredientEntry[] ingredientControls;
    private XUiV_Grid grid;

    public override void Init()
    {
        base.Init();

        grid = ViewComponent as XUiV_Grid;
        ingredientControls = GetChildrenByType<XUiC_CrafterIngredientEntry>();

        IsDirty = true;
    }

    // Clear ingredient UI completely
    public void Clear()
    {
        CurrentRecipe = null;

        for (int i = 0; i < ingredientControls.Length; i++)
        {
            var entry = ingredientControls[i];
            entry.SetIngredient(null);
            entry.ViewComponent.Enabled = false;
            entry.ViewComponent.IsVisible = false;
        }

        IsDirty = true;
    }

    // Show ingredients for this recipe
    public void ShowIngredients(Recipe recipe)
    {
        CurrentRecipe = recipe;

        if (recipe == null || recipe.ingredients == null || recipe.ingredients.Count == 0)
        {
            Clear();
            return;
        }

        int index = 0;

        // Filter out zero-count ingredients
        var validIngredients = recipe.ingredients.FindAll(i => i.count > 0);

        foreach (var item in validIngredients)
        {
            if (index >= ingredientControls.Length)
                break;

            var entry = ingredientControls[index];
            entry.SetIngredient(item);
            entry.ViewComponent.Enabled = true;
            entry.ViewComponent.IsVisible = true;

            index++;
        }

        // Disable unused slots
        for (; index < ingredientControls.Length; index++)
        {
            ingredientControls[index].SetIngredient(null);
            ingredientControls[index].ViewComponent.Enabled = false;
            ingredientControls[index].ViewComponent.IsVisible = false;
        }

        IsDirty = true;
    }

    public override void Update(float _dt)
    {
        if (IsDirty)
        {
            IsDirty = false;
        }

        base.Update(_dt);
    }
}
