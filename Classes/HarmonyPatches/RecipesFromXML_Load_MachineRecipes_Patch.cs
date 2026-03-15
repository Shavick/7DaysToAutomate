using HarmonyLib;

[HarmonyPatch(typeof(RecipesFromXml))]
[HarmonyPatch("LoadRecipies")]
public static class RecipesFromXML_Load_MachineRecipes_Patch
{
    public static void Postfix(XmlFile _xmlFile)
    {
        MachineRecipeRegistry.LoadFromXml(_xmlFile);
    }
}
