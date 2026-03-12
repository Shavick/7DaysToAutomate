namespace _7DaysToAutomate.Classes.Net_Packages
{
    public class NetPackagePipeProbe : NetPackage
    {
        public enum MessageType : byte
        {
            RequestSnapshot = 0,
            SnapshotResponse = 1,
            CloseResponse = 2
        }

        private MessageType _messageType;
        private int _entityId;
        private int _clrIdx;
        private Vector3i _blockPos;

        private string _targetType;
        private string _title;
        private readonly string[] _lines = new string[6];

        public NetPackagePipeProbe SetupRequest(int entityId, int clrIdx, Vector3i blockPos)
        {
            _messageType = MessageType.RequestSnapshot;
            _entityId = entityId;
            _clrIdx = clrIdx;
            _blockPos = blockPos;
            _targetType = string.Empty;
            _title = string.Empty;
            for (int i = 0; i < _lines.Length; i++)
                _lines[i] = string.Empty;
            return this;
        }

        public NetPackagePipeProbe SetupSnapshotResponse(int entityId, int clrIdx, Vector3i blockPos, string targetType, string title, string[] lines)
        {
            _messageType = MessageType.SnapshotResponse;
            _entityId = entityId;
            _clrIdx = clrIdx;
            _blockPos = blockPos;
            _targetType = targetType ?? string.Empty;
            _title = title ?? string.Empty;
            for (int i = 0; i < _lines.Length; i++)
                _lines[i] = (lines != null && i < lines.Length) ? (lines[i] ?? string.Empty) : string.Empty;
            return this;
        }

        public NetPackagePipeProbe SetupCloseResponse(int entityId)
        {
            _messageType = MessageType.CloseResponse;
            _entityId = entityId;
            _clrIdx = 0;
            _blockPos = Vector3i.zero;
            _targetType = string.Empty;
            _title = string.Empty;
            for (int i = 0; i < _lines.Length; i++)
                _lines[i] = string.Empty;
            return this;
        }

        public override int GetLength()
        {
            return 512;
        }

        public override void read(PooledBinaryReader _reader)
        {
            _messageType = (MessageType)_reader.ReadByte();
            _entityId = _reader.ReadInt32();
            _clrIdx = _reader.ReadInt32();
            _blockPos = new Vector3i(_reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32());
            _targetType = _reader.ReadString();
            _title = _reader.ReadString();
            for (int i = 0; i < _lines.Length; i++)
                _lines[i] = _reader.ReadString();
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            _writer.Write((byte)_messageType);
            _writer.Write(_entityId);
            _writer.Write(_clrIdx);
            _writer.Write(_blockPos.x);
            _writer.Write(_blockPos.y);
            _writer.Write(_blockPos.z);
            _writer.Write(_targetType ?? string.Empty);
            _writer.Write(_title ?? string.Empty);
            for (int i = 0; i < _lines.Length; i++)
                _writer.Write(_lines[i] ?? string.Empty);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null)
            {
                Log.Error("[PipeProbe][Net] World is null");
                return;
            }

            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;

            if (cm.IsServer && _messageType == MessageType.RequestSnapshot)
            {
                Log.Out($"[PipeProbe][Net][SERVER] Request entity={_entityId} clrIdx={_clrIdx} pos={_blockPos}");

                if (!PipeProbeHudManager.TryBuildSnapshotServer(world, _clrIdx, _blockPos, out string targetType, out string title, out string[] lines))
                {
                    Log.Out($"[PipeProbe][Net][SERVER] No probe target at {_blockPos}; sending close");

                    cm.SendPackage(
                        NetPackageManager.GetPackage<NetPackagePipeProbe>().SetupCloseResponse(_entityId),
                        false,
                        _entityId,
                        -1,
                        -1,
                        null,
                        192,
                        false
                    );
                    return;
                }

                cm.SendPackage(
                    NetPackageManager.GetPackage<NetPackagePipeProbe>().SetupSnapshotResponse(_entityId, _clrIdx, _blockPos, targetType, title, lines),
                    false,
                    _entityId,
                    -1,
                    -1,
                    null,
                    192,
                    false
                );
                return;
            }

            if (!cm.IsServer)
            {
                EntityPlayerLocal localPlayer = world.GetPrimaryPlayer() as EntityPlayerLocal;
                if (localPlayer == null)
                    return;

                if (_messageType == MessageType.CloseResponse)
                {
                    Log.Out("[PipeProbe][Net][CLIENT] CloseResponse");
                    PipeProbeHudManager.Close(localPlayer);
                    return;
                }

                if (_messageType == MessageType.SnapshotResponse)
                {
                    Log.Out($"[PipeProbe][Net][CLIENT] SnapshotResponse type={_targetType} pos={_blockPos}");
                    PipeProbeHudManager.ApplyServerSnapshot(localPlayer, _targetType, _blockPos, _title, _lines);
                    return;
                }
            }
        }
    }
}
