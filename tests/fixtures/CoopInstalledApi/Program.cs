using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Linq.Expressions;

internal static class Program
{
    private static string[] roots;
    private static readonly HashSet<string> resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static int assertions;
    private static void Check(bool condition,string label) { assertions++;if(!condition)throw new Exception(label); }
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    {
        string name=new AssemblyName(args.Name).Name;
        if(name.EndsWith(".resources",StringComparison.OrdinalIgnoreCase)||!resolving.Add(name))return null;
        try { foreach(string root in roots) { string path=Path.Combine(root,name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path); }return null; }
        finally { resolving.Remove(name); }
    }
    private static Type Require(Assembly assembly,string name) { Type value=assembly.GetType(name,true);Check(value!=null,name);return value; }
    private static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;CultureInfo.CurrentUICulture=CultureInfo.InvariantCulture;
        roots=args;AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
        Assembly main=Assembly.LoadFrom(Path.Combine(args[0],"EscapeFromDuckovCoopMod.dll"));
        Assembly api=Assembly.LoadFrom(Path.Combine(args[0],"EscapeFromDuckovModApi.dll"));
        Type network=Require(api,"EscapeFromDuckovCoopMod.ModNetworkApi");
        foreach(string name in new[]{"RegisterHandler","SendToServer","SendToClient","Broadcast"})
        { MethodInfo method=network.GetMethod(name,BindingFlags.Public|BindingFlags.Static);Check(method!=null,name);Console.WriteLine(method); }
        Type writer=network.GetMethod("SendToServer").GetParameters()[1].ParameterType.GetGenericArguments()[0];
        MethodInfo put=writer.GetMethod("Put",new[]{typeof(byte[])});
        Check(put!=null,"NetDataWriter.Put(byte[]) available");
        object buffer=Activator.CreateInstance(writer);
        byte[] payload={0x7B,0x22,0x41,0x22,0x7D};
        put.Invoke(buffer,new object[]{payload});
        byte[] raw=(byte[])writer.GetMethod("CopyData").Invoke(buffer,null);
        Check(BitConverter.ToString(raw)==BitConverter.ToString(payload),"writer byte[] emits raw payload without hidden length framing");
        MethodInfo buildPayload = network.GetMethod("BuildPayload", BindingFlags.NonPublic | BindingFlags.Static);
        Check(buildPayload != null && buildPayload.GetParameters().Length == 1, "installed native payload builder contract");
        int writes = 0;
        Action<object> write = value => { writes++; put.Invoke(value, new object[] { payload }); };
        var argument = Expression.Parameter(writer, "writer");
        var builder = Expression.Lambda(buildPayload.GetParameters()[0].ParameterType,
            Expression.Invoke(Expression.Constant(write), Expression.Convert(argument, typeof(object))), argument).Compile();
        byte[] nativePayload = (byte[])buildPayload.Invoke(null, new object[] { builder });
        Check(writes == 1 && BitConverter.ToString(nativePayload) == BitConverter.ToString(payload),
            "actual installed BuildPayload executes callback synchronously once before returning exact bytes");
        Type context=Require(api,"EscapeFromDuckovCoopMod.ModMessageContext");
        PropertyInfo memory=context.GetProperty("Payload");
        Check(memory!=null,"payload property");
        MethodInfo toArray=memory.PropertyType.GetMethod("ToArray",Type.EmptyTypes);
        Check(toArray!=null&&toArray.ReturnType==typeof(byte[]),"actual ReadOnlyMemory<byte>.ToArray reflection method");
        ConstructorInfo ctor=context.GetConstructors()[0];
        object message=ctor.Invoke(new object[]{null,null,"BossRush.Cooperative.v1",payload});
        byte[] roundtrip=(byte[])toArray.Invoke(memory.GetValue(message,null),null);
        Check(BitConverter.ToString(roundtrip)==BitConverter.ToString(payload),"payload copy preserves raw writer bytes");
        Type events=Require(api,"EscapeFromDuckovCoopMod.ModApiEvents");
        foreach(string name in new[]{"PlayerSpawned","AiSpawned","ServerRegisterAI","ItemSnapshotCustomDataRequested","ItemSnapshotCustomDataApplied"})
            Check(events.GetEvent(name,BindingFlags.Public|BindingFlags.Static)!=null,name);
        Type service=Require(main,"EscapeFromDuckovCoopMod.NetService");
        Check(service.GetMethod("ResolvePeerDisplayName", BindingFlags.Public|BindingFlags.Instance) != null, "native display name lookup is public");
        Type chatType = Require(main, "EscapeFromDuckovCoopMod.ChatMessageRpc");
        Type coopTool = Require(main, "EscapeFromDuckovCoopMod.CoopTool");
        MethodInfo sendChat = coopTool.GetMethod("SendRpc", BindingFlags.Public|BindingFlags.Static).MakeGenericMethod(chatType);
        Check(sendChat.GetParameters().Length == 2 && sendChat.GetParameters()[0].ParameterType == chatType.MakeByRefType(),
            "installed native chat uses existing by-reference RPC broadcast contract");
        object chatMessage = Activator.CreateInstance(chatType);
        foreach (string field in new[] { "SenderId", "SenderName", "Content" }) chatType.GetField(field).SetValue(chatMessage, "BossRush compatibility");
        object chatWriter = Activator.CreateInstance(writer);
        chatType.GetMethod("Serialize").Invoke(chatMessage, new[] { chatWriter });
        byte[] chatBytes = (byte[])writer.GetMethod("CopyData").Invoke(chatWriter, null);
        Check(BitConverter.ToString(chatBytes).Contains(BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes("BossRush compatibility"))),
            "actual installed native chat serializer preserves compatibility text");
        foreach(string name in new[]{"GetPlayerId","GetSelfNetworkId"})Check(service.GetMethod(name,BindingFlags.Public|BindingFlags.Instance)!=null,name);
        foreach(string name in new[]{"connectedPeer","remoteCharacters","clientRemoteCharacters","netManager"})Check(service.GetField(name,BindingFlags.Public|BindingFlags.Instance)!=null,name);
        Type netManager=service.GetField("netManager",BindingFlags.Public|BindingFlags.Instance).FieldType;
        PropertyInfo connectedPeers=netManager.GetProperty("ConnectedPeerList",BindingFlags.Public|BindingFlags.Instance);
        Check(connectedPeers!=null&&typeof(System.Collections.IEnumerable).IsAssignableFrom(connectedPeers.PropertyType),"native connected peer list supports event-independent disconnect reconciliation");
        Type database=Require(main,"EscapeFromDuckovCoopMod.CoopSyncDatabase");
        foreach(string name in new[]{"AI","Loot"})Check(database.GetProperty(name,BindingFlags.Public|BindingFlags.Static)!=null,name);
        Type loot=database.GetProperty("Loot").PropertyType;
        Check(loot.GetMethod("Register").GetParameters().Length==3,"loot registration has world box/inventory/uid");
        Check(loot.GetMethod("Unregister",new[]{loot.GetMethod("Register").GetParameters()[0].ParameterType})!=null,"loot unregister accepts actual world box");
        Type gate=Require(main,"EscapeFromDuckovCoopMod.LootSearchWorldGate");
        Check(gate.GetMethod("EnsureWorldFlag",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!=null,"world inventory flag API");
        Type manager=Require(main,"EscapeFromDuckovCoopMod.COOPManager");
        Check(manager.GetField("AI",BindingFlags.Public|BindingFlags.Static)!=null,"AI native owner");
        Type aiRegistry=database.GetProperty("AI").PropertyType;
        Check(aiRegistry.GetProperty("Entries")!=null,"AI registered entry enumeration");
        foreach(string path in new[]{main.Location,api.Location})
            using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))Console.WriteLine(Path.GetFileName(path)+" SHA256 "+BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""));
        Console.WriteLine("CoopInstalledApi: PASS "+assertions+" read-only installed DLL contract assertions; no game process.");
    }
}
