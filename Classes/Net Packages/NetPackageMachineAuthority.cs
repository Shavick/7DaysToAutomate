using System.Reflection;
using UnityEngine;

internal static class NetPackageMachineAuthority
{
    private const float MaxMachineControlDistance = 12f;

    public static bool TryValidateRequester(World world, NetPackage package, int requesterEntityId, Vector3i machinePos, string logPrefix, out EntityPlayer requester)
    {
        requester = null;

        if (world == null)
        {
            Log.Warning($"[{logPrefix}] Validation failed: world is null");
            return false;
        }

        if (requesterEntityId <= 0)
        {
            Log.Warning($"[{logPrefix}] Validation failed: requester entity id is invalid ({requesterEntityId})");
            return false;
        }

        if (TryGetServerSenderEntityId(package, out int senderEntityId) && senderEntityId > 0 && senderEntityId != requesterEntityId)
        {
            Log.Warning($"[{logPrefix}] Validation failed: requester id mismatch requester={requesterEntityId} sender={senderEntityId}");
            return false;
        }

        requester = world.GetEntity(requesterEntityId) as EntityPlayer;
        if (requester == null)
        {
            Log.Warning($"[{logPrefix}] Validation failed: requester entity not found ({requesterEntityId})");
            return false;
        }

        Vector3 requesterPos = requester.GetPosition();
        Vector3 machineCenter = new Vector3(machinePos.x + 0.5f, machinePos.y + 0.5f, machinePos.z + 0.5f);
        float sqrDistance = (requesterPos - machineCenter).sqrMagnitude;
        float maxSqrDistance = MaxMachineControlDistance * MaxMachineControlDistance;

        if (sqrDistance > maxSqrDistance)
        {
            Log.Warning($"[{logPrefix}] Validation failed: requester too far from machine requester={requester.entityId} machine={machinePos} distance={Mathf.Sqrt(sqrDistance):0.00} max={MaxMachineControlDistance:0.00}");
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
