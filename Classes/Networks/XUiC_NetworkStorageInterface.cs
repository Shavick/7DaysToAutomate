using System;
using System.Collections.Generic;

public class XUiC_NetworkStorageInterface : XUiController
{
    private Vector3i blockPosition;
    private TileEntityNetworkStorageInterface te;
    private XUiC_NetworkStorageList storageList;
    private ulong lastRefreshWorldTime;

    private readonly List<NetworkDisplayStack> displayStacksCache = new List<NetworkDisplayStack>();

    public static void Open(EntityPlayerLocal player, Vector3i pos)
    {
        if (player?.playerUI == null)
            return;

        XUiC_NetworkStorageInterface ctrl = player.playerUI.xui?.GetChildByType<XUiC_NetworkStorageInterface>();
        if (ctrl != null)
            ctrl.blockPosition = pos;

        player.playerUI.windowManager.Open("NetworkStorageInterface", true, false, true);
    }

    public override void Init()
    {
        base.Init();

        XUiV_Button closeBtn = GetChildById("closeButton")?.ViewComponent as XUiV_Button;
        if (closeBtn != null)
            closeBtn.Controller.OnPress += (c, b) => xui.playerUI.windowManager.Close("NetworkStorageInterface");

        XUiV_Button depositHoldingBtn = GetChildById("depositHoldingButton")?.ViewComponent as XUiV_Button;
        if (depositHoldingBtn != null)
            depositHoldingBtn.Controller.OnPress += (c, b) => TryDepositHolding();

        XUiV_Button depositAllBtn = GetChildById("depositAllButton")?.ViewComponent as XUiV_Button;
        if (depositAllBtn != null)
            depositAllBtn.Controller.OnPress += (c, b) => TryDepositAll();

        storageList = GetChildByType<XUiC_NetworkStorageList>();
        storageList?.SetOwner(this);
    }

    public override void OnOpen()
    {
        base.OnOpen();

        te = GetTileEntity();
        RefreshSnapshot(true);
        RefreshBindings(true);
        storageList?.ForceRefresh();
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (te == null)
        {
            te = GetTileEntity();
            if (te == null)
                return;
        }

        RefreshSnapshot(false);
        RefreshBindings(false);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        te = GetTileEntity();

        switch (bindingName)
        {
            case "network_id":
                value = te?.NetworkId.ToString() ?? Guid.Empty.ToString();
                return true;
            case "snapshot_revision":
                value = te == null ? "0" : te.LastSnapshotRevision.ToString();
                return true;
            case "stack_types":
                value = te == null ? "0" : te.LastSnapshotStackTypes.ToString();
                return true;
            case "total_items":
                value = te == null ? "0" : te.LastSnapshotTotalItems.ToString();
                return true;
            case "top_summary":
                value = te == null || string.IsNullOrEmpty(te.LastSnapshotTopSummary)
                    ? "(empty network or no available stacks)"
                    : te.LastSnapshotTopSummary;
                return true;
            case "network_status":
                value = GetNetworkStatusText();
                return true;
            case "interaction_hint":
                value = "Left click: +1 | Right/Shift click: +stack | Buttons: deposit held/all";
                return true;
            default:
                return false;
        }
    }

    public int GetSnapshotRevision()
    {
        return te?.LastSnapshotRevision ?? 0;
    }

    public List<NetworkDisplayStack> GetDisplayStacks(bool forceRebuild)
    {
        WorldBase world = GameManager.Instance?.World;
        if (world == null || world.IsRemote())
            return new List<NetworkDisplayStack>();

        te = GetTileEntity();
        if (te == null || te.NetworkId == Guid.Empty)
            return new List<NetworkDisplayStack>();

        if (forceRebuild)
            NetworkStorageService.TryBuildStorageSnapshot(world, te.NetworkId, out _);

        if (!NetworkStorageService.TryGetCachedSnapshot(te.NetworkId, out NetworkStorageSnapshot cached) || cached == null)
            return new List<NetworkDisplayStack>();

        displayStacksCache.Clear();
        for (int i = 0; i < cached.DisplayStacks.Count; i++)
            displayStacksCache.Add(cached.DisplayStacks[i]);

        return displayStacksCache;
    }

    public void TryWithdraw(string displayKey, int count)
    {
        if (string.IsNullOrEmpty(displayKey) || count <= 0)
            return;

        Helper.RequestNetworkStorageWithdraw(blockPosition, displayKey, count);
        RefreshAfterMutation();
    }

    private void TryDepositHolding()
    {
        Helper.RequestNetworkStorageDepositHolding(blockPosition);
        RefreshAfterMutation();
    }

    private void TryDepositAll()
    {
        Helper.RequestNetworkStorageDepositAll(blockPosition);
        RefreshAfterMutation();
    }

    private void RefreshAfterMutation()
    {
        RefreshSnapshot(true);
        RefreshBindings(true);
        storageList?.ForceRefresh();
    }

    private string GetNetworkStatusText()
    {
        if (te == null)
            return "Interface not found";

        if (te.NetworkId == Guid.Empty)
            return "No network linked";

        return "Linked";
    }

    private void RefreshSnapshot(bool force)
    {
        WorldBase world = GameManager.Instance?.World;
        if (world == null || world.IsRemote())
            return;

        if (te == null)
            te = GetTileEntity();

        if (te == null)
            return;

        ulong now = world.GetWorldTime();
        if (!force && now >= lastRefreshWorldTime && (now - lastRefreshWorldTime) < 10UL)
            return;

        lastRefreshWorldTime = now;

        te.ResolveNetworkId(world, true);
        te.RefreshSnapshotSummary(world);
    }

    private TileEntityNetworkStorageInterface GetTileEntity()
    {
        WorldBase world = GameManager.Instance?.World;
        if (world == null)
            return null;

        return world.GetTileEntity(0, blockPosition) as TileEntityNetworkStorageInterface;
    }
}
