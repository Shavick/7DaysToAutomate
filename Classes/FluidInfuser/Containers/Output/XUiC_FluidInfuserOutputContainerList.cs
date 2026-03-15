using System.Collections.Generic;

public class XUiC_FluidInfuserOutputContainerList : XUiController
{
    public TileEntityFluidInfuser te;
    private Vector3i blockPos;
    public XUiC_FluidInfuserOutputContainerEntry[] entries;

    public void SetContext(TileEntityFluidInfuser infuser, Vector3i pos)
    {
        te = infuser;
        blockPos = pos;
        IsDirty = true;
    }

    public override void Init()
    {
        base.Init();
        entries = GetChildrenByType<XUiC_FluidInfuserOutputContainerEntry>();
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i].OutputList = this;
                entries[i].OnPress += OnEntryPressed;
            }
        }

        IsDirty = true;
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (!IsDirty)
            return;

        if (entries == null || entries.Length == 0 || te == null)
        {
            IsDirty = false;
            return;
        }

        WorldBase world = GameManager.Instance?.World;
        if (world == null)
        {
            IsDirty = false;
            return;
        }

        List<OutputTargetInfo> targets = te.GetAvailableOutputTargets(world);
        for (int i = 0; i < entries.Length; i++)
        {
            if (targets != null && i < targets.Count)
            {
                OutputTargetInfo target = targets[i];
                TileEntityComposite comp = world.GetTileEntity(0, target.BlockPos) as TileEntityComposite;
                entries[i].SetTarget(comp, target);
                entries[i].SetSelected(
                    target.BlockPos == te.SelectedOutputChestPos &&
                    target.TransportMode == te.SelectedOutputMode &&
                    target.PipeGraphId == te.SelectedOutputPipeGraphId);
                entries[i].ViewComponent.Enabled = true;
                entries[i].ViewComponent.IsVisible = true;
            }
            else
            {
                entries[i].SetTarget(null, null);
                entries[i].SetSelected(false);
                entries[i].ViewComponent.Enabled = false;
                entries[i].ViewComponent.IsVisible = false;
            }
        }

        IsDirty = false;
    }

    public void OnEntryPressed(XUiController sender, int mouseButton)
    {
        XUiC_FluidInfuserOutputContainerEntry entry = sender as XUiC_FluidInfuserOutputContainerEntry;
        if (entry?.OutputTarget == null)
            return;

        OutputTargetInfo target = entry.OutputTarget;
        Helper.RequestFluidInfuserSelectOutput(blockPos, target.BlockPos, (int)target.TransportMode, target.PipeGraphId.ToString());
        IsDirty = true;
        RefreshBindings(true);
    }
}
