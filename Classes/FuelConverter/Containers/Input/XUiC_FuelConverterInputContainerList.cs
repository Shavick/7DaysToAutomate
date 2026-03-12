using System;
using System.Collections.Generic;

public class XUiC_FuelConverterInputContainerList : XUiController
{
    public TileEntityFuelConverter te;
    private Vector3i blockPos;

    public XUiC_FuelConverterInputContainerEntry[] entries;
    public XUiC_FuelConverterInputContainerEntry SelectedEntry;

    public void SetContext(TileEntityFuelConverter converter, Vector3i pos)
    {
        te = converter;
        blockPos = pos;
        IsDirty = true;
    }

    public override void Init()
    {
        base.Init();

        entries = GetChildrenByType<XUiC_FuelConverterInputContainerEntry>();
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

        if (targets == null || targets.Count == 0)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i].SetContainer(null, Vector3i.zero, Guid.Empty);
                entries[i].SetSelected(false);
                entries[i].ViewComponent.Enabled = false;
                entries[i].ViewComponent.IsVisible = false;
            }

            SelectedEntry = null;
            IsDirty = false;
            return;
        }

        SelectedEntry = null;

        for (int i = 0; i < entries.Length; i++)
        {
            if (i < targets.Count)
            {
                InputTargetInfo target = targets[i];
                TileEntityComposite comp = world.GetTileEntity(0, target.BlockPos) as TileEntityComposite;

                entries[i].SetContainer(comp, target.BlockPos, target.PipeGraphId);
                entries[i].ViewComponent.Enabled = true;
                entries[i].ViewComponent.IsVisible = true;

                bool isSelected =
                    target.BlockPos == te.SelectedInputChestPos &&
                    target.PipeGraphId == te.SelectedInputPipeGraphId;

                entries[i].SetSelected(isSelected);
                entries[i].RefreshBindings(true);
                entries[i].IsDirty = true;

                if (isSelected)
                    SelectedEntry = entries[i];
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
        XUiC_FuelConverterInputContainerEntry entry = sender as XUiC_FuelConverterInputContainerEntry;
        if (entry == null)
            return;

        if (entry.ContainerPos == Vector3i.zero || entry.PipeGraphId == Guid.Empty)
            return;

        Helper.RequestFuelConverterSelectInput(blockPos, entry.ContainerPos, entry.PipeGraphId.ToString());
        IsDirty = true;
        RefreshBindings(true);
    }
}
