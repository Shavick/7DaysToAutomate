namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageMelterControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestSelectInput = 0,
            RequestCycleRecipe = 1
        }

        private Vector3i _blockPos;
        private MessageType _messageType;
        private int _requesterEntityId;
        private Vector3i _targetChestPos;
        private string _pipeGraphId;
        private int _direction;

        public NetPackageMelterControl SetupSelectInput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectInput;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = chestPos;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            _direction = 0;
            return this;
        }

        public NetPackageMelterControl SetupCycleRecipe(Vector3i blockPos, int requesterEntityId, int direction)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestCycleRecipe;
            _requesterEntityId = requesterEntityId;
            _targetChestPos = Vector3i.zero;
            _pipeGraphId = string.Empty;
            _direction = direction;
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
            _targetChestPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
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
            bw.Write(_pipeGraphId ?? string.Empty);
            bw.Write(_direction);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            if (!NetPackageMachineAuthority.TryValidateRequester(world, this, _requesterEntityId, _blockPos, "MelterControl", out EntityPlayer requester))
                return;

            TileEntity te = world.GetTileEntity(_blockPos);
            if (!(te is TileEntityMelter melter))
                return;

            switch (_messageType)
            {
                case MessageType.RequestSelectInput:
                    melter.ServerSelectInputContainer(_targetChestPos, _pipeGraphId);
                    break;
                case MessageType.RequestCycleRecipe:
                    melter.ServerCycleFluidSelection(_direction);
                    break;
            }
        }
    }
}
