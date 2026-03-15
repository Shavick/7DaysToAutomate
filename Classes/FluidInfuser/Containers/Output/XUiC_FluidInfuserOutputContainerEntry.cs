using UnityEngine;

public class XUiC_FluidInfuserOutputContainerEntry : XUiController
{
    public TileEntityComposite ContainerTE;
    public OutputTargetInfo OutputTarget;
    public bool IsSelected { get; private set; }
    public XUiC_FluidInfuserOutputContainerList OutputList;

    public override void Init()
    {
        base.Init();
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
        RefreshBindings(true);
    }

    public override void OnPressed(int mouseButton)
    {
        base.OnPressed(mouseButton);
        OutputList?.OnEntryPressed(this, mouseButton);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        switch (bindingName)
        {
            case "containername":
                value = GetDisplayName(ContainerTE);
                return true;

            case "containericon":
                value = ContainerTE != null ? ContainerTE.blockValue.Block.GetIconName() : string.Empty;
                return true;

            case "isselected":
                value = IsSelected ? "true" : "false";
                return true;

            case "backgroundcolor":
                value = IsSelected ? "0,255,170,110" : "255,255,255,0";
                return true;
        }

        return false;
    }

    private static string GetDisplayName(TileEntityComposite te)
    {
        if (te == null)
            return string.Empty;

        var sign = te.GetFeature<TEFeatureSignable>();
        string signText = sign?.signText?.Text;
        if (!string.IsNullOrEmpty(signText))
            return signText;

        return te.teData?.Block?.GetLocalizedBlockName() ?? string.Empty;
    }
}
