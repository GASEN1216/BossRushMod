using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BossRush;
using HarmonyLib;
using ItemStatsSystem;
internal static class NativeLootProgram
{
    private static int checks;
    private static string[] roots;
    private static MethodInfo register,open;
    private static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    {foreach(string root in roots){string path=Path.Combine(root,new AssemblyName(args.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}return null;}
    private static Type Map(Type type)
    {
        if(type.IsByRef)return Map(type.GetElementType()).MakeByRefType();
        if(type.IsGenericType)
        {var arguments=Array.ConvertAll(type.GetGenericArguments(),Map);return type.GetGenericTypeDefinition().MakeGenericType(arguments);}
        if(type.FullName=="UnityEngine.Component")return typeof(UnityEngine.MonoBehaviour);
        Type local=typeof(NativeLootProgram).Assembly.GetType(type.FullName,false)??typeof(NativeLootProgram).Assembly.GetType(type.Name,false);
        if(local==null&&type.Name=="LootBoxLoader")local=typeof(LootBoxLoader);
        return local??type;
    }
    private static IEnumerable<CodeInstruction> Body(IEnumerable<CodeInstruction> ignored,MethodBase __originalMethod,ILGenerator generator)
    {
        MethodInfo source=__originalMethod.DeclaringType==typeof(NativeLoaderBoundary)?register:open;
        int nativeLocalBase=generator.DeclareLocal(typeof(int)).LocalIndex+1;
        var original=PatchProcessor.GetOriginalInstructions(source,generator);
        var locals=new List<LocalBuilder>();foreach(var local in source.GetMethodBody().LocalVariables)locals.Add(generator.DeclareLocal(Map(local.LocalType)));
        foreach(var instruction in original)
        {
            int index=-1;bool store=false,address=false;
            if(instruction.opcode==OpCodes.Ldloc_0||instruction.opcode==OpCodes.Stloc_0)index=0;
            if(instruction.opcode==OpCodes.Ldloc_1||instruction.opcode==OpCodes.Stloc_1)index=1;
            if(instruction.opcode==OpCodes.Ldloc_2||instruction.opcode==OpCodes.Stloc_2)index=2;
            if(instruction.opcode==OpCodes.Ldloc_3||instruction.opcode==OpCodes.Stloc_3)index=3;
            if(instruction.opcode==OpCodes.Ldloc||instruction.opcode==OpCodes.Ldloc_S||instruction.opcode==OpCodes.Stloc||instruction.opcode==OpCodes.Stloc_S||instruction.opcode==OpCodes.Ldloca||instruction.opcode==OpCodes.Ldloca_S)
                index=instruction.operand is LocalBuilder?((LocalBuilder)instruction.operand).LocalIndex-nativeLocalBase:Convert.ToInt32(instruction.operand);
            if(index>=0)
            {store=instruction.opcode.Name.StartsWith("stloc");address=instruction.opcode.Name.StartsWith("ldloca");instruction.opcode=store?OpCodes.Stloc:address?OpCodes.Ldloca:OpCodes.Ldloc;instruction.operand=locals[index];}
            var method=instruction.operand as MethodInfo;var field=instruction.operand as FieldInfo;
            if(method!=null)
            {
                Type owner=Map(method.DeclaringType);
                if(owner!=method.DeclaringType)
                {
                    var candidates=owner.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);
                    MethodInfo mapped=null;
                    foreach(var candidate in candidates)if(candidate.Name==method.Name&&candidate.GetParameters().Length==method.GetParameters().Length){mapped=candidate;break;}
                    if(mapped==null)throw new Exception("unmapped native call: "+method);
                    if(method.IsGenericMethod)mapped=mapped.MakeGenericMethod(Array.ConvertAll(method.GetGenericArguments(),Map));
                    instruction.operand=mapped;
                }
            }
            else if(field!=null&&Map(field.DeclaringType)!=field.DeclaringType)
            {instruction.operand=Map(field.DeclaringType).GetField(field.Name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);if(instruction.operand==null)throw new Exception("unmapped native field: "+field);}
            yield return instruction;
        }
    }
    private static void Main(string[] args)
    {
        roots=args;AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
        try { Run(args); }
        catch(Exception error) {Console.WriteLine("FAIL NativeLoot: "+error.GetType().Name+": "+error.Message);Environment.ExitCode=1;}
    }
    private static void Run(string[] args)
    {
        var personal=new InteractableLootbox();var ordinary=new InteractableLootbox();
        Check(CoopArenaPersonalLoot.Attach(personal),"real marker installs its narrow native hooks before personal crate setup");
        Check(personal.gameObject.Persistent&&!personal.IsInteractable(),"partial personal crate holds its original inventory across scene unload and cannot be opened");
        Check(CoopArenaPersonalLoot.TryPublish(personal)&&!personal.gameObject.Persistent&&personal.IsInteractable(),"only completed owner publication restores normal interactive scene lifetime");
        Check(LootboxDetectUtil.IsPrivateInventory(personal.Inventory)&&!LootboxDetectUtil.IsPrivateInventory(ordinary.Inventory),"only the receipt-created marked inventory changes native privacy");
        Assembly native=Assembly.LoadFrom(Path.Combine(args[0],"EscapeFromDuckovCoopMod.dll"));
        register=Find(native,"LootBoxLoaderRegisterUtil").GetMethod("TryRegister",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        open=Find(native,"LootViewPatch").GetMethod("Postfix",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        foreach(var instruction in PatchProcessor.GetOriginalInstructions(register))
            if(instruction.operand is MemberInfo) Console.WriteLine("Native signature: "+instruction.operand);
        Check(register.GetMethodBody().GetILAsByteArray().Length==264&&open.GetMethodBody().GetILAsByteArray().Length==91,"installed complete Loader registration and loot-open method IL identities are unchanged");
        var il=new Harmony("BossRush.Tests.NativePersonalLoot");
        il.Patch(typeof(NativeLoaderBoundary).GetMethod("Register"),transpiler:new HarmonyMethod(typeof(NativeLootProgram),"Body"));
        il.Patch(typeof(NativeOpenBoundary).GetMethod("Open"),transpiler:new HarmonyMethod(typeof(NativeLootProgram),"Body"));
        var first=personal.gameObject.AddComponent<LootBoxLoader>();var second=ordinary.gameObject.AddComponent<LootBoxLoader>();
        NativeLoaderBoundary.Register(first);NativeOpenBoundary.Open(personal);
        Check(CoopSyncDatabase.Loot.Registers==0&&LootSearchWorldGate.Flags==0&&COOPManager.LootNet.Requests==0,"installed native Loader and open paths cannot register or replace a genuine personal reward inventory");
        NativeLoaderBoundary.Register(second);NativeOpenBoundary.Open(ordinary);
        Check(CoopSyncDatabase.Loot.Registers==1&&LootSearchWorldGate.Flags==1&&COOPManager.LootNet.Requests==1,"ordinary box keeps original native shared registration and loot state request");
        BossRushCoop.LocalAssetId="slot-B";
        Check(!personal.IsInteractable()&&ordinary.IsInteractable(),"replacement slot cannot open the original private crate");
        Check(LootboxDetectUtil.IsPrivateInventory(personal.Inventory),"switching slot cannot downgrade original private inventory into native shared loot");
        var marker=personal.Inventory.GetComponent<CoopArenaPersonalLoot>();
        var tick=typeof(CoopArenaPersonalLoot).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance);
        Duckov.UI.LootView.Instance=new Duckov.UI.LootView {TargetInventory=personal.Inventory};tick.Invoke(marker,null);
        Check(personal.gameObject.activeSelf&&!personal.GetComponent<UnityEngine.Renderer>().enabled&&!personal.GetComponent<UnityEngine.Collider>().enabled&&Duckov.UI.LootView.Instance==null,"wrong asset closes only its own inventory and hides physical interaction while preserving live owner component");
        BossRushCoop.LocalAssetId="slot-A";Check(personal.IsInteractable(),"return to the exact original asset reuses the full original box");
        tick.Invoke(marker,null);Check(personal.GetComponent<UnityEngine.Renderer>().enabled&&personal.GetComponent<UnityEngine.Collider>().enabled,"original asset restores existing renderers and colliders without replacing inventory");
        BossRushCoop.RunId="room-B";Check(!personal.IsInteractable(),"another room cannot claim an old marked personal box");
        CoopArenaPersonalLoot.Shutdown();Check(LootboxDetectUtil.IsPrivateInventory(personal.Inventory),"runtime shutdown preserves privacy while an original box still exists");
        UnityEngine.Object.Destroy(personal.gameObject);
        Check(marker==null&&(int)typeof(CoopArenaPersonalLoot).GetField("markers",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)==0,"mandatory Unity gameobject destruction releases its attached marker and static hook owner");
        var visual=new UnityEngine.GameObject().AddComponent<VictoryRewardShadowCrateController>();
        var owner=new ModBehaviour();bool originalOwner=true;visual.TestSetup(owner,()=>originalOwner);visual.TestTick();
        Check(visual!=null&&owner.Disposed==0,"real visual update keeps original active asset without cancellation");
        originalOwner=false;visual.TestTick();Check(visual==null&&owner.Disposed==1,"real owner loss destroys disposed visual so original asset can retry rather than retain an immortal cancelled reference");
        BossRushCoop.RunId="room-A";
        var held=new InteractableLootbox();held.transform.position=new UnityEngine.Vector3(-100,50,-200);
        var originalInventory=held.Inventory;Check(CoopArenaPersonalLoot.Attach(held),"actual original pending box captured before mandatory new scene");
        UnityEngine.SceneManagement.SceneManager.Handle++;CharacterMainControl.Main.transform.position=new UnityEngine.Vector3(1000,10,2000);
        LevelManager.AfterInit=false;
        Check(!CoopArenaPersonalLoot.TryPublish(held)&&held.gameObject.Persistent&&!held.IsInteractable(),"cross-scene reward cannot ACK while the new official player is not initialized");
        LevelManager.AfterInit=true;UnityEngine.Physics.Ground=false;
        Check(!CoopArenaPersonalLoot.TryPublish(held)&&held.Inventory==originalInventory,"missing ground retains the same original inventory debt without publishing at stale coordinates");
        UnityEngine.Physics.Ground=true;
        Check(CoopArenaPersonalLoot.TryPublish(held)&&held.transform.position.x==1000&&held.transform.position.z==2000&&Math.Abs(held.transform.position.y+1.85f)<.001f&&held.Inventory==originalInventory,
            "same-build-index new scene relocates the same complete inventory to the initialized owner actual ground before ACK");
        UnityEngine.Object.Destroy(held.gameObject);CoopArenaPersonalLoot.Shutdown();
        Console.WriteLine("PASS NativeLoot: "+checks+" production marker and complete installed registration/open IL assertions; Unity inventory/UI boundaries only");
        il.UnpatchAll(il.Id);
    }
    private static Type Find(Assembly assembly,string name)
    {Type[] types;try{types=assembly.GetTypes();}catch(ReflectionTypeLoadException error){types=error.Types;}foreach(Type type in types)if(type!=null&&type.Name==name)return type;throw new Exception("native type missing: "+name);}
}
