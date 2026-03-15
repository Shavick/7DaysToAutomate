namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageFluidInfuserControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestSelectRecipe = 0,
            RequestSelectInput = 1,
            RequestSelectOutput = 2
        }

        private Vector3i _blockPos;
        private MessageType _messageType;
        private int _requesterEntityId;
        private string _recipeKey;
        private Vector3i _targetChestPos;
        private int _outputMode;
        private string _pipeGraphId;

        public NetPackageFluidInfuserControl SetupSelectRecipe(Vector3i blockPos, int requesterEntityId, string recipeKey)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectRecipe;
            _requesterEntityId = requesterEntityId;
            _recipeKey = recipeKey ?? string.Empty;
            _targetChestPos = Vector3i.zero;
            _outputMode = 0;
            _pipeGraphId = string.Empty;
            return this;
        }

        public NetPackageFluidInfuserControl SetupSelectInput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectInput;
            _requesterEntityId = requesterEntityId;
            _recipeKey = string.Empty;
            _targetChestPos = chestPos;
            _outputMode = 0;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            return this;
        }

        public NetPackageFluidInfuserControl SetupSelectOutput(Vector3i blockPos, int requesterEntityId, Vector3i chestPos, int outputMode, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectOutput;
            _requesterEntityId = requesterEntityId;
            _recipeKey = string.Empty;
            _targetChestPos = chestPos;
            _outputMode = outputMode;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            return this;
        }

        public override int GetLength()
        {
            return 132;
        }

        public override void read(PooledBinaryReader br)
        {
            _blockPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            _messageType = (MessageType)br.ReadByte();
            _requesterEntityId = br.ReadInt32();
            _recipeKey = br.ReadString() ?? string.Empty;
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
            bw.Write(_recipeKey ?? string.Empty);
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

            if (!NetPackageMachineAuthority.TryValidateRequester(world, this, _requesterEntityId, _blockPos, "FluidInfuserControl", out EntityPlayer requester))
                return;

            TileEntity te = world.GetTileEntity(_blockPos);
            if (!(te is TileEntityFluidInfuser infuser))
                return;

            switch (_messageType)
            {
                case MessageType.RequestSelectRecipe:
                    infuser.ServerSelectRecipe(_recipeKey);
                    break;

                case MessageType.RequestSelectInput:
                    infuser.ServerSelectInputContainer(_targetChestPos, _pipeGraphId);
                    break;

                case MessageType.RequestSelectOutput:
                    infuser.ServerSelectOutputContainer(_targetChestPos, (OutputTransportMode)_outputMode, _pipeGraphId);
                    break;
            }
        }
    }
}
