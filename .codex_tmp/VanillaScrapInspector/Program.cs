using System.Reflection;
using System.Reflection.Emit;

if (args.Length < 3)
{
    Console.WriteLine("Usage: MethodIL <assembly-path> <type.contains> <method.contains>");
    return;
}

string asmPath = args[0];
string typeNeedle = args[1];
string methodNeedle = args[2];
Assembly asm = Assembly.LoadFrom(asmPath);
Dictionary<short, OpCode> ops = BuildOpcodeMap();

foreach (Type t in asm.GetTypes().Where(t => (t.FullName ?? "").Contains(typeNeedle, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.FullName))
{
    foreach (MethodInfo m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)
        .Where(m => m.Name.Contains(methodNeedle, StringComparison.OrdinalIgnoreCase))
        .OrderBy(m => m.Name))
    {
        Console.WriteLine($"=== {t.FullName}.{m.Name} ===");
        MethodBody? body;
        try { body = m.GetMethodBody(); } catch { continue; }
        if (body == null) { Console.WriteLine("<no body>"); continue; }
        byte[] il = body.GetILAsByteArray() ?? [];
        int i=0;
        while (i < il.Length)
        {
            int start = i;
            short code = il[i++];
            if (code == 0xFE) code = (short)(0xFE00 | il[i++]);
            if (!ops.TryGetValue(code, out OpCode op)) break;

            object? operand = null;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineI:
                    operand = (sbyte)il[i]; i += 1; break;
                case OperandType.ShortInlineVar:
                    operand = il[i]; i += 1; break;
                case OperandType.ShortInlineBrTarget:
                    operand = (sbyte)il[i] + i + 1; i += 1; break;
                case OperandType.InlineVar:
                    operand = BitConverter.ToUInt16(il, i); i += 2; break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                    operand = BitConverter.ToInt32(il, i); i += 4; break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    operand = BitConverter.ToInt32(il, i); i += 4; break;
                case OperandType.InlineI8:
                    operand = BitConverter.ToInt64(il, i); i += 8; break;
                case OperandType.InlineR:
                    operand = BitConverter.ToDouble(il, i); i += 8; break;
                case OperandType.ShortInlineR:
                    operand = BitConverter.ToSingle(il, i); i += 4; break;
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(il, i); i += 4;
                    int[] targets = new int[count];
                    for (int j=0;j<count;j++){ targets[j]=BitConverter.ToInt32(il,i)+i+4; i+=4; }
                    operand = string.Join(",", targets);
                    break;
            }

            string extra = operand != null ? $" {operand}" : string.Empty;
            if ((op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj) && operand is int mt)
            {
                try { var mb = m.Module.ResolveMethod(mt); extra = $" -> {mb?.DeclaringType?.FullName}.{mb?.Name}"; } catch { }
            }
            else if (op == OpCodes.Ldstr && operand is int st)
            {
                try { extra = $" \"{m.Module.ResolveString(st)}\""; } catch { }
            }
            else if ((op == OpCodes.Ldfld || op == OpCodes.Ldsfld || op == OpCodes.Stfld || op == OpCodes.Stsfld) && operand is int ft)
            {
                try { var f = m.Module.ResolveField(ft); extra = $" -> {f?.DeclaringType?.FullName}.{f?.Name}"; } catch { }
            }

            Console.WriteLine($"{start:D4}: {op.Name}{extra}");
        }
    }
}

static Dictionary<short, OpCode> BuildOpcodeMap()
{
    var map = new Dictionary<short, OpCode>();
    foreach (FieldInfo fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
        if (fi.GetValue(null) is OpCode op)
            map[op.Value] = op;
    }
    return map;
}
