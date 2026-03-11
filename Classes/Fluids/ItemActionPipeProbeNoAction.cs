public class PipeProbeNoAction : ItemAction
{
    public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
    {
        bool hasInvData = _actionData?.invData != null;
        Log.Out($"[PipeProbe][Action0] ExecuteAction released={_bReleased} hasInvData={hasInvData} (intentional no-op)");
    }
}
