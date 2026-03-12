using System.Collections.Generic;

public class XUiC_FuelConverterOutputContainerList : XUiController
{
    public TileEntityFuelConverter te;
    private Vector3i blockPos;

    public XUiC_FuelConverterOutputContainerEntry[] entries;
    public XUiC_FuelConverterOutputContainerEntry SelectedEntry;

    public void SetContext(TileEntityFuelConverter converter, Vector3i pos)
    {
        te = converter;
        blockPos = pos;
        IsDirty = true;
    }

    public override void Init()
    {
        base.Init();

        entries = GetChildrenByType<XUiC_FuelConverterOutputContainerEntry>();
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

        if (targets == null || targets.Count == 0)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i].SetTarget(null, null);
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
                OutputTargetInfo target = targets[i];
                TileEntityComposite comp = world.GetTileEntity(0, target.BlockPos) as TileEntityComposite;

                if (comp == null)
                {
                    entries[i].SetTarget(null, null);
                    entries[i].SetSelected(false);
                    entries[i].ViewComponent.Enabled = false;
                    entries[i].ViewComponent.IsVisible = false;
                    continue;
                }

                entries[i].SetTarget(comp, target);
                entries[i].ViewComponent.Enabled = true;
                entries[i].ViewComponent.IsVisible = true;

                bool isSelected =
                    target.BlockPos == te.SelectedOutputChestPos &&
                    target.TransportMode == te.SelectedOutputMode &&
                    target.PipeGraphId == te.SelectedOutputPipeGraphId;

                entries[i].SetSelected(isSelected);
                entries[i].RefreshBindings(true);
                entries[i].IsDirty = true;

                if (isSelected)
                    SelectedEntry = entries[i];
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
        XUiC_FuelConverterOutputContainerEntry entry = sender as XUiC_FuelConverterOutputContainerEntry;
        if (entry == null || entry.OutputTarget == null)
            return;

        OutputTargetInfo target = entry.OutputTarget;

        Helper.RequestFuelConverterSelectOutput(
            blockPos,
            target.BlockPos,
            (int)target.TransportMode,
            target.PipeGraphId.ToString());

        IsDirty = true;
        RefreshBindings(true);
    }
}
