using System;
using System.Globalization;

public class XUiC_FluidMixerInfo : XUiController
{
    private Vector3i blockPosition;
    private TileEntityFluidMixer te;

    public override void Init()
    {
        base.Init();

        var closeBtn = GetChildById("closeButton")?.ViewComponent as XUiV_Button;
        if (closeBtn != null)
            closeBtn.Controller.OnPress += (c, b) => xui.playerUI.windowManager.Close("FluidMixerInfo");

        var powerBtn = GetChildById("powerbutton")?.ViewComponent as XUiV_Button;
        if (powerBtn != null)
            powerBtn.Controller.OnPress += (c, b) => TogglePower();

        var recipePrevBtn = GetChildById("recipeprevbutton")?.ViewComponent as XUiV_Button;
        if (recipePrevBtn != null)
            recipePrevBtn.Controller.OnPress += (c, b) => Helper.RequestFluidMixerCycleRecipe(blockPosition, -1);

        var recipeNextBtn = GetChildById("recipenextbutton")?.ViewComponent as XUiV_Button;
        if (recipeNextBtn != null)
            recipeNextBtn.Controller.OnPress += (c, b) => Helper.RequestFluidMixerCycleRecipe(blockPosition, 1);
    }

    public static void Open(EntityPlayerLocal player, Vector3i pos)
    {
        if (player?.playerUI == null)
            return;

        var ctrl = player.playerUI.xui?.GetChildByType<XUiC_FluidMixerInfo>();
        if (ctrl != null)
            ctrl.blockPosition = pos;

        player.playerUI.windowManager.Open("FluidMixerInfo", true, false, true);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        te = GetTileEntity();
        RefreshBindings(true);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (te == null)
            te = GetTileEntity();

        if (te == null || !te.NeedsUiRefresh)
            return;

        te.NeedsUiRefresh = false;
        RefreshBindings(true);
    }

    private TileEntityFluidMixer GetTileEntity()
    {
        if (blockPosition == default || GameManager.Instance?.World == null)
            return null;

        return GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityFluidMixer;
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        TileEntityFluidMixer mixer = GetTileEntity();
        WorldBase world = GameManager.Instance?.World;

        switch (bindingName?.Trim() ?? string.Empty)
        {
            case "machinename":
                value = mixer?.blockValue.Block?.GetLocalizedBlockName() ?? "Fluid Mixer";
                return true;
            case "selected_recipe":
                value = mixer?.GetSelectedRecipeDisplayName() ?? "None";
                return true;
            case "powerbutton":
                if (mixer == null)
                {
                    value = "Turn On";
                    return true;
                }

                value = mixer.IsOn ? "Turn Off" : "Turn On";
                return true;
            case "machine_state":
                if (mixer == null)
                {
                    value = "Offline";
                    return true;
                }

                value = mixer.AreAllRequirementsMet(world) ? (mixer.IsProcessing ? "Mixing" : "Ready") : "Waiting";
                return true;
            case "cycle_timer":
                value = mixer == null ? "0/0" : $"{mixer.CycleTickCounter}/{Math.Max(1, mixer.CycleTickLength)}";
                return true;
            case "last_action":
                value = mixer?.LastAction ?? "Idle";
                return true;
            case "block_reason":
                value = mixer?.LastBlockReason ?? string.Empty;
                return true;
            case "pending_fluid_input_a_name":
                value = ToFluidDisplayName(mixer?.PendingFluidInputAType);
                return true;
            case "pending_fluid_input_a":
                value = mixer == null ? "0 gal" : $"{FormatGallons(mixer.PendingFluidInputAAmountMg)} gal";
                return true;
            case "pending_fluid_input_b_name":
                value = ToFluidDisplayName(mixer?.PendingFluidInputBType);
                return true;
            case "pending_fluid_input_b":
                value = mixer == null ? "0 gal" : $"{FormatGallons(mixer.PendingFluidInputBAmountMg)} gal";
                return true;
            case "pending_fluid_output_name":
                value = ToFluidDisplayName(mixer?.PendingFluidOutputType);
                return true;
            case "pending_fluid_output":
                value = mixer == null ? "0/0 gal" : $"{FormatGallons(mixer.pendingFluidOutput)}/{FormatGallons(mixer.pendingFluidOutputCapacityMg)} gal";
                return true;
            case "req_recipe":
                value = mixer != null && mixer.HasSelectedRecipe() ? "true" : "false";
                return true;
            case "req_not_recipe":
                value = mixer != null && mixer.HasSelectedRecipe() ? "false" : "true";
                return true;
            case "req_fluid_input":
                value = mixer != null && mixer.HasFluidInputRequirement(world) ? "true" : "false";
                return true;
            case "req_not_fluid_input":
                value = mixer != null && mixer.HasFluidInputRequirement(world) ? "false" : "true";
                return true;
            case "req_fluid_output":
                value = mixer != null && mixer.HasFluidOutputRequirement(world) ? "true" : "false";
                return true;
            case "req_not_fluid_output":
                value = mixer != null && mixer.HasFluidOutputRequirement(world) ? "false" : "true";
                return true;
        }

        return false;
    }

    private void TogglePower()
    {
        TileEntityFluidMixer mixer = GetTileEntity();
        if (mixer == null)
            return;

        Helper.RequestMachinePowerToggle(mixer.GetClrIdx(), blockPosition, !mixer.IsOn);
        RefreshBindings(true);
    }

    private static string ToFluidDisplayName(string fluidType)
    {
        if (string.IsNullOrWhiteSpace(fluidType))
            return "None";

        string normalized = fluidType.Trim().Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string FormatGallons(int milliGallons)
    {
        double gallons = milliGallons / (double)FluidConstants.MilliGallonsPerGallon;
        return gallons.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
