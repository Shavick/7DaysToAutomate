using System;
using System.Collections.Generic;
using System.IO;

public partial class HigherLogicRegistry
{
    private FluidMixerSnapshot CloneFluidMixerSnapshot(FluidMixerSnapshot source)
    {
        return new FluidMixerSnapshot
        {
            MachineId = source.MachineId,
            Position = source.Position,
            WorldTime = source.WorldTime,
            LastHLRSimTime = source.LastHLRSimTime,
            IsOn = source.IsOn,
            SelectedRecipeKey = source.SelectedRecipeKey,
            SelectedFluidType = source.SelectedFluidType,
            SelectedFluidGraphId = source.SelectedFluidGraphId,
            IsProcessing = source.IsProcessing,
            CycleTickCounter = source.CycleTickCounter,
            CycleTickLength = source.CycleTickLength,
            ActiveRecipeKey = source.ActiveRecipeKey,
            PendingFluidInputAType = source.PendingFluidInputAType,
            PendingFluidInputAAmountMg = source.PendingFluidInputAAmountMg,
            PendingFluidInputBType = source.PendingFluidInputBType,
            PendingFluidInputBAmountMg = source.PendingFluidInputBAmountMg,
            PendingFluidOutputType = source.PendingFluidOutputType,
            PendingFluidOutput = source.PendingFluidOutput,
            PendingFluidOutputCapacityMg = source.PendingFluidOutputCapacityMg,
            MachineRecipeGroupsCsv = source.MachineRecipeGroupsCsv,
            LastAction = source.LastAction,
            LastBlockReason = source.LastBlockReason
        };
    }

    private CasterSnapshot CloneCasterSnapshot(CasterSnapshot source)
    {
        var clone = new CasterSnapshot
        {
            MachineId = source.MachineId,
            Position = source.Position,
            WorldTime = source.WorldTime,
            LastHLRSimTime = source.LastHLRSimTime,
            IsOn = source.IsOn,
            SelectedOutputChestPos = source.SelectedOutputChestPos,
            SelectedOutputMode = source.SelectedOutputMode,
            SelectedOutputPipeGraphId = source.SelectedOutputPipeGraphId,
            SelectedRecipeKey = source.SelectedRecipeKey,
            SelectedFluidType = source.SelectedFluidType,
            SelectedFluidGraphId = source.SelectedFluidGraphId,
            IsProcessing = source.IsProcessing,
            CycleTickCounter = source.CycleTickCounter,
            CycleTickLength = source.CycleTickLength,
            ActiveRecipeKey = source.ActiveRecipeKey,
            MachineRecipeGroupsCsv = source.MachineRecipeGroupsCsv,
            PendingFluidInputType = source.PendingFluidInputType,
            PendingFluidInputAmountMg = source.PendingFluidInputAmountMg,
            PendingOutputs = new Dictionary<string, int>(StringComparer.Ordinal),
            LastAction = source.LastAction,
            LastBlockReason = source.LastBlockReason
        };

        if (source.PendingOutputs != null)
        {
            foreach (var kvp in source.PendingOutputs)
                clone.PendingOutputs[kvp.Key] = kvp.Value;
        }

        return clone;
    }

    private void SaveFluidMixerSnapshot(BinaryWriter bw, FluidMixerSnapshot mixer)
    {
        bw.Write(mixer.WorldTime);
        bw.Write(mixer.LastHLRSimTime);
        bw.Write(mixer.IsOn);
        bw.Write(mixer.SelectedRecipeKey ?? string.Empty);
        bw.Write(mixer.SelectedFluidType ?? string.Empty);
        bw.Write(mixer.SelectedFluidGraphId.ToString());
        bw.Write(mixer.IsProcessing);
        bw.Write(mixer.CycleTickCounter);
        bw.Write(mixer.CycleTickLength);
        bw.Write(mixer.ActiveRecipeKey ?? string.Empty);
        bw.Write(mixer.PendingFluidInputAType ?? string.Empty);
        bw.Write(mixer.PendingFluidInputAAmountMg);
        bw.Write(mixer.PendingFluidInputBType ?? string.Empty);
        bw.Write(mixer.PendingFluidInputBAmountMg);
        bw.Write(mixer.PendingFluidOutputType ?? string.Empty);
        bw.Write(mixer.PendingFluidOutput);
        bw.Write(mixer.PendingFluidOutputCapacityMg);
        bw.Write(mixer.MachineRecipeGroupsCsv ?? string.Empty);
        bw.Write(mixer.LastAction ?? string.Empty);
        bw.Write(mixer.LastBlockReason ?? string.Empty);
    }

    private void SaveCasterSnapshot(BinaryWriter bw, CasterSnapshot caster)
    {
        bw.Write(caster.WorldTime);
        bw.Write(caster.LastHLRSimTime);
        bw.Write(caster.IsOn);
        bw.Write(caster.SelectedOutputChestPos.x);
        bw.Write(caster.SelectedOutputChestPos.y);
        bw.Write(caster.SelectedOutputChestPos.z);
        bw.Write((int)caster.SelectedOutputMode);
        bw.Write(caster.SelectedOutputPipeGraphId.ToString());
        bw.Write(caster.SelectedRecipeKey ?? string.Empty);
        bw.Write(caster.SelectedFluidType ?? string.Empty);
        bw.Write(caster.SelectedFluidGraphId.ToString());
        bw.Write(caster.IsProcessing);
        bw.Write(caster.CycleTickCounter);
        bw.Write(caster.CycleTickLength);
        bw.Write(caster.ActiveRecipeKey ?? string.Empty);
        bw.Write(caster.MachineRecipeGroupsCsv ?? string.Empty);
        bw.Write(caster.PendingFluidInputType ?? string.Empty);
        bw.Write(caster.PendingFluidInputAmountMg);

        int pendingCount = caster.PendingOutputs?.Count ?? 0;
        bw.Write(pendingCount);
        if (caster.PendingOutputs != null)
        {
            foreach (var kvp in caster.PendingOutputs)
            {
                bw.Write(kvp.Key ?? string.Empty);
                bw.Write(kvp.Value);
            }
        }

        bw.Write(caster.LastAction ?? string.Empty);
        bw.Write(caster.LastBlockReason ?? string.Empty);
    }

    private void LoadFluidMixerSnapshot(BinaryReader br, FluidMixerSnapshot mixer, int snapshotVersion)
    {
        mixer.WorldTime = br.ReadUInt64();
        mixer.LastHLRSimTime = snapshotVersion >= 1 ? br.ReadUInt64() : mixer.WorldTime;
        mixer.IsOn = br.ReadBoolean();
        mixer.SelectedRecipeKey = br.ReadString() ?? string.Empty;
        mixer.SelectedFluidType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();

        string fluidGraph = br.ReadString();
        if (!Guid.TryParse(fluidGraph, out mixer.SelectedFluidGraphId))
            mixer.SelectedFluidGraphId = Guid.Empty;

        mixer.IsProcessing = br.ReadBoolean();
        mixer.CycleTickCounter = Math.Max(0, br.ReadInt32());
        mixer.CycleTickLength = Math.Max(1, br.ReadInt32());
        mixer.ActiveRecipeKey = br.ReadString() ?? string.Empty;
        mixer.PendingFluidInputAType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
        mixer.PendingFluidInputAAmountMg = Math.Max(0, br.ReadInt32());
        mixer.PendingFluidInputBType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
        mixer.PendingFluidInputBAmountMg = Math.Max(0, br.ReadInt32());
        mixer.PendingFluidOutputType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
        mixer.PendingFluidOutput = Math.Max(0, br.ReadInt32());
        mixer.PendingFluidOutputCapacityMg = Math.Max(1, br.ReadInt32());
        mixer.MachineRecipeGroupsCsv = br.ReadString() ?? string.Empty;
        mixer.LastAction = br.ReadString() ?? string.Empty;
        mixer.LastBlockReason = br.ReadString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(mixer.MachineRecipeGroupsCsv))
            mixer.MachineRecipeGroupsCsv = "fluid_mixer";

        if (!mixer.IsProcessing)
        {
            mixer.CycleTickCounter = 0;
            mixer.ActiveRecipeKey = string.Empty;
            mixer.PendingFluidInputAType = string.Empty;
            mixer.PendingFluidInputAAmountMg = 0;
            mixer.PendingFluidInputBType = string.Empty;
            mixer.PendingFluidInputBAmountMg = 0;
        }
    }

    private void LoadCasterSnapshot(BinaryReader br, CasterSnapshot caster, int snapshotVersion)
    {
        caster.WorldTime = br.ReadUInt64();
        caster.LastHLRSimTime = snapshotVersion >= 1 ? br.ReadUInt64() : caster.WorldTime;
        caster.IsOn = br.ReadBoolean();

        int outX = br.ReadInt32();
        int outY = br.ReadInt32();
        int outZ = br.ReadInt32();
        caster.SelectedOutputChestPos = new Vector3i(outX, outY, outZ);
        caster.SelectedOutputMode = (OutputTransportMode)br.ReadInt32();
        string outputGraph = br.ReadString();
        if (!Guid.TryParse(outputGraph, out caster.SelectedOutputPipeGraphId))
            caster.SelectedOutputPipeGraphId = Guid.Empty;

        caster.SelectedRecipeKey = br.ReadString() ?? string.Empty;
        caster.SelectedFluidType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
        string fluidGraph = br.ReadString();
        if (!Guid.TryParse(fluidGraph, out caster.SelectedFluidGraphId))
            caster.SelectedFluidGraphId = Guid.Empty;

        caster.IsProcessing = br.ReadBoolean();
        caster.CycleTickCounter = Math.Max(0, br.ReadInt32());
        caster.CycleTickLength = Math.Max(1, br.ReadInt32());
        caster.ActiveRecipeKey = br.ReadString() ?? string.Empty;
        caster.MachineRecipeGroupsCsv = br.ReadString() ?? string.Empty;
        caster.PendingFluidInputType = (br.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
        caster.PendingFluidInputAmountMg = Math.Max(0, br.ReadInt32());

        int pendingCount = Math.Max(0, br.ReadInt32());
        caster.PendingOutputs = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < pendingCount; i++)
        {
            string itemName = br.ReadString() ?? string.Empty;
            int count = br.ReadInt32();
            if (string.IsNullOrEmpty(itemName) || count <= 0)
                continue;

            caster.PendingOutputs[itemName] = count;
        }

        caster.LastAction = br.ReadString() ?? string.Empty;
        caster.LastBlockReason = br.ReadString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(caster.MachineRecipeGroupsCsv))
            caster.MachineRecipeGroupsCsv = "mold";

        if (!caster.IsProcessing)
        {
            caster.CycleTickCounter = 0;
            caster.ActiveRecipeKey = string.Empty;
            caster.PendingFluidInputType = string.Empty;
            caster.PendingFluidInputAmountMg = 0;
        }
    }
}
