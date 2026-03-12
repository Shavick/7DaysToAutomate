namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageFluidDecanterControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestSelectInput = 0,
            RequestSelectOutput = 1,
            RequestCycleFluid = 2
        }

        private Vector3i _blockPos;
        private MessageType _messageType;
        private int _requesterEntityId;

        private Vector3i _targetChestPos;
        private int _outputMode;
        private string _pipeGraphId;
        private int _direction;

        public NetPackageFluidDecanterControl SetupSelectInput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectInput;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = chestPos;
            _outputMode = 0;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            _direction = 0;
            return this;
        }

        public NetPackageFluidDecanterControl SetupSelectOutput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, int outputMode, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectOutput;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = chestPos;
            _outputMode = outputMode;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            _direction = 0;
            return this;
        }

        public NetPackageFluidDecanterControl SetupCycleFluid(Vector3i blockPos, int requesterEntityId, int direction)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestCycleFluid;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = Vector3i.zero;
            _outputMode = 0;
            _pipeGraphId = string.Empty;
            _direction = direction;
            return this;
        }

        public override int GetLength()
        {
            return 132;
        }

        public override void read(PooledBinaryReader br)
        {
            int x = br.ReadInt32();
            int y = br.ReadInt32();
            int z = br.ReadInt32();
            _blockPos = new Vector3i(x, y, z);

            _messageType = (MessageType)br.ReadByte();
            _requesterEntityId = br.ReadInt32();

            int tx = br.ReadInt32();
            int ty = br.ReadInt32();
            int tz = br.ReadInt32();
            _targetChestPos = new Vector3i(tx, ty, tz);

            _outputMode = br.ReadInt32();
            _pipeGraphId = br.ReadString() ?? string.Empty;
            _direction = br.ReadInt32();
        }

        public override void write(PooledBinaryWriter bw)
        {
            base.write(bw);

            bw.Write(_blockPos.x);
            bw.Write(_blockPos.y);
            bw.Write(_blockPos.z);

            bw.Write((byte)_messageType);
            bw.Write(_requesterEntityId);

            bw.Write(_targetChestPos.x);
            bw.Write(_targetChestPos.y);
            bw.Write(_targetChestPos.z);

            bw.Write(_outputMode);
            bw.Write(_pipeGraphId ?? string.Empty);
            bw.Write(_direction);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null)
                return;

            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            if (!NetPackageMachineAuthority.TryValidateRequester(world, this, _requesterEntityId, _blockPos, "FluidDecanterControl", out EntityPlayer requester))
                return;

            TileEntity te = world.GetTileEntity(_blockPos);
            if (!(te is TileEntityFluidDecanter converter))
                return;

            switch (_messageType)
            {
                case MessageType.RequestSelectInput:
                    converter.ServerSelectInputContainer(_targetChestPos, _pipeGraphId);
                    break;

                case MessageType.RequestSelectOutput:
                    converter.ServerSelectOutputContainer(_targetChestPos, (OutputTransportMode)_outputMode, _pipeGraphId);
                    break;

                case MessageType.RequestCycleFluid:
                    converter.ServerCycleFluidSelection(_direction);
                    break;
            }
        }
    }
}

