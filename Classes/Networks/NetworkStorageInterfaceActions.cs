using System;

public static class NetworkStorageInterfaceActions
{
    public static bool TryExecuteWithdraw(World world, Vector3i interfacePos, EntityPlayer requester, string displayKey, int requestedCount, out int deliveredCount)
    {
        deliveredCount = 0;

        if (!TryGetInterface(world, interfacePos, out TileEntityNetworkStorageInterface te))
            return false;

        if (requester == null || string.IsNullOrEmpty(displayKey) || requestedCount <= 0)
            return false;

        if (!NetworkStorageService.TryResolveItemValueByDisplayKey(world, te.NetworkId, displayKey, out ItemValue itemValue) || itemValue == null)
            return false;

        if (!NetworkStorageService.TryWithdraw(world, te.NetworkId, itemValue, requestedCount, out int withdrawn) || withdrawn <= 0)
            return false;

        ItemStack withdrawnStack = new ItemStack(itemValue.Clone(), withdrawn);
        int accepted = TryGiveToPlayerInventory(requester, withdrawnStack);
        deliveredCount = accepted;

        int remainder = withdrawn - accepted;
        if (remainder > 0)
        {
            NetworkStorageService.TryDeposit(world, te.NetworkId, new ItemStack(itemValue.Clone(), remainder), out _);
        }

        te.RefreshSnapshotSummary(world);
        te.setModified();
        return deliveredCount > 0;
    }

    public static bool TryExecuteDepositHolding(World world, Vector3i interfacePos, EntityPlayer requester, out int depositedCount)
    {
        depositedCount = 0;

        if (!TryGetInterface(world, interfacePos, out TileEntityNetworkStorageInterface te))
            return false;

        if (requester?.inventory == null)
            return false;

        ItemStack held = requester.inventory.holdingItemStack;
        if (!NetworkItemIdentity.IsValidStack(held))
            return false;

        ItemStack toDeposit = new ItemStack(held.itemValue.Clone(), held.count);
        if (!NetworkStorageService.TryDeposit(world, te.NetworkId, toDeposit, out int moved) || moved <= 0)
            return false;

        requester.inventory.DecHoldingItem(moved);
        te.RefreshSnapshotSummary(world);
        te.setModified();
        depositedCount = moved;
        return true;
    }

    public static bool TryExecuteDepositAll(World world, Vector3i interfacePos, EntityPlayer requester, out int depositedCount)
    {
        depositedCount = 0;

        if (!TryGetInterface(world, interfacePos, out TileEntityNetworkStorageInterface te))
            return false;

        if (requester == null)
            return false;

        bool changedBag = false;
        bool changedToolbelt = false;

        ItemStack[] bagSlots = requester.bag?.GetSlots();
        if (bagSlots != null)
        {
            for (int i = 0; i < bagSlots.Length; i++)
            {
                ItemStack slot = bagSlots[i];
                if (!NetworkItemIdentity.IsValidStack(slot))
                    continue;

                ItemStack toDeposit = new ItemStack(slot.itemValue.Clone(), slot.count);
                if (!NetworkStorageService.TryDeposit(world, te.NetworkId, toDeposit, out int moved) || moved <= 0)
                    continue;

                depositedCount += moved;
                changedBag = true;

                int remaining = slot.count - moved;
                if (remaining <= 0)
                    bagSlots[i] = ItemStack.Empty;
                else
                    bagSlots[i] = new ItemStack(slot.itemValue.Clone(), remaining);
            }
        }

        ItemStack[] toolbeltSlots = requester.inventory?.GetSlots();
        if (toolbeltSlots != null)
        {
            for (int i = 0; i < toolbeltSlots.Length; i++)
            {
                ItemStack slot = toolbeltSlots[i];
                if (!NetworkItemIdentity.IsValidStack(slot))
                    continue;

                ItemStack toDeposit = new ItemStack(slot.itemValue.Clone(), slot.count);
                if (!NetworkStorageService.TryDeposit(world, te.NetworkId, toDeposit, out int moved) || moved <= 0)
                    continue;

                depositedCount += moved;
                changedToolbelt = true;

                int remaining = slot.count - moved;
                if (remaining <= 0)
                    toolbeltSlots[i] = ItemStack.Empty;
                else
                    toolbeltSlots[i] = new ItemStack(slot.itemValue.Clone(), remaining);
            }
        }

        if (changedBag && requester.bag != null)
            requester.bag.SetSlots(bagSlots);

        if (changedToolbelt && requester.inventory != null)
            requester.inventory.SetSlots(toolbeltSlots, true);

        if (depositedCount <= 0)
            return false;

        te.RefreshSnapshotSummary(world);
        te.setModified();
        return true;
    }

    private static bool TryGetInterface(World world, Vector3i interfacePos, out TileEntityNetworkStorageInterface te)
    {
        te = null;

        if (world == null || world.IsRemote())
            return false;

        if (!(world.GetTileEntity(interfacePos) is TileEntityNetworkStorageInterface target))
            return false;

        if (!target.ResolveNetworkId(world, true) || target.NetworkId == Guid.Empty)
            return false;

        te = target;
        return true;
    }

    private static int TryGiveToPlayerInventory(EntityPlayer requester, ItemStack stack)
    {
        if (requester == null || !NetworkItemIdentity.IsValidStack(stack))
            return 0;

        int before = stack.count;

        requester.bag?.TryStackItem(0, stack);
        requester.inventory?.TryStackItem(0, stack);

        if (stack.count > 0)
            requester.bag?.AddItem(stack);

        if (stack.count > 0)
            requester.inventory?.AddItem(stack);

        int accepted = before - Math.Max(0, stack.count);
        return Math.Max(0, accepted);
    }
}
