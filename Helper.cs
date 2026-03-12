using _7DaysToAutomate.Classes.Net_Packages;

public static class Helper
{
    public static void PrepareMachineForUiOpen(World world, TileEntity te, Vector3i blockPos)
    {
        if (world == null || te == null || world.IsRemote())
            return;

        if (te is TileEntityUniversalExtractor ex)
        {
            ex.EnsureTimersLoaded();
            ex.setModified();
        }

        if (te is TileEntityUniversalCrafter crafter)
        {
            crafter.RefreshAvailableInputTargets(world);
            crafter.RefreshAvailableOutputTargets(world);
            crafter.ResolveSelectedInputContainer();
            crafter.ResolveSelectedOutputContainer();
            crafter.setModified();
            crafter.NeedsUiRefresh = true;
        }

        if (te is TileEntityFluidDecanter converter)
        {
            converter.RefreshAvailableInputTargets(world);
            converter.RefreshAvailableOutputTargets(world);
            converter.ResolveSelectedInputContainer();
            converter.ResolveSelectedOutputContainer();
            converter.ResolveFluidOutputGraph(world);
            converter.setModified();
            converter.NeedsUiRefresh = true;
        }
    }
    private static int GetLocalRequesterEntityId(World world)
    {
        if (world == null)
            return -1;

        EntityPlayerLocal localPlayer = world.GetPrimaryPlayer() as EntityPlayerLocal;
        return localPlayer?.entityId ?? -1;
    }

    public static void RequestMachineUIOpen(int clrIdx, Vector3i blockPos, int entityPlayerId, string customUi)
    {
        World world = GameManager.Instance?.World;
        if (world == null)
        {
            Log.Error("[NetPkg][MachineUI] RequestMachineUIOpen world is null");
            return;
        }

        var te = world.GetTileEntity(clrIdx, blockPos);
        PrepareMachineForUiOpen(world, te, blockPos);

        var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;

        // Local-host open path: only valid when this instance is actually the server
        if (cm.IsServer)
        {
            var localPlayer = world.GetPrimaryPlayer() as EntityPlayerLocal;
            if (localPlayer != null && localPlayer.entityId == entityPlayerId)
            {
                if (te is TileEntityMachine machine && machine.IsDevLogging)
                    Log.Out($"[NetPkg][MachineUI][SERVER] Local host detected -> opening UI locally for {localPlayer.entityName}");

                localPlayer.AimingGun = false;

                if (customUi == "CrafterInfo")
                {
                    XUiC_UniversalCrafter.Open(localPlayer, blockPos);
                    return;
                }

                if (customUi == "ExtractorInfo")
                {
                    XUiC_IronExtractorInfo.Open(localPlayer, blockPos);
                    return;
                }


                if (customUi == "FluidDecanterInfo")
                {
                    XUiC_FluidDecanterInfo.Open(localPlayer, blockPos);
                    return;
                }
                Log.Error($"[NetPkg][MachineUI][SERVER] Unknown local-host UI key '{customUi}'");
                return;
            }
        }

        // Otherwise send open request to server (dedi/p2p non-host client)
        if (!cm.IsServer)
        {
            Log.Out("[NetPkg][MachineUI][CLIENT] Connection is client");
            Log.Out("[NetPkg][MachineUI][CLIENT] Requesting machine open from server...");

            cm.SendToServer(
                NetPackageManager.GetPackage<NetPackageOpenMachineUi>()
                    .Setup(clrIdx, blockPos, entityPlayerId, NetPackageOpenMachineUi.MessageType.RequestOpen, customUi),
                false
            );
            return;
        }

        if (!world.Players.dict.TryGetValue(entityPlayerId, out EntityPlayer player))
            return;

        if (player == null)
        {
            Log.Out($"[OpenUI] Server: player not found id={entityPlayerId}");
            return;
        }

        cm.SendPackage(
            NetPackageManager.GetPackage<NetPackageOpenMachineUi>()
                .Setup(clrIdx, blockPos, entityPlayerId, NetPackageOpenMachineUi.MessageType.OpenClient, customUi),
            false,
            entityPlayerId,
            -1,
            -1,
            null,
            192,
            false
        );
    }

    public static void RequestMachinePowerToggle(int clrIdx, Vector3i blockPos, bool powerState)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[PowerToggle] World is null");
            return;
        }

        // CLIENT (dedicated client OR p2p non-host): request server
        if (world.IsRemote())
        {
            Log.Out($"[PowerToggle][CLIENT] Request clrIdx={clrIdx} pos={blockPos} powerState={powerState}");
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageToggleMachinePower>()
                    .Setup(clrIdx, blockPos, GetLocalRequesterEntityId(world), NetPackageToggleMachinePower.MessageType.RequestToggle, powerState),
                false
            );
            return;
        }

        // SERVER (singleplayer OR p2p host OR dedicated): apply directly
        var te = world.GetTileEntity(clrIdx, blockPos) as TileEntityMachine;
        if (te == null)
        {
            Log.Error($"[PowerToggle][SERVER] No TE at clrIdx={clrIdx} pos={blockPos}");
            return;
        }

        Log.Out($"[PowerToggle][SERVER] Apply clrIdx={clrIdx} pos={blockPos} powerState={powerState}");
        te.SetPowerState(powerState);
    }
    public static void RequestCrafterSelectRecipe(Vector3i blockPos, string recipeName)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[Crafter][Helper] RequestCrafterSelectRecipe world is null");
            return;
        }

        if (world.IsRemote())
        {
            Log.Out("[Crafter][Helper] Sending RequestCrafterSelectRecipe to server");

            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageCrafterControl>()
                    .SetupSelectRecipe(blockPos, GetLocalRequesterEntityId(world), recipeName),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityUniversalCrafter;
        if (te == null)
        {
            Log.Error($"[Crafter][Helper] No crafter at pos={blockPos}");
            return;
        }

        te.ServerSelectRecipe(recipeName);
    }

    public static void RequestCrafterSelectInput(Vector3i blockPos, Vector3i chestPos, string pipeGraphId)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[Crafter][Helper] RequestCrafterSelectInput world is null");
            return;
        }

        if (world.IsRemote())
        {
            Log.Out($"[Crafter][Helper] Sending RequestCrafterSelectInput to server pos={chestPos} graph={pipeGraphId}");

            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageCrafterControl>()
                    .SetupSelectInput(blockPos, GetLocalRequesterEntityId(world), chestPos, pipeGraphId),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityUniversalCrafter;
        if (te == null)
        {
            Log.Error($"[Crafter][Helper] No crafter at pos={blockPos}");
            return;
        }

        te.ServerSelectInputContainer(chestPos, pipeGraphId);
    }

    public static void RequestCrafterSelectOutput(Vector3i blockPos, Vector3i targetPos, int mode, string pipeGraphId)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[Crafter][Helper] RequestCrafterSelectOutput world is null");
            return;
        }

        if (world.IsRemote())
        {
            Log.Out("[Crafter][Helper] Sending RequestCrafterSelectOutput to server");
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageCrafterControl>()
                    .SetupSelectOutput(blockPos, GetLocalRequesterEntityId(world), targetPos, mode, pipeGraphId),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityUniversalCrafter;
        if (te == null)
        {
            Log.Error($"[Crafter][Helper] No crafter at pos={blockPos}");
            return;
        }

        te.ServerSelectOutputContainer(targetPos, (OutputTransportMode)mode, pipeGraphId);
    }

    public static void RequestCrafterSetEnabled(Vector3i blockPos, bool enabled)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[Crafter][Helper] RequestCrafterSetEnabled world is null");
            return;
        }

        if (world.IsRemote())
        {
            Log.Out("[Crafter][Helper] Sending RequestSetEnabled to server");

            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageCrafterControl>()
                    .SetupSetEnabled(blockPos, GetLocalRequesterEntityId(world), enabled),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityUniversalCrafter;
        if (te == null)
        {
            Log.Error($"[Crafter][Helper] No crafter at pos={blockPos}");
            return;
        }

        te.ServerSetEnabled(enabled);
    }

    public static void RequestExtractorSelectOutput(Vector3i blockPos, Vector3i targetPos, int mode, string pipeGraphId)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[Extractor][Helper] RequestExtractorSelectOutput world is null");
            return;
        }

        if (world.IsRemote())
        {
            Log.Out("[Extractor][Helper] Sending RequestExtractorSelectOutput to server");
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageExtractorControl>()
                    .SetupSelectOutput(blockPos, GetLocalRequesterEntityId(world), targetPos, mode, pipeGraphId),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityUniversalExtractor;
        if (te == null)
        {
            Log.Error($"[Extractor][Helper] No extractor at pos={blockPos}");
            return;
        }

        te.ServerSelectOutputContainer(targetPos, (OutputTransportMode)mode, pipeGraphId);
    }

    public static void RequestCrafterSetPriority(Vector3i blockPos, int priority)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[Crafter][Helper] RequestCrafterSetPriority world is null");
            return;
        }

        if (world.IsRemote())
        {
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageCrafterControl>()
                    .SetupSetPriority(blockPos, GetLocalRequesterEntityId(world), priority),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityUniversalCrafter;
        if (te == null)
        {
            Log.Error($"[Crafter][Helper] No crafter at pos={blockPos}");
            return;
        }

        te.ServerSetPipePriority(priority);
    }

    public static void RequestExtractorSetPriority(Vector3i blockPos, int priority)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[Extractor][Helper] RequestExtractorSetPriority world is null");
            return;
        }

        if (world.IsRemote())
        {
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageExtractorControl>()
                    .SetupSetPriority(blockPos, GetLocalRequesterEntityId(world), priority),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityUniversalExtractor;
        if (te == null)
        {
            Log.Error($"[Extractor][Helper] No extractor at pos={blockPos}");
            return;
        }

        te.ServerSetPipePriority(priority);
    }
    public static void RequestFluidDecanterSelectInput(Vector3i blockPos, Vector3i chestPos, string pipeGraphId)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[FluidDecanter][Helper] RequestFluidDecanterSelectInput world is null");
            return;
        }

        if (world.IsRemote())
        {
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageFluidDecanterControl>()
                    .SetupSelectInput(blockPos, GetLocalRequesterEntityId(world), chestPos, pipeGraphId),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityFluidDecanter;
        if (te == null)
        {
            Log.Error($"[FluidDecanter][Helper] No converter at pos={blockPos}");
            return;
        }

        te.ServerSelectInputContainer(chestPos, pipeGraphId);
    }

    public static void RequestFluidDecanterSelectOutput(Vector3i blockPos, Vector3i targetPos, int mode, string pipeGraphId)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[FluidDecanter][Helper] RequestFluidDecanterSelectOutput world is null");
            return;
        }

        if (world.IsRemote())
        {
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageFluidDecanterControl>()
                    .SetupSelectOutput(blockPos, GetLocalRequesterEntityId(world), targetPos, mode, pipeGraphId),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityFluidDecanter;
        if (te == null)
        {
            Log.Error($"[FluidDecanter][Helper] No converter at pos={blockPos}");
            return;
        }

        te.ServerSelectOutputContainer(targetPos, (OutputTransportMode)mode, pipeGraphId);
    }

    public static void RequestFluidDecanterCycleFluid(Vector3i blockPos, int direction)
    {
        var world = GameManager.Instance.World;
        if (world == null)
        {
            Log.Error("[FluidDecanter][Helper] RequestFluidDecanterCycleFluid world is null");
            return;
        }

        if (world.IsRemote())
        {
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageFluidDecanterControl>()
                    .SetupCycleFluid(blockPos, GetLocalRequesterEntityId(world), direction),
                false
            );
            return;
        }

        var te = world.GetTileEntity(blockPos) as TileEntityFluidDecanter;
        if (te == null)
        {
            Log.Error($"[FluidDecanter][Helper] No converter at pos={blockPos}");
            return;
        }

        te.ServerCycleFluidSelection(direction);
    }
    public static void RequestPipeProbeSnapshot(int clrIdx, Vector3i blockPos, int entityPlayerId)
    {
        var world = GameManager.Instance?.World;
        if (world == null)
            return;

        var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;

        if (world.IsRemote())
        {
            cm.SendToServer(
                NetPackageManager.GetPackage<NetPackagePipeProbe>()
                    .SetupRequest(entityPlayerId, clrIdx, blockPos),
                false
            );
            return;
        }

        cm.SendPackage(
            NetPackageManager.GetPackage<NetPackagePipeProbe>()
                .SetupRequest(entityPlayerId, clrIdx, blockPos),
            false,
            entityPlayerId,
            -1,
            -1,
            null,
            192,
            false
        );
    }
}
















