using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

public sealed class MachineRecipeInput
{
    public string ItemName { get; }
    public int Count { get; }

    public MachineRecipeInput(string itemName, int count)
    {
        ItemName = itemName ?? string.Empty;
        Count = Math.Max(1, count);
    }
}

public sealed class MachineRecipeItemOutput
{
    public string ItemName { get; }
    public int Count { get; }

    public MachineRecipeItemOutput(string itemName, int count)
    {
        ItemName = itemName ?? string.Empty;
        Count = Math.Max(1, count);
    }
}

public sealed class MachineRecipeFluidInput
{
    public string Type { get; }
    public int AmountMg { get; }

    public MachineRecipeFluidInput(string type, int amountMg)
    {
        Type = type ?? string.Empty;
        AmountMg = Math.Max(1, amountMg);
    }
}

public sealed class MachineRecipeFluidOutput
{
    public string Type { get; }
    public int AmountMg { get; }

    public MachineRecipeFluidOutput(string type, int amountMg)
    {
        Type = type ?? string.Empty;
        AmountMg = Math.Max(1, amountMg);
    }
}

public sealed class MachineRecipeGasOutput
{
    public string Type { get; }
    public int AmountMg { get; }

    public MachineRecipeGasOutput(string type, int amountMg)
    {
        Type = type ?? string.Empty;
        AmountMg = Math.Max(1, amountMg);
    }
}

public sealed class MachineRecipe
{
    public string Name { get; }
    public string Machine { get; }
    public int? CraftTimeTicks { get; }
    public IReadOnlyList<MachineRecipeInput> Inputs { get; }
    public IReadOnlyList<MachineRecipeFluidInput> FluidInputs { get; }
    public IReadOnlyList<MachineRecipeItemOutput> ItemOutputs { get; }
    public IReadOnlyList<MachineRecipeFluidOutput> FluidOutputs { get; }
    public IReadOnlyList<MachineRecipeGasOutput> GasOutputs { get; }
    public string NormalizedKey { get; }
    public string SourceContext { get; }
    public int SourceOrder { get; }

    public MachineRecipe(
        string name,
        string machine,
        int? craftTimeTicks,
        List<MachineRecipeInput> inputs,
        List<MachineRecipeFluidInput> fluidInputs,
        List<MachineRecipeItemOutput> itemOutputs,
        List<MachineRecipeFluidOutput> fluidOutputs,
        List<MachineRecipeGasOutput> gasOutputs,
        string normalizedKey,
        string sourceContext,
        int sourceOrder)
    {
        Name = name ?? string.Empty;
        Machine = machine ?? string.Empty;
        CraftTimeTicks = craftTimeTicks;
        Inputs = (inputs ?? new List<MachineRecipeInput>()).AsReadOnly();
        FluidInputs = (fluidInputs ?? new List<MachineRecipeFluidInput>()).AsReadOnly();
        ItemOutputs = (itemOutputs ?? new List<MachineRecipeItemOutput>()).AsReadOnly();
        FluidOutputs = (fluidOutputs ?? new List<MachineRecipeFluidOutput>()).AsReadOnly();
        GasOutputs = (gasOutputs ?? new List<MachineRecipeGasOutput>()).AsReadOnly();
        NormalizedKey = normalizedKey ?? string.Empty;
        SourceContext = sourceContext ?? string.Empty;
        SourceOrder = sourceOrder;
    }
}

public static class MachineRecipeRegistry
{
    private static readonly object Sync = new object();
    private static readonly List<MachineRecipe> allRecipes = new List<MachineRecipe>();
    private static readonly Dictionary<string, MachineRecipe> recipesByKey = new Dictionary<string, MachineRecipe>(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<MachineRecipe>> recipesByMachine = new Dictionary<string, List<MachineRecipe>>(StringComparer.Ordinal);
    private static readonly List<string> duplicateOverrideLog = new List<string>();
    private static readonly HashSet<string> warnedUnknownGroups = new HashSet<string>(StringComparer.Ordinal);

    private static bool loaded;
    private static int totalEntries;
    private static int validEntries;
    private static int skippedEntries;
    private static int dedupedEntries;

    public static bool IsLoaded
    {
        get
        {
            lock (Sync)
                return loaded;
        }
    }

    public static void LoadFromXml(XmlFile xmlFile)
    {
        lock (Sync)
        {
            ClearInternal();

            loaded = true;

            XElement root = xmlFile?.XmlDoc?.Root;
            if (root == null)
            {
                Log.Warning("[MachineRecipe] Load skipped - XML root not found");
                LogSummary();
                return;
            }

            int order = 0;
            foreach (XElement node in root.Elements("machineRecipe"))
            {
                totalEntries++;
                order++;

                if (!TryParseMachineRecipe(node, order, out MachineRecipe parsed, out string error))
                {
                    skippedEntries++;
                    Log.Warning($"[MachineRecipe] Skipped invalid entry #{order}: {error}");
                    continue;
                }

                if (parsed == null || string.IsNullOrEmpty(parsed.NormalizedKey))
                {
                    skippedEntries++;
                    Log.Warning($"[MachineRecipe] Skipped invalid entry #{order}: missing normalized key");
                    continue;
                }

                if (recipesByKey.TryGetValue(parsed.NormalizedKey, out MachineRecipe replaced))
                {
                    dedupedEntries++;
                    duplicateOverrideLog.Add(
                        $"key={parsed.NormalizedKey} replaced '{replaced.SourceContext}' with '{parsed.SourceContext}'");
                }

                recipesByKey[parsed.NormalizedKey] = parsed;
            }

            allRecipes.AddRange(recipesByKey.Values);
            allRecipes.Sort((a, b) => a.SourceOrder.CompareTo(b.SourceOrder));

            validEntries = allRecipes.Count;

            for (int i = 0; i < allRecipes.Count; i++)
            {
                MachineRecipe recipe = allRecipes[i];
                if (!recipesByMachine.TryGetValue(recipe.Machine, out List<MachineRecipe> list))
                {
                    list = new List<MachineRecipe>();
                    recipesByMachine[recipe.Machine] = list;
                }

                list.Add(recipe);
            }

            LogSummary();
        }
    }

    public static List<MachineRecipe> GetRecipesForMachineGroups(string machineGroupsCsv, bool warnUnknownGroups = false)
    {
        lock (Sync)
        {
            List<string> groups = ParseMachineGroups(machineGroupsCsv);
            List<MachineRecipe> results = new List<MachineRecipe>();

            for (int i = 0; i < groups.Count; i++)
            {
                string group = groups[i];
                if (!recipesByMachine.TryGetValue(group, out List<MachineRecipe> groupRecipes) || groupRecipes == null)
                {
                    if (warnUnknownGroups && warnedUnknownGroups.Add(group))
                        Log.Warning($"[MachineRecipe] Block references unknown MachineRecipes group '{group}'");

                    continue;
                }

                for (int j = 0; j < groupRecipes.Count; j++)
                    results.Add(groupRecipes[j]);
            }

            results.Sort((a, b) => string.Compare(GetSortLabel(a), GetSortLabel(b), StringComparison.OrdinalIgnoreCase));
            return results;
        }
    }

    public static bool TryGetRecipeByKey(string normalizedKey, out MachineRecipe recipe)
    {
        lock (Sync)
        {
            return recipesByKey.TryGetValue(normalizedKey ?? string.Empty, out recipe);
        }
    }

    public static List<string> GetAllMachineGroups()
    {
        lock (Sync)
        {
            List<string> groups = new List<string>(recipesByMachine.Keys);
            groups.Sort(StringComparer.Ordinal);
            return groups;
        }
    }

    public static Dictionary<string, int> GetMachineGroupCounts()
    {
        lock (Sync)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kvp in recipesByMachine)
                counts[kvp.Key] = kvp.Value?.Count ?? 0;
            return counts;
        }
    }

    public static List<string> GetDuplicateOverrideLog()
    {
        lock (Sync)
        {
            return new List<string>(duplicateOverrideLog);
        }
    }

    public static void GetSummary(out bool isLoaded, out int total, out int valid, out int skipped, out int deduped)
    {
        lock (Sync)
        {
            isLoaded = loaded;
            total = totalEntries;
            valid = validEntries;
            skipped = skippedEntries;
            deduped = dedupedEntries;
        }
    }

    public static string BuildNormalizedKey(
        string machine,
        int? craftTimeTicks,
        List<MachineRecipeInput> inputs,
        List<MachineRecipeFluidInput> fluidInputs,
        List<MachineRecipeItemOutput> itemOutputs,
        List<MachineRecipeFluidOutput> fluidOutputs,
        List<MachineRecipeGasOutput> gasOutputs)
    {
        string craftToken = craftTimeTicks.HasValue
            ? craftTimeTicks.Value.ToString(CultureInfo.InvariantCulture)
            : "~";

        return
            $"machine={machine}|ct={craftToken}|in={FormatInputs(inputs)}|fin={FormatFluidInputs(fluidInputs)}|out={FormatItemOutputs(itemOutputs)}|fout={FormatFluidOutputs(fluidOutputs)}|gout={FormatGasOutputs(gasOutputs)}";
    }

    public static bool TryParseGallonsToMg(string rawAmount, out int amountMg)
    {
        amountMg = 0;
        if (string.IsNullOrWhiteSpace(rawAmount))
            return false;

        string trimmed = rawAmount.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double gallons) || gallons <= 0d)
            return false;

        double mg = gallons * FluidConstants.MilliGallonsPerGallon;
        if (mg > int.MaxValue)
            mg = int.MaxValue;

        if (mg < 1d)
            mg = 1d;

        amountMg = (int)Math.Round(mg, MidpointRounding.AwayFromZero);
        return true;
    }

    public static List<string> ParseMachineGroups(string machineGroupsCsv)
    {
        List<string> groups = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(machineGroupsCsv))
            return groups;

        string[] parts = machineGroupsCsv.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string group = (parts[i] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(group))
                continue;

            if (!seen.Add(group))
                continue;

            groups.Add(group);
        }

        return groups;
    }

    private static void ClearInternal()
    {
        allRecipes.Clear();
        recipesByKey.Clear();
        recipesByMachine.Clear();
        duplicateOverrideLog.Clear();
        warnedUnknownGroups.Clear();

        totalEntries = 0;
        validEntries = 0;
        skippedEntries = 0;
        dedupedEntries = 0;
    }

    private static void LogSummary()
    {
        Log.Out(
            $"[MachineRecipe] Loaded machine recipes: total={totalEntries}, valid={validEntries}, skipped={skippedEntries}, deduped={dedupedEntries}");
    }

    private static string GetSortLabel(MachineRecipe recipe)
    {
        if (recipe == null)
            return string.Empty;

        if (recipe.ItemOutputs != null && recipe.ItemOutputs.Count > 0)
        {
            string itemName = recipe.ItemOutputs[0]?.ItemName ?? string.Empty;
            ItemValue itemValue = ItemClass.GetItem(itemName, false);
            if (itemValue?.ItemClass != null)
                return itemValue.ItemClass.GetLocalizedItemName();

            return itemName;
        }

        if (recipe.FluidOutputs != null && recipe.FluidOutputs.Count > 0)
            return ToDisplayType(recipe.FluidOutputs[0]?.Type);

        if (recipe.GasOutputs != null && recipe.GasOutputs.Count > 0)
            return ToDisplayType(recipe.GasOutputs[0]?.Type);

        if (!string.IsNullOrEmpty(recipe.Name))
            return recipe.Name;

        return recipe.NormalizedKey ?? string.Empty;
    }

    private static string ToDisplayType(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
            return string.Empty;

        string normalized = rawType.Trim().Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static bool TryParseMachineRecipe(XElement node, int order, out MachineRecipe recipe, out string error)
    {
        recipe = null;
        error = string.Empty;

        if (node == null)
        {
            error = "Node is null";
            return false;
        }

        string machine = (node.Attribute("machine")?.Value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(machine))
        {
            error = "Attribute 'machine' is required";
            return false;
        }

        string name = (node.Attribute("name")?.Value ?? string.Empty).Trim();

        int? craftTimeTicks = null;
        string craftTimeRaw = (node.Attribute("craft_time")?.Value ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(craftTimeRaw))
        {
            if (!int.TryParse(craftTimeRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCraftTime) || parsedCraftTime <= 0)
            {
                error = $"Invalid craft_time '{craftTimeRaw}'";
                return false;
            }

            craftTimeTicks = parsedCraftTime;
        }

        Dictionary<string, int> inputCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, int> fluidInputMgByType = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, int> outputCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, int> fluidMgByType = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, int> gasMgByType = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (XElement child in node.Elements())
        {
            if (child == null)
                continue;

            string tag = child.Name.LocalName;
            switch (tag)
            {
                case "input":
                    if (!TryParseItemCountNode(child, "item", out string inputItem, out int inputCount, out string inputError))
                    {
                        error = $"Invalid <input>: {inputError}";
                        return false;
                    }

                    AddClamped(inputCounts, inputItem, inputCount);
                    break;

                case "fluid_input":
                    if (!TryParseFluidGasNode(child, out string fluidInputType, out int fluidInputMg, out string fluidInputError))
                    {
                        error = $"Invalid <fluid_input>: {fluidInputError}";
                        return false;
                    }

                    AddClamped(fluidInputMgByType, fluidInputType, fluidInputMg);
                    break;

                case "output":
                    if (!TryParseItemCountNode(child, "item", out string outputItem, out int outputCount, out string outputError))
                    {
                        error = $"Invalid <output>: {outputError}";
                        return false;
                    }

                    AddClamped(outputCounts, outputItem, outputCount);
                    break;

                case "fluid_output":
                    if (!TryParseFluidGasNode(child, out string fluidType, out int fluidMg, out string fluidError))
                    {
                        error = $"Invalid <fluid_output>: {fluidError}";
                        return false;
                    }

                    AddClamped(fluidMgByType, fluidType, fluidMg);
                    break;

                case "gas_output":
                    if (!TryParseFluidGasNode(child, out string gasType, out int gasMg, out string gasError))
                    {
                        error = $"Invalid <gas_output>: {gasError}";
                        return false;
                    }

                    AddClamped(gasMgByType, gasType, gasMg);
                    break;
            }
        }

        if (inputCounts.Count <= 0)
        {
            error = "Recipe requires at least one <input>";
            return false;
        }

        if (outputCounts.Count <= 0 && fluidMgByType.Count <= 0 && gasMgByType.Count <= 0)
        {
            error = "Recipe requires at least one output (<output>, <fluid_output>, or <gas_output>)";
            return false;
        }

        List<MachineRecipeInput> inputs = new List<MachineRecipeInput>();
        List<MachineRecipeFluidInput> fluidInputs = new List<MachineRecipeFluidInput>();
        List<MachineRecipeItemOutput> outputs = new List<MachineRecipeItemOutput>();
        List<MachineRecipeFluidOutput> fluidOutputs = new List<MachineRecipeFluidOutput>();
        List<MachineRecipeGasOutput> gasOutputs = new List<MachineRecipeGasOutput>();

        List<string> sortedInputKeys = new List<string>(inputCounts.Keys);
        sortedInputKeys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < sortedInputKeys.Count; i++)
        {
            string key = sortedInputKeys[i];
            inputs.Add(new MachineRecipeInput(key, inputCounts[key]));
        }

        List<string> sortedFluidInputKeys = new List<string>(fluidInputMgByType.Keys);
        sortedFluidInputKeys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < sortedFluidInputKeys.Count; i++)
        {
            string key = sortedFluidInputKeys[i];
            fluidInputs.Add(new MachineRecipeFluidInput(key, fluidInputMgByType[key]));
        }

        List<string> sortedOutputKeys = new List<string>(outputCounts.Keys);
        sortedOutputKeys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < sortedOutputKeys.Count; i++)
        {
            string key = sortedOutputKeys[i];
            outputs.Add(new MachineRecipeItemOutput(key, outputCounts[key]));
        }

        List<string> sortedFluidKeys = new List<string>(fluidMgByType.Keys);
        sortedFluidKeys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < sortedFluidKeys.Count; i++)
        {
            string key = sortedFluidKeys[i];
            fluidOutputs.Add(new MachineRecipeFluidOutput(key, fluidMgByType[key]));
        }

        List<string> sortedGasKeys = new List<string>(gasMgByType.Keys);
        sortedGasKeys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < sortedGasKeys.Count; i++)
        {
            string key = sortedGasKeys[i];
            gasOutputs.Add(new MachineRecipeGasOutput(key, gasMgByType[key]));
        }

        string normalizedKey = BuildNormalizedKey(machine, craftTimeTicks, inputs, fluidInputs, outputs, fluidOutputs, gasOutputs);
        string sourceContext = $"machine={machine},name={name},order={order}";

        recipe = new MachineRecipe(
            name,
            machine,
            craftTimeTicks,
            inputs,
            fluidInputs,
            outputs,
            fluidOutputs,
            gasOutputs,
            normalizedKey,
            sourceContext,
            order);

        return true;
    }

    private static void AddClamped(Dictionary<string, int> target, string key, int add)
    {
        if (target == null || string.IsNullOrEmpty(key) || add <= 0)
            return;

        int existing = target.TryGetValue(key, out int current) ? current : 0;
        long total = (long)existing + add;
        if (total > int.MaxValue)
            total = int.MaxValue;

        target[key] = (int)total;
    }

    private static bool TryParseItemCountNode(
        XElement node,
        string attrName,
        out string itemName,
        out int count,
        out string error)
    {
        itemName = string.Empty;
        count = 0;
        error = string.Empty;

        if (node == null)
        {
            error = "Node is null";
            return false;
        }

        itemName = (node.Attribute(attrName)?.Value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(itemName))
        {
            error = $"Missing attribute '{attrName}'";
            return false;
        }

        string countRaw = (node.Attribute("count")?.Value ?? string.Empty).Trim();
        if (!int.TryParse(countRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count <= 0)
        {
            error = $"Invalid count '{countRaw}'";
            return false;
        }

        return true;
    }

    private static bool TryParseFluidGasNode(
        XElement node,
        out string type,
        out int amountMg,
        out string error)
    {
        type = string.Empty;
        amountMg = 0;
        error = string.Empty;

        if (node == null)
        {
            error = "Node is null";
            return false;
        }

        type = (node.Attribute("type")?.Value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(type))
        {
            error = "Missing attribute 'type'";
            return false;
        }

        string gallonsRaw = (node.Attribute("gallons")?.Value ?? string.Empty).Trim();
        if (!TryParseGallonsToMg(gallonsRaw, out amountMg))
        {
            error = $"Invalid gallons '{gallonsRaw}'";
            return false;
        }

        return true;
    }

    private static string FormatInputs(List<MachineRecipeInput> inputs)
    {
        if (inputs == null || inputs.Count == 0)
            return string.Empty;

        List<string> entries = new List<string>(inputs.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            MachineRecipeInput input = inputs[i];
            if (input == null)
                continue;

            entries.Add($"{input.ItemName}*{input.Count}");
        }

        return string.Join(";", entries);
    }

    private static string FormatItemOutputs(List<MachineRecipeItemOutput> outputs)
    {
        if (outputs == null || outputs.Count == 0)
            return string.Empty;

        List<string> entries = new List<string>(outputs.Count);
        for (int i = 0; i < outputs.Count; i++)
        {
            MachineRecipeItemOutput output = outputs[i];
            if (output == null)
                continue;

            entries.Add($"{output.ItemName}*{output.Count}");
        }

        return string.Join(";", entries);
    }

    private static string FormatFluidInputs(List<MachineRecipeFluidInput> inputs)
    {
        if (inputs == null || inputs.Count == 0)
            return string.Empty;

        List<string> entries = new List<string>(inputs.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            MachineRecipeFluidInput input = inputs[i];
            if (input == null)
                continue;

            entries.Add($"{input.Type}*{input.AmountMg}");
        }

        return string.Join(";", entries);
    }

    private static string FormatFluidOutputs(List<MachineRecipeFluidOutput> outputs)
    {
        if (outputs == null || outputs.Count == 0)
            return string.Empty;

        List<string> entries = new List<string>(outputs.Count);
        for (int i = 0; i < outputs.Count; i++)
        {
            MachineRecipeFluidOutput output = outputs[i];
            if (output == null)
                continue;

            entries.Add($"{output.Type}*{output.AmountMg}");
        }

        return string.Join(";", entries);
    }

    private static string FormatGasOutputs(List<MachineRecipeGasOutput> outputs)
    {
        if (outputs == null || outputs.Count == 0)
            return string.Empty;

        List<string> entries = new List<string>(outputs.Count);
        for (int i = 0; i < outputs.Count; i++)
        {
            MachineRecipeGasOutput output = outputs[i];
            if (output == null)
                continue;

            entries.Add($"{output.Type}*{output.AmountMg}");
        }

        return string.Join(";", entries);
    }
}
