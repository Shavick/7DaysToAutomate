using System;
using System.Collections.Generic;
using System.IO;

public abstract class TileEntityMachine : TileEntity
{
    public const int MinPipePriority = 0;
    public const int MaxPipePriority = 9;
    public const int DefaultPipePriority = 0;

    protected Guid machineGuid = Guid.Empty;
    protected Dictionary<string, int> pendingOutput = new Dictionary<string, int>();
    protected int pipePriority = DefaultPipePriority;

    public Guid MachineGuid => machineGuid;
    public int PipePriority => pipePriority;

    public bool IsDevLogging => blockValue.Block?.Properties?.GetBool("DevLogs") == true;
    protected bool simulatedByHLR = false;

    public bool IsOn;
    public bool NeedsUiRefresh;

    public void TogglePower()
    {
        Log.Out($"[Machine][{ToWorldPos()}] TogglePower() called. IsOn(before)={IsOn} IsRemote={GameManager.Instance.World.IsRemote()}");
        SetPowerState(!IsOn);
        Log.Out($"[Machine][{ToWorldPos()}] TogglePower() finished. IsOn(after)={IsOn}");
    }

    public virtual int ReceiveBufferedInput(string itemName, int count)
    {
        return 0;
    }

    public void SetPowerState(bool state)
    {
        Log.Out($"[Machine][{ToWorldPos()}] SetPowerState({state}) ENTER. IsOn(before)={IsOn} IsRemote={GameManager.Instance.World.IsRemote()}");

        if (IsOn == state)
        {
            Log.Out($"[Machine][{ToWorldPos()}] SetPowerState EARLY RETURN (no change)");
            return;
        }

        IsOn = state;
        OnPowerStateChanged(state);
        NeedsUiRefresh = true;

        if (!GameManager.Instance.World.IsRemote())
        {
            Log.Out($"[Machine][{ToWorldPos()}] SetPowerState SERVER -> calling setModified()");
            setModified();
        }
        else
        {
            Log.Out($"[Machine][{ToWorldPos()}] SetPowerState CLIENT -> not calling setModified()");
        }

        Log.Out($"[Machine][{ToWorldPos()}] SetPowerState EXIT. IsOn(after)={IsOn}");

    }

    protected virtual void OnPowerStateChanged(bool state)
    {
        // Overrided by individual machines for specific logic
    }

    public abstract override TileEntityType GetTileEntityType();

    private void DevLog(string msg)
    {
        if (!IsDevLogging)
            return;

        Log.Out($"[Extractor][TE][{ToWorldPos()}] {msg}");
    }

    protected void EnsureMachineGuid()
    {
        if (machineGuid == Guid.Empty)
        {
            machineGuid = Guid.NewGuid();
        }
    }

    protected void AddPendingOutput(string itemName, int count)
    {
        if (count <= 0)
            return;
        if (pendingOutput.TryGetValue(itemName, out int existing))
            pendingOutput[itemName] = existing + count;
        else
            pendingOutput[itemName] = count;
    }

    protected void ClearPendingOutput()
    {
        pendingOutput.Clear();
    }

    protected void LogPendingOutput()
    {
        if (!IsDevLogging)
            return;

        foreach (var kvp in pendingOutput)
            Log.Out($"[Machine][{ToWorldPos()}] Pending {kvp.Value}x {kvp.Key}");
    }

    protected TileEntityMachine(Chunk chunk) : base(chunk)
    {
        EnsureMachineGuid();
    }

    public virtual IHLRSnapshot BuildHLRSnapshot(WorldBase world)
    {
        return null;
    }

    public virtual void ApplyHLRSnapshot(object shapshot)
    {
        //TODO: Apply snapshot logic
    }

    public virtual void SetSimulatedByHLR(bool value)
    {
        simulatedByHLR = value;
    }

    protected bool IsSimulatingHLR()
    {
        return simulatedByHLR;
    }

    public bool ServerSetPipePriority(int requestedPriority)
    {
        int clampedPriority = requestedPriority;
        if (clampedPriority < MinPipePriority)
            clampedPriority = MinPipePriority;
        else if (clampedPriority > MaxPipePriority)
            clampedPriority = MaxPipePriority;

        if (pipePriority == clampedPriority)
            return false;

        pipePriority = clampedPriority;
        NeedsUiRefresh = true;

        if (!GameManager.Instance.World.IsRemote())
            setModified();

        return true;
    }

    public override void setModified()
    {
        base.setModified();
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        //Log.Out($"[Machine][{ToWorldPos()}] WRITE BEGIN mode={mode}");

        base.write(bw, mode);

        // GUID
        //Log.Out($"[Machine][{ToWorldPos()}] WRITE GUID = {machineGuid}");
        bw.Write(machineGuid.ToString());
        bw.Write(pendingOutput.Count);

        //Log.Out($"[Machine][{ToWorldPos()}] WRITE pendingOutput COUNT = {pendingOutput.Count}");

        foreach (var kvp in pendingOutput)
        {
            //Log.Out($"[Machine][{ToWorldPos()}] WRITE pending {kvp.Key} x{kvp.Value}");
            bw.Write(kvp.Key);
            bw.Write(kvp.Value);
        }

        bw.Write(pipePriority);

        //Log.Out($"[Machine][{ToWorldPos()}] WRITE END");
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        //Log.Out($"[Machine][{ToWorldPos()}] READ BEGIN mode={mode}");

        base.read(br, mode);

        // GUID
        string guidStr = br.ReadString();
        //Log.Out($"[Machine][{ToWorldPos()}] READ GUID string = '{guidStr}'");

        if (!string.IsNullOrEmpty(guidStr))
            machineGuid = Guid.Parse(guidStr);
        else
            EnsureMachineGuid();

        //Log.Out($"[Machine][{ToWorldPos()}] READ GUID = {machineGuid}");

        pendingOutput.Clear();

        int pendingCount = br.ReadInt32();
        //Log.Out($"[Machine][{ToWorldPos()}] READ pendingOutput COUNT = {pendingCount}");

        for (int i = 0; i < pendingCount; i++)
        {
            string item = br.ReadString();
            int count = br.ReadInt32();

            //Log.Out($"[Machine][{ToWorldPos()}] READ pending {item} x{count}");
            pendingOutput[item] = count;
        }

        try
        {
            int savedPriority = br.ReadInt32();
            if (savedPriority < MinPipePriority)
                pipePriority = MinPipePriority;
            else if (savedPriority > MaxPipePriority)
                pipePriority = MaxPipePriority;
            else
                pipePriority = savedPriority;
        }
        catch (EndOfStreamException)
        {
            // Backward compatibility with saves created before pipePriority existed.
            pipePriority = DefaultPipePriority;
        }

        //Log.Out($"[Machine][{ToWorldPos()}] READ END");
    }
}

