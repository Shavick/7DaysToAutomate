using System;

public class NetPackageExtractor : NetPackage
{

    private Vector3i BlockPosition;
    private int ClrIdx;
    private bool IsOn;

    public NetPackageExtractor Setup(Vector3i blockPosition, int clrIdx, bool isOn)
    {

        return this;
    }

    public override int GetLength()
    {
        throw new NotImplementedException();
    }

    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        throw new NotImplementedException();
    }

    public override void read(PooledBinaryReader _reader)
    {
        throw new NotImplementedException();
    }
}
