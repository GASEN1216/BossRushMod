using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using System.Reflection.Emit;using HarmonyLib;
internal static class Native
{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 internal static int Preserved;private static string[] roots;private static readonly Dictionary<MethodBase,MethodInfo> methods=new Dictionary<MethodBase,MethodInfo>();
 private static Assembly Resolve(object sender,ResolveEventArgs args){foreach(string root in roots){string path=Path.Combine(root,new AssemblyName(args.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}return null;}
 private static Type Map(Type type)
 {
  if(type.IsByRef)return Map(type.GetElementType()).MakeByRefType();if(type.IsArray)return Map(type.GetElementType()).MakeArrayType();if(type.IsGenericParameter)return type;
  if(type.IsGenericType){Type def=type.GetGenericTypeDefinition();Type own=typeof(Native).Assembly.GetType(def.FullName,false)??def;return own.MakeGenericType(type.GetGenericArguments().Select(Map).ToArray());}
  Type boundary=typeof(Native).Assembly.GetType(type.FullName,false);if(boundary!=null)return boundary;
  string assembly=type.Assembly.GetName().Name;
  if(assembly.StartsWith("UnityEngine")||assembly.StartsWith("TeamSoda")||assembly.StartsWith("ItemStatsSystem")||assembly.StartsWith("EscapeFromDuckov")||assembly=="UniTask"||assembly=="SodaLocalization")throw new Exception("unmapped host boundary "+type);
  return type;
 }
 private static object Member(object value)
 {
  Type type=value as Type;if(type!=null)return Map(type);
  FieldInfo field=value as FieldInfo;if(field!=null){Type owner=Map(field.DeclaringType);if(owner==field.DeclaringType)return field;var mappedField=owner.GetField(field.Name,F)??throw new Exception("missing field boundary "+field);if(mappedField.FieldType!=Map(field.FieldType))throw new Exception("field signature mismatch "+field+" actual="+mappedField.FieldType);return mappedField;}
  ConstructorInfo ctor=value as ConstructorInfo;if(ctor!=null){Type owner=Map(ctor.DeclaringType);return owner==ctor.DeclaringType?ctor:owner.GetConstructors(F).Single(c=>c.IsStatic==ctor.IsStatic&&c.GetParameters().Select(p=>p.ParameterType).SequenceEqual(ctor.GetParameters().Select(p=>Map(p.ParameterType))));}
  MethodInfo method=value as MethodInfo;if(method==null)return value;Type mapped=Map(method.DeclaringType);if(mapped==method.DeclaringType){if(method.IsGenericMethod&&!method.IsGenericMethodDefinition)return method.GetGenericMethodDefinition().MakeGenericMethod(method.GetGenericArguments().Select(Map).ToArray());return method;}
  var choices=mapped.GetMethods(F).Where(m=>m.Name==method.Name&&m.GetParameters().Length==method.GetParameters().Length&&m.GetGenericArguments().Length==method.GetGenericArguments().Length).ToArray();
  if(choices.Length==0)throw new Exception("missing method boundary "+method+" @"+method.DeclaringType);
  var matches=choices.Where(m=>m.ReturnType==Map(method.ReturnType)&&m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(method.GetParameters().Select(p=>Map(p.ParameterType)))).ToArray();
  MethodInfo found=matches.Length==1?matches[0]:throw new Exception("signature mismatch boundary "+method+" @"+method.DeclaringType+" choices="+string.Join(";",choices.Select(c=>c.ToString())));
  return method.IsGenericMethod?found.MakeGenericMethod(method.GetGenericArguments().Select(Map).ToArray()):found;
 }
 private static IEnumerable<CodeInstruction> Body(IEnumerable<CodeInstruction> ignored,ILGenerator generator,MethodBase __originalMethod){
  MethodInfo source=methods[__originalMethod];var reader=new DynamicMethod("ReadOfficialUse",typeof(void),Type.EmptyTypes).GetILGenerator();var original=PatchProcessor.GetOriginalInstructions(source,reader).ToList();
  var locals=source.GetMethodBody().LocalVariables.Select(v=>generator.DeclareLocal(Map(v.LocalType))).ToArray();
  var mapped=original.Select(c=>{var x=new CodeInstruction(c);x.operand=Member(c.operand);Program.Check(x.opcode==c.opcode&&x.labels.SequenceEqual(c.labels)&&x.blocks.SequenceEqual(c.blocks),"actual full method opcode/branch/EH preserved");Preserved++;return x;}).ToList();
  var labels=new Dictionary<Label,Label>();foreach(var x in mapped)foreach(var label in x.labels)if(!labels.ContainsKey(label))labels.Add(label,generator.DefineLabel());
  foreach(var x in mapped){for(int n=0;n<x.labels.Count;n++)x.labels[n]=labels[x.labels[n]];if(x.operand is Label)x.operand=labels[(Label)x.operand];if(x.operand is Label[])x.operand=((Label[])x.operand).Select(l=>labels[l]).ToArray();
   int index=-1;if(x.opcode==OpCodes.Ldloc_0||x.opcode==OpCodes.Stloc_0)index=0;if(x.opcode==OpCodes.Ldloc_1||x.opcode==OpCodes.Stloc_1)index=1;if(x.opcode==OpCodes.Ldloc_2||x.opcode==OpCodes.Stloc_2)index=2;if(x.opcode==OpCodes.Ldloc_3||x.opcode==OpCodes.Stloc_3)index=3;
   if(x.opcode==OpCodes.Ldloc||x.opcode==OpCodes.Ldloc_S||x.opcode==OpCodes.Stloc||x.opcode==OpCodes.Stloc_S||x.opcode==OpCodes.Ldloca||x.opcode==OpCodes.Ldloca_S){var l=x.operand as LocalBuilder;index=l!=null?l.LocalIndex:Convert.ToInt32(x.operand);}
   if(index>=0){bool store=x.opcode.Name.StartsWith("stloc"),address=x.opcode.Name.StartsWith("ldloca");x.opcode=store?OpCodes.Stloc:address?OpCodes.Ldloca:OpCodes.Ldloc;x.operand=locals[index];}yield return x;
  }
 }
 internal static void Install(string managed){
  roots=new[]{managed};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;var item=Assembly.LoadFrom(Path.Combine(managed,"ItemStatsSystem.dll"));var core=Assembly.LoadFrom(Path.Combine(managed,"TeamSoda.Duckov.Core.dll"));
  var h=new Harmony("BossRush.Tests.WaylayerClaimUse.FullOfficial");
  foreach(var row in new[]{"ItemStatsSystem.Item|Use","ItemStatsSystem.UsageUtilities|IsUsable","ItemStatsSystem.UsageUtilities|Use","ItemStatsSystem.UsageBehavior|Use","CA_UseItem|OnFinish","CA_UseItem|OnStart","CA_UseItem|OnUpdateAction","CA_UseItem|OnStop","CharacterMainControl|UseItem","Duckov.ItemUsage.Drug|CanBeUsed","Duckov.ItemUsage.Drug|OnUse","Duckov.ItemUsage.Drug|CheckCanHeal","Duckov.ItemUsage.Drug|Heal","Duckov.ItemUsage.FoodDrink|CanBeUsed","Duckov.ItemUsage.FoodDrink|OnUse","Duckov.ItemUsage.FoodDrink|Eat"}){
   var parts=row.Split('|');Type sourceType=item.GetType(parts[0],false)??core.GetType(parts[0],true);var method=sourceType.GetMethod(parts[1],F);var dest=(MethodInfo)Member(method);methods.Add(dest,method);
   h.Patch(dest,transpiler:new HarmonyMethod(typeof(Native),"Body"));Console.WriteLine("FULL OFFICIAL "+method+" bytes="+method.GetMethodBody().GetILAsByteArray().Length);
  }
 }
}
