using System;

public enum OutputTransportMode
{
    Adjacent = 0,
    Pipe = 1,
    Bluetooth = 2
}

public class OutputTargetInfo
{
    public Vector3i BlockPos;
    public OutputTransportMode TransportMode;
    public Guid PipeGraphId;

    public OutputTargetInfo(Vector3i blockPos, OutputTransportMode transportMode, Guid pipeGraphId = default(Guid))
    {
        BlockPos = blockPos;
        TransportMode = transportMode;
        PipeGraphId = pipeGraphId;
    }

    public override string ToString()
    {
        return $"Pos={BlockPos} Mode={TransportMode} PipeGraphId={PipeGraphId}";
    }
}