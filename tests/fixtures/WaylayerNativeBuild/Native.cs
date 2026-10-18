using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using System.Reflection.Emit;using HarmonyLib;
internal static class Native
{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 internal static MethodInfo Source,CorpseSource;internal static int Preserved,Changed,Inserted;private static string[] roots;
 private static Assembly Resolve(object sender,ResolveEventArgs args){foreach(string root in roots){string path=Path.Combine(root,new AssemblyName(args.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}return null;}
 private static Type Map(Type type)
 {
  if(type.IsByRef)return Map(type.GetElementType()).MakeByRefType();if(type.IsArray)return Map(type.GetElementType()).MakeArrayType();if(type.IsGenericParameter)return type;
  if(type.IsGenericType){Type def=type.GetGenericTypeDefinition();Type own=typeof(Native).Assembly.GetType(def.FullName,false)??def;return own.MakeGenericType(type.GetGenericArguments().Select(Map).ToArray());}
  Type boundary=typeof(Native).Assembly.GetType(type.FullName,false);if(boundary!=null)return boundary;
  string assembly=type.Assembly.GetName().Name;
  if(assembly.StartsWith("UnityEngine")||assembly.StartsWith("TeamSoda")||assembly.StartsWith("ItemStatsSystem")||assembly.StartsWith("EscapeFromDuckov")||assembly=="UniTask")throw new Exception("unmapped host boundary "+type);
  return type;
 }
 private static object Member(object value)
 {
  Type type=value as Type;if(type!=null)return Map(type);
  FieldInfo field=value as FieldInfo;if(field!=null){Type owner=Map(field.DeclaringType);return owner==field.DeclaringType?field:owner.GetField(field.Name,F)??throw new Exception("missing field boundary "+field);}
  ConstructorInfo ctor=value as ConstructorInfo;if(ctor!=null){Type owner=Map(ctor.DeclaringType);return owner==ctor.DeclaringType?ctor:owner.GetConstructors(F).Single(c=>c.IsStatic==ctor.IsStatic&&c.GetParameters().Select(p=>p.ParameterType).SequenceEqual(ctor.GetParameters().Select(p=>Map(p.ParameterType))));}
  MethodInfo method=value as MethodInfo;if(method==null)return value;Type mapped=Map(method.DeclaringType);if(mapped==method.DeclaringType){if(method.IsGenericMethod&&!method.IsGenericMethodDefinition)return method.GetGenericMethodDefinition().MakeGenericMethod(method.GetGenericArguments().Select(Map).ToArray());return method;}
  var choices=mapped.GetMethods(F).Where(m=>m.Name==method.Name&&m.GetParameters().Length==method.GetParameters().Length&&m.GetGenericArguments().Length==method.GetGenericArguments().Length).ToArray();
  if(choices.Length==0)throw new Exception("missing method boundary "+method);
  var matches=choices.Where(m=>m.ReturnType==Map(method.ReturnType)&&m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(method.GetParameters().Select(p=>Map(p.ParameterType)))).ToArray();
  MethodInfo found=choices.Length==1?choices[0]:matches.Length==1?matches[0]:throw new Exception("ambiguous boundary "+method+" @"+method.DeclaringType+" choices="+string.Join(";",choices.Select(c=>c.ToString())));
  return method.IsGenericMethod?found.MakeGenericMethod(method.GetGenericArguments().Select(Map).ToArray()):found;
 }
 private static IEnumerable<CodeInstruction> Body(IEnumerable<CodeInstruction> ignored,ILGenerator generator,MethodBase __originalMethod)
 {
  bool corpse=__originalMethod.DeclaringType==typeof(InteractableLootbox);MethodInfo source=corpse?CorpseSource:Source;
  var reader=new DynamicMethod("ReadNative",typeof(void),Type.EmptyTypes).GetILGenerator();
  var original=PatchProcessor.GetOriginalInstructions(source,reader).ToList();
  var locals=source.GetMethodBody().LocalVariables.Select(v=>generator.DeclareLocal(Map(v.LocalType))).ToArray();
  var mapped=original.Select(c=>{var value=new CodeInstruction(c);value.operand=Member(value.operand);return value;}).ToList();
  var method=typeof(BossRush.CoopWaylayerNativeBuildScope).GetMethod(corpse?"CorpseCalls":"BuildCalls",F);
  var rewritten=((IEnumerable<CodeInstruction>)method.Invoke(null,new object[]{mapped})).ToList();
  Program.Assert(rewritten.Count==mapped.Count+2,"exact two root-binding instructions inserted");
  int input=0;
  for(int output=0;output<rewritten.Count;output++)
  {
   if(output>0&&rewritten[output].opcode==OpCodes.Ldloc_0&&output+1<rewritten.Count&&(rewritten[output+1].operand as MethodInfo)?.Name=="BindRoot")
   {Inserted+=2;output++;continue;}
   if(corpse&&rewritten[output].opcode==OpCodes.Dup&&output+2<rewritten.Count&&(rewritten[output+2].operand as MethodInfo)?.Name=="BindCorpseBox")
   {Inserted++;continue;}
   if(corpse&&(rewritten[output].operand as MethodInfo)?.Name=="BindCorpseBox"){Inserted++;continue;}
   var before=mapped[input++];var after=rewritten[output];var call=after.operand as MethodInfo;
   if(call!=null&&(call.Name=="AddBuiltChild"||call.Name=="PlugBuiltChild"||call.Name=="AddCorpseChild"))
   {Program.Assert(before.operand is MethodInfo&&((MethodInfo)before.operand).Name==(call.Name=="AddBuiltChild"?"AddAt":call.Name=="AddCorpseChild"?"AddAndMerge":"Plug"),"exact original mutation call replaced");Changed++;}
   else{Program.Assert(before.opcode==after.opcode&&Equals(before.operand,after.operand),"all other complete native operands preserved");Preserved++;}
   Program.Assert(before.labels.SequenceEqual(after.labels)&&before.blocks.SequenceEqual(after.blocks),"native branches and exception regions preserved");
  }
  var labels=new Dictionary<Label,Label>();foreach(var value in rewritten)foreach(var label in value.labels)if(!labels.ContainsKey(label))labels.Add(label,generator.DefineLabel());
  foreach(var value in rewritten)
  {
   for(int label=0;label<value.labels.Count;label++)value.labels[label]=labels[value.labels[label]];
   if(value.operand is Label)value.operand=labels[(Label)value.operand];if(value.operand is Label[])value.operand=((Label[])value.operand).Select(l=>labels[l]).ToArray();
   int index=-1;if(value.opcode==OpCodes.Ldloc_0||value.opcode==OpCodes.Stloc_0)index=0;if(value.opcode==OpCodes.Ldloc_1||value.opcode==OpCodes.Stloc_1)index=1;if(value.opcode==OpCodes.Ldloc_2||value.opcode==OpCodes.Stloc_2)index=2;if(value.opcode==OpCodes.Ldloc_3||value.opcode==OpCodes.Stloc_3)index=3;
   if(value.opcode==OpCodes.Ldloc||value.opcode==OpCodes.Ldloc_S||value.opcode==OpCodes.Stloc||value.opcode==OpCodes.Stloc_S||value.opcode==OpCodes.Ldloca||value.opcode==OpCodes.Ldloca_S){var local=value.operand as LocalBuilder;index=local!=null?local.LocalIndex:Convert.ToInt32(value.operand);}
   if(index>=0){bool store=value.opcode.Name.StartsWith("stloc"),address=value.opcode.Name.StartsWith("ldloca");value.opcode=store?OpCodes.Stloc:address?OpCodes.Ldloca:OpCodes.Ldloc;value.operand=locals[index];}
   yield return value;
  }
 }
 internal static void SiteFailures()
 {
  foreach(bool corpse in new[]{false,true})
  {
   MethodInfo source=corpse?CorpseSource:Source;var reader=new DynamicMethod("InspectSites",typeof(void),Type.EmptyTypes).GetILGenerator();
   var original=PatchProcessor.GetOriginalInstructions(source,reader).Select(c=>{var copy=new CodeInstruction(c);copy.operand=Member(copy.operand);return copy;}).ToList();
   var method=typeof(BossRush.CoopWaylayerNativeBuildScope).GetMethod(corpse?"CorpseCalls":"BuildCalls",F);
   foreach(string site in corpse?new[]{"CreateLocalInventory","AddAndMerge"}:new[]{"AddAt","Plug","GetResult"})foreach(bool duplicate in new[]{false,true})
   {
    var changed=original.Select(c=>new CodeInstruction(c)).ToList();int index=changed.FindIndex(c=>(c.operand as MethodInfo)?.Name==site);Program.Assert(index>=0,"installed native unique site exists "+site);
    if(duplicate)
    {
     if(site=="GetResult"){changed.InsertRange(index+2,new[]{new CodeInstruction(changed[index]),new CodeInstruction(OpCodes.Stloc_0)});}else changed.Insert(index,new CodeInstruction(changed[index]));
    }
    else changed.RemoveAt(index);
    bool rejected=false;try{((IEnumerable<CodeInstruction>)method.Invoke(null,new object[]{changed})).ToList();}catch(TargetInvocationException error){rejected=error.InnerException is InvalidOperationException;}
    Program.Assert(rejected,(duplicate?"duplicate":"missing")+" native "+site+" fails closed");
   }
  }
 }
 internal static void Install(string[] paths)
 {
  roots=paths;AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
  var native=Assembly.LoadFrom(Path.Combine(paths[0],"EscapeFromDuckovCoopMod.dll"));Source=native.GetType("EscapeFromDuckovCoopMod.ItemTool",true).GetMethod("BuildItemFromSnapshot",F);
  Program.Assert(Source.ReturnType.FullName=="ItemStatsSystem.Item"&&Source.GetMethodBody().GetILAsByteArray().Length==688,"actual synchronous complete 688-byte installed method");
  var harmony=new Harmony("Test.WaylayerNativeBuild.FullNative");
  harmony.Patch(typeof(EscapeFromDuckovCoopMod.ItemTool).GetMethod("BuildItemFromSnapshot"),prefix:new HarmonyMethod(typeof(BossRush.CoopWaylayerNativeBuildScope),"BeforeBuild"),postfix:new HarmonyMethod(typeof(BossRush.CoopWaylayerNativeBuildScope),"AfterBuild"),transpiler:new HarmonyMethod(typeof(Native),"Body"),finalizer:new HarmonyMethod(typeof(BossRush.CoopWaylayerNativeBuildScope),"FinishBuild"));
  Console.WriteLine("FULL NATIVE BuildItemFromSnapshot 688 bytes; preserved="+Preserved+" changed="+Changed+" inserted="+Inserted);
  var core=Assembly.LoadFrom(Path.Combine(paths[1],"TeamSoda.Duckov.Core.dll"));CorpseSource=core.GetType("InteractableLootbox",true).GetMethod("CreateFromItem",F);
  Program.Assert(CorpseSource!=null,"installed official corpse builder found");
  File.WriteAllLines("official-corpse-il.txt",PatchProcessor.GetOriginalInstructions(CorpseSource).Select(c=>c.ToString()));
  harmony.Patch(typeof(InteractableLootbox).GetMethod("CreateFromItem"),prefix:new HarmonyMethod(typeof(BossRush.CoopWaylayerNativeBuildScope),"BeforeCorpse"),transpiler:new HarmonyMethod(typeof(Native),"Body"),finalizer:new HarmonyMethod(typeof(BossRush.CoopWaylayerNativeBuildScope),"FinishCorpse"));
  Console.WriteLine("FULL OFFICIAL CreateFromItem "+CorpseSource.GetMethodBody().GetILAsByteArray().Length+" bytes; all other native opcodes/operands preserved");
 }
}
