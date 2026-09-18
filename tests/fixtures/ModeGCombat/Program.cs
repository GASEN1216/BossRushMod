using System;
using System.Collections.Generic;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

internal static class Program
{
    private static int passed, failed;
    private static void Check(bool ok,string message) { if (!ok) throw new Exception(message); }
    private static void Near(float expected,float actual,string message) { Check(Math.Abs(expected-actual)<0.001f,message+": "+actual); }
    private static void Case(string name,Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS "+name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL "+name+": "+e.Message); }
    }
    private static CharacterMainControl Character(float health=100f)
    {
        var c = new CharacterMainControl { CharacterItem = new Item() };
        foreach (string key in new[]{"MaxHealth","GunDamageMultiplier","MeleeDamageMultiplier","WalkSpeed","RunSpeed","GunShootSpeedMultiplier"})
            c.CharacterItem.stats[key] = new Stat { BaseValue = key=="MaxHealth" ? health : 1f };
        c.Health = new Health { owner=c, CurrentHealth=health };
        c.gameObject.components.Add(c); c.gameObject.components.Add(c.Health); c.gameObject.components.Add(c.CharacterItem);
        return c;
    }
    private sealed class Battle : IDisposable
    {
        public readonly ModeGRunState state = new ModeGRunState(1,2,"test",0,1);
        public readonly CharacterMainControl player = Character();
        public readonly CharacterMainControl boss;
        public readonly ModeGCombatTelemetry telemetry;
        public readonly ModeGAdaptiveCombat adaptive;
        public ModeGDirectDamageClass terminal;
        public int deaths;
        public Battle(int wave=1,float health=100f)
        {
            CharacterMainControl.Main=player; boss=Character(health);
            state.TryAdvanceLifecycle(ModeGLifecyclePhase.Starting); state.TryAdvanceLifecycle(ModeGLifecyclePhase.Active);
            state.waveEpoch=wave; state.actIndex=wave/3; state.combatPhase=ModeGCombatPhase.Fighting;
            state.RegisterTrackedBoss(boss.Health,boss);
            telemetry=new ModeGCombatTelemetry(state,(h,d)=>{
                deaths++; terminal=telemetry.ClassifyTerminalDamage(h,d); state.UnregisterTrackedBoss(h);
            });
            adaptive=new ModeGAdaptiveCombat(state);
            telemetry.BeginWave(player,health); telemetry.SubscribeCombat(); telemetry.SubscribeCombat();
            telemetry.SubscribeDead(); telemetry.SubscribeDead();
        }
        public void Hit(float raw,float final,int weapon=100,float distance=20f,bool effect=false)
        {
            boss.Health.Hurt(new DamageInfo { fromCharacter=player, damageValue=raw, finalDamage=final,
                fromWeaponItemID=weapon, damagePoint=new Vector3(distance,0,0), isFromBuffOrEffect=effect });
        }
        public void Dispose()
        {
            telemetry.UnsubscribeCombat(); telemetry.UnsubscribeDead(); adaptive.RestoreAllModifiers();
            UnityEngine.Object.Destroy(boss.gameObject); UnityEngine.Object.Destroy(player.gameObject);
            Check(boss==null && boss.Health==null,"销毁角色必须连带销毁组件");
        }
    }
    private static ItemAgent_Gun Gun(int id,bool explosiveFields=true)
    {
        var ammo=new Item { TypeID=id, Constants=new Constants() };
        ammo.Constants.data["damageMultiplier"]=1;
        if (explosiveFields) { ammo.Constants.data["ExplosionDamage"]=0; ammo.Constants.data["ExplosionRange"]=0; }
        ItemAssetsCollection.items[id]=ammo;
        return new ItemAgent_Gun { GunItemSetting=new ItemSetting_Gun { TargetBulletID=id }, ShotCount=1,
            CharacterDamageMultiplier=1, Damage=10, ExplosionDamageMultiplier=1 };
    }
    private static void Shoot(ItemAgent_Gun gun,int count=5) { for (int i=0;i<count;i++) gun.Shoot(); }
    public static int Main()
    {
        ItemAssetsCollection.metadata[100]=new ItemMetaData { id=100,tags=new[]{new Duckov.Utilities.Tag { name="Gun" }} };
        ItemAssetsCollection.metadata[101]=new ItemMetaData { id=101,tags=new[]{new Duckov.Utilities.Tag { name="MeleeWeapon" }} };
        Case("致命一击在注销前计分且后续 OnHurt 不重复",()=>{
            using(var b=new Battle()) {
                b.Hit(100,100);
                Near(100,b.telemetry.TotalDirectDamage,"一击击杀完整贡献");
                Check(b.deaths==1 && b.terminal==ModeGDirectDamageClass.Gun,"死亡/末击归因");
                Check(ModeGAdaptiveCombat.IsDistanceAxisBroken(b.telemetry,ModeGDistanceVerdict.Close),"远距收尾破解");
            }
        });
        Case("贡献按护甲后伤害而非输入伤害",()=>{
            using(var b=new Battle()) {
                b.Hit(500,5); Near(5,b.telemetry.TotalDirectDamage,"减伤后计数");
                Check(!ModeGAdaptiveCombat.IsDistanceAxisBroken(b.telemetry,ModeGDistanceVerdict.Close),"未达到20%生命门槛");
            }
        });
        Case("暴击与零伤害使用官方结算值",()=>{
            using(var b=new Battle()) {
                b.Hit(5,25); b.Hit(500,0); Near(25,b.telemetry.TotalDirectDamage,"暴击计入零伤害排除");
                Check(ModeGAdaptiveCombat.IsDistanceAxisBroken(b.telemetry,ModeGDistanceVerdict.Close),"有效暴击可破解");
            }
        });
        Case("间接击杀可推进而不计末击/直接贡献",()=>{
            using(var b=new Battle()) {
                b.Hit(20,20); b.Hit(80,80,effect:true);
                Near(20,b.telemetry.TotalDirectDamage,"只记直接伤害");
                Check(b.deaths==1 && b.terminal==ModeGDirectDamageClass.NotScoreable,"DOT仍正常结案");
            }
        });
        Case("精确抑制与非登记目标不污染遥测",()=>{
            using(var b=new Battle()) {
                using(ModeGTelemetrySuppressionScope.Enter(b.boss.Health)) b.Hit(30,30);
                Near(0,b.telemetry.TotalDirectDamage,"领域附伤不计分");
                var other=Character(); other.Health.Hurt(new DamageInfo { fromCharacter=b.player,damageValue=30,finalDamage=30,fromWeaponItemID=100 });
                Near(0,b.telemetry.TotalDirectDamage,"非登记目标"); UnityEngine.Object.Destroy(other.gameObject);
            }
        });
        Case("角色切换后伤害门槛和生产结算共同失效",()=>{
            using(var b=new Battle()) {
                b.Hit(50,50); LevelManager.Switch(b.player);
                Check(!ModeGAdaptiveCombat.IsDistanceAxisBroken(b.telemetry,ModeGDistanceVerdict.Close),"失效波不得破解");
                var module=new ModeGRuntimeModule(b.state,b.telemetry,b.adaptive);
                module.Settle(ModeGDistanceVerdict.Close,ModeGDirectDamageClass.Gun);
                Check(b.adaptive.TotalResolve==0,"生产结算不加分");
            }
        });
        Case("无爆炸字段的普通弹药可学习",()=>{
            using(var b=new Battle()) {
                Shoot(Gun(201,false)); Check(b.telemetry.TotalAmmoSamples==5,"普通弹药有效样本");
                Check(ModeGAdaptiveCombat.SelectAmmoBan(4,2,b.telemetry)==201,"普通弹药可被点名");
            }
        });
        Case("禁令保留休整违规且已点名弹药不重复",()=>{
            using(var b=new Battle()) {
                var gun=Gun(202); Shoot(gun);
                Check(ModeGAdaptiveCombat.SelectAmmoBan(3,2,b.telemetry)==202,"学习波选中");
                b.telemetry.ArmAmmoBan(202); b.state.combatPhase=ModeGCombatPhase.Intermission; gun.Shoot();
                b.telemetry.BeginWave(b.player,100); b.telemetry.ArmAmmoBan(202);
                Check(b.telemetry.ArmedBanViolationCount==1,"开战不能抹掉休整违规");
                b.telemetry.DisarmAmmoBan(); b.state.combatPhase=ModeGCombatPhase.Fighting; Shoot(gun);
                Check(ModeGAdaptiveCombat.SelectAmmoBan(3,5,b.telemetry)==-1,"全局命名排除");
            }
        });
        Case("污染样本不会产生下一波弹药或属性目标",()=>{
            using(var b=new Battle()) {
                Shoot(Gun(203)); b.Hit(40,40); LevelManager.Switch(b.player);
                Check(ModeGAdaptiveCombat.SelectAmmoBan(3,2,b.telemetry)==-1,"污染弹药样本");
                Check(ModeGAdaptiveCombat.PredictAttributeLockFamily(b.telemetry)==ModeGDirectDamageClass.NotScoreable,"污染属性样本");
            }
        });
        Case("有界缓存溢出后禁止继续按部分样本计分",()=>{
            using(var b=new Battle()) {
                b.Hit(40,40);
                for(int i=0;i<33;i++) Shoot(Gun(300+i));
                Check(b.telemetry.IsTelemetryDegraded,"缓存溢出标记");
                Check(ModeGAdaptiveCombat.SelectAmmoBan(3,2,b.telemetry)==-1,"降级样本不点名");
                var module=new ModeGRuntimeModule(b.state,b.telemetry,b.adaptive);
                module.Settle(ModeGDistanceVerdict.Close,ModeGDirectDamageClass.Gun);
                Check(b.adaptive.TotalResolve==0,"降级波不加分");
            }
        });
        Case("属性反制达标仍要求相反系收尾且保留提示",()=>{
            using(var b=new Battle(3)) {
                b.Hit(30,30); Check(b.adaptive.ApplyAttributeLock(b.player,b.telemetry),"封锁枪械");
                Near(0.75f,b.player.CharacterItem.GetStat("GunDamageMultiplier").Value,"枪伤受限");
                b.telemetry.BeginWave(b.player,100); b.Hit(30,30,101);
                Check(!b.adaptive.IsAttributeAxisBroken(b.telemetry,ModeGDirectDamageClass.Gun),"错误末击不能破解");
                Check(b.adaptive.IsAttributeAxisBroken(b.telemetry,ModeGDirectDamageClass.Melee),"正确末击可破解");
                var m=new ModeGRuntimeModule(b.state,b.telemetry,b.adaptive).Objective(ModeGCounterAxis.Attribute);
                Check(HudHarness.Compose(m).Contains("BossRush_ModeG_Hud_NeedTerminal"),"达标后仍提示收尾要求");
                b.adaptive.ClearAttributeLock(); Near(1,b.player.CharacterItem.GetStat("GunDamageMultiplier").Value,"卸除恢复");
            }
        });
        Case("退出退订后事件不再驱动旧局",()=>{
            var b=new Battle(); b.Dispose(); Shoot(Gun(500));
            Health.EmitDead(b.boss.Health,default(DamageInfo));
            Check(b.telemetry.ShotSequence==0 && b.deaths==0,"退订后无回调");
        });
        Case("缓存预热后直接伤害热路径无托管分配",()=>{
            using(var b=new Battle()) {
                b.Hit(0.01f,0.01f); // 填充武器分类与 Boss 字典。
                long before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<1000;i++) b.Hit(0.01f,0.01f);
                long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                Check(allocated==0,"热路径分配字节="+allocated);
                Near(10.01f,b.telemetry.TotalDirectDamage,"全部命中仍计分");
            }
        });
        Case("八契约候选覆盖稳定且排除上局选择",()=>{
            var seen=new HashSet<int>();
            for(int prior=-1;prior<8;prior++) for(ulong seed=0;seed<100;seed++) {
                var pair=ModeGFateContract.SelectEntryCandidatePair(seed,prior);
                var again=ModeGFateContract.SelectEntryCandidatePair(seed,prior);
                Check(pair.Length==2 && pair[0]<pair[1] && pair[0]!=prior && pair[1]!=prior,"合法二选一");
                Check(pair[0]==again[0] && pair[1]==again[1],"确定性"); seen.Add(pair[0]); seen.Add(pair[1]);
            }
            Check(seen.Count==8,"所有契约可出场");
        });
        Case("九波计划在1至12个官方Boss池中均可构建",()=>{
            int[] expected={1,2,1,1,3,1,1,3,1};
            var signatures=new[]{"managed_a","managed_b","managed_c"};
            for(int size=1;size<=12;size++) {
                var pool=new List<string>(); for(int i=0;i<size;i++) pool.Add("boss_"+i);
                for(ulong seed=0;seed<60;seed++) foreach(ModeGRunFormat format in Enum.GetValues(typeof(ModeGRunFormat))) {
                    var plan=ModeGWavePlan.Build(seed,format,signatures,pool,0,null,1,null);
                    Check(plan!=null && plan.TotalBossCount==14,"编排可启动");
                    for(int w=0;w<9;w++) {
                        var wave=plan.GetWave(w); Check(wave.bossCount==expected[w],"九波节拍");
                        foreach(string key in wave.bossPresetKeys) Check(pool.Contains(key)||Array.IndexOf(signatures,key)>=0,"主槽合法来源");
                        foreach(string key in wave.reservePresetKeys) Check(pool.Contains(key)||Array.IndexOf(signatures,key)>=0,"后备合法来源");
                        if(wave.isNemesisWave) Check(wave.bossPresetKeys[0]==plan.nemesisPresetKey,"三次同宿敌");
                    }
                }
            }
        });
        Case("Resolve按真实轴排程可达到11且不超额",()=>{
            var a=new ModeGAdaptiveCombat(null);
            for(int w=0;w<9;w++) {
                a.RecordResolve(ModeGAdaptiveCombat.GetAxisForWave(w));
                if(w==1||w==4||w==7) Check(a.RecordLastStandResolve(),"三次处决");
            }
            Check(a.TotalResolve==11,"上限可达");
            Check(!a.RecordResolve(ModeGCounterAxis.Distance)&&!a.RecordLastStandResolve(),"不超上限");
        });
        Case("八契约都有九波内的合法达成路径",()=>{
            // 3轮距离回声/弹药学习，2轮属性切换，每幕各1次限时处决。
            // 第一幕2轴+处决=3，后两幕各3轴+处决=4；不假设不存在的第4次处决。
            var p=new ModeGContractProgress { distanceResolves=3,ammoResolves=3,attributeResolves=2,
                lastStandCount=3,nemesisR3FinalBlowDirect=true,maxConsecutiveAxisBreaks=8,
                resolvesPerAct=new[]{3,4,4},distanceEchoCount=3,ammoBanCount=3,attributeLockCount=2,
                ammoBanAvailableOnNemesisWaves=3 };
            for(int id=0;id<8;id++) {
                Check(ModeGFateContract.Evaluate(id,p),"契约可达 "+id);
                Check(!ModeGFateContract.Evaluate(id,default(ModeGContractProgress)),"空进度不能达成 "+id);
            }
            p.resolvesPerAct[0]=1; Check(!ModeGFateContract.Evaluate(3,p),"三幕无缺不能靠后两幕补首幕");
            p.lastStandCount=2; Check(!ModeGFateContract.Evaluate(6,p),"最终时刻必须三次处决");
            p.nemesisR3FinalBlowDirect=false; Check(!ModeGFateContract.Evaluate(1,p),"终末行刑者必须R3直伤");
        });
        Case("奖励计划确定且所有Resolve档复用同一前缀",()=>{
            var candidates=new List<ModeGRewardCandidate>();
            for(int i=1;i<=30;i++) candidates.Add(new ModeGRewardCandidate(1000+i,100*i,1));
            var full=ModeGRewardTransaction.BuildSlotPlan(77,11,candidates);
            Check(full!=null && full.Count==10,"完整奖励计划");
            var unique=new HashSet<int>(); foreach(var item in full) {
                Check(unique.Add(item.typeId),"无重复奖励"); Check(item.estimatedValue<=2900,"排除P95以上");
            }
            int[] counts={6,6,6,7,7,7,8,8,8,9,10,10};
            candidates.Reverse();
            for(int resolve=0;resolve<=11;resolve++) {
                var plan=ModeGRewardTransaction.BuildSlotPlan(77,resolve,candidates);
                Check(plan.Count==counts[resolve],"奖励档位");
                for(int i=0;i<plan.Count;i++) Check(plan[i].typeId==full[i].typeId,"同种子同前缀且与输入顺序无关");
            }
            Check(ModeGRewardTransaction.BuildSlotPlan(77,11,new List<ModeGRewardCandidate>())==null,"空池拒绝");
            candidates.RemoveRange(0,25);
            Check(ModeGRewardTransaction.BuildSlotPlan(77,11,candidates)==null,"不足池拒绝");
        });
        Case("历史选择和未知存档版本不被覆盖",()=>{
            Saves.SavesSystem.ChangeSlot(); ModeGProfilePersistence.RecordSelectedContract(7); ModeGProfilePersistence.FlushPending(false);
            Check(ModeGProfilePersistence.GetLastSelectedContractId()==7,"最高ID选择可持久");
            Saves.SavesSystem.ChangeSlot();
            // 重新触发读档边界；未知版本按空视图读取，禁止写回。
            ModeGProfilePersistence.ShutdownSubscription();
            Saves.SavesSystem.Data[ModeGProfilePersistence.StorageKey]=new ModeGProfilePersistence.ProfileDto { schemaVersion=42,totalRuns=99 };
            typeof(ModeGProfilePersistence).GetField("_cache",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).SetValue(null,null);
            ModeGProfilePersistence.LoadOrInit();
            Check(ModeGProfilePersistence.HasWriteBarrier && !ModeGProfilePersistence.RecordSelectedContract(2),"未知版本拒写");
            Check(Saves.SavesSystem.Load<ModeGProfilePersistence.ProfileDto>(ModeGProfilePersistence.StorageKey).totalRuns==99,"旧数据保留");
        });
        Console.WriteLine("ModeGCombat: "+passed+" PASS / "+failed+" FAIL");
        return failed==0 ? 0 : 1;
    }
}
