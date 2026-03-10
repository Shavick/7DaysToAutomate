using UnityEngine;

public class XUiC_CrafterRecipeEntry : XUiController
{
    public Recipe recipe;

    public XUiV_Label lblName;

    public XUiV_Sprite icoRecipe;

    public XUiV_Sprite background;

    public XUiC_CrafterRecipeList RecipeList;

    private bool isHovered = false;

    public Recipe Recipe
    {
        get => recipe;
        set
        {
            recipe = value;

            if (background != null)
            {
                // Grey out if empty
                if (recipe == null)
                    background.Color = new Color32(64, 64, 64, byte.MaxValue);
            }

            if (ViewComponent != null)
            {
                ViewComponent.IsNavigatable = (ViewComponent.IsSnappable = recipe != null);
            }
        }
    }

    public void SetRecipe(Recipe recipe)
    {
        this.Recipe = recipe;
        IsDirty = true;
        RefreshBindings();

        if (recipe == null && background != null)
        {
            background.Color = new Color32(64, 64, 64, byte.MaxValue);
        }
    }

    public override void Init()
    {
        base.Init();

        // Find our child views by ID from controls.xml
        for (int i = 0; i < children.Count; i++)
        {
            XUiView v = children[i].ViewComponent;
            if (v == null) continue;

            if (v.ID.EqualsCaseInsensitive("name"))
            {
                lblName = v as XUiV_Label;
            }
            else if (v.ID.EqualsCaseInsensitive("icon"))
            {
                icoRecipe = v as XUiV_Sprite;
            }
            else if (v.ID.EqualsCaseInsensitive("background"))
            {
                background = v as XUiV_Sprite;
            }
        }

        IsDirty = true;
    }

    public override void OnHovered(bool _isOver)
    {
        isHovered = _isOver;

        if (background != null)
        {
            if (_isOver)
            {
                background.Color = new Color32(96, 96, 96, 255);   // Hover tint
            }
            else
            {
                background.Color = new Color32(64, 64, 64, 255);   // Default tint
            }

            // 👈 Required to actually update the UI sprite
            background.IsDirty = true;
        }

        base.OnHovered(_isOver);
    }

    public override void OnPressed(int _mouseButton)
    {
        base.OnPressed(_mouseButton);

        if (recipe == null || RecipeList == null)
            return;

        RecipeList.OnRecipePressed(this);
    }

    [PublicizedFrom(EAccessModifier.Protected)]
    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        switch (bindingName)
        {
            case "hasrecipe":
                value = (recipe != null).ToString();
                return true;

            case "recipename":
                value = (recipe != null) ? Localization.Get(recipe.GetName()) : "";
                return true;

            case "recipeicon":
                value = (recipe != null) ? (recipe.GetIcon() ?? "") : "";
                return true;

            case "tooltip":
                value = recipe != null ? Localization.Get(recipe.GetName()) : "";
                return true;
        }
        return false;
    }

    public void Refresh()
    {
        IsDirty = true;
        RefreshBindings();
    }
}
