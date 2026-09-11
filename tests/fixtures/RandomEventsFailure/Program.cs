using System;
using BossRush;
class Program
{
 static int n; static void C(bool x,string s){if(!x)throw new Exception(s);n++;Console.WriteLine("PASS "+s);}
 static void Main(){
  var d=new DirectorProbe(); var fail=new E(true); d.Start(fail,true); d.TickEventActive(.1f); C(d.Events==0&&d.Cleaned&&d.Cooldown,"all failures refund exactly once and clean"); d.TickEventActive(.1f); C(d.Events==0,"failure cannot refund twice");
  d=new DirectorProbe(); d.Start(new E(false),true); d.TickEventActive(.1f); C(d.Events==1&&!d.Cleaned,"partial success retains quota");
  d=new DirectorProbe(); d.Start(fail,false); d.TickEventActive(.1f); C(d.Events==0&&d.Cooldown,"F3 failure never refunds natural quota");
  var old=new RandomEventContext(); var fresh=new RandomEventContext(); var i=new IntruderProbe(); i.Start(old); i.Cleanup(); i.Start(fresh); i.InvokeLate(old); C(i.Failures==0&&i.Pending==1,"late intruder failure cannot mutate new context");
  var m=new MerchantProbe(); m.Start(old); m.Cleanup(); m.Start(fresh); m.InvokeLate(old); C(!m.Failed,"late merchant failure cannot mutate new context");
  Console.WriteLine("checks="+n);
 }
 sealed class E:RandomEventBase{bool f;public E(bool x){f=x;}public override bool HasFailedToStart{get{return f;}}}
}

