public class XUiC_GrinderInfo : XUiController
{
    private Vector3i blockPosition;
    private TileEntityGrinder te;
    private int inputIndex;
    private int outputIndex;

    public static void Open()
    {

    }

    public override void Init()
    {

    }

    public override void OnOpen()
    {

    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
    }

    private TileEntityGrinder GetTileEntity()
    {
        if (blockPosition == Vector3i.zero || GameManager.Instance.World == null) return null;

        return GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityGrinder;
    }

    private void TogglePower()
    {
        var grinder = GetTileEntity();
        if (grinder == null) return;
        Helper.RequestMachinePowerToggle(grinder.GetClrIdx(), blockPosition, !grinder.IsOn);
    }
}