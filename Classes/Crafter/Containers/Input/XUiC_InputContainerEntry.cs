using System;
using UnityEngine;

public class XUiC_InputContainerEntry : XUiController
{
    public TileEntityComposite ContainerTE;
    public Vector3i ContainerPos = Vector3i.zero;
    public Guid PipeGraphId = Guid.Empty;

    public XUiV_Label lblName;
    public XUiV_Sprite background;
    public XUiV_Sprite iconSprite;
    public bool IsSelected { get; private set; } = false;
    public bool HasIngredients { get; private set; } = false;

    public XUiC_InputContainerList InputList;

    public override void Init()
    {
        base.Init();
        Log.Out("[Crafter][InputEntry] ViewComponent type = " + ViewComponent?.GetType()?.Name);
        Log.Out("[Crafter][InputEntry] Init: starting child scan");

        for (int i = 0; i < children.Count; i++)
        {
            XUiView v = children[i].ViewComponent;
            if (v == null) continue;

            Log.Out($"[Crafter][InputEntry]   Child[{i}] id={v.ID} type={v.GetType().Name}");

            if (v.ID.EqualsCaseInsensitive("name"))
                lblName = v as XUiV_Label;
            else if (v.ID.EqualsCaseInsensitive("background"))
                background = v as XUiV_Sprite;
            else if (v.ID.EqualsCaseInsensitive("icon"))
                iconSprite = v as XUiV_Sprite;
        }

        Log.Out($"[Crafter][InputEntry] Init done: lblName={(lblName != null)} background={(background != null)} icon={(iconSprite != null)}");
        IsDirty = true;
    }

    public void SetSelected(bool isSelected)
    {
        IsSelected = isSelected;
    }

    public void HasIngreidnets(bool hasIngredients)
    {
        HasIngredients = hasIngredients;
    }

    public void SetContainer(TileEntityComposite te, Vector3i containerPos, Guid pipeGraphId)
    {
        ContainerTE = te;
        ContainerPos = containerPos;
        PipeGraphId = pipeGraphId;

        IsDirty = true;
        RefreshBindings();

        if (ContainerTE == null)
        {
            if (lblName != null) lblName.Text = "";
            if (background != null) background.Color = new Color32(64, 64, 64, 255);
            Log.Out($"[Crafter][InputEntry] SetContainer: NULL container pos={ContainerPos} graph={PipeGraphId}");
            return;
        }

        string name = GetDisplayName(te);
        string icon = te.blockValue.Block.GetIconName();

        if (lblName != null) lblName.Text = name;
        if (background != null) background.Color = new Color32(64, 64, 64, 255);

        Log.Out($"[Crafter][InputEntry] SetContainer: name='{name}' icon='{icon}' worldPos={ContainerPos} graph={PipeGraphId}");
    }

    public void ClearContainer()
    {
        ContainerTE = null;
        ContainerPos = Vector3i.zero;
        PipeGraphId = Guid.Empty;

        IsDirty = true;
        RefreshBindings();

        if (lblName != null) lblName.Text = "";
        if (background != null) background.Color = new Color32(64, 64, 64, 255);

        Log.Out("[Crafter][InputEntry] ClearContainer");
    }

    private string GetDisplayName(TileEntityComposite te)
    {
        if (te == null)
        {
            return string.Empty;
        }
        var sign = te.GetFeature<TEFeatureSignable>();
        string signText = sign?.signText?.Text;
        if (!string.IsNullOrEmpty(signText))
        {
            return signText;
        }

        return te.teData.Block?.GetLocalizedBlockName();
    }

    public override void OnHovered(bool _isOver)
    {

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

            background.IsDirty = true;
        }

        base.OnHovered(_isOver);
    }

    public override void OnPressed(int mouseButton)
    {
        base.OnPressed(mouseButton);

        Log.Out($"[Crafter][InputEntry] OnPressed: button={mouseButton} pos={ContainerPos} graph={PipeGraphId} te={(ContainerTE != null ? "OK" : "NULL")}");

        InputList?.OnEntryPressed(this, mouseButton);

        RefreshBindings(true);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        switch (bindingName)
        {
            case "containername":
                value = GetDisplayName(ContainerTE);
                return true;

            case "containericon":
                value = (ContainerTE != null) ? ContainerTE.blockValue.Block.GetIconName() : "";
                return true;
        }

        if (bindingName == "isselected")
        {
            value = IsSelected ? "true" : "false";
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
