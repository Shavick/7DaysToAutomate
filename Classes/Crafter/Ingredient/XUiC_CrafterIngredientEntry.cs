using UnityEngine;

public class XUiC_CrafterIngredientEntry : XUiController
{
    public ItemStack ingredient;

    private XUiV_Label lblName;
    private XUiV_Label lblCount;
    private XUiV_Sprite icoItem;
    private XUiV_Sprite background;

    private bool isHovered = false;

    public override void Init()
    {
        base.Init();

        for (int i = 0; i < children.Count; i++)
        {
            var v = children[i].ViewComponent;
            if (v == null) continue;

            if (v.ID.EqualsCaseInsensitive("itemName"))
                lblName = v as XUiV_Label;

            else if (v.ID.EqualsCaseInsensitive("itemCount"))
                lblCount = v as XUiV_Label;

            else if (v.ID.EqualsCaseInsensitive("icon"))
                icoItem = v as XUiV_Sprite;

            else if (v.ID.EqualsCaseInsensitive("background"))
                background = v as XUiV_Sprite;
        }

        IsDirty = true;
    }

    public void SetIngredient(ItemStack ing)
    {
        ingredient = ing;
        Refresh();
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        if (ingredient == null)
        {
            value = "";
            return true;
        }

        switch (bindingName)
        {
            case "ingredientname":
                value = ingredient.itemValue.ItemClass.GetLocalizedItemName();
                return true;

            case "ingredientcount":
                value = "x " + ingredient.count.ToString();
                return true;

            case "ingredienticon":
                value = ingredient.itemValue.ItemClass.GetIconName();
                return true;
        }

        return false;
    }

    public override void OnHovered(bool _isOver)
    {
        isHovered = _isOver;

        if (background != null)
        {
            background.Color = _isOver
                ? new Color32(96, 96, 96, 255)
                : new Color32(64, 64, 64, 255);
        }

        base.OnHovered(_isOver);
    }

    public void Refresh()
    {
        IsDirty = true;
        RefreshBindings();
    }
}
