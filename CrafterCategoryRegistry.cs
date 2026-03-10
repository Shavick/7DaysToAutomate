using System.Collections.Generic;
using System.Linq;

public static class CrafterCategoryRegistry
{
    public static bool Initialized { get; private set; }
    public static readonly HashSet<string> Groups = new HashSet<string>();

    public static void TryInitialize()
    {
        if (Initialized)
            return;

        foreach (var item in ItemClass.list)
        {
            if (item?.Properties == null)
                continue;

            var groupStr = item.Properties.GetString("Group");
            if (string.IsNullOrEmpty(groupStr))
                continue;

            foreach (var g in groupStr.Split(','))
            {
                var root = g.Split('/')[0].Trim();
                if (!string.IsNullOrEmpty(root))
                    Groups.Add(root);
            }
        }

        Initialized = true;

        foreach (var g in Groups.OrderBy(x => x))
            Log.Out("[GROUP] " + g);
    }
}
