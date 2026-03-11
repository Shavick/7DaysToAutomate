using System;

public class TileEntityFluidStorage : TileEntityMachine
{
    private const int PersistVersion = 1;

    private string fluidType = string.Empty;
    private int fluidAmountMg = 0;
    private int acceptedThisTickMg = 0;

    public TileEntityFluidStorage(Chunk chunk) : base(chunk)
    {
    }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.FluidStorage);
    }

    public string FluidType => fluidType;
    public int FluidAmountMg => fluidAmountMg;

    public int GetCapacityMg()
    {
        int gallons = GetPropertyInt("FluidStorageCapacityGallons", 1000);
        if (gallons < 0)
            gallons = 0;

        return gallons * FluidConstants.MilliGallonsPerGallon;
    }

    public int GetInputCapMgPerTick()
    {
        int gps = GetPropertyInt("FluidInputCapGps", 250);
        if (gps < 0)
            gps = 0;

        return (gps * FluidConstants.MilliGallonsPerGallon) / FluidConstants.SimulationTicksPerSecond;
    }

    public int GetOutputCapMgPerTick()
    {
        int gps = GetPropertyInt("FluidOutputCapGps", GetPropertyInt("FluidInputCapGps", 250));
        if (gps < 0)
            gps = 0;

        return (gps * FluidConstants.MilliGallonsPerGallon) / FluidConstants.SimulationTicksPerSecond;
    }

    public bool CanAcceptType(string requestedFluidType)
    {
        if (string.IsNullOrEmpty(requestedFluidType))
            return false;

        if (string.IsNullOrEmpty(fluidType))
            return true;

        return string.Equals(fluidType, requestedFluidType, StringComparison.Ordinal);
    }

    public int GetFreeSpaceMg()
    {
        int capacity = GetCapacityMg();
        int free = capacity - fluidAmountMg;
        return free > 0 ? free : 0;
    }

    public int GetRemainingInputBudgetMg()
    {
        int remaining = GetInputCapMgPerTick() - acceptedThisTickMg;
        return remaining > 0 ? remaining : 0;
    }

    public int GetAvailableOutputMg()
    {
        int cappedByOutput = GetOutputCapMgPerTick();
        if (cappedByOutput <= 0)
            return 0;

        int available = Math.Min(fluidAmountMg, cappedByOutput);
        return available > 0 ? available : 0;
    }

    public void ResetTickBudget()
    {
        acceptedThisTickMg = 0;
    }

    public int AcceptFluid(string requestedFluidType, int requestedMg)
    {
        if (!CanAcceptType(requestedFluidType) || requestedMg <= 0)
            return 0;

        int amount = Math.Min(requestedMg, GetFreeSpaceMg());
        amount = Math.Min(amount, GetRemainingInputBudgetMg());
        if (amount <= 0)
            return 0;

        fluidAmountMg += amount;
        acceptedThisTickMg += amount;

        int capacity = GetCapacityMg();
        if (fluidAmountMg > capacity)
            fluidAmountMg = capacity;

        if (string.IsNullOrEmpty(fluidType) && fluidAmountMg > 0)
            fluidType = requestedFluidType;

        setModified();
        return amount;
    }

    public int RemoveFluid(int requestedMg)
    {
        if (requestedMg <= 0 || fluidAmountMg <= 0)
            return 0;

        int removed = Math.Min(requestedMg, fluidAmountMg);
        fluidAmountMg -= removed;

        if (fluidAmountMg <= 0)
        {
            fluidAmountMg = 0;
            fluidType = string.Empty;
        }

        setModified();
        return removed;
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        if (mode == StreamModeWrite.Persistency)
            bw.Write(PersistVersion);

        bw.Write(fluidType ?? string.Empty);
        bw.Write(fluidAmountMg);
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);

        if (mode == StreamModeRead.Persistency)
            br.ReadInt32();

        fluidType = br.ReadString() ?? string.Empty;
        fluidAmountMg = br.ReadInt32();

        if (fluidAmountMg < 0)
            fluidAmountMg = 0;

        int cap = GetCapacityMg();
        if (fluidAmountMg > cap)
            fluidAmountMg = cap;

        if (fluidAmountMg == 0)
            fluidType = string.Empty;
    }

    private int GetPropertyInt(string propertyName, int fallback)
    {
        string raw = blockValue.Block?.Properties?.GetString(propertyName);
        if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out int value))
            return fallback;

        return value;
    }
}
