using System;
using System.Collections.Generic;

public class XUiC_FluidInfuserInputContainerList : XUiController
{
    public TileEntityFluidInfuser te;
    private Vector3i blockPos;
    public XUiC_FluidInfuserInputContainerEntry[] entries;

    public void SetContext(TileEntityFluidInfuser infuser, Vector3i pos)
    {
        te = infuser;
        blockPos = pos;
        IsDirty = true;
    }

    public override void Init()
    {
        base.Init();
        entries = GetChildrenByType<XUiC_FluidInfuserInputContainerEntry>();
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i].InputList = this;
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

        List<InputTargetInfo> targets = te.GetAvailableInputTargets(world);
        for (int i = 0; i < entries.Length; i++)
        {
            if (targets != null && i < targets.Count)
            {
                InputTargetInfo target = targets[i];
                TileEntityComposite comp = world.GetTileEntity(0, target.BlockPos) as TileEntityComposite;
                entries[i].SetContainer(comp, target.BlockPos, target.PipeGraphId);
                entries[i].SetSelected(target.BlockPos == te.SelectedInputChestPos && target.PipeGraphId == te.SelectedInputPipeGraphId);
                entries[i].ViewComponent.Enabled = true;
                entries[i].ViewComponent.IsVisible = true;
            }
            else
            {
                entries[i].SetContainer(null, Vector3i.zero, Guid.Empty);
                entries[i].SetSelected(false);
                entries[i].ViewComponent.Enabled = false;
                entries[i].ViewComponent.IsVisible = false;
            }
        }

        IsDirty = false;
    }

    public void OnEntryPressed(XUiController sender, int mouseButton)
    {
        XUiC_FluidInfuserInputContainerEntry entry = sender as XUiC_FluidInfuserInputContainerEntry;
        if (entry == null || entry.ContainerPos == Vector3i.zero || entry.PipeGraphId == Guid.Empty)
            return;

        Helper.RequestFluidInfuserSelectInput(blockPos, entry.ContainerPos, entry.PipeGraphId.ToString());
        IsDirty = true;
        RefreshBindings(true);
    }
}
