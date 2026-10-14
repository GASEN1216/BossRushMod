using System;
using System.Collections.Generic;
using BossRush;
namespace UnityEngine { public class SerializeField:Attribute{} }
class Program
{
 static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 [Serializable] class Record {public string name;public List<Variable> vars;public int[] slots;}
 [Serializable] class Variable { [UnityEngine.SerializeField] private string key;[UnityEngine.SerializeField] private byte[] data;public Variable(string k,byte[] b){key=k;data=b;}public string Key{get{return key;}}public byte[] Data{get{return data;}} }
 [Serializable] class Packet {public Record[] records;public long revision;public ulong seed;public string text;public bool accepted;[NonSerialized]public string forbidden;public int absent=7;}
 [Serializable] class Cycle {public Cycle self;}
 static void Reject(string json){bool rejected=false;try{BossRushCoopJson.FromJson<Packet>(json);}catch(Exception){rejected=true;}Check(rejected,"reject malformed or type-confused packet");}
 static void Main(){
 var roster=new BossRushCoopRoster{generation=3,revision=long.MaxValue,ready=true,players=new[]{new BossRushCoopRosterEntry{id="host",assetId="slot-a",connected=true},new BossRushCoopRosterEntry{id="guest",assetId="slot-b",connected=true}}};
 string json=BossRushCoopJson.ToJson(roster);var restored=BossRushCoopJson.FromJson<BossRushCoopRoster>(json);
 Check(restored.players.Length==2&&restored.players[1].assetId=="slot-b"&&restored.revision==long.MaxValue,"actual roster keeps nested player array and exact long revision");
 var packet=new Packet{records=new[]{new Record{name="owner1",vars=new List<Variable>{new Variable("RF_STAT_护甲",new byte[]{0,1,127,255})},slots=new[]{1,4}},new Record{name="owner2",vars=new List<Variable>(),slots=new int[0]}},revision=9007199254740993L,seed=ulong.MaxValue,text="亡魂😀\n\"\\\t",accepted=true,forbidden="never"};
 json=BossRushCoopJson.ToJson(packet);var result=BossRushCoopJson.FromJson<Packet>(json);
 Check(result.records.Length==2&&result.records[1].vars.Count==0&&result.records[0].vars[0].Key=="RF_STAT_护甲","nested owner records lists and private serialized fields retained");
 Check(result.records[0].vars[0].Data[3]==255&&result.records[0].slots[1]==4,"raw equipment bytes and inventory slots retained");
 Check(result.text==packet.text&&result.revision==packet.revision&&result.seed==ulong.MaxValue&&result.accepted&&!json.Contains("never"),"unicode escaping exact integers booleans and NonSerialized contract");
 Check(BossRushCoopJson.FromJson<Packet>("{\"records\":null}").absent==0,"missing fields use zero defaults rather than constructor side effects");
 Reject("{\"revision\":1.2}");Reject("{\"accepted\":\"true\"}");Reject("{\"records\":{}}");Reject("{\"revision\":null");
 var cycle=new Cycle();cycle.self=cycle;bool caught=false;try{BossRushCoopJson.ToJson(cycle);}catch(FormatException){caught=true;}Check(caught,"cycle fails explicitly instead of stack overflow");
 BossRushCoopJson.ResetStaticCaches();Check(BossRushCoopJson.FromJson<Packet>(json).records.Length==2,"cache teardown preserves subsequent DTO mapping");
 Console.WriteLine("CoopJson: PASS "+checks+" production codec assertions; no Unity serializer substitute.");
 }
}
