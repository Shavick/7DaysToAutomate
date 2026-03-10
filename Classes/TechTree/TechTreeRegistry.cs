using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class TechTreeRegistry
{
    private const int MinCoord = -1500;
    private const int MaxCoord = 1500;

    public class TechNode
    {
        public string Id;
        public string Tree;
        public int X;
        public int Y;
        public string Icon;
        public List<string> Unlocks = new List<string>();
        public List<TechNode> Requires = new List<TechNode>();
    }

    private static readonly Dictionary<string, Dictionary<string, TechNode>> trees =
        new Dictionary<string, Dictionary<string, TechNode>>(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, TechNode> allNodes =
        new Dictionary<string, TechNode>(StringComparer.OrdinalIgnoreCase);

    public static void RegisterNode(TechNode node)
    {
        if (node == null) return;

        if (node.X < MinCoord || node.X > MaxCoord || node.Y < MinCoord || node.Y > MaxCoord)
        {
            Debug.LogError($"[Research] Node '{node.Id}' has out-of-range coordinates ({node.X},{node.Y}).");
            return;
        }

        if (string.IsNullOrEmpty(node.Tree))
        {
            Debug.LogError($"[Research] Node '{node.Id}' missing tree assignment.");
            return;
        }

        if (allNodes.ContainsKey(node.Id))
        {
            Debug.LogError($"[Research] Duplicate research node ID detected: {node.Id}");
            return;
        }

        allNodes[node.Id] = node;

        if (!trees.TryGetValue(node.Tree, out var dict))
        {
            dict = new Dictionary<string, TechNode>(StringComparer.OrdinalIgnoreCase);
            trees[node.Tree] = dict;
        }

        dict[node.Id] = node;
    }

    public static void BuildDependencies()
    {
        foreach (var node in allNodes.Values)
            node.Requires.Clear();

        foreach (var node in allNodes.Values)
        {
            foreach (var id in node.Unlocks)
            {
                if (!allNodes.TryGetValue(id, out var target))
                {
                    Debug.LogError($"[Research] Node '{node.Id}' references unknown unlock '{id}'.");
                    continue;
                }

                if (!target.Requires.Contains(node))
                    target.Requires.Add(node);
            }
        }
    }

    public static Dictionary<string, TechNode> GetTree(string treeName)
    {
        if (trees.TryGetValue(treeName, out var tree))
            return tree;
        return null;
    }

    public static IEnumerable<string> GetTrees() => trees.Keys;

    public static TechNode GetNode(string id)
    {
        allNodes.TryGetValue(id, out var node);
        return node;
    }

    public static IEnumerable<TechNode> GetAllNodes() => allNodes.Values;

    public static void Clear()
    {
        trees.Clear();
        allNodes.Clear();
    }

    public static void DebugDump()
    {
        Log.Out("========== TECH TREE DEBUG DUMP ==========");

        foreach (var kvp in trees)
        {
            string treeName = kvp.Key;
            Log.Out($"-- TREE: {treeName} --");

            foreach (var nodeEntry in kvp.Value)
            {
                var n = nodeEntry.Value;

                Log.Out($"   Node: {n.Id}   Pos=({n.X},{n.Y}) Icon={n.Icon}");

                if (n.Unlocks.Count > 0)
                    Log.Out($"      Unlocks: {string.Join(", ", n.Unlocks)}");
                else
                    Log.Out("      Unlocks: (none)");

                if (n.Requires.Count > 0)
                    Log.Out($"      Requires: {string.Join(", ", n.Requires.Select(r => r.Id))}");
                else
                    Log.Out("      Requires: (none)");
            }
        }

        Log.Out("==========================================");
    }

}
