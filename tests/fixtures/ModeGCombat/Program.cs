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
    private static void Tick(ModeGRewardStrictMaterializer materializer)
    {
        typeof(ModeGRewardStrictMaterializer).GetMethod("Update",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(materializer,null);
    }
    public static int Main()
    {
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        ItemAssetsCollection.metadata[100]=new ItemMetaData { id=100,tags=new[]{new Duckov.Utilities.Tag { name="Gun" }} };
        ItemAssetsCollection.metadata[101]=new ItemMetaData { id=101,tags=new[]{new Duckov.Utilities.Tag { name="MeleeWeapon" }} };
        Case("全局死亡订阅被截断仍结案并在注销前发布客人死亡事实",()=>{
            Action<Health,DamageInfo> breaker=(h,d)=>{throw new InvalidOperationException("foreign death subscriber");};
            Health.OnDead+=breaker;
            try {
                BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="sweep-room";
                using(var b=new Battle(0)) {
                    var runtime=new ModeGRuntimeModule(b.state,b.telemetry,b.adaptive);
                    BossRushCoop.Players.Clear();BossRushCoop.Players.Add(new BossRushCoopPlayer{Id="local",AssetId=BossRushCoop.LocalAssetId,IsLocal=true,Character=b.player});
                    BossRushCoop.Players.Add(new BossRushCoopPlayer{Id="guest",AssetId="original-guest",Character=Character()});
                    runtime.BindCoopBoss(-71,b.boss);runtime.FreezeAsset();ModeGEntryRuntime.UseRuntime(runtime);
                    ModeGEntryRuntime.AuditPublisher=true;BossRushCoop.Players[1].AssetId="switched-guest";
                    bool interrupted=false;try {b.Hit(100,100);}catch(InvalidOperationException){interrupted=true;}
                    Check(interrupted&&b.boss.Health.IsDead&&runtime.ActiveCoopBosses==1&&b.deaths==0,"真实多播异常截断遥测，已死 Boss 仍登记");
                    Time.unscaledTime=100;runtime.SpawnInFlight=true;runtime.AuditSweep();
                    Check(runtime.ActiveCoopBosses==1&&ModeGEntryRuntime.PublishedDeaths==0,"生成事务中不走正常战斗补偿");
                    runtime.SpawnInFlight=false;runtime.AuditSweep();
                    Check(runtime.ActiveCoopBosses==0&&b.state.TrackedBossCount==0&&runtime.WaveSettlementPending,"真实 MarkKilled 清登记并排入波结算");
                    Check(ModeGEntryRuntime.PublishedDeaths==1,"在注销前发布一次共享死亡给个人交付入口");
                    Check(ModeGEntryRuntime.DeliveredAssets.Count==1&&ModeGEntryRuntime.DeliveredAssets[0]=="guest:original-guest","真实生产发布器沿冻结参与者资产交付死亡，不借当前新槽");
                    runtime.AuditSweep();Health.OnDead-=breaker;Health.EmitDead(b.boss.Health,default(DamageInfo));runtime.AuditSweep();
                    Check(ModeGEntryRuntime.PublishedDeaths==1,"重复轮询不重发，常规遥测也忽略已注销 Boss");
                }
                BossRushCoop.IsHost=false;
                using(var b=new Battle(0)) {
                    var runtime=new ModeGRuntimeModule(b.state,b.telemetry,b.adaptive);runtime.BindCoopBoss(-72,b.boss);runtime.FreezeAsset();ModeGEntryRuntime.UseRuntime(runtime);
                    b.boss.Health.IsDead=true;runtime.AuditSweep();
                    Check(runtime.ActiveCoopBosses==1&&ModeGEntryRuntime.PublishedDeaths==0,"客端不得扫描结案原生副本");
                }
                BossRushCoop.IsHost=true;
                using(var b=new Battle(0)) {
                    var runtime=new ModeGRuntimeModule(b.state,b.telemetry,b.adaptive);runtime.BindCoopBoss(-73,b.boss);runtime.FreezeAsset();ModeGEntryRuntime.UseRuntime(runtime);
                    runtime.RememberCoopBossIdentity(-73,b.boss.Health);Health original=b.boss.Health;
                    UnityEngine.Object.Destroy(b.boss.gameObject);
                    Check(original==null&&!ReferenceEquals(original,null),"原生先销毁保留 exact Health 托管身份");
                    Check(runtime.ResolveCoopBossIdentity(original)==-73,"实际身份解析器找回此前公布的原生 AI id");
                    BossRushCoop.SceneGeneration++;Check(runtime.ResolveCoopBossIdentity(original)==0,"冻结 AI 身份不得越过原生场景代次");BossRushCoop.SceneGeneration--;
                    Time.unscaledTime=101;runtime.AuditSweep();
                    Check(runtime.ActiveCoopBosses==0&&runtime.WaveSettlementPending&&ModeGEntryRuntime.DeliveredAssets.Count==1,"已销毁 Health 同时补 host 波结算和真实 guest 个人死亡发布");
                    Check(runtime.ResolveCoopBossIdentity(original)==0,"已注销原 Health 不可借冻结表再发伪死亡");
                }
            } finally {Health.OnDead-=breaker;ModeGEntryRuntime.AuditPublisher=false;ModeGEntryRuntime.UseRuntime(null);foreach(var peer in BossRushCoop.Players)if(!peer.IsLocal)UnityEngine.Object.Destroy(peer.Character.gameObject);BossRushCoop.Players.Clear();BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;BossRushCoop.RunId="";}
        });
        Case("原生副本先销毁后个人事实仍按原引用结算，未知引用拒绝",()=>{
            using(var b=new Battle(0)) {
                var runtime=new ModeGRuntimeModule(b.state,b.telemetry,b.adaptive);
                runtime.BindCoopBoss(-17,b.boss);ModeGEntryRuntime.UseRuntime(runtime);
                BossRushCoop.Entities["p:local"]=b.player;BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;
                try {
                    runtime.FreezeAsset();
                    Check(runtime.ApplyCoopCombat(new ModeGCoopCombat{Run=1,Admission=true,Sequence=9000,Order=0}),"实际准入");
                    Health original=b.boss.Health;UnityEngine.Object.Destroy(b.boss.gameObject);
                    Check(b.boss==null&&original==null&&!ReferenceEquals(original,null),"原生销毁连带Health，托管引用保留");
                    var damage=new ModeGCoopCombat{Run=1,Wave=0,Boss=-17,Sequence=91,Order=1,Source="p:local",Weapon=100,Damage=20,Final=20,Phase=(int)ModeGCombatPhase.Fighting};
                    var dead=new ModeGCoopCombat{Run=1,Wave=0,Boss=-17,Sequence=92,Order=2,Source="p:local",Weapon=100,Damage=80,Final=80,Dead=true,Phase=(int)ModeGCombatPhase.Fighting};
                    Check(!runtime.ApplyCoopCombat(dead),"销毁不允许乱序越过前序伤害");
                    BossRushCoop.SceneGeneration++;
                    Check(!runtime.ApplyCoopCombat(damage),"旧场景原引用不能在同图重载代次结算");BossRushCoop.SceneGeneration--;
                    runtime.AuditCacheUnregistered(-99,new Health());
                    Check(!runtime.ApplyCoopCombat(new ModeGCoopCombat{Run=1,Wave=0,Boss=-99,Sequence=93,Order=1,Dead=true}),"缓存中未登记伪引用不能结算");
                    Check(!runtime.ApplyCoopCombat(new ModeGCoopCombat{Run=1,Wave=0,Boss=-100,Sequence=94,Order=1,Dead=true}),"未知AI不能结算");
                    Check(runtime.ApplyCoopCombat(damage),"原Health身份保留真实护甲后伤害");Near(20,b.telemetry.TotalDirectDamage,"伤害不吞");
                    Check(runtime.ApplyCoopCombat(dead),"销毁后的死亡事实走真实HandleBossDead并ACK");
                    Check(runtime.ActiveCoopBosses==0&&b.state.TrackedBossCount==0&&runtime.WaveSettlementPending,"真实生成事务注销并触发下一帧波结算");
                    Check(runtime.TotalKills==1,"本人真实末击保留个人击杀数量");Near(100,b.telemetry.TotalDirectDamage,"末击完整计分");
                    Check(runtime.ApplyCoopCombat(dead)&&runtime.ApplyCoopCombat(damage),"死亡和伤害重发确认");
                    Check(runtime.TotalKills==1,"重发不增个人击杀");Near(100,b.telemetry.TotalDirectDamage,"重发不增贡献");
                    Check(!runtime.ApplyCoopCombat(new ModeGCoopCombat{Run=2,Wave=0,Boss=-17,Sequence=95,Order=3,Dead=true}),"旧缓存不可跨run使用");
                    runtime.AuditReleaseBosses();Check(runtime.CachedHealthCount==0,"终局/换波释放全部原实体引用");
                }finally {BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;BossRushCoop.Entities.Clear();ModeGEntryRuntime.UseRuntime(null);}
            }
        });
        Case("托管Boss副本销毁后按冻结key保留原类型个人成就",()=>{
            ModeGCombatTelemetry.DropPendingAchievementReports();
            ModBehaviour.Instance.AchievementTypes.Clear();ModBehaviour.Instance.AchievementTokens.Clear();
            var keys=new[]{ModeGEncounterVariation.ManagedDragonDescendantKey,ModeGEncounterVariation.ManagedDragonKingKey,ModeGEncounterVariation.ManagedPhantomWitchKey,"official_boss"};
            var types=new[]{"DragonDescendant","DragonKing","PhantomWitch","Normal"};
            try {
                for(int i=0;i<keys.Length;i++) using(var b=new Battle(0)) {
                    var runtime=new ModeGRuntimeModule(b.state,b.telemetry,b.adaptive);
                    runtime.BindCoopBoss(-17,b.boss,keys[i]);ModeGEntryRuntime.UseRuntime(runtime);
                    BossRushCoop.Entities["p:local"]=b.player;BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;
                    runtime.FreezeAsset();
                    Check(runtime.ApplyCoopCombat(new ModeGCoopCombat{Run=1,Admission=true,Sequence=9000,Order=0}),"类型回归真实准入");
                    UnityEngine.Object.Destroy(b.boss.gameObject);
                    var dead=new ModeGCoopCombat{Run=1,Wave=0,Boss=-17,Sequence=92,Order=1,Source="p:local",Weapon=100,Damage=100,Final=100,Dead=true,Phase=(int)ModeGCombatPhase.Fighting};
                    Check(runtime.ApplyCoopCombat(dead)&&runtime.ApplyCoopCombat(dead),"类型回归真实死亡与重发");
                    ModeGCombatTelemetry.ConsumePendingAchievementReports(b.state);
                    Check(ModBehaviour.Instance.AchievementTypes.Count==i+1&&ModBehaviour.Instance.AchievementTypes[i]==types[i],"原类型只入账一次: "+keys[i]);
                    Check(ModBehaviour.Instance.AchievementTokens[i]!=0,"销毁对象仍保留实例token");
                }
            }finally {BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;BossRushCoop.Entities.Clear();ModeGEntryRuntime.UseRuntime(null);ModeGCombatTelemetry.DropPendingAchievementReports();}
        });
        Case("联网原生回调跳过，个人收据按顺序只计本人护甲后伤害",()=>{
            using(var b=new Battle()) {
                var runtime=new ModeGRuntimeModule(b.state,b.telemetry,b.adaptive);
                runtime.BindCoopBoss(-17,b.boss);
                var other=Character(); BossRushCoop.Entities["p:local"]=b.player; BossRushCoop.Entities["p:other"]=other;
                BossRushCoop.IsConnected=true; BossRushCoop.IsHost=false;
                try {
                    runtime.FreezeAsset();
                    Check(runtime.ApplyCoopCombat(new ModeGCoopCombat{Run=b.state.runId,Admission=true,Sequence=8000,Order=0}),"真实准入基线授权从第一条事实开始");
                    b.Hit(500,20); Near(0,b.telemetry.TotalDirectDamage,"客端原生伤害不重复计分");
                    var first=new ModeGCoopCombat { Run=1,Wave=b.state.waveEpoch,Boss=-17,Sequence=81,Order=1,
                        Source="p:local",Weapon=100,Damage=500,Final=20,Phase=(int)ModeGCombatPhase.Fighting,
                        Point=new Vector3(100,0,0),SourcePosition=new Vector3(80,0,0) };
                    var second=new ModeGCoopCombat { Run=1,Wave=b.state.waveEpoch,Boss=-17,Sequence=82,Order=2,
                        Source="p:other",Weapon=100,Damage=80,Final=80,Dead=true,Phase=(int)ModeGCombatPhase.Fighting };
                    Check(!runtime.ApplyCoopCombat(second),"后到序号不能越过尚未收到的本人伤害");
                    Check(runtime.ApplyCoopCombat(first),"本人伤害收据接收");
                    Near(20,b.telemetry.TotalDirectDamage,"以护甲后伤害计分");
                    Check(runtime.ApplyCoopCombat(first),"重复收据可确认且不再计分");
                    Near(20,b.telemetry.TotalDirectDamage,"去重保持个人贡献");
                    Check(runtime.ApplyCoopCombat(second) && runtime.CoopBossDeaths==1,"他人末击仍推进共享 Boss 结案");
                    Near(20,b.telemetry.TotalDirectDamage,"他人伤害不归本人");
                    Check(runtime.ApplyCoopCombat(second) && runtime.CoopBossDeaths==1,"末击重发不会重复推进世界");
                } finally { BossRushCoop.IsConnected=false; BossRushCoop.Entities.Clear(); UnityEngine.Object.Destroy(other.gameObject); }
            }
        });
        Case("致命一击在注销前计分且后续 OnHurt 不重复",()=>{
            using(var b=new Battle()) {
                b.Hit(100,100);
                Near(100,b.telemetry.TotalDirectDamage,"一击击杀完整贡献");
                Check(b.deaths==1 && b.terminal==ModeGDirectDamageClass.Gun,"死亡/末击归因");
                Check(ModeGAdaptiveCombat.IsDistanceAxisBroken(b.telemetry,ModeGDistanceVerdict.Close),"远距收尾破解");
            }
        });
        Case("随从击杀已登记 Boss 正常结案但不伪造主角直伤",()=>{
            using(var b=new Battle()) {
                var companion=Character();
                b.boss.Health.Hurt(new DamageInfo { fromCharacter=companion,damageValue=100,finalDamage=100,fromWeaponItemID=100 });
                Check(b.deaths==1 && b.terminal==ModeGDirectDamageClass.NotScoreable,"随从末击结案且不归入主角武器轴");
                Near(0,b.telemetry.TotalDirectDamage,"随从伤害不污染主角直伤分母");
                UnityEngine.Object.Destroy(companion.gameObject);
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
        Case("刷怪首选点阻塞时仍能找到合法双Boss和三Boss组合",()=>{
            var source=new[]{new Vector3(13,0,0),new Vector3(13,0,8),new Vector3(13,0,-8),new Vector3(21,0,0)};
            for(int count=2;count<=3;count++) {
                Vector3[] selected;
                Check(ModBehaviour.SelectFormation(source,count,ModeGPlanVariant.Split,out selected),"有合法组合不得中止整局 count="+count);
                for(int i=0;i<count;i++) for(int j=0;j<i;j++)
                    Check((selected[i]-selected[j]).sqrMagnitude>=100,"保留10米安全间距");
            }
            Vector3[] rejected;
            Check(!ModBehaviour.SelectFormation(new[]{source[0],source[1]},2,ModeGPlanVariant.Split,out rejected),"几何不足仍拒绝");
        });
        Case("刷怪超时不包含暂停和恢复边界",()=>{
            float elapsed=ModeGRuntimeModule.SpawnWait(0,0,5,false,false);
            elapsed=ModeGRuntimeModule.SpawnWait(elapsed,5,35,false,true);
            elapsed=ModeGRuntimeModule.SpawnWait(elapsed,35,65,true,true);
            elapsed=ModeGRuntimeModule.SpawnWait(elapsed,65,95,true,false);
            Near(5,elapsed,"长暂停和恢复不消耗预算");
            elapsed=ModeGRuntimeModule.SpawnWait(elapsed,95,105,false,false);
            Near(15,elapsed,"真实未暂停15秒仍超时");
            Near(15,ModeGRuntimeModule.SpawnWait(elapsed,105,104,false,false),"时钟回退不倒扣");
        });
        Case("编队选点与独立穷举的可行性一致且结果稳定",()=>{
            var random=new Random(1909);
            foreach(ModeGPlanVariant variant in Enum.GetValues(typeof(ModeGPlanVariant)))
            for(int sample=0;sample<200;sample++) for(int count=2;count<=3;count++) {
                var source=new Vector3[8];
                for(int i=0;i<source.Length;i++) source[i]=new Vector3(random.Next(-30,31),0,random.Next(-30,31));
                var spec=ModeGWavePlan.GetFormationSpec(variant);
                float pairSq=spec.bossPairMinDistance*spec.bossPairMinDistance;
                float playerSq=spec.playerMinDistance*spec.playerMinDistance;
                bool possible=false;
                for(int i=0;i<source.Length;i++) for(int j=i+1;j<source.Length;j++) {
                    if(source[i].sqrMagnitude<playerSq || source[j].sqrMagnitude<playerSq || (source[i]-source[j]).sqrMagnitude<pairSq) continue;
                    if(count==2) possible=true;
                    for(int k=j+1;k<source.Length;k++)
                        if(source[k].sqrMagnitude>=playerSq && (source[k]-source[i]).sqrMagnitude>=pairSq && (source[k]-source[j]).sqrMagnitude>=pairSq) possible=true;
                }
                Vector3[] chosen,again;
                bool found=ModBehaviour.SelectFormation(source,count,variant,out chosen);
                Check(found==possible,"合法组合不能漏选 "+variant);
                Check(ModBehaviour.SelectFormation(source,count,variant,out again)==found,"重复查询一致");
                if(!found) continue;
                for(int i=0;i<count;i++) {
                    Check(chosen[i].sqrMagnitude>=playerSq && (chosen[i]-again[i]).sqrMagnitude==0,"玩家间距与确定性");
                    for(int j=0;j<i;j++) Check((chosen[i]-chosen[j]).sqrMagnitude>=pairSq,"Boss间距");
                }
            }
        });
        Case("败北中断契约连胜且同一终局不重复记账",()=>{
            Saves.SavesSystem.ChangeSlot();
            ModeGProfilePersistence.IncrementContractStreak(); ModeGProfilePersistence.IncrementContractStreak();
            ModeGProfilePersistence.RecordRun(ModeGBattleResult.Defeat,5,30,7,false,"defeat-test");
            var p=ModeGProfilePersistence.Current;
            Check(p.contractStreak==0,"败北不能保留契约连胜");
            Check(p.totalRuns==1 && p.totalDefeats==1 && p.totalBossKills==7,"败北历史保留");
            ModeGProfilePersistence.RecordRun(ModeGBattleResult.Defeat,5,30,7,false,"defeat-test");
            Check(ModeGProfilePersistence.Current.totalRuns==1,"重复死亡不能二次记账");
            ModeGProfilePersistence.IncrementContractStreak();
            Check(ModeGProfilePersistence.Current.contractStreak==1,"下一次完成从1开始");
        });
        Case("奖励每帧一件且完成回调恰好一次",()=>{
            var m=new ModeGRewardStrictMaterializer(); var inventory=new Inventory();
            int completed=0, items=0, baseline=ItemAssetsCollection.InstantiateCount;
            Check(m.Initialize(new[]{1,2,3},inventory,(id,item,ok)=>{Check(ok,"交付成功");items++;},
                (total,ok,bad)=>{Check(total==3 && ok==3 && bad==0,"完成统计");completed++;}),"初始化");
            Tick(m); Check(items==1 && completed==0 && ItemAssetsCollection.InstantiateCount==baseline+1,"单帧限额");
            Tick(m); Tick(m); Tick(m); m.CancelAndDestroy();
            Check(m.IsFinished && completed==1 && items==3,"完成幂等");
        });
        Case("背包中途销毁仍结案并释放奖励租约",()=>{
            var m=new ModeGRewardStrictMaterializer(); var inventory=new Inventory();
            int completed=0, good=-1, bad=-1;
            int lease=ModeGLateCleanupSink.AcquireLease("fixture");
            try {
                m.Initialize(new[]{1,2,3},inventory,null,(total,ok,failed)=>{
                    completed++;good=ok;bad=failed;ModeGLateCleanupSink.ReleaseLease(lease);
                });
                Tick(m); UnityEngine.Object.Destroy(inventory); Tick(m);
                Check(m.IsFinished && completed==1 && good==1 && bad==2,"丢失背包不能永远挂起");
                Check(!ModeGLateCleanupSink.HasPendingLeases,"租约释放");
            } finally { m.CancelAndDestroy(); ModeGLateCleanupSink.ReleaseLease(lease); }
        });
        Case("物品交付事件取消奖励时正确计算在途物品",()=>{
            var m=new ModeGRewardStrictMaterializer(); var inventory=new Inventory();
            int completed=0, good=-1, bad=-1;
            inventory.OnAdding=item=>m.CancelAndDestroy();
            m.Initialize(new[]{1,2,3},inventory,null,(total,ok,failed)=>{completed++;good=ok;bad=failed;});
            Tick(m); Tick(m);
            Check(m.IsFinished && completed==1 && good==1 && bad==2,"已交付当前件应记成功，其余取消");
        });
        Case("逐件回调取消奖励不访问已清空快照",()=>{
            var m=new ModeGRewardStrictMaterializer(); int completed=0, good=-1, bad=-1;
            m.Initialize(new[]{1,2,3},new Inventory(),(id,item,ok)=>m.CancelAndDestroy(),
                (total,ok,failed)=>{completed++;good=ok;bad=failed;});
            Tick(m); Tick(m); m.CancelAndDestroy();
            Check(completed==1 && good==1 && bad==2,"取消统计守恒且不重入");
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
        Case("房主切槽保留世界死亡订阅和队友回执并撤销新槽个人计分",()=>{
            BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;
            using(var battle=new Battle(2)) {
                var runtime=new ModeGRuntimeModule(battle.state,battle.telemetry,battle.adaptive);
                BossRushCoop.Players.Clear();BossRushCoop.Players.Add(new BossRushCoopPlayer {Id="local",AssetId="asset-1",IsLocal=true,Character=battle.player});
                for(int i=1;i<16;i++)BossRushCoop.Players.Add(new BossRushCoopPlayer {Id="p"+i,AssetId="guest-"+i,Character=Character()});
                BossRushCoop.LocalAssetId="asset-1";runtime.FreezeAsset();ModeGEntryRuntime.UseRuntime(runtime);
                battle.Hit(20,20); Check(battle.adaptive.ApplyAttributeLock(battle.player,battle.telemetry),"旧槽属性锁生效");
                BossRushCoop.LocalAssetId="asset-2";
                Check(runtime.CheckCoopAsset() && runtime.StartupDisarmed && runtime.AssetEnds==0 && runtime.CoopPersonalDefeated,"房主仅退出个人账本，共享局仍存活");
                BossRushCoop.Players[0].AssetId="asset-2";
                Check(runtime.CanContinueWorld && runtime.CoopAnyParticipantAlive(),"真实异步续行判据允许房主继续世界任务且 15 名队友仍可玩");
                Check(battle.player.CharacterItem.GetStat("GunDamageMultiplier").modifiers.Count==0,"旧个人属性锁立即恢复");
                Near(20,battle.telemetry.TotalDirectDamage,"已记成绩保持");
                battle.Hit(10,10); Shoot(Gun(301),1);
                Near(20,battle.telemetry.TotalDirectDamage,"新槽伤害和开火不进入旧遥测");
                Check(ModeGEntryRuntime.PublishedDamage==2,"错槽仍发送队友需要的世界伤害事实");
                battle.Hit(100,100);
                Check(battle.deaths==1 && ModeGEntryRuntime.PublishedDeaths==1 && battle.state.TrackedBossCount==0,"错槽仍结案共享 Boss 并发布末击");
                Check(!runtime.ApplyCoopCombat(new ModeGCoopCombat { Run=1,Order=1,Sequence=1 }),"旧个人回执不进入新槽");
                BossRushCoop.LocalAssetId="asset-1";
                Check(runtime.CheckCoopAsset() && !ModeGEntryRuntime.CanApplyPersonalCombat,"切回原槽也不复活已退场个人成绩");
                for(int i=1;i<16;i++)BossRushCoop.Players[i].Character.Health.IsDead=true;
                Check(!runtime.CoopAnyParticipantAlive(),"新槽存活房主不能掩盖原参与者全死");
                foreach(var peer in BossRushCoop.Players)if(!peer.IsLocal)UnityEngine.Object.Destroy(peer.Character.gameObject);
                BossRushCoop.Players.Clear();
            }
            ModeGEntryRuntime.UseRuntime(null);BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;
        });
        Case("延迟成就只消费原资产且切回后恰好一次",()=>{
            ModeGCombatTelemetry.DropPendingAchievementReports();ModBehaviour.Instance.AchievementReports=0;
            BossRushCoop.LocalAssetId="asset-1";
            ModeGCombatTelemetry.EnqueueAchievementReport(17,"Normal",false);
            BossRushCoop.LocalAssetId="asset-2";
            ModeGCombatTelemetry.ConsumePendingAchievementReports(null);
            Check(ModBehaviour.Instance.AchievementReports==0,"不能把原槽的延迟成就写入新槽");
            BossRushCoop.LocalAssetId="asset-1";
            ModeGCombatTelemetry.ConsumePendingAchievementReports(null);ModeGCombatTelemetry.ConsumePendingAchievementReports(null);
            Check(ModBehaviour.Instance.AchievementReports==1,"原槽延迟成就保留且只消费一次");
        });
        Case("客端切槽结束本人旧局且不能把旧战斗回执写入新槽",()=>{
            BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;
            using(var battle=new Battle()) {
                var runtime=new ModeGRuntimeModule(battle.state,battle.telemetry,battle.adaptive);
                BossRushCoop.LocalAssetId="asset-1";runtime.FreezeAsset();
                Check(runtime.CheckCoopAsset(),"创建槽继续");
                BossRushCoop.LocalAssetId="asset-2";
                Check(!runtime.CheckCoopAsset() && runtime.StartupDisarmed && runtime.AssetEnds==1
                    && battle.state.exitReason==ModeGExitReason.SceneChanged,"旧局退出且不向新槽退款");
                Check(!runtime.ApplyCoopCombat(new ModeGCoopCombat { Run=1,Order=1,Sequence=1 }),"旧回执不能进入新个人遥测");
                Check(runtime.AssetEnds==1,"重复旧回执不能重复终局");BossRushCoop.LocalAssetId="asset-1";
            }
            BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;
        });
        Case("原房主停网立即拒绝旧世界，暂断和原单机保持既有权限",()=>{
            BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="host-owned-room";
            using(var battle=new Battle()) {
                var runtime=new ModeGRuntimeModule(battle.state,battle.telemetry,battle.adaptive);runtime.FreezeAsset();
                Check(runtime.CoopRunHasWorldAuthority&&runtime.CanContinueWorld,"G冻结原host世界");
                BossRushCoop.SessionReady=false;
                Check(runtime.CoopRunHasWorldAuthority&&runtime.CanContinueWorld&&runtime.CheckCoopAsset(),"G同房握手暂失不吞已有玩家局");
                BossRushCoop.RunId="";Check(runtime.CanContinueWorld,"G原host空握手保留原房世界");
                BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;
                Console.WriteLine("G_HOST_STOP_REPRO authority="+runtime.CoopRunHasWorldAuthority+" callback="+runtime.CanContinueWorld);
                Check(!runtime.CoopRunHasWorldAuthority&&!runtime.CanContinueWorld,"G原host真正Stop不可转为单机继续旧世界");
                Check(!runtime.CheckCoopAsset()&&runtime.AssetEnds==1,"G原host Stop沿原End结清旧owner");
            }
            BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;BossRushCoop.RunId="";
            using(var battle=new Battle()) {
                var solo=new ModeGRuntimeModule(battle.state,battle.telemetry,battle.adaptive);solo.FreezeAsset();
                Check(solo.CoopRunHasWorldAuthority&&solo.CanContinueWorld,"G原单机仍正常开局");
                BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="new-room";
                Check(!solo.CoopRunHasWorldAuthority&&!solo.CanContinueWorld,"G旧单机不能带世界进新房间");
            }
            BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;BossRushCoop.RunId="";BossRushCoop.SessionReady=true;
        });
        Case("原客端断线不获得本地世界生成权限且新房间结清旧局",()=>{
            BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;BossRushCoop.RunId="old-room";BossRushCoop.LocalAssetId="asset-1";
            using(var battle=new Battle()) {
                var runtime=new ModeGRuntimeModule(battle.state,battle.telemetry,battle.adaptive);runtime.FreezeAsset();
                Check(runtime.CoopRunWasClient&&!runtime.CoopRunHasWorldAuthority&&runtime.CanContinueWorld,"原客端角色冻结且允许联网个人续作");
                BossRushCoop.IsConnected=false;Check(BossRushCoop.HasWorldAuthority&&!runtime.CoopRunHasWorldAuthority&&!runtime.CanContinueWorld,"断线门面单机权限不能覆盖真实run客端角色");
                BossRushCoop.IsConnected=true;BossRushCoop.RunId="";Check(runtime.CheckCoopAsset()&&runtime.AssetEnds==0,"空握手不是新房间");
                BossRushCoop.RunId="new-room";Check(!runtime.CanContinueWorld,"新房间在下一帧End之前已阻止旧异步续作");Check(!runtime.CheckCoopAsset()&&runtime.AssetEnds==1,"非空新房间沿原End终结旧核心");
            }
            BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="host-old";
            using(var battle=new Battle()){var runtime=new ModeGRuntimeModule(battle.state,battle.telemetry,battle.adaptive);runtime.FreezeAsset();Check(runtime.CanContinueWorld,"原房主本房可续作");BossRushCoop.RunId="host-new";Check(!runtime.CanContinueWorld,"原房主迟到工厂不可向新房间提交旧世界对象");BossRushCoop.RunId="host-old";BossRushCoop.IsHost=false;Check(!runtime.CanContinueWorld,"原房主失去世界权限后不可续作旧生成");}
            BossRushCoop.IsHost=false;
            BossRushCoop.IsConnected=false;BossRushCoop.RunId="";BossRushCoop.LocalAssetId="offline";
            using(var battle=new Battle()){var runtime=new ModeGRuntimeModule(battle.state,battle.telemetry,battle.adaptive);runtime.FreezeAsset();Check(!runtime.CoopRunWasClient&&runtime.CoopRunHasWorldAuthority&&runtime.CanContinueWorld,"无COOP原单机仍有完整世界权限和续行");}
        });
        Console.WriteLine("ModeGCombat: "+passed+" PASS / "+failed+" FAIL");
        return failed==0 ? 0 : 1;
    }
}
