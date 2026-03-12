using System;
using System.Collections.Generic;
using System.Reflection;

public class XUiC_IronExtractorInfo : XUiController
{
    TileEntityUniversalExtractor te;
    Vector3i blockPosition;
    private bool hasStorage = false;
    private XUiC_ExtractorOutputContainerList outputList;
    private XUiC_TextInput priorityInput;
    private bool suppressPriorityInputEvents;
    private int? localPipePriorityOverride;
    private int lastPriorityRequest = int.MinValue;
    private int lastPipePriority = int.MinValue;
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

        var priorityDownBtn = GetChildById("prioritydownbutton")?.ViewComponent as XUiV_Button;
        if (priorityDownBtn != null)
            priorityDownBtn.Controller.OnPress += (c, b) => AdjustPriority(-1);

        var priorityUpBtn = GetChildById("priorityupbutton")?.ViewComponent as XUiV_Button;
        if (priorityUpBtn != null)
            priorityUpBtn.Controller.OnPress += (c, b) => AdjustPriority(1);


        priorityInput = windowGroup.Controller.GetChildById("extractorPriorityInput") as XUiC_TextInput;
        if (priorityInput != null)
        {
            priorityInput.OnChangeHandler += HandlePriorityChanged;
            priorityInput.OnSubmitHandler += HandlePrioritySubmit;
        }

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
    private void HandlePriorityChanged(XUiController sender, string text, bool fromCode = false)
    {
        if (suppressPriorityInputEvents)
            return;

        ApplyPriority(text);
    }

    private void HandlePrioritySubmit(XUiController sender, string text)
    {
        if (suppressPriorityInputEvents)
            return;

        ApplyPriority(text);
    }

    private void ApplyPriority(string text)
    {
        if (!int.TryParse(text, out int requested))
            return;

        RequestPriorityChange(requested);
    }

    private void AdjustPriority(int delta)
    {
        int basePriority;
        if (!TryReadPriorityInputValue(out basePriority))
            basePriority = localPipePriorityOverride ?? (GetExtractor()?.PipePriority ?? TileEntityMachine.DefaultPipePriority);

        RequestPriorityChange(basePriority + delta);
    }

    private void RequestPriorityChange(int requested)
    {
        if (requested < TileEntityMachine.MinPipePriority)
            requested = TileEntityMachine.MinPipePriority;
        else if (requested > TileEntityMachine.MaxPipePriority)
            requested = TileEntityMachine.MaxPipePriority;

        UpdatePriorityInputDisplay(requested);

        if (lastPriorityRequest == requested && localPipePriorityOverride.HasValue && localPipePriorityOverride.Value == requested)
        {
            RefreshBindings(true);
            return;
        }

        lastPriorityRequest = requested;
        localPipePriorityOverride = requested;
        RefreshBindings(true);

        Helper.RequestExtractorSetPriority(blockPosition, requested);
    }

    private bool TryReadPriorityInputValue(out int value)
    {
        value = TileEntityMachine.MinPipePriority;

        if (priorityInput == null)
            return false;

        if (TryReadTextMember(priorityInput, out string text) && int.TryParse(text, out value))
            return true;

        object viewComponent = priorityInput.ViewComponent;
        if (viewComponent != null && TryReadTextMember(viewComponent, out text) && int.TryParse(text, out value))
            return true;

        return false;
    }

    private void UpdatePriorityInputDisplay(int value)
    {
        if (priorityInput == null)
            return;

        string text = value.ToString();
        suppressPriorityInputEvents = true;

        try
        {
            if (TryWriteTextMember(priorityInput, text))
                return;

            object viewComponent = priorityInput.ViewComponent;
            if (viewComponent != null && TryWriteTextMember(viewComponent, text))
                return;
        }
        finally
        {
            suppressPriorityInputEvents = false;
        }
    }

    private static bool TryReadTextMember(object target, out string text)
    {
        text = null;
        if (target == null)
            return false;

        Type type = target.GetType();
        PropertyInfo textProperty = type.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
        if (textProperty != null && textProperty.CanRead && textProperty.PropertyType == typeof(string))
        {
            text = textProperty.GetValue(target, null) as string;
            return true;
        }

        return false;
    }

    private static bool TryWriteTextMember(object target, string text)
    {
        if (target == null)
            return false;

        Type type = target.GetType();

        MethodInfo setTextStringBool = type.GetMethod(
            "SetText",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(string), typeof(bool) },
            null);
        if (setTextStringBool != null)
        {
            setTextStringBool.Invoke(target, new object[] { text, true });
            return true;
        }

        MethodInfo setTextString = type.GetMethod(
            "SetText",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(string) },
            null);
        if (setTextString != null)
        {
            setTextString.Invoke(target, new object[] { text });
            return true;
        }

        PropertyInfo textProperty = type.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
        if (textProperty != null && textProperty.CanWrite && textProperty.PropertyType == typeof(string))
        {
            textProperty.SetValue(target, text, null);
            return true;
        }

        return false;
    }private void UpdateHasStorage()
    {

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
        {
            UpdatePriorityInputDisplay(TileEntityMachine.MinPipePriority);
            return;
        }

        if (outputList == null)
            outputList = GetChildByType<XUiC_ExtractorOutputContainerList>();

        if (outputList != null)
            outputList.SetContext(te, blockPosition);

        UpdateHasStorage();
        UpdatePriorityInputDisplay(te.PipePriority);
        RefreshBindings(true);
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);

        if (te == null)
            te = GetExtractor();
        if (te == null)
        {
            UpdatePriorityInputDisplay(TileEntityMachine.MinPipePriority);
            return;
        }


        if (te.PipePriority != lastPipePriority)
        {
            lastPipePriority = te.PipePriority;
            UpdatePriorityInputDisplay(te.PipePriority);
            RefreshBindings(true);
        }

        if (localPipePriorityOverride.HasValue && te.PipePriority == localPipePriorityOverride.Value)
        {
            localPipePriorityOverride = null;
            UpdatePriorityInputDisplay(te.PipePriority);
            RefreshBindings(true);
        }

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
            if (string.Equals(_bindingName?.Trim(), "fluidfuelvisible", StringComparison.OrdinalIgnoreCase))
            {
                value = "false";
                return true;
            }
            if (string.Equals(_bindingName?.Trim(), "fluidfuelenabled", StringComparison.OrdinalIgnoreCase))
            {
                value = "false";
                return true;
            }

            if (string.Equals(_bindingName?.Trim(), "fluidfueldisabled", StringComparison.OrdinalIgnoreCase))
            {
                value = "false";
                return true;
            }

            if (string.Equals(_bindingName?.Trim(), "fluidfuelfill", StringComparison.OrdinalIgnoreCase))
            {
                value = "0";
                return true;
            }

            if (string.Equals(_bindingName?.Trim(), "fluidfueltext", StringComparison.OrdinalIgnoreCase))
            {
                value = string.Empty;
                return true;
            }

            if (string.Equals(_bindingName?.Trim(), "fluidfuelstatus", StringComparison.OrdinalIgnoreCase))
            {
                value = string.Empty;
                return true;
            }

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


        if (string.Equals(_bindingName?.Trim(), "pipepriority", StringComparison.OrdinalIgnoreCase))
        {
            if (localPipePriorityOverride.HasValue)
                value = localPipePriorityOverride.Value.ToString();
            else
                value = extractor.PipePriority.ToString();
            return true;
        }

        if (string.Equals(_bindingName?.Trim(), "fluidfuelenabled", StringComparison.OrdinalIgnoreCase))
        {
            value = extractor.IsFluidFuelEnabled ? "true" : "false";
            return true;
        }

        if (string.Equals(_bindingName?.Trim(), "fluidfuelvisible", StringComparison.OrdinalIgnoreCase))
        {
            value = extractor.IsFluidFuelEnabled ? "true" : "false";
            return true;
        }

        if (string.Equals(_bindingName?.Trim(), "fluidfueldisabled", StringComparison.OrdinalIgnoreCase))
        {
            value = extractor.IsFluidFuelEnabled ? "false" : "true";
            return true;
        }

        if (string.Equals(_bindingName?.Trim(), "fluidfuelfill", StringComparison.OrdinalIgnoreCase))
        {
            value = extractor.FluidFuelFillPercent.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        if (string.Equals(_bindingName?.Trim(), "fluidfueltext", StringComparison.OrdinalIgnoreCase))
        {
            if (!extractor.IsFluidFuelEnabled)
            {
                value = string.Empty;
                return true;
            }

            int current = (extractor.FluidFuelBufferAmountMg + (FluidConstants.MilliGallonsPerGallon / 2)) / FluidConstants.MilliGallonsPerGallon;
            int cap = (extractor.FluidFuelBufferCapacityMg + (FluidConstants.MilliGallonsPerGallon / 2)) / FluidConstants.MilliGallonsPerGallon;
            value = $"{current}/{cap}g";
            return true;
        }

        if (string.Equals(_bindingName?.Trim(), "fluidfuelstatus", StringComparison.OrdinalIgnoreCase))
        {
            if (!extractor.IsFluidFuelEnabled)
            {
                value = string.Empty;
                return true;
            }

            value = string.IsNullOrEmpty(extractor.LastFluidFuelStatus) ? string.Empty : extractor.LastFluidFuelStatus;
            return true;
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
                    sb.AppendLine($"- {count} {name} every {speed}");
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







