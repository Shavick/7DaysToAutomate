namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageCrafterControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestSelectRecipe = 0,
            RequestSelectInput = 1,
            RequestSelectOutput = 2,
            RequestSetEnabled = 3,
            RequestSetPriority = 4
        }

        private Vector3i _blockPos;
        private MessageType _messageType;

        private string _recipeName;
        private Vector3i _targetChestPos;
        private bool _enabled;

        private int _outputMode;
        private string _pipeGraphId;
        private int _priority;

        public NetPackageCrafterControl SetupSelectRecipe(Vector3i blockPos, string recipeName)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectRecipe;
            _recipeName = recipeName ?? string.Empty;
            _targetChestPos = Vector3i.zero;
            _enabled = false;
            _outputMode = 0;
            _pipeGraphId = string.Empty;
            _priority = TileEntityMachine.DefaultPipePriority;
            return this;
        }

        public NetPackageCrafterControl SetupSelectInput(Vector3i blockPos, Vector3i chestPos, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectInput;
            _recipeName = string.Empty;
            _targetChestPos = chestPos;
            _enabled = false;
            _outputMode = 0;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            _priority = TileEntityMachine.DefaultPipePriority;
            return this;
        }

        public NetPackageCrafterControl SetupSelectOutput(Vector3i blockPos, Vector3i chestPos, int mode, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectOutput;
            _recipeName = string.Empty;
            _targetChestPos = chestPos;
            _enabled = false;
            _outputMode = mode;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            _priority = TileEntityMachine.DefaultPipePriority;
            return this;
        }

        public NetPackageCrafterControl SetupSetEnabled(Vector3i blockPos, bool enabled)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSetEnabled;
            _recipeName = string.Empty;
            _targetChestPos = Vector3i.zero;
            _enabled = enabled;
            _outputMode = 0;
            _pipeGraphId = string.Empty;
            _priority = TileEntityMachine.DefaultPipePriority;
            return this;
        }

        public NetPackageCrafterControl SetupSetPriority(Vector3i blockPos, int priority)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSetPriority;
            _recipeName = string.Empty;
            _targetChestPos = Vector3i.zero;
            _enabled = false;
            _outputMode = 0;
            _pipeGraphId = string.Empty;
            _priority = priority;
            return this;
        }

        public override int GetLength()
        {
            return 128;
        }

        public override void read(PooledBinaryReader _br)
        {
            int x = _br.ReadInt32();
            int y = _br.ReadInt32();
            int z = _br.ReadInt32();
            _blockPos = new Vector3i(x, y, z);

            _messageType = (MessageType)_br.ReadByte();

            _recipeName = _br.ReadString();

            int tx = _br.ReadInt32();
            int ty = _br.ReadInt32();
            int tz = _br.ReadInt32();
            _targetChestPos = new Vector3i(tx, ty, tz);

            _enabled = _br.ReadBoolean();

            _outputMode = _br.ReadInt32();
            _pipeGraphId = _br.ReadString();
            _priority = _br.ReadInt32();
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);

            _bw.Write(_blockPos.x);
            _bw.Write(_blockPos.y);
            _bw.Write(_blockPos.z);

            _bw.Write((byte)_messageType);

            _bw.Write(_recipeName ?? string.Empty);

            _bw.Write(_targetChestPos.x);
            _bw.Write(_targetChestPos.y);
            _bw.Write(_targetChestPos.z);

            _bw.Write(_enabled);

            _bw.Write(_outputMode);
            _bw.Write(_pipeGraphId ?? string.Empty);
            _bw.Write(_priority);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null)
            {
                Log.Error("[CrafterControl] World is null");
                return;
            }

            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                Log.Warning("[CrafterControl] Ignored on non-server");
                return;
            }

            Log.Out($"[CrafterControl] ProcessPackage type={_messageType} blockPos={_blockPos} recipe='{_recipeName}' targetChest={_targetChestPos} enabled={_enabled} outputMode={_outputMode} pipeGraphId='{_pipeGraphId}' priority={_priority}");

            TileEntity te = world.GetTileEntity(_blockPos);
            if (!(te is TileEntityUniversalCrafter crafter))
            {
                Log.Warning($"[CrafterControl] No TileEntityUniversalCrafter at pos={_blockPos}");
                return;
            }

            switch (_messageType)
            {
                case MessageType.RequestSelectRecipe:
                    Log.Out($"[CrafterControl] ServerSelectRecipe '{_recipeName}' at {_blockPos}");
                    crafter.ServerSelectRecipe(_recipeName);
                    break;

                case MessageType.RequestSelectInput:
                    Log.Out($"[CrafterControl] ServerSelectInputContainer {_targetChestPos} pipeGraphId='{_pipeGraphId}' at {_blockPos}");
                    crafter.ServerSelectInputContainer(_targetChestPos, _pipeGraphId);
                    break;

                case MessageType.RequestSelectOutput:
                    Log.Out($"[CrafterControl] ServerSelectOutputContainer {_targetChestPos} mode={_outputMode} pipeGraphId='{_pipeGraphId}' at {_blockPos}");
                    crafter.ServerSelectOutputContainer(_targetChestPos, (OutputTransportMode)_outputMode, _pipeGraphId);
                    break;

                case MessageType.RequestSetEnabled:
                    Log.Out($"[CrafterControl] ServerSetEnabled {_enabled} at {_blockPos}");
                    crafter.ServerSetEnabled(_enabled);
                    break;

                case MessageType.RequestSetPriority:
                    Log.Out($"[CrafterControl] ServerSetPriority {_priority} at {_blockPos}");
                    crafter.ServerSetPipePriority(_priority);
                    break;

                default:
                    Log.Warning($"[CrafterControl] Unknown message type {_messageType}");
                    break;
            }
        }
    }
}
