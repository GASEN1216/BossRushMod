using System;
using BossRush;

internal static partial class Program
{
    /// <summary>
    /// owner 2026-09-30：岛上敌人手里的武器至少品质 3，头目 / 岛主 / 具名对手 / 噬风至少品质 5。
    /// 品质表链接生产 SkyIslandEnemyArmoryRules；真实遭遇 owner 与正式序章 SpawnBoss 必须给每一名刷出来的敌人
    /// 按它自己的档次配一次枪。官方物品表、槽位与弹匣是宿主边界，这里不证。
    /// </summary>
    private static void CheckEnemyArmory()
    {
        foreach (SkyIslandEnemyTier tier in (SkyIslandEnemyTier[])Enum.GetValues(typeof(SkyIslandEnemyTier)))
        {
            SkyIslandWeaponBand band = SkyIslandEnemyArmoryRules.For(tier);
            int floor = SkyIslandEnemyArmoryRules.IsBossTier(tier) ? 5 : 3;
            Check(band.Floor >= floor && band.Min >= band.Floor && band.Max >= band.Min, "armory band floor " + tier);
            Check(band.AmmoMin >= 2 && band.AmmoMin <= band.Min, "armory ammo band " + tier);
            Check(!band.Keeps(band.Min - 1) && band.Keeps(band.Min) && band.Keeps(7), "armory keeps only qualified weapons " + tier);
        }
        Check(!SkyIslandEnemyArmoryRules.IsBossTier(SkyIslandEnemyTier.Scav) && !SkyIslandEnemyArmoryRules.IsBossTier(SkyIslandEnemyTier.Elite),
            "ordinary tiers are not boss tiers");
        foreach (SkyIslandEnemyTier tier in new[] { SkyIslandEnemyTier.Champion, SkyIslandEnemyTier.Chief, SkyIslandEnemyTier.Storm, SkyIslandEnemyTier.Lord })
            Check(SkyIslandEnemyArmoryRules.IsBossTier(tier), "boss tier " + tier);
        for (int rank = 1; rank <= 8; rank++)
            Check(SkyIslandEnemyArmoryRules.ForPatrol(rank).Floor >= 3, "patrol rank " + rank + " floor");
        Check(SkyIslandEnemyArmoryRules.ForPatrol(8).Min > SkyIslandEnemyArmoryRules.ForPatrol(1).Min, "higher patrol ranks carry better guns");
        // 其它 Mod 的武器照发，只挡本 Mod 自己的 500xxx（龙息、断界戟这类主角绑定的传说级奖励）。
        Check(SkyIslandEnemyArmoryRules.IsOwnModItem(500001) && SkyIslandEnemyArmoryRules.IsOwnModItem(500103)
            && !SkyIslandEnemyArmoryRules.IsOwnModItem(92235) && !SkyIslandEnemyArmoryRules.IsOwnModItem(254)
            && !SkyIslandEnemyArmoryRules.IsOwnModItem(501000), "only this mod's own items are excluded");
        Check(SkyIslandEnemyArmoryRules.ReserveRounds(30) == 90 && SkyIslandEnemyArmoryRules.ReserveRounds(5) == 45, "reserve rounds");

        // 每一组（自动 / 手动 / 噬风 / 回响 / 夜限定带队）从真实 owner 刷出来，逐位核对配枪档次。
        SkyIslandBossForge.Night = true;
        foreach (SkyIslandEncounterDefinition def in SkyIslandContent.CreateFallback().Encounters)
        {
            Reset();
            SkyIslandEnemyArmory.Reset();
            string id = def.Id;
            using (var world = new World(id == "StormEcho" ? "Storm" : id))
            {
                if (id == "StormEcho") world.Saved.Add("Storm");
                if (def.Manual) Check(world.Encounters.BeginChallenge(id), "armory manual combat starts " + id);
                else world.Tick();
                Check(CharacterRandomPreset.Created.Count == def.Count, "armory group spawned " + id);
                Check(SkyIslandEnemyArmory.Characters.Count == def.Count, "every spawned enemy armed once " + id);
                for (int i = 0; i < def.Count; i++)
                {
                    Check(SkyIslandEnemyArmory.Characters[i] == CharacterRandomPreset.Created[i], id + " armed actor " + i);
                    Check(SkyIslandEnemyArmory.Tiers[i] == def.TierFor(i), id + " armed with own tier " + i);
                    if (SkyIslandBossRules.Find(id, i) != null)
                        Check(SkyIslandEnemyArmoryRules.For(SkyIslandEnemyArmory.Tiers[i]).Floor >= 5, id + " boss lead carries quality 5+");
                }
            }
        }
        SkyIslandBossForge.Night = false;

        Reset();
        SkyIslandEnemyArmory.Reset();
        var flow = new SkyIslandPreludeFlow();
        flow.SpawnForTest();
        Check(flow.Spawned != null && SkyIslandEnemyArmory.Characters.Count == 1 && SkyIslandEnemyArmory.Characters[0] == flow.Spawned
            && SkyIslandEnemyArmory.Tiers[0] == SkyIslandEnemyTier.Chief, "prelude Warden armed as chief");
        SkyIslandEnemyArmory.Reset();
        Reset();
    }
}
