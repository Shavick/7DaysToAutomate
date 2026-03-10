using HarmonyLib;
using System.Xml.Linq;

[HarmonyPatch(typeof(ProgressionFromXml))]
[HarmonyPatch("Load")]
public class ProgressionFromXML_Load_Patches
{
    static void Postfix(XmlFile _xmlFile)
    {
        if (_xmlFile == null || _xmlFile.XmlDoc == null)
            return;

        XElement root = _xmlFile.XmlDoc.Root;
        if (root == null)
            return;

        TechTreeRegistry.Clear();

        // Look for: <tech_tree name="Mechanical">
        foreach (var treeElement in root.Elements("tech_tree"))
        {
            string treeName = (string)treeElement.Attribute("name");
            if (string.IsNullOrEmpty(treeName))
            {
                Log.Error("[Research] Found <tech_tree> without name=");
                continue;
            }

            // Process children <node />
            foreach (var nodeElement in treeElement.Elements("node"))
            {
                var tech = new TechTreeRegistry.TechNode();

                tech.Tree = treeName;
                tech.Id =
                    (string)nodeElement.Attribute("id") ??
                    (string)nodeElement.Attribute("name");

                if (string.IsNullOrEmpty(tech.Id))
                {
                    Log.Error($"[Research] Node in '{treeName}' missing id/name.");
                    continue;
                }

                tech.X = ReadInt(nodeElement.Attribute("x"));
                tech.Y = ReadInt(nodeElement.Attribute("y"));
                tech.Icon = (string)nodeElement.Attribute("icon") ?? "";

                // unlocks="A,B,C"
                string unlocksRaw = (string)nodeElement.Attribute("unlocks");
                if (!string.IsNullOrEmpty(unlocksRaw))
                {
                    foreach (var part in unlocksRaw.Split(','))
                    {
                        var entry = part.Trim();
                        if (entry.Length > 0)
                            tech.Unlocks.Add(entry);
                    }
                }

                TechTreeRegistry.RegisterNode(tech);
            }
        }

        TechTreeRegistry.BuildDependencies();
        Log.Out("[Research] Tech Tree Loaded Successfully.");
        TechTreeRegistry.DebugDump();
    }

    private static int ReadInt(XAttribute attr)
    {
        if (attr == null) return 0;
        if (int.TryParse(attr.Value, out int v)) return v;
        return 0;
    }
}

