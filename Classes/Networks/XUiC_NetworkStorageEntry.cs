using System.Reflection;

public class XUiC_NetworkStorageEntry : XUiController
{
    public XUiC_NetworkStorageList OwnerList;
    public NetworkDisplayStack DisplayStack { get; private set; }

    public void SetDisplayStack(NetworkDisplayStack displayStack)
    {
        DisplayStack = displayStack;
        IsDirty = true;
        RefreshBindings(true);
    }

    public override void OnPressed(int mouseButton)
    {
        base.OnPressed(mouseButton);
        if (DisplayStack == null)
            return;

        bool isShift = IsShiftModifierActive();
        OwnerList?.OnEntryPressed(this, mouseButton, isShift);
    }

    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        switch (bindingName)
        {
            case "network_item_icon":
                value = DisplayStack?.DisplayStack?.itemValue?.ItemClass?.GetIconName() ?? string.Empty;
                return true;

            case "network_item_name":
                value = DisplayStack?.DisplayStack?.itemValue?.ItemClass?.GetLocalizedItemName() ?? string.Empty;
                return true;

            case "network_item_count":
                value = DisplayStack == null ? string.Empty : $"x {DisplayStack.TotalCount}";
                return true;

            case "network_item_tooltip":
                value = BuildTooltip();
                return true;
        }

        return false;
    }

    private string BuildTooltip()
    {
        if (DisplayStack == null || !NetworkItemIdentity.IsValidStack(DisplayStack.DisplayStack))
            return string.Empty;

        string itemName = DisplayStack.DisplayStack.itemValue.ItemClass.GetLocalizedItemName();
        string itemTypeName = NetworkItemIdentity.GetStableDisplayName(DisplayStack.DisplayStack);

        return $"{itemName}\nType: {itemTypeName}\nTotal: {DisplayStack.TotalCount}";
    }

    private bool IsShiftModifierActive()
    {
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer as EntityPlayerLocal;
        if (player == null)
            return false;

        object playerInput = player.playerInput;
        if (playerInput == null)
            return false;

        object permanentActions = GetPropertyValue(playerInput, "PermanentActions");
        if (permanentActions == null)
            return false;

        // Action names differ across game/input revisions; probe a small set.
        string[] candidates =
        {
            "Run",
            "Sprint",
            "Modifier1",
            "Shift"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            object action = GetPropertyValue(permanentActions, candidates[i]);
            if (action == null)
                continue;

            if (ReadBoolProperty(action, "IsPressed") || ReadBoolProperty(action, "WasPressed"))
                return true;
        }

        return false;
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        if (instance == null || string.IsNullOrEmpty(propertyName))
            return null;

        PropertyInfo prop = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (prop == null || !prop.CanRead)
            return null;

        return prop.GetValue(instance, null);
    }

    private static bool ReadBoolProperty(object instance, string propertyName)
    {
        object value = GetPropertyValue(instance, propertyName);
        return value is bool b && b;
    }
}
