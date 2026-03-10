using UnityEngine;

public class XUiC_ExtractorOutputContainerEntry : XUiController
{
    public TileEntityComposite ContainerTE;
    public OutputTargetInfo OutputTarget;

    public XUiV_Label lblName;
    public XUiV_Sprite background;
    public XUiV_Sprite iconSprite;

    public bool IsSelected { get; private set; } = false;

    public XUiC_ExtractorOutputContainerList OutputList;

    public override void Init()
    {
        base.Init();

        for (int i = 0; i < children.Count; i++)
        {
            XUiView v = children[i].ViewComponent;
            if (v == null) continue;

            if (v.ID.EqualsCaseInsensitive("name"))
                lblName = v as XUiV_Label;
            else if (v.ID.EqualsCaseInsensitive("background"))
                background = v as XUiV_Sprite;
            else if (v.ID.EqualsCaseInsensitive("icon"))
                iconSprite = v as XUiV_Sprite;
        }

        IsDirty = true;
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
    }

    public void SetTarget(TileEntityComposite te, OutputTargetInfo target)
    {
        ContainerTE = te;
        OutputTarget = target;

        IsDirty = true;
        RefreshBindings();

        if (ContainerTE == null)
        {
            if (lblName != null) lblName.Text = "";
            if (background != null) background.Color = new Color32(64, 64, 64, 255);
            if (iconSprite != null) iconSprite.SpriteName = "";
            return;
        }

        string name = GetDisplayName(te);
        if (lblName != null) lblName.Text = name;

        if (iconSprite != null)
            iconSprite.SpriteName = te.blockValue.Block.GetIconName();

        if (background != null)
            background.Color = new Color32(64, 64, 64, 255);
    }

    private string GetDisplayName(TileEntityComposite te)
    {
        if (te == null)
            return "";

        var sign = te.GetFeature<TEFeatureSignable>();
        string signText = sign?.signText?.Text;

        if (!string.IsNullOrEmpty(signText))
            return signText;

        return te.teData?.Block?.GetLocalizedBlockName() ?? "";
    }

    public override void OnPressed(int mouseButton)
    {
        base.OnPressed(mouseButton);

        if (OutputList != null)
            OutputList.OnEntryPressed(this, mouseButton);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        switch (bindingName)
        {
            case "containername":
                value = GetDisplayName(ContainerTE);
                return true;

            case "containericon":
                value = (ContainerTE != null)
                    ? ContainerTE.blockValue.Block.GetIconName()
                    : "";
                return true;

            case "isselected":
                value = IsSelected ? "true" : "false";
                return true;

            case "backgroundcolor":
                value = IsSelected ? "0,255,0,80" : "255,255,255,0";
                return true;
        }

        return false;
    }
}