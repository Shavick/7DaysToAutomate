using System;
using System.Collections.Generic;

public class XUiC_InputContainerList : XUiController
{
    public TileEntityUniversalCrafter te;
    private Vector3i blockPos;

    public XUiC_InputContainerEntry[] entries;
    public XUiC_InputContainerEntry SelectedEntry { get; set; }
    public int length;
    public bool selected = false;

    public void SetContext(TileEntityUniversalCrafter te, Vector3i pos)
    {
        this.te = te;
        if (te != null)
            te.xUiC_InputContainerList = this;

        blockPos = pos;

        if (te == null)
            Log.Error($"[Crafter][InputList] SetContext: TE is NULL at {pos}");
        else
            Log.Out($"[Crafter][InputList] SetContext: TE set at {pos}");

        IsDirty = true;
    }

    public override void Init()
    {
        base.Init();

        entries = GetChildrenByType<XUiC_InputContainerEntry>();
        var grid = ViewComponent as XUiV_Grid;
        length = (grid != null) ? grid.Rows * grid.Columns : (entries?.Length ?? 0);

        Log.Out($"[Crafter][InputList] Init: entries={entries?.Length ?? -1} length={length}");

        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                entry.InputList = this;
                entry.OnPress += OnEntryPressed;
                Log.Out($"[Crafter][InputList] Init: wired entry index={i}");
            }
        }

        IsDirty = true;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        Log.Out("[Crafter][InputList] OnOpen");
        IsDirty = true;
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);

        if (!IsDirty)
            return;

        if (entries == null || entries.Length == 0)
        {
            IsDirty = false;
            return;
        }

        if (te == null)
        {
            Log.Error("[Crafter][InputList] Update: TE is NULL");
            IsDirty = false;
            return;
        }

        World world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[Crafter][InputList] Update: world is NULL");
            IsDirty = false;
            return;
        }

        List<InputTargetInfo> list = te.GetAvailableInputTargets(world);
        int inputCount = list?.Count ?? -1;

        Log.Out($"[Crafter][InputList] Update: inputTargets={inputCount} slots={entries.Length}");

        if (list == null || list.Count == 0)
        {
            Log.Warning("[Crafter][InputList] Update: No input targets, clearing all slots");

            for (int i = 0; i < entries.Length; i++)
            {
                entries[i].SetContainer(null, Vector3i.zero, Guid.Empty);
                entries[i].SetSelected(false);
                entries[i].ViewComponent.Enabled = false;
                entries[i].ViewComponent.IsVisible = true;
                RefreshBindings();
                IsDirty = true;
            }

            SelectedEntry = null;
            IsDirty = false;
            return;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            if (i < list.Count)
            {
                InputTargetInfo target = list[i];
                TileEntityComposite comp = world.GetTileEntity(0, target.BlockPos) as TileEntityComposite;

                Log.Out($"[Crafter][InputList] Slot {i}: targetPos={target.BlockPos} graph={target.PipeGraphId} te={(comp != null ? "OK" : "NULL")}");

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

                Log.Out($"[Crafter][InputList] Slot {i}: entryPos={target.BlockPos} selectedPos={te.SelectedInputChestPos} entryGraph={target.PipeGraphId} selectedGraph={te.SelectedInputPipeGraphId} IsSelected={entries[i].IsSelected}");
            }
            else
            {
                Log.Out($"[Crafter][InputList] Slot {i}: cleared (no container)");
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
        Log.Out("[Crafter][UI] Input OnEntryPressed fired");

        var entry = sender as XUiC_InputContainerEntry;
        if (entry == null)
        {
            Log.Error("[Crafter][UI] Input OnEntryPressed sender invalid");
            return;
        }

        if (entry.ContainerPos == Vector3i.zero || entry.PipeGraphId == Guid.Empty)
        {
            Log.Warning("[Crafter][UI] Input OnEntryPressed target invalid");
            return;
        }

        Vector3i targetPos = entry.ContainerPos;
        string pipeGraphId = entry.PipeGraphId.ToString();

        Log.Out($"[Crafter][UI] Request input select: pos={targetPos} graph={pipeGraphId}");

        Helper.RequestCrafterSelectInput(blockPos, targetPos, pipeGraphId);
        IsDirty = true;
        RefreshBindings(true);
    }
}