using System.Runtime.CompilerServices;

public static class WorldHLR
{
    private static readonly ConditionalWeakTable<World, HigherLogicRegistry> table
        = new ConditionalWeakTable<World, HigherLogicRegistry>();

    public static HigherLogicRegistry GetOrCreate(World world)
    {
        return table.GetValue(world, w =>
        {
            var hlr = new HigherLogicRegistry(w);
            hlr.Init();
            return hlr;
        });
    }

    public static bool TryGet(World world, out HigherLogicRegistry hlr)
    {
        return table.TryGetValue(world, out hlr);
    }
}
