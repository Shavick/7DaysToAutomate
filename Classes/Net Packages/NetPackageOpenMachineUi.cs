namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageOpenMachineUi : NetPackage
    {
        public enum MessageType : byte
        {
            RequestOpen,
            OpenClient
        }

        private int ClrIdx;
        private int BlockPosX;
        private int BlockPosY;
        private int BlockPosZ;
        private Vector3i BlockPos;
        private int EntityIdThatOpenedIt;
        private string CustomUi;
        private NetPackageOpenMachineUi.MessageType type;

        public NetPackageOpenMachineUi Setup(int _clrIdx, Vector3i _pos, int _entityIdThatOpenedIt,
            MessageType messageType, string _customUi)
        {
            Log.Warning("NetPackageOpenMachine Setup called.");
            type = messageType;
            ClrIdx = _clrIdx;
            BlockPos = _pos;
            BlockPosX = _pos.x;
            BlockPosY = _pos.y;
            BlockPosZ = _pos.z;
            EntityIdThatOpenedIt = _entityIdThatOpenedIt;
            CustomUi = _customUi;
            return this;
        }

        public override void write(PooledBinaryWriter _writer)
        {
            Log.Warning("NetPackageOpenMachine Write called.");
            base.write(_writer);
            _writer.Write((byte)type);
            _writer.Write(ClrIdx);
            _writer.Write(BlockPosX);
            _writer.Write(BlockPosY);
            _writer.Write(BlockPosZ);
            _writer.Write(EntityIdThatOpenedIt);
            _writer.Write(CustomUi);
        }

        public override void read(PooledBinaryReader _reader)
        {
            Log.Warning("NetPackageOpenMachine Read called.");
            type = (NetPackageOpenMachineUi.MessageType)_reader.ReadByte();
            ClrIdx = _reader.ReadInt32();
            BlockPosX = _reader.ReadInt32();
            BlockPosY = _reader.ReadInt32();
            BlockPosZ = _reader.ReadInt32();
            EntityIdThatOpenedIt = _reader.ReadInt32();
            CustomUi = _reader.ReadString();
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            Vector3i BlockPos = new Vector3i(BlockPosX, BlockPosY, BlockPosZ);
            Log.Out($"[NetPkg][MachineUI] ProcessPackage | Type={type} | Pos={BlockPos} | PlayerId={EntityIdThatOpenedIt} | UI={CustomUi} | IsServer={ConnectionManager.Instance.IsServer}");

            // =========================
            // SERVER: Handle UI request
            // =========================
            if (ConnectionManager.Instance.IsServer && type == MessageType.RequestOpen)
            {
                if (!NetPackageMachineAuthority.TryValidateRequester(_world, this, EntityIdThatOpenedIt, BlockPos, "MachineUI", out EntityPlayer requester))
                    return;

                int requesterId = requester.entityId;

                TileEntity te = _world.GetTileEntity(ClrIdx, BlockPos);

                if (te == null)
                {
                    Log.Error($"[NetPkg][MachineUI][SERVER] No TileEntity found at {BlockPos}");
                    return;
                }

                if (!(te is TileEntityMachine))
                {
                    Log.Error($"[NetPkg][MachineUI][SERVER] TileEntity at {BlockPos} is not a TileEntityMachine");
                    return;
                }
                Helper.PrepareMachineForUiOpen(_world, te, BlockPos);

                if (string.IsNullOrEmpty(CustomUi))
                {
                    Log.Error($"[NetPkg][MachineUI][SERVER] CustomUi string is null or empty for {BlockPos}");
                    return;
                }

                if (CustomUi != "ExtractorInfo" && CustomUi != "CrafterInfo" && CustomUi != "FluidDecanterInfo" && CustomUi != "FluidInfuserInfo")
                {
                    Log.Error($"[NetPkg][MachineUI][SERVER] Unknown UI key '{CustomUi}'");
                    return;
                }

                Log.Out($"[NetPkg][MachineUI][SERVER] Player validated: {requester.entityName} ({requester.entityId})");
                var response = NetPackageManager.GetPackage<NetPackageOpenMachineUi>()
                    .Setup(ClrIdx, BlockPos, requesterId, MessageType.OpenClient, CustomUi);

                Log.Out($"[NetPkg][MachineUI][SERVER] Sending OpenClient packet to player {requester.entityName}");

                ConnectionManager.Instance.SendPackage(
                    response,
                    false,
                    requesterId,
                    -1,
                    -1,
                    null,
                    192,
                    false
                );

                return;
            }

            // =========================
            // CLIENT: Open the UI
            // =========================
            if (!ConnectionManager.Instance.IsServer && type == MessageType.OpenClient)
            {
                Log.Out($"[NetPkg][MachineUI][CLIENT] OpenClient received for UI '{CustomUi}' at {BlockPos}");

                EntityPlayerLocal localPlayer = _world.GetPrimaryPlayer() as EntityPlayerLocal;

                if (localPlayer == null)
                {
                    Log.Error($"[NetPkg][MachineUI][CLIENT] Local player not available");
                    return;
                }

                Log.Out($"[NetPkg][MachineUI][CLIENT] Local player resolved: {localPlayer.entityName}");

                TileEntity te = _world.GetTileEntity(ClrIdx, BlockPos);
                Log.Out($"[CLIENT][TE_LOOKUP] clrIdx={ClrIdx} pos={BlockPos} -> {(te == null ? "NULL" : te.GetType().Name)}");
                Log.Out($"[CLIENT][TE_LOOKUP] IsServer={ConnectionManager.Instance.IsServer} IsRemote={_world.IsRemote()}");

                TileEntity tePacket = _world.GetTileEntity(ClrIdx, BlockPos);
                TileEntity teZero = _world.GetTileEntity(0, BlockPos);

                Log.Out($"[CLIENT][TE_LOOKUP] packetClrIdx={ClrIdx} pos={BlockPos} -> {(tePacket == null ? "NULL" : tePacket.GetType().Name)}");
                Log.Out($"[CLIENT][TE_LOOKUP] zeroClrIdx=0 pos={BlockPos} -> {(teZero == null ? "NULL" : teZero.GetType().Name)}");

                if (te == null)
                {
                    Log.Error($"[NetPkg][MachineUI][CLIENT] TileEntity for position {BlockPos} has returned null");
                }

                if (string.IsNullOrEmpty(CustomUi))
                {
                    Log.Error($"[NetPkg][MachineUI][CLIENT] CustomUi string is null or empty");
                    return;
                }

                switch (CustomUi)
                {
                    case "ExtractorInfo":

                        if (!(te is TileEntityUniversalExtractor))
                        {
                            Log.Error($"[NetPkg][MachineUI][CLIENT] TileEntity is not the correct type 'TileEntityUniversalExtractor' for given customUI {CustomUi}");
                            return;
                        }

                        Log.Out($"[NetPkg][MachineUI][CLIENT] Opening Extractor UI at {BlockPos}");

                        localPlayer.AimingGun = false;
                        XUiC_IronExtractorInfo.Open(localPlayer, BlockPos);

                        Log.Out($"[NetPkg][MachineUI][CLIENT] Extractor UI open call executed");
                        break;

                    case "CrafterInfo":
                        if (!(te is TileEntityUniversalCrafter))
                        {
                            Log.Error($"[NetPkg][MachineUI][CLIENT] TileEntity is not TileEntityUniversalCrafter for UI {CustomUi}");
                            return;
                        }

                        Log.Out($"[NetPkg][MachineUI][CLIENT] Opening Crafter UI at {BlockPos}");
                        localPlayer.AimingGun = false;
                        XUiC_UniversalCrafter.Open(localPlayer, BlockPos);
                        Log.Out($"[NetPkg][MachineUI][CLIENT] Crafter UI open call executed");
                        break;

                    case "FluidDecanterInfo":
                        if (!(te is TileEntityFluidDecanter))
                        {
                            Log.Error($"[NetPkg][MachineUI][CLIENT] TileEntity is not TileEntityFluidDecanter for UI {CustomUi}");
                            return;
                        }

                        Log.Out($"[NetPkg][MachineUI][CLIENT] Opening Fluid Decanter UI at {BlockPos}");
                        localPlayer.AimingGun = false;
                        XUiC_FluidDecanterInfo.Open(localPlayer, BlockPos);
                        Log.Out($"[NetPkg][MachineUI][CLIENT] Fluid Decanter UI open call executed");
                        break;

                    case "FluidInfuserInfo":
                        if (!(te is TileEntityFluidInfuser))
                        {
                            Log.Error($"[NetPkg][MachineUI][CLIENT] TileEntity is not TileEntityFluidInfuser for UI {CustomUi}");
                            return;
                        }

                        Log.Out($"[NetPkg][MachineUI][CLIENT] Opening Fluid Infuser UI at {BlockPos}");
                        localPlayer.AimingGun = false;
                        XUiC_FluidInfuserInfo.Open(localPlayer, BlockPos);
                        Log.Out($"[NetPkg][MachineUI][CLIENT] Fluid Infuser UI open call executed");
                        break;

                    default:
                        Log.Error($"[NetPkg][MachineUI][CLIENT] Unknown UI key '{CustomUi}'");
                        break;
                }
            }
        }
        public override int GetLength()
        {
            int s = (CustomUi == null) ? 1 : (CustomUi.Length * 2 + 1);
            return 21 + s;
        }

    }
}











