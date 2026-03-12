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

        private void WorldShuttingDown(ref ModEvents.SWorldShuttingDownData _data)
        {
            World world = GameManager.Instance.World;

            Log.Out("[HLR][Lifecycle] WorldShuttingDown fired");

            if (world == null)
            {
                Log.Out("[HLR][Lifecycle] WorldShuttingDown — World already null");
                return;
            }

            if (WorldHLR.TryGet(world, out HigherLogicRegistry hlr))
            {
                Log.Out("[HLR][Lifecycle] HLR found — calling Save()");
                hlr.Save();
            }
            else
            {
                Log.Out("[HLR][Lifecycle] No HLR found — nothing to save");
            }
        }

        private void CreateWorldDone(ref ModEvents.SCreateWorldDoneData _data)
        {
            World world = GameManager.Instance.World;

            Log.Out("[HLR][Lifecycle] CreateWorldDone fired");

            if (world == null)
            {
                Log.Error("[HLR][Lifecycle] CreateWorldDone — World is NULL (unexpected)");
                return;
            }

            Log.Out("[HLR][Lifecycle] World ready");

            var hlr = WorldHLR.GetOrCreate(world);

            Log.Out("[HLR][Lifecycle] HLR instance acquired — calling Load()");
            hlr.Load();
        }

        private void GameStartDone(ref ModEvents.SGameStartDoneData _data)
        {
            World world = GameManager.Instance?.World;
            if (world == null)
                return;

            if (PipeGraphManager.IsDevLoggingEnabled(world))
                Log.Out("[PipeGraphManager][Lifecycle] GameStartDone fired");

            PipeGraphManager.RebuildAllGraphs(world);
            FluidGraphManager.ClearAll();
        }

        private void OnGameUpdate(ref ModEvents.SGameUpdateData _data)
        {
            CrafterCategoryRegistry.TryInitialize();

            var world = GameManager.Instance?.World;
            if (world == null)
                return;

            PipeGraphManager.ProcessDirtyGraphs(world);
            PipeTransportManager.ProcessJobs(world);

            FluidGraphManager.ProcessDirtyGraphs(world);
            FluidTransportManager.Process(world);
            PipeProbeHudManager.UpdateAutoProbe(world);

            var hlr = WorldHLR.GetOrCreate(world);
            hlr.Update(world.GetWorldTime());
        }
    }
}








