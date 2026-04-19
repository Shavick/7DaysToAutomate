using System;
using System.Collections.Generic;

public class XUiC_NetworkStorageList : XUiController
{
    private XUiC_NetworkStorageInterface owner;
    private XUiC_TextInput searchInput;
    private string searchTerm = string.Empty;
    private int lastRevision = -1;

    private readonly List<NetworkDisplayStack> filteredStacks = new List<NetworkDisplayStack>();
    private XUiC_NetworkStorageEntry[] entryControls;

    public void SetOwner(XUiC_NetworkStorageInterface interfaceController)
    {
        owner = interfaceController;
    }

    public override void Init()
    {
        base.Init();

        entryControls = GetChildrenByType<XUiC_NetworkStorageEntry>();
        for (int i = 0; i < entryControls.Length; i++)
            entryControls[i].OwnerList = this;

        searchInput = windowGroup?.Controller?.GetChildById("networkSearchInput") as XUiC_TextInput;
        if (searchInput != null)
        {
            searchInput.OnChangeHandler += OnSearchChanged;
            searchInput.OnSubmitHandler += OnSearchSubmitted;
        }

        RefreshNow(true);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (owner == null)
            return;

        int revision = owner.GetSnapshotRevision();
        if (revision != lastRevision)
        {
            RefreshNow(false);
            return;
        }

        if (!IsDirty)
            return;

        RenderEntries();
    }

    public void ForceRefresh()
    {
        RefreshNow(false);
    }

    public void OnEntryPressed(XUiC_NetworkStorageEntry entry, int mouseButton, bool isShift)
    {
        if (entry?.DisplayStack == null || owner == null)
            return;

        int count = 1;
        if (mouseButton == 1 || isShift)
        {
            int maxStack = entry.DisplayStack.DisplayStack?.itemValue?.ItemClass?.Stacknumber?.Value ?? 1;
            count = Math.Max(1, maxStack);
        }

        string displayKey = NetworkItemIdentity.GetDisplayKey(entry.DisplayStack.DisplayStack);
        owner.TryWithdraw(displayKey, count);
    }

    private void OnSearchChanged(XUiController sender, string text, bool fromCode = false)
    {
        searchTerm = text ?? string.Empty;
        RefreshNow(false);
    }

    private void OnSearchSubmitted(XUiController sender, string text)
    {
        searchTerm = text ?? string.Empty;
        RefreshNow(false);
    }

    private void RefreshNow(bool forceSnapshotRebuild)
    {
        if (owner == null)
            return;

        List<NetworkDisplayStack> allStacks = owner.GetDisplayStacks(forceSnapshotRebuild);
        BuildFilteredList(allStacks);
        lastRevision = owner.GetSnapshotRevision();
        IsDirty = true;
        RenderEntries();
    }

    private void BuildFilteredList(List<NetworkDisplayStack> source)
    {
        filteredStacks.Clear();
        if (source == null || source.Count == 0)
            return;

        bool hasSearch = !string.IsNullOrWhiteSpace(searchTerm);
        for (int i = 0; i < source.Count; i++)
        {
            NetworkDisplayStack stack = source[i];
            if (stack == null || !NetworkItemIdentity.IsValidStack(stack.DisplayStack))
                continue;

            if (hasSearch)
            {
                string itemName = stack.DisplayStack.itemValue.ItemClass.GetLocalizedItemName() ?? string.Empty;
                string stableName = NetworkItemIdentity.GetStableDisplayName(stack.DisplayStack);
                if (itemName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0 &&
                    stableName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }

            filteredStacks.Add(stack);
        }
    }

    private void RenderEntries()
    {
        if (entryControls == null || entryControls.Length == 0)
        {
            IsDirty = false;
            return;
        }

        for (int i = 0; i < entryControls.Length; i++)
        {
            XUiC_NetworkStorageEntry entry = entryControls[i];
            NetworkDisplayStack stack = (i < filteredStacks.Count) ? filteredStacks[i] : null;
            entry.SetDisplayStack(stack);

            bool visible = stack != null;
            if (entry.ViewComponent != null)
            {
                entry.ViewComponent.Enabled = visible;
                entry.ViewComponent.IsVisible = visible;
            }
        }

        IsDirty = false;
    }
}
