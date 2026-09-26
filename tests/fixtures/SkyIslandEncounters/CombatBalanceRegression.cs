using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using BossRush;

internal static partial class Program
{
    private static void NearCombat(float actual, float expected, string label)
    {
        Check(Math.Abs(actual - expected) < 0.0002f, label + ": actual=" + actual + ", expected=" + expected);
    }

    private static void CheckCombatBalance()
    {
        // 独立保存的 Wiki 原始快照逐字段核对生产表，离线测试不访问网络。
        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText("tests/fixtures/SkyIslandEncounters/VanillaCombatReference.json")))
        {
            foreach (JsonElement row in doc.RootElement.GetProperty("baselines").EnumerateArray())
            {
                var baseline = SkyIslandCombatBalance.Find(row.GetProperty("id").GetString());
                foreach (FieldInfo field in typeof(SkyIslandCombatBaseline).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                    if (field.FieldType == typeof(float))
                        NearCombat((float)field.GetValue(baseline), row.GetProperty(field.Name).GetSingle(), "Wiki snapshot " + baseline.PresetId + "/" + field.Name);
            }
        }
        string[] encounters = { "G", "S4", "D", "S2", "C", "S3", "S1", "F", "K1_Relay", "K2_Relay", "K3_Relay", "Zheling", "BellKeeper", "Storm", "StormEcho", "E_03" };
        string[] ids = { "Boss_Alex", "Boss_3Shot", "Boss_Hunter", "Boss_Speedy", "Boss_Tagilla", "Boss_Grenade", "Boss_Fly", "Boss_Vida", "Boss_Speedy_Ice", "Boss_Deng", "Boss_BALeader", "Boss_Killa", "Boss_SnowMan", "Boss_Island_Koukou", "Boss_Island_Koukou", "Scav_Snow_Elete" };
        for (int n = 0; n < encounters.Length; n++)
        {
            Reset();
            string id = encounters[n];
            var def = Array.Find(SkyIslandContent.CreateFallback().Encounters, e => e.Id == id);
            var baseline = SkyIslandCombatBalance.For(id, 0, def.TierFor(0));
            Check(baseline.PresetId == "EnemyPreset_" + ids[n], "role maps to explicit vanilla version " + id);
            // 各角色都从真实遭遇 owner 触发，保证是在官方工厂调用前完成数值接线。
            SkyIslandBossForge.Night = true;
            using (var world = new World(id == "StormEcho" ? "Storm" : id))
            {
                if (id == "StormEcho") world.Saved.Add("Storm");
                if (def.Manual) Check(world.Encounters.BeginChallenge(id), "manual combat starts " + id);
                else world.Tick();
                var clone = CharacterRandomPreset.Clones[0];
                var source = CharacterRandomPreset.Source;
                NearCombat(clone.health, baseline.Health * 1.5f, id + " health before factory");
                NearCombat(CharacterRandomPreset.Created[0].Health.CurrentHealth, baseline.Health * 1.5f, id + " spawns at full new health");
                NearCombat(clone.damageMultiplier, baseline.Damage * 1.5f, id + " gun damage");
                NearCombat(clone.meleeDamageMultiplier, baseline.Damage * 1.5f, id + " melee damage");
                Check(clone.setMeleeDamageMultiplier, id + " melee override enabled");
                NearCombat(clone.moveSpeedFactor, baseline.MoveSpeed * 1.5f, id + " movement");
                NearCombat(clone.bulletSpeedMultiplier, baseline.BulletSpeed * 1.5f, id + " bullets");
                NearCombat(clone.gunDistanceMultiplier, baseline.Range * 1.5f, id + " gun range");
                NearCombat(clone.gunScatterMultiplier, baseline.Scatter / 1.5f, id + " accuracy");
                NearCombat(clone.nightVisionAbility, baseline.NightVision * 1.5f, id + " night vision");
                NearCombat(clone.aiCombatFactor, baseline.AiCombat * 1.5f, id + " NPC combat factor");
                NearCombat(clone.sightDistance, baseline.Sight * 1.5f, id + " sight");
                NearCombat(clone.hearingAbility, baseline.Hearing * 1.5f, id + " hearing");
                NearCombat(clone.reactionTime, baseline.Reaction / 1.5f, id + " reaction");
                NearCombat(clone.shootDelay, baseline.ShootDelay / 1.5f, id + " firing delay");
                NearCombat(clone.nightReactionTimeFactor, baseline.NightReaction, id + " night ratio unchanged");
                NearCombat(clone.gunCritRateGain, Math.Min(1f, baseline.Crit * 1.5f), id + " crit bound");
                NearCombat(source.health, 45f, id + " source resource untouched");
                Check(clone.exp == source.exp && clone.dropBoxOnDead, id + " original economy retained");
                SkyIslandCombatPreset.Apply(clone, source, id, 0, def.TierFor(0));
                NearCombat(clone.health, baseline.Health * 1.5f, id + " preparing twice does not compound");
                // 同组随从不能误吃带队 Boss 的参照。
                for (int i = 1; i < def.Count; i++)
                {
                    var follower = SkyIslandCombatBalance.For(id, i, def.TierFor(i));
                    NearCombat(CharacterRandomPreset.Clones[i].health,
                        (follower == null ? source.health : follower.Health) * 1.5f, id + " follower " + i);
                }
            }
        }
        SkyIslandBossForge.Night = false;
        // 普通敌人的不同底模、独立近战倍率及暴击惩罚不能被统一成同一个角色。
        foreach (float health in new[] { 35f, 45f, 69f, 145f, 180f })
        {
            var source = new CharacterRandomPreset { health = health, damageMultiplier = 0.82f, gunCritRateGain = -0.3f,
                setMeleeDamageMultiplier = false, meleeDamageMultiplier = 2f, reactionTime = 0.6f, nameKey = "official", exp = 20 };
            var clone = source.Copy();
            SkyIslandCombatPreset.Apply(clone, source, "C_02", 0, SkyIslandEnemyTier.Scav);
            NearCombat(clone.health, health * 1.5f, "ordinary source-relative health");
            NearCombat(clone.damageMultiplier, 1.23f, "ordinary gun damage");
            NearCombat(clone.meleeDamageMultiplier, 3f, "ordinary independent melee damage");
            NearCombat(clone.gunCritRateGain, -0.2f, "negative crit penalty improves");
            NearCombat(clone.reactionTime, 0.4f, "ordinary reaction improves");
            Check(clone.nameKey == "official" && clone.exp == 20, "ordinary kills and economy retain original keys");
            SkyIslandCombatPreset.Apply(clone, source, "C_02", 0, SkyIslandEnemyTier.Scav);
            NearCombat(clone.health, health * 1.5f, "ordinary idempotence");
            bool rejected = false;
            try { SkyIslandCombatPreset.Apply(source, source, "C_02", 0, SkyIslandEnemyTier.Scav); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "never modify original resource");
        }
        Reset();
    }
}
