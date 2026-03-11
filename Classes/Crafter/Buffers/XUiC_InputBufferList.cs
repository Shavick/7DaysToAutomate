using System.Collections.Generic;

public class XUiC_InputBufferList : XUiController
{
    private TileEntityUniversalCrafter te;
    private XUiC_InputBufferEntry[] entries;

    public void SetContext(TileEntityUniversalCrafter targetTe)
    {
        te = targetTe;
        IsDirty = true;
    }

    public override void Init()
    {
        base.Init();

        entries = GetChildrenByType<XUiC_InputBufferEntry>();
        IsDirty = true;
    }

    public override void OnOpen()
    {
        base.OnOpen();
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

        List<KeyValuePair<string, int>> bufferItems = BuildBufferItemList();

        for (int i = 0; i < entries.Length; i++)
        {
            XUiC_InputBufferEntry entry = entries[i];

            if (i < bufferItems.Count)
            {
                KeyValuePair<string, int> kvp = bufferItems[i];
                entry.SetItem(kvp.Key, kvp.Value);
                entry.ViewComponent.Enabled = true;
                entry.ViewComponent.IsVisible = true;
            }
            else
            {
                entry.ClearItem();
                entry.ViewComponent.Enabled = false;
                entry.ViewComponent.IsVisible = false;
            }
        }

        IsDirty = false;
    }

    private List<KeyValuePair<string, int>> BuildBufferItemList()
    {
        List<KeyValuePair<string, int>> results = new List<KeyValuePair<string, int>>();

        if (te?.inputBuffer == null || te.inputBuffer.Count == 0)
            return results;

        foreach (KeyValuePair<string, int> kvp in te.inputBuffer)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Value <= 0)
                continue;

            results.Add(kvp);
        }

        results.Sort((a, b) =>
        {
            int countCompare = b.Value.CompareTo(a.Value);
            if (countCompare != 0)
                return countCompare;

            return string.CompareOrdinal(a.Key, b.Key);
        });

        return results;
    }
}
