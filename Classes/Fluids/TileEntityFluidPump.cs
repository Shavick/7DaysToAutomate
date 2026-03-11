using System;
using System.Collections.Generic;

public class TileEntityFluidPump : TileEntityMachine
{
    private const int PersistVersion = 1;
    private const int EventCapacity = 5;

    private readonly List<PumpEvent> recentEvents = new List<PumpEvent>();

    public bool PumpEnabled = true;

    public TileEntityFluidPump(Chunk chunk) : base(chunk)
    {
    }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.FluidPump);
    }

    public bool IsActivePump()
    {
        return PumpEnabled;
    }

    public int GetOutputCapMgPerTick()
    {
        int gps = GetPropertyInt("FluidPumpOutputCapGps", 250);
        if (gps < 0)
            gps = 0;

        return (gps * FluidConstants.MilliGallonsPerGallon) / FluidConstants.SimulationTicksPerSecond;
    }

    public void RecordBlockedEvent(ulong worldTime, string reason, string detail)
    {
        recentEvents.Add(new PumpEvent
        {
            WorldTime = worldTime,
            Reason = reason ?? string.Empty,
            Detail = detail ?? string.Empty
        });

        while (recentEvents.Count > EventCapacity)
            recentEvents.RemoveAt(0);
    }

    public IEnumerable<string> GetRecentEventsSummary()
    {
        foreach (PumpEvent ev in recentEvents)
            yield return $"t={ev.WorldTime} {ev.Reason} {ev.Detail}";
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        if (mode == StreamModeWrite.Persistency)
            bw.Write(PersistVersion);

        bw.Write(PumpEnabled);
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);

        if (mode == StreamModeRead.Persistency)
            br.ReadInt32();

        PumpEnabled = br.ReadBoolean();
    }

    private int GetPropertyInt(string propertyName, int fallback)
    {
        string raw = blockValue.Block?.Properties?.GetString(propertyName);
        if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out int value))
            return fallback;

        return value;
    }

    private struct PumpEvent
    {
        public ulong WorldTime;
        public string Reason;
        public string Detail;
    }
}

