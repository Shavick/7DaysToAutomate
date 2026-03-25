namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageCasterControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestCycleRecipe = 0,
            RequestSelectOutput = 1
        }

        private Vector3i _blockPos;
        private MessageType _messageType;
        private int _requesterEntityId;
        private int _direction;
        private Vector3i _targetChestPos;
        private int _outputMode;
        private string _pipeGraphId;

        public NetPackageCasterControl SetupCycleRecipe(Vector3i blockPos, int requesterEntityId, int direction)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestCycleRecipe;
            _requesterEntityId = requesterEntityId;
            _direction = direction;
            _targetChestPos = Vector3i.zero;
            _outputMode = 0;
            _pipeGraphId = string.Empty;
            return this;
        }

        public NetPackageCasterControl SetupSelectOutput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, int outputMode, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectOutput;
            _requesterEntityId = requesterEntityId;
            _direction = 0;
            _targetChestPos = chestPos;
            _outputMode = outputMode;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            return this;
        }

        public override int GetLength()
        {
            return 120;
        }

        public override void read(PooledBinaryReader br)
        {
            _blockPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            _messageType = (MessageType)br.ReadByte();
            _requesterEntityId = br.ReadInt32();
            _direction = br.ReadInt32();
            _targetChestPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            _outputMode = br.ReadInt32();
            _pipeGraphId = br.ReadString() ?? string.Empty;
        }

        public override void write(PooledBinaryWriter bw)
        {
            base.write(bw);
            bw.Write(_blockPos.x);
            bw.Write(_blockPos.y);
            bw.Write(_blockPos.z);
            bw.Write((byte)_messageType);
            bw.Write(_requesterEntityId);
            bw.Write(_direction);
            bw.Write(_targetChestPos.x);
            bw.Write(_targetChestPos.y);
            bw.Write(_targetChestPos.z);
            bw.Write(_outputMode);
            bw.Write(_pipeGraphId ?? string.Empty);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            if (!NetPackageMachineAuthority.TryValidateRequester(world, this, _requesterEntityId, _blockPos, "CasterControl", out EntityPlayer requester))
                return;

            TileEntity te = world.GetTileEntity(_blockPos);
            if (!(te is TileEntityCaster caster))
                return;

            switch (_messageType)
            {
                case MessageType.RequestCycleRecipe:
                    caster.ServerCycleRecipe(_direction);
                    break;

                case MessageType.RequestSelectOutput:
                    caster.ServerSelectOutputContainer(_targetChestPos, (OutputTransportMode)_outputMode, _pipeGraphId);
                    break;
            }
        }
    }
}
