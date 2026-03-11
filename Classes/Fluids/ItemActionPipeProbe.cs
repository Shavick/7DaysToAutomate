using System;

public class PipeProbe : ItemAction
{
    public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
    {
        try
        {
            bool hasActionData = _actionData != null;
            bool hasInvData = _actionData?.invData != null;
            string holder = _actionData?.invData?.holdingEntity == null ? "null" : _actionData.invData.holdingEntity.GetType().Name;
            Log.Out($"[PipeProbe][Action1] ExecuteAction released={_bReleased} hasActionData={hasActionData} hasInvData={hasInvData} holder={holder}");

            if (_bReleased)
                return;

            if (_actionData?.invData == null)
            {
                Log.Out("[PipeProbe][Action1] Skip: invData is null");
                return;
            }

            World world = _actionData.invData.world as World;
            if (world == null)
            {
                Log.Out("[PipeProbe][Action1] Skip: world is null");
                return;
            }

            EntityPlayerLocal player = _actionData.invData.holdingEntity as EntityPlayerLocal;
            if (player == null)
            {
                Log.Out("[PipeProbe][Action1] Skip: holding entity is not EntityPlayerLocal");
                return;
            }

            WorldRayHitInfo hitInfo = _actionData.hitInfo ?? _actionData.invData.hitInfo;
            bool handled = PipeProbeHudManager.TryHandleUseAction(world, player, hitInfo);
            Log.Out($"[PipeProbe][Action1] TryHandleUseAction handled={handled} hitValid={(hitInfo != null && hitInfo.bHitValid)}");
        }
        catch (Exception ex)
        {
            Log.Error($"[PipeProbe][Action1] Exception: {ex}");
        }
    }
}
