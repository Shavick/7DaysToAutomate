public class XUiC_UniversalWasher : XUiController
{
    TileEntityUniversalWasher te;
    Vector3i blockPosition;

    public override void Init()
    {
        base.Init();
        te = GetTileEntity();

        if (te == null)
            Log.Error($"[Washer] Tile Entity is null at pos {blockPosition}");

        if (te.IsDevLogging)
            Log.Out("Washer Window Init() Called");
    }

    private TileEntityUniversalWasher GetTileEntity()
    {
        if (blockPosition == default)
            return null;

        if (GameManager.Instance.World == null)
            return null;

        var te = GameManager.Instance.World.GetTileEntity(blockPosition) as TileEntityUniversalWasher;
        return te;
    }

    public static void Open(EntityPlayerLocal _player, Vector3i pos)
    {

    }

}
