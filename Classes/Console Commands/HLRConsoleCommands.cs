using System;
using System.Collections.Generic;

public class HLRConsoleCommands : ConsoleCmdAbstract
{
    public override string[] getCommands()
    {
        return new string[]
        {
                "hlr"
        };
    }

    public override string getDescription()
    {
        return "Higher Logic Registry debug commands";
    }

    public override string getHelp()
    {
        return
            "hlr devlogs            - toggle HLR dev logging\n" +
            "hlr devlogs on         - enable HLR dev logging\n" +
            "hlr devlogs off        - disable HLR dev logging\n" +
            "hlr list               - list all HLR snapshot counts\n" +
            "hlr list <type>        - list HLR snapshot count for a type";
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        try
        {
            if (_params.Count == 0)
            {
                SdtdConsole.Instance.Output("Invalid Command. Use 'help hlr'");
                return;
            }

            //if (!GamePrefs.GetBool(EnumGamePrefs.DebugMenuEnabled))
            //{
            //    SdtdConsole.Instance.Output("[HLR] This command requires debug mode.");
            //    return;
            //}

            switch (_params[0])
            {
                case "devlogs":
                    HandleDevLogs(_params);
                    return;

                case "list":
                    HandleList(_params);
                    return;
            }

            SdtdConsole.Instance.Output("Invalid Command. Use 'help hlr'");
        }
        catch (Exception ex)
        {
            SdtdConsole.Instance.Output($"Unexpected error: {ex.Message}");
            Log.Exception(ex);
        }
    }

    private void HandleDevLogs(List<string> _params)
    {
        if (_params.Count == 1)
        {
            // toggle
            HigherLogicRegistry.DevLogs = !HigherLogicRegistry.DevLogs;
        }
        else
        {
            switch (_params[1])
            {
                case "on":
                    HigherLogicRegistry.DevLogs = true;
                    break;

                case "off":
                    HigherLogicRegistry.DevLogs = false;
                    break;

                default:
                    SdtdConsole.Instance.Output("Usage: hlr devlogs [on|off]");
                    return;
            }
        }

        SdtdConsole.Instance.Output(
            $"[HLR] DevLogs {(HigherLogicRegistry.DevLogs ? "ENABLED" : "DISABLED")}"
        );
    }

    private void HandleList(List<string> _params)
    {
        World world = GameManager.Instance?.World;
        if (world == null)
        {
            SdtdConsole.Instance.Output("[HLR] FAIL — World is null");
            return;
        }

        HigherLogicRegistry hlr = WorldHLR.GetOrCreate(world);

        if (_params.Count == 1)
        {
            Dictionary<string, int> counts = hlr.GetSnapshotCountsByType();

            SdtdConsole.Instance.Output($"[HLR] Total snapshots: {hlr.GetTotalSnapshotCount()}");
            SdtdConsole.Instance.Output($"[HLR] Real snapshots: {hlr.GetRealSnapshotCount()}");
            SdtdConsole.Instance.Output($"[HLR] Phantom snapshots: {hlr.GetPhantomSnapshotCount()}");

            if (counts.Count == 0)
            {
                SdtdConsole.Instance.Output("[HLR] No snapshots registered");
                return;
            }

            foreach (var kvp in counts)
            {
                int realCount = hlr.GetRealSnapshotCount(kvp.Key);
                int phantomCount = hlr.GetPhantomSnapshotCount(kvp.Key);

                SdtdConsole.Instance.Output(
                    $"[HLR] {kvp.Key}: total={kvp.Value}, real={realCount}, phantom={phantomCount}"
                );
            }

            return;
        }

        string snapshotKind = _params[1];
        int total = hlr.GetSnapshotCount(snapshotKind);
        int real = hlr.GetRealSnapshotCount(snapshotKind);
        int phantom = hlr.GetPhantomSnapshotCount(snapshotKind);

        SdtdConsole.Instance.Output(
            $"[HLR] {snapshotKind}: total={total}, real={real}, phantom={phantom}"
        );
    }

}