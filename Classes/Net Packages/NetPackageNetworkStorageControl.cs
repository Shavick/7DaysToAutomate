using System.Reflection;

namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageNetworkStorageControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestWithdraw = 0,
            RequestDepositHolding = 1,
            RequestDepositAll = 2
        }

        private Vector3i _interfacePos;
        private MessageType _messageType;
        private int _requesterEntityId;
        private string _displayKey;
        private int _count;

        public NetPackageNetworkStorageControl SetupWithdraw(Vector3i interfacePos, int requesterEntityId, string displayKey, int count)
        {
            _interfacePos = interfacePos;
            _messageType = MessageType.RequestWithdraw;
            _requesterEntityId = requesterEntityId;
            _displayKey = displayKey ?? string.Empty;
            _count = count;
            return this;
        }

        public NetPackageNetworkStorageControl SetupDepositHolding(Vector3i interfacePos, int requesterEntityId)
        {
            _interfacePos = interfacePos;
            _messageType = MessageType.RequestDepositHolding;
            _requesterEntityId = requesterEntityId;
            _displayKey = string.Empty;
            _count = 0;
            return this;
        }

        public NetPackageNetworkStorageControl SetupDepositAll(Vector3i interfacePos, int requesterEntityId)
        {
            _interfacePos = interfacePos;
            _messageType = MessageType.RequestDepositAll;
            _requesterEntityId = requesterEntityId;
            _displayKey = string.Empty;
            _count = 0;
            return this;
        }

        public override int GetLength()
        {
            int keySize = (_displayKey == null) ? 1 : (_displayKey.Length * 2 + 1);
            return 40 + keySize;
        }

        public override void read(PooledBinaryReader br)
        {
            _interfacePos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            _messageType = (MessageType)br.ReadByte();
            _requesterEntityId = br.ReadInt32();
            _displayKey = br.ReadString() ?? string.Empty;
            _count = br.ReadInt32();
        }

        public override void write(PooledBinaryWriter bw)
        {
            base.write(bw);
            bw.Write(_interfacePos.x);
            bw.Write(_interfacePos.y);
            bw.Write(_interfacePos.z);
            bw.Write((byte)_messageType);
            bw.Write(_requesterEntityId);
            bw.Write(_displayKey ?? string.Empty);
            bw.Write(_count);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            if (!TryValidateRequesterNoDistance(world, this, _requesterEntityId, out EntityPlayer requester))
                return;

            switch (_messageType)
            {
                case MessageType.RequestWithdraw:
                    NetworkStorageInterfaceActions.TryExecuteWithdraw(world, _interfacePos, requester, _displayKey, _count, out _);
                    break;

                case MessageType.RequestDepositHolding:
                    NetworkStorageInterfaceActions.TryExecuteDepositHolding(world, _interfacePos, requester, out _);
                    break;

                case MessageType.RequestDepositAll:
                    NetworkStorageInterfaceActions.TryExecuteDepositAll(world, _interfacePos, requester, out _);
                    break;
            }
        }

        private static bool TryValidateRequesterNoDistance(World world, NetPackage package, int requesterEntityId, out EntityPlayer requester)
        {
            requester = null;

            if (world == null || requesterEntityId <= 0)
                return false;

            if (TryGetServerSenderEntityId(package, out int senderEntityId) && senderEntityId > 0 && senderEntityId != requesterEntityId)
            {
                Log.Warning($"[NetworkStorageControl] Validation failed: requester mismatch requester={requesterEntityId} sender={senderEntityId}");
                return false;
            }

            requester = world.GetEntity(requesterEntityId) as EntityPlayer;
            if (requester == null)
            {
                Log.Warning($"[NetworkStorageControl] Validation failed: requester not found ({requesterEntityId})");
                return false;
            }

            return true;
        }

        private static bool TryGetServerSenderEntityId(NetPackage package, out int senderEntityId)
        {
            senderEntityId = -1;

            if (package == null)
                return false;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] candidates =
            {
                "SenderEntityId",
                "senderEntityId",
                "_senderEntityId",
                "SenderId",
                "senderId",
                "_senderId"
            };

            System.Type packageType = package.GetType();

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];

                PropertyInfo property = packageType.GetProperty(candidate, flags);
                if (property != null && property.PropertyType == typeof(int) && property.CanRead)
                {
                    senderEntityId = (int)property.GetValue(package, null);
                    return true;
                }

                FieldInfo field = packageType.GetField(candidate, flags);
                if (field != null && field.FieldType == typeof(int))
                {
                    senderEntityId = (int)field.GetValue(package);
                    return true;
                }
            }

            return false;
        }
    }
}
