using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Reads installed DLL metadata/IL only. No Unity assembly is loaded or executed by this check.
internal static class OfficialAssemblyContract
{
    private static readonly Dictionary<ushort, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public)
        .Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(c=>unchecked((ushort)c.Value));
    private static string Name(MetadataReader r, EntityHandle h)
    {
        if(h.Kind==HandleKind.MethodSpecification)return Name(r,r.GetMethodSpecification((MethodSpecificationHandle)h).Method);
        if(h.Kind==HandleKind.MemberReference){var m=r.GetMemberReference((MemberReferenceHandle)h);return Name(r,m.Parent)+"."+r.GetString(m.Name);}
        if(h.Kind==HandleKind.MethodDefinition){var m=r.GetMethodDefinition((MethodDefinitionHandle)h);return Name(r,m.GetDeclaringType())+"."+r.GetString(m.Name);}
        if(h.Kind==HandleKind.FieldDefinition)return r.GetString(r.GetFieldDefinition((FieldDefinitionHandle)h).Name);
        if(h.Kind==HandleKind.TypeReference)return r.GetString(r.GetTypeReference((TypeReferenceHandle)h).Name);
        if(h.Kind==HandleKind.TypeDefinition)return r.GetString(r.GetTypeDefinition((TypeDefinitionHandle)h).Name);
        return h.Kind.ToString();
    }
    private static TypeDefinition Type(MetadataReader r,string name) {return r.GetTypeDefinition(r.TypeDefinitions.Single(h=>r.GetString(r.GetTypeDefinition(h).Name)==name));}
    private sealed class Instruction
    {
        internal int Offset, BranchTarget;
        internal OpCode Op;
        internal string Reference;
    }
    private static List<Instruction> Instructions(PEReader pe, MetadataReader r,string type,string method)
    {
        var m=r.GetMethodDefinition(Type(r,type).GetMethods().Single(h=>r.GetString(r.GetMethodDefinition(h).Name)==method));
        byte[] il=pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes();var result=new List<Instruction>();
        for(int i=0;i<il.Length;)
        {
            var instruction=new Instruction { Offset=i, BranchTarget=-1 };
            ushort code=il[i++];if(code==0xfe)code=(ushort)(0xfe00|il[i++]);OpCode op=Codes[code];int size;
            switch(op.OperandType){case OperandType.InlineNone:size=0;break;case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:size=1;break;case OperandType.InlineVar:size=2;break;case OperandType.InlineI8:case OperandType.InlineR:size=8;break;case OperandType.InlineSwitch:size=4+BitConverter.ToInt32(il,i)*4;break;default:size=4;break;}
            instruction.Op=op;
            if(op.OperandType==OperandType.InlineMethod||op.OperandType==OperandType.InlineField)instruction.Reference=Name(r,MetadataTokens.EntityHandle(BitConverter.ToInt32(il,i)));
            if(op.OperandType==OperandType.ShortInlineBrTarget)instruction.BranchTarget=i+size+unchecked((sbyte)il[i]);
            if(op.OperandType==OperandType.InlineBrTarget)instruction.BranchTarget=i+size+BitConverter.ToInt32(il,i);
            result.Add(instruction);
            i+=size;
        }
        return result;
    }
    private static List<string> References(PEReader pe, MetadataReader r,string type,string method)
    {
        var refs=Instructions(pe,r,type,method).Where(i=>i.Reference!=null).Select(i=>i.Reference).ToList();
        Console.WriteLine(type+"."+method+": "+string.Join(" -> ",refs));return refs;
    }
    private static void Require(bool ok,string label){if(!ok)throw new Exception("DLL contract: "+label);}
    private static void Ordered(List<string> refs,params string[] names)
    {
        int from=0;foreach(string name in names){int at=refs.FindIndex(from,s=>s==name);Require(at>=0,"ordered "+name);from=at+1;}
    }
    internal static void Run(string managed)
    {
        using(var stream=File.OpenRead(Path.Combine(managed,"TeamSoda.Duckov.Core.dll")))using(var pe=new PEReader(stream))
        {
            var r=pe.GetMetadataReader();Console.WriteLine("Official Core MVID="+r.GetGuid(r.GetModuleDefinition().Mvid));
            var harvest=References(pe,r,"Crop","Harvest");Ordered(harvest,"Cost.Return","UniTaskExtensions.Forget","Crop.DestroyCrop");Require(harvest.Count(s=>s=="Cost.Return")==1,"one harvest delivery");
            string returnState=r.GetString(r.GetTypeDefinition(Type(r,"Cost").GetNestedTypes().Single(h=>r.GetString(r.GetTypeDefinition(h).Name).StartsWith("<Return>d__",StringComparison.Ordinal))).Name);
            var delivery=References(pe,r,returnState,"MoveNext");
            Ordered(delivery,"ItemAssetsCollection.InstantiateAsync","Item.get_MaxStackCount","toPlayerInventory",
                "ItemUtilities.SendToPlayerCharacterInventory","ItemUtilities.SendToPlayerStorage");
            // Inventory API returns success; Cost.Return owns the fallback, not the inventory API.
            var route=Instructions(pe,r,returnState,"MoveNext");
            int send=route.FindIndex(i=>i.Reference=="ItemUtilities.SendToPlayerCharacterInventory");
            int storage=route.FindIndex(i=>i.Reference=="ItemUtilities.SendToPlayerStorage");
            var branch=route[send+1];
            Require((branch.Op==OpCodes.Brtrue||branch.Op==OpCodes.Brtrue_S)
                && branch.BranchTarget>route[storage].Offset,"successful backpack delivery skips storage");
            Require(route.Skip(send+2).Take(storage-send-2).All(i=>i.Op.FlowControl!=FlowControl.Branch
                && i.Op.FlowControl!=FlowControl.Cond_Branch),"failed backpack delivery falls through to storage");
            Console.WriteLine("Cost.Return inventory failure falls through to storage; success branches to IL_"+branch.BranchTarget.ToString("x4"));
            Require(References(pe,r,"ItemUtilities","SendToPlayerCharacterInventory").Contains("ItemUtilities.AddAndMerge"),"inventory route uses official merge");
            Ordered(References(pe,r,"ItemUtilities","SendToPlayerStorage"),"Item.Detach","PlayerStorage.Push");
            Ordered(References(pe,r,"PlayerStorage","Push"),"Item.Combine","Inventory.GetFirstEmptyPosition",
                "Inventory.AddAt","PlayerStorage.get_IncomingItemBuffer","ItemTreeData.FromItem","ItemTreeExtensions.DestroyTree");
            var use=References(pe,r,"CA_UseItem","OnFinish");Ordered(use,"Item.Use","Item.get_StackCount","Item.set_StackCount");
            Require(Type(r,"CA_UseItem").GetFields().Any(h=>r.GetString(r.GetFieldDefinition(h).Name)=="item"),"fruit prefix injected item field exists");
            Require(Type(r,"CharacterActionBase").GetFields().Any(h=>r.GetString(r.GetFieldDefinition(h).Name)=="characterController"),"fruit prefix character owner exists");
            References(pe,r,"CA_Attack","OnStart");References(pe,r,"CharacterMainControl","Attack");
            References(pe,r,"CharacterMainControl","SetCharacterModel");
            Ordered(References(pe,r,"Quest","TryComplete"),"Quest.get_Complete","Quest.AreTasksFinished","Quest.set_Complete");
            References(pe,r,"QuestManager","ActivateQuest");References(pe,r,"QuestManager","GenerateSaveData");
            foreach(string type in new[]{"Quest","Task","Reward"})
            {
                var fields=Type(r,type).GetFields().Select(h=>r.GetString(r.GetFieldDefinition(h).Name)).ToArray();
                foreach(string name in type=="Quest"?new[]{"id","questGiverID","requiredItemID","requiredItemCount","rewards"}:new[]{"id","master"})Require(fields.Contains(name),type+"."+name);
            }
        }
        using(var stream=File.OpenRead(Path.Combine(managed,"ItemStatsSystem.dll")))using(var pe=new PEReader(stream))
        {
            var r=pe.GetMetadataReader();Ordered(References(pe,r,"UsageUtilities","Use"),"UsageBehavior.CanBeUsed","UsageBehavior.Use");
        }
        Console.WriteLine("Official quest/garden/use DLL contracts: PASS");
    }
}
