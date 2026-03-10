using System;
using System.Collections.Generic;

public class XUiC_IronExtractorInfo : XUiController
{
    TileEntityUniversalExtractor te;
    Vector3i blockPosition;
    private bool hasStorage = false;
    private XUiC_ExtractorOutputContainerList outputList;
    public override void Init()
    {
        base.Init();
        IsDirty = true;

        te = GetExtractor();
        UpdateHasStorage();

        XUiView closeButtonView = (XUiV_Button)base.GetChildById("closeButton").ViewComponent;
        closeButtonView.Controller.OnPress += (c, b) => xui.playerUI.windowManager.Close("ExtractorInfo");

        var powerBtn = GetChildById("powerbutton")?.ViewComponent as XUiV_Button;
        if (powerBtn != null)
            powerBtn.Controller.OnPress += (c, b) => PowerButton_OnPress();

        outputList = GetChildByType<XUiC_ExtractorOutputContainerList>();
        if (outputList != null && te != null)
            outputList.SetContext(te, blockPosition);
    }

    private void PowerButton_OnPress()
    {
        te = GetExtractor();
        if (te == null)
        {
            Log.Error("Tile entity is Null");
            return;
        }
        int clrIdx = te.GetClrIdx();
        Log.Out($"[ExtractorUI] Request power toggle at {blockPosition} (clrIdx={clrIdx})");
        Helper.RequestMachinePowerToggle(clrIdx, blockPosition, !te.IsOn);
        RefreshBindings();
    }

    private void UpdateHasStorage()
    {
        Log.Out("UpdateHasStorage called.");

        te = GetExtractor();
        if (te == null)
        {
            hasStorage = false;
            RefreshBindings(true);
            return;
        }

        List<OutputTargetInfo> outputs = te.GetAvailableOutputTargets(GameManager.Instance.World);
        hasStorage = outputs != null && outputs.Count > 0;

        RefreshBindings(true);
    }

    public static void Open(EntityPlayerLocal _player, Vector3i pos)
    {
        Log.Out("Attempt Extractor Open...");

        WorldBase world = GameManager.Instance.World;
        TileEntity te = world.GetTileEntity(pos);
        if (te == null)
        {
            Log.Error($"TileEntity for XUiC_IronExtractorInfo is null at pos {pos}");
            return;
        }

        var ctrl = _player.playerUI.xui.GetChildByType<XUiC_IronExtractorInfo>();
        if (ctrl == null)
        {
            Log.Error("Unable to find XUiC_IronExtractorInfo");
            return;
        }


        // MUST be set before windowManager.Open(), because OnOpen() runs during Open()
        ctrl.blockPosition = pos;
        ctrl.RefreshBindings();

        // Optional: clear cached TE so OnOpen will re-resolve cleanly
        ctrl.te = null;
        _player.playerUI.windowManager.Open("ExtractorInfo", true, false, true);
    }

    public override void OnOpen()
    {
        base.OnOpen();

        te = GetExtractor();
        if (te == null)
            return;

        if (outputList == null)
            outputList = GetChildByType<XUiC_ExtractorOutputContainerList>();

        if (outputList != null)
            outputList.SetContext(te, blockPosition);

        UpdateHasStorage();
        RefreshBindings(true);
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);

        if (te == null)
            te = GetExtractor();

        if (te == null)
            return;

        if (!te.NeedsUiRefresh)
            return;

        te.NeedsUiRefresh = false;

        UpdateHasStorage();

        if (outputList != null)
            outputList.IsDirty = true;

        RefreshBindings(true);
    }

    private void OnCloseButtonPressed(XUiController _sender, int _mouseButton)
    {
        xui.playerUI.windowManager.Close(WindowGroup);
    }

    private TileEntityUniversalExtractor GetExtractor()
    {
        if (blockPosition == default)
        {
            //Log.Error("[ExtractorInfo] GetExtractor: blockPosition is default (0,0,0)");
            return null;
        }
        if (GameManager.Instance?.World == null)
        {
            //Log.Error("[ExtractorInfo] GetExtractor: World is null");
            return null;
        }
        var te = GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityUniversalExtractor;
        //Log.Error($"[ExtractorInfo] GetExtractor: pos={blockPosition}, te={(te != null ? "found" : "null/missing")}");
        return te;
    }

    public override bool GetBindingValueInternal(ref string value, string _bindingName)
    {
        if (string.Equals(_bindingName?.Trim(), "hasstorage", StringComparison.OrdinalIgnoreCase))
        {
            value = hasStorage ? "true" : "false";
            return true;
        }

        if (string.Equals(_bindingName?.Trim(), "nothasstorage", StringComparison.OrdinalIgnoreCase))
        {
            value = !hasStorage ? "true" : "false";
            return true;
        }

        var extractor = GetExtractor();

        if (extractor == null)
        {
            value = "";
            return true;
        }

        var lines = new List<string>();
        foreach (var t in extractor.timers)
        {
            string item = t.Resource;
            string count = t.MinCount == t.MaxCount ? t.MinCount.ToString() : $"{t.MinCount} - {t.MaxCount}";
            string speed = $"{t.Speed}";
            lines.Add($"{item}: {count} every {speed}s");
        }

        switch (_bindingName)
        {
            case "extractorname":
                value = extractor.blockValue.Block.GetLocalizedBlockName() ?? "Unknown";
                return true;

            case "produceditems":
                value = string.Join("\n", lines);
                return true;

            case "itemlist":
                value = string.Join(", ", extractor.timers.ConvertAll(t => t.Resource));
                return true;

            case "countlist":
                value = string.Join(", ", extractor.timers.ConvertAll(t => t.MinCount == t.MaxCount ? t.MaxCount.ToString() : $"{t.MinCount}-{t.MaxCount}"));
                return true;

            case "speedlist":
                value = string.Join(", ", extractor.timers.ConvertAll(t => $"{t.Speed}s"));
                return true;

            case "production":
                var sb = new System.Text.StringBuilder();
                foreach (var t in extractor.timers)
                {
                    var itemValue = ItemClass.GetItem(t.Resource, false);
                    if (itemValue == null) continue;

                    string name = itemValue.ItemClass.GetLocalizedItemName() ?? t.Resource;
                    string count = t.MinCount == t.MaxCount ? t.MinCount.ToString() : $"{t.MinCount}-{t.MaxCount}";
                    string speed = $"{t.Speed}s";
                    sb.AppendLine($"• {count} {name} every {speed}");
                }
                value = sb.ToString().Trim();
                return true;

            case "powerbutton":
                value = extractor.isExtractorOn ? "Turn Off" : "Turn On";
                return true;
        }

        return false;
    }
}