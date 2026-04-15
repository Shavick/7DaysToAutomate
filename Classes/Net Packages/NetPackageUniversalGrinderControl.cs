namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageUniversalGrinderControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestSelectInput = 0,
            RequestSelectOutput = 1,
            RequestToggleMods = 2
        }

        private Vector3i _blockPos;
        private MessageType _messageType;
        private int _requesterEntityId;
        private Vector3i _targetChestPos;
        private int _outputMode;
        private string _pipeGraphId;

        public NetPackageUniversalGrinderControl SetupSelectInput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectInput;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = chestPos;
            _outputMode = 0;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            return this;
        }

        public NetPackageUniversalGrinderControl SetupSelectOutput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, int outputMode, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectOutput;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = chestPos;
            _outputMode = outputMode;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            return this;
        }

        public NetPackageUniversalGrinderControl SetupToggleMods(Vector3i blockPos, int requesterEntityId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestToggleMods;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = Vector3i.zero;
            _outputMode = 0;
            _pipeGraphId = string.Empty;
            return this;
        }

        public override int GetLength()
        {
            return 128;
        }

        public override void read(PooledBinaryReader br)
        {
            _blockPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            _messageType = (MessageType)br.ReadByte();
            _requesterEntityId = br.ReadInt32();
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

            if (!NetPackageMachineAuthority.TryValidateRequester(world, this, _requesterEntityId, _blockPos, "GrinderControl", out EntityPlayer requester))
                return;

            TileEntity te = world.GetTileEntity(_blockPos);
            if (!(te is TileEntityUniversalGrinder grinder))
                return;

            switch (_messageType)
            {
                case MessageType.RequestSelectInput:
                    grinder.ServerSelectInputContainer(_targetChestPos, _pipeGraphId);
                    break;
                case MessageType.RequestSelectOutput:
                    grinder.ServerSelectOutputContainer(_targetChestPos, (OutputTransportMode)_outputMode, _pipeGraphId);
                    break;
                case MessageType.RequestToggleMods:
                    grinder.ServerToggleProcessMods();
                    break;
            }
        }
    }
}
