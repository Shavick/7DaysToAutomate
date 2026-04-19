namespace _7DaysToAutomate.Classes.Net_Packages
{

    public class NetPackageGrinderControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestSelectedInput = 0,
            RequestSelectedOutput = 1,
        }
        private Vector3i _blockPos;
        private MessageType _messageType;
        private int _requesterEntityId;
        private Vector3i _targetChestPos;
        private string _pipeGraphId;
        public NetPackageGrinderControl SetupSelectInput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectedInput;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = chestPos;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            return this;
        }
        public NetPackageGrinderControl SetupSelectOutput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectedOutput;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = chestPos;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            return this;
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            _writer.Write(_blockPos.x);
            _writer.Write(_blockPos.y);
            _writer.Write(_blockPos.z);
            _writer.Write((byte)_messageType);
            _writer.Write(_requesterEntityId);
            _writer.Write(_targetChestPos.x);
            _writer.Write(_targetChestPos.y);
            _writer.Write(_targetChestPos.z);
            _writer.Write(_pipeGraphId ?? string.Empty);
        }

        public override void read(PooledBinaryReader _reader)
        {
            _blockPos = new Vector3i(_reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32());
            _messageType = (MessageType)_reader.ReadByte();
            _requesterEntityId = _reader.ReadInt32();
            _targetChestPos = new Vector3i(_reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32());
            _pipeGraphId = _reader.ReadString() ?? string.Empty;
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (_world == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                Log.Error("[GrinderControl] Invalid world or not server");
                return;
            }

            if (!NetPackageMachineAuthority.TryValidateRequester(_world, this, _requesterEntityId, _blockPos, "GrinderControl", out EntityPlayer requester))
            {
                Log.Warning("[GrinderControl] Requester validation failed");
                return;
            }

            TileEntity te = _world.GetTileEntity(_blockPos);
            if (!(te is TileEntityGrinder grinder))
            {
                Log.Error($"[GrinderControl] No grinder found at {_blockPos}");
                return;
            }

            switch (_messageType)
            {
                case MessageType.RequestSelectedInput:
                    grinder.ServerSelectInputContainer(_targetChestPos, _pipeGraphId);
                    break;
                case MessageType.RequestSelectedOutput:
                    grinder.ServerSelectOutputContainer(_targetChestPos, _pipeGraphId);
                    break;
            }
        }

        public override int GetLength()
        {
            return 120; // 3 ints for blockPos, 1 byte for messageType, 1 int for requesterEntityId, 3 ints for targetChestPos, and a string (max 64 chars) for pipeGraphId
        }
    }
}
