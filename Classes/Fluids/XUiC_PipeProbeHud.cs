public class XUiC_PipeProbeHud : XUiController
{
    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        if (PipeProbeHudManager.TryGetBindingValue(bindingName, out string resolved))
        {
            value = resolved;
            return true;
        }

        return false;
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);

        if (PipeProbeHudManager.IsVisible)
            RefreshBindings(true);
    }
}
