using System.Collections.Generic;

public class XUiC_OutputContainerList : XUiController
{
    public TileEntityUniversalCrafter te;
    private Vector3i blockPos;

    public XUiC_OutputContainerEntry[] entries;
    public XUiC_OutputContainerEntry SelectedEntry;

    private bool IsDevLoggingEnabled()
    {
        return te != null && te.IsDevLogging;
    }

    private void DevLog(string message)
    {
        if (IsDevLoggingEnabled())
            Log.Out(message);
    }
    public void SetContext(TileEntityUniversalCrafter te, Vector3i pos)
    {
        this.te = te;
        this.blockPos = pos;
        IsDirty = true;
    }

    public override void Init()
    {
        base.Init();

        entries = GetChildrenByType<XUiC_OutputContainerEntry>();

        foreach (var entry in entries)
        {
            entry.OutputList = this;
            entry.OnPress += OnEntryPressed;
        }

        IsDirty = true;
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (!IsDirty)
            return;

        if (entries == null || entries.Length == 0)
        {
            IsDirty = false;
            return;
        }

        if (te == null)
        {
            IsDirty = false;
            return;
        }

        WorldBase world = GameManager.Instance.World;
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
                entries[i].ViewComponent.Enabled = true;
                entries[i].ViewComponent.IsVisible = true;
                entries[i].RefreshBindings(true);
                entries[i].IsDirty = true;
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
                    target.PipeGraphId == te.SelectedPipeGraphId;

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

    public void OnEntryPressed(XUiController sender, int button)
    {
        var entry = sender as XUiC_OutputContainerEntry;
        if (entry == null || entry.ContainerTE == null || entry.OutputTarget == null)
            return;

        OutputTargetInfo target = entry.OutputTarget;

        DevLog($"[Crafter][UI] Request output select: pos={target.BlockPos} mode={target.TransportMode} pipeGraphId={target.PipeGraphId}");

        Helper.RequestCrafterSelectOutput(
            blockPos,
            target.BlockPos,
            (int)target.TransportMode,
            target.PipeGraphId.ToString());

        IsDirty = true;
        RefreshBindings(true);
    }
}