namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackageExtractorControl : NetPackage
    {
        public enum MessageType : byte
        {
            RequestSelectOutput = 0,
            RequestSetEnabled = 1,
            RequestSetPriority = 2
        }

        private Vector3i _blockPos;
        private MessageType _messageType;

        private Vector3i _targetChestPos;
        private int _outputMode;
        private string _pipeGraphId;
        private bool _enabled;
        private int _priority;

        public NetPackageExtractorControl SetupSelectOutput(Vector3i blockPos, Vector3i chestPos, int outputMode, string pipeGraphId)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSelectOutput;
            _targetChestPos = chestPos;
            _outputMode = outputMode;
            _pipeGraphId = pipeGraphId ?? string.Empty;
            _enabled = false;
            _priority = TileEntityMachine.DefaultPipePriority;
            return this;
        }

        public NetPackageExtractorControl SetupSetEnabled(Vector3i blockPos, bool enabled)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSetEnabled;
            _targetChestPos = Vector3i.zero;
            _outputMode = 0;
            _pipeGraphId = string.Empty;
            _enabled = enabled;
            _priority = TileEntityMachine.DefaultPipePriority;
            return this;
        }

        public NetPackageExtractorControl SetupSetPriority(Vector3i blockPos, int priority)
        {
            _blockPos = blockPos;
            _messageType = MessageType.RequestSetPriority;
            _targetChestPos = Vector3i.zero;
            _outputMode = 0;
            _pipeGraphId = string.Empty;
            _enabled = false;
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

            int tx = _br.ReadInt32();
            int ty = _br.ReadInt32();
            int tz = _br.ReadInt32();
            _targetChestPos = new Vector3i(tx, ty, tz);

            _outputMode = _br.ReadInt32();
            _pipeGraphId = _br.ReadString();
            _enabled = _br.ReadBoolean();
            _priority = _br.ReadInt32();
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);

            _bw.Write(_blockPos.x);
            _bw.Write(_blockPos.y);
            _bw.Write(_blockPos.z);

            _bw.Write((byte)_messageType);

            _bw.Write(_targetChestPos.x);
            _bw.Write(_targetChestPos.y);
            _bw.Write(_targetChestPos.z);

            _bw.Write(_outputMode);
            _bw.Write(_pipeGraphId ?? string.Empty);
            _bw.Write(_enabled);
            _bw.Write(_priority);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null)
            {
                Log.Error("[ExtractorControl] World is null");
                return;
            }

            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                Log.Warning("[ExtractorControl] Ignored on non-server");
                return;
            }

            Log.Out($"[ExtractorControl] ProcessPackage type={_messageType} blockPos={_blockPos} targetChest={_targetChestPos} outputMode={_outputMode} pipeGraphId='{_pipeGraphId}' enabled={_enabled} priority={_priority}");

            TileEntity te = world.GetTileEntity(_blockPos);
            if (!(te is TileEntityUniversalExtractor extractor))
            {
                Log.Warning($"[ExtractorControl] No TileEntityUniversalExtractor at pos={_blockPos}");
                return;
            }

            switch (_messageType)
            {
                case MessageType.RequestSelectOutput:
                    Log.Out($"[ExtractorControl] ServerSelectOutputContainer {_targetChestPos} mode={_outputMode} pipeGraphId='{_pipeGraphId}' at {_blockPos}");
                    extractor.ServerSelectOutputContainer(_targetChestPos, (OutputTransportMode)_outputMode, _pipeGraphId);
                    break;

                case MessageType.RequestSetEnabled:
                    Log.Out($"[ExtractorControl] ServerSetEnabled {_enabled} at {_blockPos}");
                    extractor.ServerSetEnabled(_enabled);
                    break;

                case MessageType.RequestSetPriority:
                    Log.Out($"[ExtractorControl] ServerSetPriority {_priority} at {_blockPos}");
                    extractor.ServerSetPipePriority(_priority);
                    break;

                default:
                    Log.Warning($"[ExtractorControl] Unknown message type {_messageType}");
                    break;
            }
        }
    }
}
