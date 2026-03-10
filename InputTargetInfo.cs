using System;

public class InputTargetInfo
{
    public Vector3i BlockPos;
    public Guid PipeGraphId;

    public InputTargetInfo(Vector3i blockPos, Guid pipeGraphId)
    {
        BlockPos = blockPos;
        PipeGraphId = pipeGraphId;
    }
}