using System.Collections.Generic;

public class ConsoleCmdHLRPhantom : ConsoleCmdAbstract
{
    public override string getDescription()
    {
        return "Adds or clears phantom extractor snapshots in the HLR for stress testing.";
    }

    public override string getHelp()
    {
        return
            "Usage:\n" +
            "  hlrp add extractor <count>\n" +
            "  hlrp add crafter <count> <recipeName>\n" +
            "  hlrp clear";
    }

    public override string[] getCommands()
    {
        return new[] { "hlrp", "hlrphantom" };
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        if (_params == null || _params.Count == 0)
        {
            SdtdConsole.Instance.Output(getHelp());
            return;
        }

        //if (!GamePrefs.GetBool(EnumGamePrefs.DebugMenuEnabled))
        //{
        //    SdtdConsole.Instance.Output("[HLR] This command requires debug mode.");
        //    return;
        //}

        World world = GameManager.Instance?.World;
        if (world == null)
        {
            SdtdConsole.Instance.Output("[HLR][Phantom] FAIL — World is null");
            return;
        }

        HigherLogicRegistry hlr = WorldHLR.GetOrCreate(world);

        string action = _params[0].ToLowerInvariant();

        if (action == "clear")
        {
            int removed = hlr.ClearPhantomMachines();
            SdtdConsole.Instance.Output($"[HLR][Phantom] SUCCESS — removed {removed} phantom machine(s)");
            return;
        }

        if (action == "add")
        {
            if (_params.Count < 3)
            {
                SdtdConsole.Instance.Output("[HLR][Phantom] FAIL — Missing machine type or count");
                SdtdConsole.Instance.Output("Usage: hlrp add <extractor|crafter> <count> [recipeName]");
                return;
            }

            string machineType = _params[1].ToLowerInvariant();

            if (!int.TryParse(_params[2], out int count) || count <= 0)
            {
                SdtdConsole.Instance.Output($"[HLR][Phantom] FAIL — Invalid count '{_params[2]}'");
                return;
            }

            if (machineType == "extractor")
            {
                int added = hlr.AddPhantomExtractors(count);
                SdtdConsole.Instance.Output($"[HLR][Phantom] SUCCESS — added {added} phantom extractor(s)");
                return;
            }

            if (machineType == "crafter")
            {
                if (_params.Count < 4)
                {
                    SdtdConsole.Instance.Output("[HLR][Phantom] FAIL — Missing recipeName for crafter");
                    SdtdConsole.Instance.Output("Usage: hlrp add crafter <count> <recipeName>");
                    return;
                }

                string recipeName = _params[3];
                int added = hlr.AddPhantomCrafters(count, recipeName);
                SdtdConsole.Instance.Output($"[HLR][Phantom] SUCCESS — added {added} phantom crafter(s) using recipe '{recipeName}'");
                return;
            }

            SdtdConsole.Instance.Output($"[HLR][Phantom] FAIL — Unknown machine type '{machineType}'");
            SdtdConsole.Instance.Output("Usage: hlrp add <extractor|crafter> <count> [recipeName]");
            return;
        }

        SdtdConsole.Instance.Output($"[HLR][Phantom] FAIL — Unknown action '{action}'");
        SdtdConsole.Instance.Output(getHelp());
    }
}