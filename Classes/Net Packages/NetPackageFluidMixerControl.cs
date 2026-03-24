namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageFluidMixerControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestCycleRecipe = 0
        }

        private Vector3i _blockPos;
        private MessageType _messageType;
        private int _requesterEntityId;
        private int _direction;

        public NetPackageFluidMixerControl SetupCycleRecipe(Vector3i blockPos, int requesterEntityId, int direction)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestCycleRecipe;
            _requesterEntityId = requesterEntityId;
            _direction = direction;
            return this;
        }

        public override int GetLength()
        {
            return 40;
        }

        public override void read(PooledBinaryReader br)
        {
            _blockPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            _messageType = (MessageType)br.ReadByte();
            _requesterEntityId = br.ReadInt32();
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
            bw.Write(_direction);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            if (!NetPackageMachineAuthority.TryValidateRequester(world, this, _requesterEntityId, _blockPos, "FluidMixerControl", out EntityPlayer requester))
                return;

            TileEntity te = world.GetTileEntity(_blockPos);
            if (!(te is TileEntityFluidMixer mixer))
                return;

            if (_messageType == MessageType.RequestCycleRecipe)
                mixer.ServerCycleRecipe(_direction);
        }
    }
}
