using HarmonyLib;
using System;
using System.Reflection;

namespace _7DaysToAutomate
{
    public class ModAPI : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            try
            {
                new Harmony(base.GetType().ToString()).PatchAll(Assembly.GetExecutingAssembly());

                ModEvents.GameUpdate.RegisterHandler(new ModEvents.ModEventHandlerDelegate<ModEvents.SGameUpdateData>(this.OnGameUpdate));
                ModEvents.CreateWorldDone.RegisterHandler(new ModEvents.ModEventHandlerDelegate<ModEvents.SCreateWorldDoneData>(this.CreateWorldDone));
                ModEvents.GameStartDone.RegisterHandler(new ModEvents.ModEventHandlerDelegate<ModEvents.SGameStartDoneData>(this.GameStartDone));
                ModEvents.WorldShuttingDown.RegisterHandler(new ModEvents.ModEventHandlerDelegate<ModEvents.SWorldShuttingDownData>(this.WorldShuttingDown));
            }
            catch (Exception ex)
            {
                Log.Error($"[7D2A] Failed to initialize mod. Message: {ex.Message}, Stack Trace: {ex.StackTrace}");
            }
        }

        private static bool IsServerWorld(World world)
        {
            return world != null && !world.IsRemote();
        }

        private void WorldShuttingDown(ref ModEvents.SWorldShuttingDownData _data)
        {
            World world = GameManager.Instance?.World;

            Log.Out("[HLR][Lifecycle] WorldShuttingDown fired");

            if (world == null)
            {
                Log.Out("[HLR][Lifecycle] WorldShuttingDown - World already null");
                return;
            }

            if (!IsServerWorld(world))
            {
                Log.Out("[HLR][Lifecycle] WorldShuttingDown - remote world, skipping server lifecycle");
                return;
            }

            if (WorldHLR.TryGet(world, out HigherLogicRegistry hlr))
            {
                Log.Out("[HLR][Lifecycle] HLR found - calling Save()");
                hlr.Save();
            }
            else
            {
                Log.Out("[HLR][Lifecycle] No HLR found - nothing to save");
            }

            PipeGraphManager.SaveToDisk(world);

            PipeTransportManager.ClearAll();
        }

        private void CreateWorldDone(ref ModEvents.SCreateWorldDoneData _data)
        {
            World world = GameManager.Instance?.World;

            Log.Out("[HLR][Lifecycle] CreateWorldDone fired");

            if (world == null)
            {
                Log.Error("[HLR][Lifecycle] CreateWorldDone - World is NULL (unexpected)");
                return;
            }

            if (!IsServerWorld(world))
            {
                Log.Out("[HLR][Lifecycle] CreateWorldDone - remote world, skipping HLR load");
                return;
            }

            Log.Out("[HLR][Lifecycle] World ready");

            var hlr = WorldHLR.GetOrCreate(world);

            Log.Out("[HLR][Lifecycle] HLR instance acquired - calling Load()");
            hlr.Load();
        }

        private void GameStartDone(ref ModEvents.SGameStartDoneData _data)
        {
            World world = GameManager.Instance?.World;
            if (!IsServerWorld(world))
                return;

            if (PipeGraphManager.IsDevLoggingEnabled(world))
                Log.Out("[PipeGraphManager][Lifecycle] GameStartDone fired");

            PipeTransportManager.ClearAll();
            if (!PipeGraphManager.LoadFromDisk(world))
                PipeGraphManager.RebuildAllGraphs(world);
            FluidGraphManager.RebuildAllGraphs(world);
        }

        private void OnGameUpdate(ref ModEvents.SGameUpdateData _data)
        {
            CrafterCategoryRegistry.TryInitialize();

            var world = GameManager.Instance?.World;
            if (world == null)
                return;

            if (IsServerWorld(world))
            {
                PipeGraphManager.ProcessDirtyGraphs(world);
                PipeTransportManager.ProcessJobs(world);

                FluidGraphManager.ProcessDirtyGraphs(world);
                FluidTransportManager.Process(world);

                var hlr = WorldHLR.GetOrCreate(world);
                hlr.Update(world.GetWorldTime());
            }

            PipeProbeHudManager.UpdateAutoProbe(world);
        }
    }
}
