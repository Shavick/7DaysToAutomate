public static class NetworkItemIdentity
{
    public static bool IsValidStack(ItemStack itemStack)
    {
        return !(itemStack.IsEmpty() || itemStack.count <= 0 || itemStack.itemValue == null || itemStack.itemValue.ItemClass == null);
    }

    public static bool AreSameForNetworkStacking(ItemStack stack1, ItemStack stack2)
    {
        if (!IsValidStack(stack1) || !IsValidStack(stack2))
            return false;

        return AreSameForNetworkStacking(stack1.itemValue, stack2.itemValue);
    }

    public static bool AreSameForNetworkStacking(ItemValue itemValue1, ItemValue itemValue2)
    {
        if (itemValue1 == null || itemValue2 == null || itemValue1.ItemClass == null || itemValue2.ItemClass == null)
            return false;

        return itemValue1.Equals(itemValue2);
    }

    public static string GetStableDisplayName(ItemStack stack)
    {
        if (!IsValidStack(stack))
            return string.Empty;

        return stack.itemValue.ItemClass.GetItemName() ?? string.Empty;
    }

    public static string GetDisplayKey(ItemStack stack)
    {
        if (!IsValidStack(stack))
            return string.Empty;

        string stableName = GetStableDisplayName(stack);
        string metadataKey = stack.itemValue.ToString();

        if (string.IsNullOrEmpty(stableName))
            return string.Empty;

        return $"{stableName}|{metadataKey}";
    }
}
