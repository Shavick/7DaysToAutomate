using System;
using System.Collections.Generic;

public class ConsoleCmdMachineRecipes : ConsoleCmdAbstract
{
    public override string getDescription()
    {
        return "Machine recipe debug commands.";
    }

    public override string getHelp()
    {
        return
            "Usage:\n" +
            "  mr open\n" +
            "  mr list [machineGroup] [verbose]\n" +
            "Examples:\n" +
            "  mr open\n" +
            "  mr list\n" +
            "  mr list fluiddecanter\n" +
            "  mr list fluiddecanter verbose";
    }

    public override string[] getCommands()
    {
        return new[] { "machinerecipes", "machinerecipe", "mr" };
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        try
        {
            if (_params == null || _params.Count == 0)
            {
                SdtdConsole.Instance.Output(getHelp());
                return;
            }

            if (string.Equals(_params[0], "open", StringComparison.OrdinalIgnoreCase))
            {
                OpenCodex();
                return;
            }

            if (!string.Equals(_params[0], "list", StringComparison.OrdinalIgnoreCase))
            {
                SdtdConsole.Instance.Output(getHelp());
                return;
            }

            MachineRecipeRegistry.GetSummary(out bool loaded, out int total, out int valid, out int skipped, out int deduped);
            SdtdConsole.Instance.Output($"[MachineRecipe] loaded={loaded} total={total} valid={valid} skipped={skipped} deduped={deduped}");

            string groupFilter = string.Empty;
            bool verbose = false;

            if (_params.Count >= 2)
            {
                if (string.Equals(_params[1], "verbose", StringComparison.OrdinalIgnoreCase))
                    verbose = true;
                else
                    groupFilter = _params[1];
            }

            if (_params.Count >= 3 && string.Equals(_params[2], "verbose", StringComparison.OrdinalIgnoreCase))
                verbose = true;

            if (string.IsNullOrEmpty(groupFilter))
            {
                Dictionary<string, int> counts = MachineRecipeRegistry.GetMachineGroupCounts();
                if (counts.Count <= 0)
                {
                    SdtdConsole.Instance.Output("[MachineRecipe] No machine recipe groups loaded.");
                }
                else
                {
                    List<string> groups = new List<string>(counts.Keys);
                    groups.Sort(StringComparer.Ordinal);
                    for (int i = 0; i < groups.Count; i++)
                    {
                        string group = groups[i];
                        int count = counts.TryGetValue(group, out int c) ? c : 0;
                        SdtdConsole.Instance.Output($"[MachineRecipe] group='{group}' count={count}");
                    }
                }
            }
            else
            {
                List<MachineRecipe> recipes = MachineRecipeRegistry.GetRecipesForMachineGroups(groupFilter, false);
                SdtdConsole.Instance.Output($"[MachineRecipe] group='{groupFilter}' count={recipes.Count}");

                for (int i = 0; i < recipes.Count; i++)
                {
                    MachineRecipe recipe = recipes[i];
                    if (recipe == null)
                        continue;

                    string display = string.IsNullOrEmpty(recipe.Name) ? recipe.NormalizedKey : recipe.Name;
                    SdtdConsole.Instance.Output(
                        $"[MachineRecipe] [{i}] name='{display}' machine='{recipe.Machine}' inputs={recipe.Inputs.Count} itemOut={recipe.ItemOutputs.Count} fluidOut={recipe.FluidOutputs.Count} gasOut={recipe.GasOutputs.Count}");
                }
            }

            if (verbose)
            {
                List<string> overrides = MachineRecipeRegistry.GetDuplicateOverrideLog();
                if (overrides.Count <= 0)
                {
                    SdtdConsole.Instance.Output("[MachineRecipe] verbose: no duplicate overrides.");
                    return;
                }

                SdtdConsole.Instance.Output($"[MachineRecipe] verbose: duplicate overrides={overrides.Count}");
                for (int i = 0; i < overrides.Count; i++)
                    SdtdConsole.Instance.Output("[MachineRecipe] " + overrides[i]);
            }
        }
        catch (Exception ex)
        {
            SdtdConsole.Instance.Output($"[MachineRecipe] ERROR - {ex.Message}");
            Log.Exception(ex);
        }
    }

    private static void OpenCodex()
    {
        World world = GameManager.Instance?.World;
        EntityPlayerLocal localPlayer = world?.GetPrimaryPlayer() as EntityPlayerLocal;
        if (localPlayer?.playerUI?.windowManager == null)
        {
            SdtdConsole.Instance.Output("[MachineRecipe] Could not open codex. Local player UI is unavailable.");
            return;
        }

        XUiC_MachineRecipeCodex.Open(localPlayer);
        SdtdConsole.Instance.Output("[MachineRecipe] Opened Machine Recipe Codex.");
    }
}
