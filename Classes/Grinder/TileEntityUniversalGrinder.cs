using System;
using System.Collections.Generic;

public class TileEntityGrinder : TileEntityMachine
{
    // - - - - - - - - - - Versioning - - - - - - - - - - 
    private const int PersistVersion = 1;

    // - - - - - - - - - - Logistics - - - - - - - - - - 
    public List<InputTargetInfo> availableInputTargets = new List<InputTargetInfo>();
    public List<OutputTargetInfo> availableOutputTargets = new List<OutputTargetInfo>();
    public Vector3i SelectedInputChestPos = Vector3i.zero;
    public Guid SelectedInputPipeGraphID = Guid.Empty;
    public Vector3i SelectedOutputChestPos = Vector3i.zero;
    public Guid SelectedOutputPipeGraphID = Guid.Empty;
    private TileEntityComposite selectedInputContainer;
    private TileEntityComposite selectedOutputContainer;

    // - - - - - - - - - - Processing state tracking - - - - - - - - - - 
    public bool IsProcessing;
    public int CycleTickCounter;
    public int CycleTickLength = 20;
    public string LastAction = "Idle";
    public string LastBlockReason = "None";
    private int refreshInterval = 20;
    private int refreshTicker;

    // - - - - - - - - - - Block configuration - - - - - - - - - - 
    public new bool IsDevLogging => blockValue.Block.Properties.GetBool("DevLogs");

    public TileEntityGrinder(Chunk chunk) : base(chunk)
    {
    }

    public override TileEntityType GetTileEntityType()
    {
        return unchecked((TileEntityType)UCTileEntityIDs.Grinder);
    }
    private enum DevLogLevel
    {
        Info,
        Warning,
        Error
    }

    private void DevLog(string msg, DevLogLevel level = DevLogLevel.Info)
    {
        if (!IsDevLogging) return;

        string prefix = $"[Grinder][TE][{ToWorldPos()}]";

        switch (level)
        {
            case DevLogLevel.Info:
                Log.Out($"{prefix} {msg}");
                break;
            case DevLogLevel.Warning:
                Log.Warning($"{prefix} {msg}");
                break;
            case DevLogLevel.Error:
                Log.Error($"{prefix} {msg}");
                break;
        }
    }

    // - - - - - - - - - - Update - - - - - - - - - - 
    public override void UpdateTick(World world)
    {
        if (world == null || world.IsRemote() || IsSimulatingHLR()) return;

        EnsureConfigLoaded();
        bool changed = false;
        refreshTicker++;
        if (refreshTicker >= refreshInterval)
        {
            refreshTicker = 0;
            RefreshAvailableInputTargets(world);
            RefreshAvailableOutputTargets(world);
            ResolveSelectedInputContainer(world);
            ResolveSelectedOutputContainer(world);

        }
        changed |= TryFlushPendingOutput(world, out string flushedBlockedReason);
        string nextAction = LastAction;
        string nextReason = flushedBlockedReason ?? "None";

        if (!IsOn)
        {
            if (IsProcessing || CycleTickCounter != 0)
            {
                if (IsProcessing || CycleTickCounter != 0)
                    changed = true;

                IsProcessing = false;
                CycleTickCounter = 0;
                nextAction = "Off";
            }
            else if (!IsProcessing)
            {
                if (!TryBeginCycle(world, out string blockedReason))
                {
                    nextAction = "Waiting";
                    nextReason = blockedReason;
                    if (CycleTickCounter != 0)
                    {
                        CycleTickCounter = 0;
                        changed = true;
                    }
                }
                else
                {
                    nextAction = "Processing";
                    nextReason = "";
                    changed = true;
                }
            }
        }
        else
        {
            CycleTickCounter++;
            changed = true;
            if (CycleTickCounter >= Math.Max(1, CycleTickLength))
            {
                CompleteCycle();
                nextAction = "Cycle Complete";
            }
            else
            {
                nextAction = "Processing";
            }
        }

        if (!string.IsNullOrEmpty(flushedBlockedReason))
            nextAction = flushedBlockedReason;

        if (!string.Equals(LastAction, nextAction, StringComparison.Ordinal))
        {
            LastAction = nextAction;
            changed = true;
        }

        if (!string.Equals(LastBlockReason, nextReason, StringComparison.Ordinal))
        {
            LastBlockReason = nextReason;
            changed = true;
        }

        if (changed)
        {
            NeedsUiRefresh = true;
            setModified();
        }

    }

    private bool TryBeginCycle(WorldBase world, out string blockedReason)
    {
        blockedReason = string.Empty;
        if (world == null)
        {
            blockedReason = "World is null";
            return false;
        }

        if (SelectedInputChestPos == Vector3i.zero || selectedInputContainer == null)
        {
            blockedReason = "No valid input container selected";
            return false;
        }

        if (SelectedOutputChestPos == Vector3i.zero || selectedOutputContainer == null)
        {
            blockedReason = "No valid output container selected";
            return false;
        }

        IsProcessing = true;
        CycleTickCounter = 0;
        CycleTickLength = Math.Max(1, CycleTickLength); // Ensure tick length is at least 1 to avoid division by zero or instant processing
        return true;
    }

    private void CompleteCycle()
    {
        DevLog("Grinder cycle completed. Performing item transfer and resetting state.");
        CycleTickCounter = 0;
    }

    // - - - - - - - - - - Target Discovery - - - - - - - - - - 
    public void RefreshAvailableInputTargets(World world)
    {
        if (world == null) return;

        List<InputTargetInfo> discovered = DiscoverAvailableInputTargets(world);
        if (AreInputTargetsEqual(availableInputTargets, discovered)) return;

        availableInputTargets = discovered;
        setModified();
    }

    public void RefreshAvailableOutputTargets(World world)
    {
        if (world == null) return;
        List<OutputTargetInfo> discovered = DiscoverAvailableOutputTargets(world);
        if (AreOutputTargetsEqual(availableOutputTargets, discovered)) return;

        availableOutputTargets = discovered;
        setModified();
    }

    public void ResolveSelectedInputContainer(WorldBase world)
    {
        selectedInputContainer = null;
        if (world == null && SelectedInputChestPos == Vector3i.zero) return;

        selectedInputContainer = world.GetTileEntity(SelectedInputChestPos) as TileEntityComposite;
    }

    public void ResolveSelectedOutputContainer(WorldBase world)
    {
        selectedOutputContainer = null;
        if (world == null && SelectedOutputChestPos == Vector3i.zero) return;

        selectedOutputContainer = world.GetTileEntity(SelectedOutputChestPos) as TileEntityComposite;
    }

    // - - - - - - - - - - Server Selection - - - - - - - - - - 

    public bool ServerSelectInputContainer(Vector3i chestPos, string pipeGraphID)
    {
        if (GameManager.Instance.World.IsRemote())
        {
            DevLog("ServerSelectInputContainer called on client! Ignoring.", DevLogLevel.Warning);
            return false;
        }

        Guid parsedGraphId = Guid.Empty;
        if (!string.IsNullOrEmpty(pipeGraphID))
            Guid.TryParse(pipeGraphID, out parsedGraphId);
        if (parsedGraphId == Guid.Empty)
        {
            LastAction = "Input Selection Failed";
            LastBlockReason = $"PipeGraphId {pipeGraphID} is invalid";
            NeedsUiRefresh = true;
            return false;
        }

        SelectedInputChestPos = chestPos;
        SelectedInputPipeGraphID = parsedGraphId;
        ResolveSelectedInputContainer(GameManager.Instance.World);

        if (chestPos == Vector3i.zero || selectedInputContainer == null)
        {
            LastAction = "Input Selection Failed";
            LastBlockReason = $"No valid chest or pipe graph selected at position {chestPos}";
            NeedsUiRefresh = true;
            return false;
        }

        NeedsUiRefresh = true;
        setModified();
        return true;
    }

    public bool ServerSelectOutputContainer(Vector3i chestPos, string pipeGraphID)
    {
        if (GameManager.Instance.World.IsRemote())
        {
            DevLog("ServerSelectOutputContainer called on client! Ignoring.", DevLogLevel.Warning);
            return false;
        }
        Guid parsedGraphId = Guid.Empty;
        if (!string.IsNullOrEmpty(pipeGraphID))
            Guid.TryParse(pipeGraphID, out parsedGraphId);
        if (parsedGraphId == Guid.Empty)
        {
            LastAction = "Output Selection Failed";
            LastBlockReason = $"PipeGraphId {pipeGraphID} is invalid";
            NeedsUiRefresh = true;
            return false;
        }
        SelectedOutputChestPos = chestPos;
        SelectedOutputPipeGraphID = parsedGraphId;
        ResolveSelectedOutputContainer(GameManager.Instance.World);
        if (chestPos == Vector3i.zero || selectedOutputContainer == null)
        {
            LastAction = "Output Selection Failed";
            LastBlockReason = $"No valid chest or pipe graph selected at position {chestPos}";
            NeedsUiRefresh = true;
            return false;
        }
        NeedsUiRefresh = true;
        setModified();
        return true;
    }

    // - - - - - - - - - - Pipe Validation and Discovery - - - - - - - - - - 
    private List<InputTargetInfo> DiscoverAvailableInputTargets(World world)
    {
        List<InputTargetInfo> discoveredTargets = new List<InputTargetInfo>();
        HashSet<string> seen = new HashSet<string>();

        Vector3i machinePos = ToWorldPos();
        Vector3i[] sides =
        {
            Vector3i.up,
            Vector3i.down,
            Vector3i.left,
            Vector3i.right,
            Vector3i.forward,
            Vector3i.back
        };

        for (int i = 0; i < sides.Length; i++)
        {
            Vector3i pipePos = machinePos + sides[i];

            TileEntityItemPipe pipeTe = world.GetTileEntity(pipePos) as TileEntityItemPipe;

            if (pipeTe == null || pipeTe.PipeGraphId == Guid.Empty) continue;

            if (!PipeGraphManager.TryGetStorageEndpoints(pipeTe.PipeGraphId, out List<Vector3i> storageEndpoints) || storageEndpoints == null || storageEndpoints.Count == 0) continue;

            for (int j = 0; j < storageEndpoints.Count; j++)
            {
                Vector3i storagePos = storageEndpoints[j];
                string key = $"{storagePos}|{pipeTe.PipeGraphId}";
                if (!seen.Add(key)) continue;

                discoveredTargets.Add(new InputTargetInfo(storagePos, pipeTe.PipeGraphId));
            }
        }
        return discoveredTargets;
    }

    private List<OutputTargetInfo> DiscoverAvailableOutputTargets(WorldBase world)
    {
        List<OutputTargetInfo> results = new List<OutputTargetInfo>();
        HashSet<string> seen = new HashSet<string>();

        Vector3i machinePos = ToWorldPos();
        Vector3i[] sides =
        {
        Vector3i.back,
        Vector3i.right,
        Vector3i.forward,
        Vector3i.left,
        Vector3i.up,
        Vector3i.down
    };

        for (int i = 0; i < sides.Length; i++)
        {
            Vector3i pipePos = machinePos + sides[i];

            TileEntityItemPipe pipeTe = world.GetTileEntity(0, pipePos) as TileEntityItemPipe;
            if (pipeTe == null || pipeTe.PipeGraphId == Guid.Empty)
                continue;

            if (!PipeGraphManager.TryGetStorageEndpoints(pipeTe.PipeGraphId, out List<Vector3i> storageEndpoints) ||
                storageEndpoints == null ||
                storageEndpoints.Count == 0)
                continue;

            for (int j = 0; j < storageEndpoints.Count; j++)
            {
                Vector3i storagePos = storageEndpoints[j];
                string key = $"{storagePos}|{pipeTe.PipeGraphId}";
                if (!seen.Add(key))
                    continue;

                results.Add(new OutputTargetInfo(storagePos, OutputTransportMode.Pipe, pipeTe.PipeGraphId));
            }
        }

        return results;
    }


    // - - - - - - - - - - Helpers - - - - - - - - - - 

    private static bool AreInputTargetsEqual(List<InputTargetInfo> left, List<InputTargetInfo> right)
    {
        int leftCount = left?.Count ?? 0;
        int rightCount = right?.Count ?? 0;
        if (leftCount != rightCount) return false;

        for (int i = 0; i < leftCount; i++)
        {
            InputTargetInfo a = left[i];
            InputTargetInfo b = right[i];

            if (a == null && b == null)
            {
                if (!ReferenceEquals(a, b)) return false; // Both null but different references
                continue;
            }

            if (a.BlockPos != b.BlockPos || a.PipeGraphId != b.PipeGraphId)
                return false;
        }
        return true;
    }

    private bool TryFlushPendingOutput(World world, out string reason)
    {
        reason = "Not implemented yet";
        if (world == null) return false;
        //TODO: Implement output flushing logic here, including checking if selected output target is valid and can accept items, then performing the item transfer.
        return false;
    }


    private bool AreOutputTargetsEqual(List<OutputTargetInfo> left, List<OutputTargetInfo> right)
    {
        int leftCount = left?.Count ?? 0;
        int rightCount = right?.Count ?? 0;
        if (leftCount != rightCount) return false;

        for (int i = 0; i < leftCount; i++)
        {
            OutputTargetInfo a = left[i];
            OutputTargetInfo b = right[i];

            if (a == null && b == null)
            {
                if (!ReferenceEquals(a, b)) return false; // Both null but different references
                continue;
            }

            if (a.BlockPos != b.BlockPos || a.PipeGraphId != b.PipeGraphId)
                return false;
        }
        return true;
    }

    // - - - - - - - - - - HLR Snapshot Generation - - - - - - - - - - 
    public override IHLRSnapshot BuildHLRSnapshot(WorldBase world)
    {
        //TODO: add GrinderSnapshot
        return null;
    }

    public override void ApplyHLRSnapshot(object shapshot)
    {
        //TODO: apply GrinderSnapshot
        World world = GameManager.Instance.World;
        RefreshAvailableInputTargets(world);
        RefreshAvailableOutputTargets(world);
        ResolveSelectedInputContainer(world);
        ResolveSelectedOutputContainer(world);
    }

    protected override void OnPowerStateChanged(bool state)
    {
        LastAction = state ? "Powered On" : "Powered Off";
        LastBlockReason = "None";
        NeedsUiRefresh = true;
    }

    private void EnsureConfigLoaded()
    {
        //TODO: Implement configuration loading logic here
    }

    public override void write(PooledBinaryWriter bw, StreamModeWrite mode)
    {
        base.write(bw, mode);

        bw.Write(PersistVersion);

        bw.Write(SelectedInputChestPos.x);
        bw.Write(SelectedInputChestPos.y);
        bw.Write(SelectedInputChestPos.z);
        bw.Write(SelectedInputPipeGraphID.ToString());

        bw.Write(SelectedOutputChestPos.x);
        bw.Write(SelectedOutputChestPos.y);
        bw.Write(SelectedOutputChestPos.z);
        bw.Write(SelectedOutputPipeGraphID.ToString());

        bw.Write(IsProcessing);
        bw.Write(CycleTickCounter);
        bw.Write(CycleTickLength);

        bw.Write(LastAction ?? string.Empty);
        bw.Write(LastBlockReason ?? string.Empty);
    }

    public override void read(PooledBinaryReader br, StreamModeRead mode)
    {
        base.read(br, mode);

        int version = br.ReadInt32();

        SelectedInputChestPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
        Guid.TryParse(br.ReadString(), out SelectedInputPipeGraphID);
        SelectedOutputChestPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
        Guid.TryParse(br.ReadString(), out SelectedOutputPipeGraphID);

        IsProcessing = br.ReadBoolean();
        CycleTickCounter = br.ReadInt32();
        CycleTickLength = br.ReadInt32();

        LastAction = br.ReadString();
        LastBlockReason = br.ReadString();

        _ = version; // Currently unused, but read for potential future use in handling version differences

        ResolveSelectedInputContainer(GameManager.Instance.World);
        ResolveSelectedOutputContainer(GameManager.Instance.World);
        NeedsUiRefresh = true;
    }

}
