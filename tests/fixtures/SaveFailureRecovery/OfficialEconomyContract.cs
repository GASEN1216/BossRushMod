using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// 只读现装 DLL 的方法 IL，不加载或执行 Unity 类型，也不接触存档。
internal static class OfficialEconomyContract
{
    private sealed class Instruction { internal OpCode Op; internal string Name; }
    private static readonly Dictionary<ushort, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
        .ToDictionary(c => unchecked((ushort)c.Value));

    private static string MethodName(MetadataReader reader, int token)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.MethodDefinition)
            return reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name);
        if (handle.Kind == HandleKind.MemberReference)
            return reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name);
        return string.Empty;
    }

    private static List<Instruction> Read(PEReader pe, MetadataReader reader, TypeDefinition type, string name)
    {
        MethodDefinition method = reader.GetMethodDefinition(type.GetMethods().Single(h =>
        {
            MethodDefinition candidate = reader.GetMethodDefinition(h);
            return reader.GetString(candidate.Name) == name
                && (candidate.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
        }));
        byte[] bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
        var result = new List<Instruction>();
        for (int i = 0; i < bytes.Length;)
        {
            ushort value = bytes[i++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[i++]);
            OpCode op = Codes[value];
            int count;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: count = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: count = 1; break;
                case OperandType.InlineVar: count = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: count = 8; break;
                case OperandType.InlineSwitch: count = 4 + BitConverter.ToInt32(bytes, i) * 4; break;
                default: count = 4; break;
            }
            result.Add(new Instruction { Op = op, Name = op.OperandType == OperandType.InlineMethod
                ? MethodName(reader, BitConverter.ToInt32(bytes, i)) : null });
            i += count;
        }
        return result;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("Official EconomyManager contract: " + message);
    }

    internal static void Verify(string managed)
    {
        using (var stream = File.OpenRead(Path.Combine(managed, "TeamSoda.Duckov.Core.dll")))
        using (var pe = new PEReader(stream))
        {
            MetadataReader reader = pe.GetMetadataReader();
            TypeDefinition type = reader.GetTypeDefinition(reader.TypeDefinitions.Single(h =>
                reader.GetString(reader.GetTypeDefinition(h).Name) == "EconomyManager"));
            var pay = Read(pe, reader, type, "Pay");
            int check = pay.FindIndex(i => i.Name == "IsEnough");
            Check(check == 3 && pay[0].Op == OpCodes.Ldarg_0 && pay[1].Op == OpCodes.Ldarg_1
                && pay[2].Op == OpCodes.Ldc_I4_1, "Pay(Cost) forces cash inclusion in its affordability precheck");
            var enough = Read(pe, reader, type, "IsEnough");
            int cash = enough.FindIndex(i => i.Name == "get_Cash");
            int sum = enough.FindIndex(cash + 1, i => i.Op == OpCodes.Add);
            Check(cash >= 0 && sum > cash && !enough.Any(i => i.Op == OpCodes.Add_Ovf || i.Op == OpCodes.Add_Ovf_Un),
                "IsEnough combines account and pocket cash using unchecked addition");
            var add = Read(pe, reader, type, "Add");
            int balance = add.FindIndex(i => i.Name == "get_Money");
            Check(balance >= 0 && add[balance + 1].Op == OpCodes.Ldarg_0 && add[balance + 2].Op == OpCodes.Add
                && add[balance + 3].Name == "set_Money", "Add passes signed amounts to the official Money setter");
            Console.WriteLine("Official EconomyManager Pay / IsEnough / signed Add IL: PASS; MVID="
                + reader.GetGuid(reader.GetModuleDefinition().Mvid));
        }
    }
}
