using System;
using UnityEngine;

public class XUiC_InputBufferEntry : XUiController
{
    private string itemName = string.Empty;
    private int itemCount;

    private XUiV_Label lblCount;
    private XUiV_Label lblName;
    private XUiV_Sprite background;

    public override void Init()
    {
        base.Init();

        for (int i = 0; i < children.Count; i++)
        {
            XUiView v = children[i].ViewComponent;
            if (v == null)
                continue;

            if (v.ID.EqualsCaseInsensitive("itemCount"))
                lblCount = v as XUiV_Label;
            else if (v.ID.EqualsCaseInsensitive("itemName"))
                lblName = v as XUiV_Label;
            else if (v.ID.EqualsCaseInsensitive("background"))
                background = v as XUiV_Sprite;
        }

        IsDirty = true;
    }

    public void SetItem(string name, int count)
    {
        itemName = name ?? string.Empty;
        itemCount = count > 0 ? count : 0;

        if (lblCount != null)
            lblCount.Text = itemCount > 0 ? itemCount.ToString() : string.Empty;

        if (lblName != null)
            lblName.Text = GetLocalizedName();

        IsDirty = true;
        RefreshBindings(true);
    }

    public void ClearItem()
    {
        itemName = string.Empty;
        itemCount = 0;

        if (lblCount != null)
            lblCount.Text = string.Empty;

        if (lblName != null)
            lblName.Text = string.Empty;

        IsDirty = true;
        RefreshBindings(true);
    }

    public override void OnHovered(bool _isOver)
    {
        if (background != null)
        {
            background.Color = _isOver
                ? new Color32(96, 96, 96, 255)
                : new Color32(64, 64, 64, 255);
        }

        base.OnHovered(_isOver);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        switch (bindingName)
        {
            case "bufferitemicon":
                value = GetIconName();
                return true;

            case "bufferitemcount":
                value = itemCount > 0 ? itemCount.ToString() : string.Empty;
                return true;

            case "bufferitemname":
                value = GetLocalizedName();
                return true;

            case "buffertooltip":
                value = itemCount > 0 ? $"{GetLocalizedName()} x{itemCount}" : string.Empty;
                return true;
        }

        return false;
    }

    private string GetLocalizedName()
    {
        if (string.IsNullOrEmpty(itemName))
            return string.Empty;

        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        if (itemValue?.ItemClass != null)
            return itemValue.ItemClass.GetLocalizedItemName();

        return itemName;
    }

    private string GetIconName()
    {
        if (string.IsNullOrEmpty(itemName))
            return string.Empty;

        ItemValue itemValue = ItemClass.GetItem(itemName, false);
        if (itemValue?.ItemClass != null)
            return itemValue.ItemClass.GetIconName();

        return string.Empty;
    }
}
