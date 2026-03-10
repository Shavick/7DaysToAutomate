using System;
using System.Collections.Generic;

public class CrafterSnapshot : IHLRSnapshot
{
    // ─────────────────────────
    // IHLRSnapshot identity
    // ─────────────────────────
    public string SnapshotKind => "Crafter";
    public int SnapshotVersion => 1;
    public Guid MachineId { get; set; }
    public Vector3i Position { get; set; }
    public ulong LastHLRSimTime;

    public bool IsPhantom;
    public float CraftProgressSeconds;


    // ─────────────────────────
    // Crafter-specific state
    // ─────────────────────────

    public string RecipeName;
    public bool IsCrafting;
    public bool DisabledByPlayer;
    public ulong CraftStartTime;
    public float BaseRecipeDuration;
    public float CraftSpeed;

    public Dictionary<string, int> IngredientCount;
    public Dictionary<string, int> UsedIngredients;
    public Dictionary<string, int> OwedResources;
}