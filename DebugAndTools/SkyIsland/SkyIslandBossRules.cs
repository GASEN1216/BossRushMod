using System;

namespace BossRush
{
    /// <summary>头目 / 岛主的招式类型：一种一个控制器，不拿同一套编排换参数（防换皮）。</summary>
    internal enum SkyIslandBossKind
    {
        /// <summary>残星匠首：G 残星工坊 · 星穹工台的岛主。</summary>
        Foreman = 0,
        /// <summary>瞭台观星手：S4 残星瞭台 · 高台的头目。</summary>
        Stargazer = 1
    }

    /// <summary>Boss 身上的一件专属装备：穿在哪个槽、是哪件、死后被抽中的权重。</summary>
    internal sealed class SkyIslandBossGearPiece
    {
        /// <summary>官方槽位 key（头盔是官方拼写 "Helmat"）。</summary>
        internal string Slot;
        internal int TypeId;
        /// <summary>整数权重，便于执行回归按种子精确计频。</summary>
        internal int Weight;
    }

    /// <summary>一件专属装备的数值：物品配置器与 Boss 配装共用这一份（价值与中英名仍只在 <see cref="SkyIslandItemRules"/>）。</summary>
    internal sealed class SkyIslandBossGearSpec
    {
        internal int TypeId;
        internal string Slot;
        /// <summary>穿上时的官方 stat（对任何穿戴者都生效：Boss 穿着就真的挡子弹）。</summary>
        internal string StatKey;
        internal float StatValue;
        /// <summary>耐久上限；0 表示不用耐久（背包）。头盔 / 护甲在 Raid 图上会被打掉耐久，归零即「破甲」。</summary>
        internal float Durability;
        internal int Quality;
        /// <summary>装备 bundle 里的 {名}_{类型} 基名（EquipmentFactory 命名规则，发布后不改）。</summary>
        internal string ModelBaseName;
        internal string IconName;
        /// <summary>`DisplayNameRaw` 本地化键（`BossRush_SkyIsland_*`，发布后不改）；描述键是它加 `_Desc`。</summary>
        internal string LocKey;
    }

    /// <summary>
    /// 一位头目 / 岛主的档案。
    /// 五栏设计说明（小环境 / 核心招式 / 克制 / 装备联动 / 串联）写在档案表每一项上方的 `// 【五栏】` 注释里——
    /// 它们只给维护者与守卫看，不编进运行时：`SkyIslandBossEcologyGuard` 解析核对每栏非空、核心招式两两不同
    /// （同一条生成管线，但不许只换参数）。中英名走 <see cref="SkyIslandBossRules.Name"/>。
    /// </summary>
    internal sealed class SkyIslandBossProfile
    {
        internal string EncounterId;
        internal int Index;
        internal SkyIslandBossKind Kind;
        internal SkyIslandEnemyTier Tier;
        internal string Id;
        /// <summary>官方击杀计数键（`Count/Kills/&lt;key&gt;` 进正式存档）：发布后冻结。</summary>
        internal string NameKey;
        /// <summary>首杀记进剧情手记（`discoveredNotes`）的 id。旗标 16 位已满，走见闻数组。</summary>
        internal string NoteId;
        /// <summary>DuckNpcs.json 的捏脸蓝图 id；空表示沿用底模 preset 的脸。</summary>
        internal string FaceId;
        internal float Health, Damage, Reaction, Scale;
        internal SkyIslandBossGearPiece[] Gear;
        /// <summary>「这一趟没掉专属装备」的权重：岛主为 0（必出一件），头目大于 0。</summary>
        internal int NoDropWeight;
    }

    /// <summary>
    /// COMPAT：天空岛头目 / 岛主的纯规则——档案表、专属装备数值、掉落抽样、相位、逃圈速度、有效血量与星工两件套的配方减耗。
    ///
    /// 【掉落口径（owner 2026-09-14 拍板）】每次都穿全套；死后按权重只留其中一件，占尸体箱一格（龙王式），
    /// 其余物品照官方尸体箱原样掉。天空岛敌人本来就 `dropBoxOnDead = true`，所以抽样只在官方建箱前的
    /// `BeforeCharacterSpawnLootOnDead` 里做一次（<see cref="SkyIslandBossLoot"/>），不经过 BossRush 奖励箱、不需要 defer 协议。
    ///
    /// 纯逻辑、无 Unity 依赖：隔离回归直接执行（tests/fixtures/SkyIslandStory）。
    /// </summary>
    internal static class SkyIslandBossRules
    {
        /// <summary>装备 bundle 名（Assets/Equipment/ 下，compile_official.bat 部署段要登记）。</summary>
        internal const string GearBundle = "skyisland_boss_gear";

        internal const string ForemanNote = "Lord_Foreman";
        internal const string StargazerNote = "Chief_Stargazer";

        /// <summary>有效血量排序用的参考穿甲：官方护甲公式 2/(clamp(护甲-穿甲)+2)。</summary>
        internal const double ReferencePierce = 2.0;

        // ---- 残星匠首 ----
        /// <summary>血线跨过即立一轮供能桩。三档，不与噬风的四档混用。</summary>
        internal static readonly float[] ForemanPhaseThresholds = { 0.70f, 0.40f, 0.15f };
        internal const int PylonCount = 2;
        internal const float PylonHealth = 45f;
        /// <summary>桩的寿命：到时自灭，避免桩落在够不着的地方把战斗卡死。</summary>
        internal const float PylonLifetime = 25f;
        internal const float PylonMinDistance = 5.5f;
        internal const float PylonMaxDistance = 8.5f;
        /// <summary>桩存活期间加在匠首头甲与身甲上的护甲值；星炉背甲被打穿后只剩一半。</summary>
        internal const float ShieldArmor = 6f;
        internal const float ShieldArmorBroken = 3f;
        internal const float StarfireRadius = 2.6f;
        internal const float StarfireTelegraph = 1.25f;
        internal const float StarfireDamage = 24f;
        /// <summary>两侧星焰圈离中心圈的横向距离：封左右走位，但中间留得出缝。</summary>
        internal const float StarfireSpread = 3.6f;
        internal const float StarfireInterval = 6.5f;
        internal const float StarfireRange = 26f;
        internal const int CastsBeforeOverheat = 2;
        internal const float OverheatSeconds = 4f;
        /// <summary>过热期间物理伤害系数加这么多（ElementFactor_Physics，官方口径）。</summary>
        internal const float OverheatDamageTaken = 0.25f;

        // ---- 瞭台观星手 ----
        internal const float MarkRange = 42f;
        /// <summary>贴到这么近它就不再标记，交给官方 AI 常规射击：给「冲上去」这条解法。</summary>
        internal const float MarkCloseRange = 8f;
        /// <summary>标记圈先跟着玩家瞄准，再原地锁定：逃圈只算锁定那一段。</summary>
        internal const float MarkAimSeconds = 0.8f;
        internal const float MarkLockSeconds = 0.6f;
        internal const float FlareRadius = 2.2f;
        internal const float FlareDamage = 20f;
        internal const int FlareShots = 2;
        internal const float FlareShotGap = 0.4f;
        internal const float MarkCooldown = 6.5f;

        /// <summary>玩家逃出一个预警圈所需的最低速度（米/秒）。与噬风同一条判据：必须 ≤ 5.5（正常跑动可达）。</summary>
        internal const float MaxEscapeSpeed = 5.5f;

        private static readonly SkyIslandBossGearSpec[] gearSpecs =
        {
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandStarbrassVisorHelm, Slot = "Helmat", StatKey = "HeadArmor", StatValue = 3f,
                Durability = 70f, Quality = 5, ModelBaseName = "StarbrassVisor_Helmet", IconName = "sky_island_starbrass_visor_helm",
                LocKey = "BossRush_SkyIsland_StarbrassVisorHelm"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandStarfurnaceHarness, Slot = "Armor", StatKey = "BodyArmor", StatValue = 3f,
                Durability = 100f, Quality = 5, ModelBaseName = "StarfurnaceHarness_Armor", IconName = "sky_island_starfurnace_harness",
                LocKey = "BossRush_SkyIsland_StarfurnaceHarness"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandStarfurnacePack, Slot = "Backpack", StatKey = "InventoryCapacity", StatValue = 6f,
                Durability = 0f, Quality = 5, ModelBaseName = "StarfurnacePack_Backpack", IconName = "sky_island_starfurnace_pack",
                LocKey = "BossRush_SkyIsland_StarfurnacePack"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandStargazerLensHelm, Slot = "Helmat", StatKey = "HeadArmor", StatValue = 2f,
                Durability = 50f, Quality = 5, ModelBaseName = "StargazerLens_Helmet", IconName = "sky_island_stargazer_lens_helm",
                LocKey = "BossRush_SkyIsland_StargazerLensHelm"
            }
        };

        private static readonly SkyIslandBossProfile[] profiles =
        {
            // 【五栏】小环境：G 残星工坊 · 星穹工台，星灯与工台之间的开阔地
            // 【五栏】核心招式：立星炉供能桩给自己加护甲，星焰落点封左右走位，放完两招星炉过热
            // 【五栏】克制：先打掉供能桩（25 秒自灭）；抓过热那 4 秒输出；星焰圈 1.25 秒预警，从两圈之间的缝走
            // 【五栏】装备联动：星铜护目盔被爆头打穿，星焰只落 1 处；星炉背甲耐久打空，供能桩给的护甲减半
            // 【五栏】串联：G 组带队，修星灯本来就要清 G 组；首杀记手记 Lord_Foreman；星工装备任意两件在渡口工台做配方少耗 1 片残铜片
            new SkyIslandBossProfile
            {
                EncounterId = "G", Index = 0, Kind = SkyIslandBossKind.Foreman, Tier = SkyIslandEnemyTier.Lord, Id = "Foreman",
                NameKey = "BossRush_SkyIsland_Lord_Foreman", NoteId = ForemanNote, FaceId = string.Empty,
                Health = 7.0f, Damage = 1.65f, Reaction = 1.55f, Scale = 1.12f, NoDropWeight = 0,
                Gear = new[]
                {
                    Piece("Helmat", BossRushItemIds.SkyIslandStarbrassVisorHelm, 35),
                    Piece("Armor", BossRushItemIds.SkyIslandStarfurnaceHarness, 35),
                    Piece("Backpack", BossRushItemIds.SkyIslandStarfurnacePack, 30)
                }
            },
            // 【五栏】小环境：S4 残星瞭台 · 群岛最高的观星台
            // 【五栏】核心招式：有视线时远程标记玩家，锁定后在标记处落两发星火
            // 【五栏】克制：躲进掩体断开视线就打断标记；贴到 8 米内它不再标记；锁定后 0.6 秒内出圈
            // 【五栏】装备联动：观星镜盔被爆头打穿，它再也标记不了
            // 【五栏】串联：S4 组带队，校准观星镜本来就要清 S4 组；首杀记手记 Chief_Stargazer；玩家戴上观星镜盔站定 2 秒会标出 40 米内的敌人
            new SkyIslandBossProfile
            {
                EncounterId = "S4", Index = 0, Kind = SkyIslandBossKind.Stargazer, Tier = SkyIslandEnemyTier.Chief, Id = "Stargazer",
                NameKey = "BossRush_SkyIsland_Chief_Stargazer", NoteId = StargazerNote, FaceId = string.Empty,
                Health = 3.4f, Damage = 1.45f, Reaction = 1.38f, Scale = 1.0f, NoDropWeight = 70,
                Gear = new[] { Piece("Helmat", BossRushItemIds.SkyIslandStargazerLensHelm, 30) }
            }
        };

        internal static SkyIslandBossProfile[] Profiles { get { return profiles; } }
        internal static SkyIslandBossGearSpec[] GearSpecs { get { return gearSpecs; } }

        /// <summary>全部专属装备 TypeID（按 TypeID 递增）。</summary>
        internal static readonly int[] AllGearTypeIds =
        {
            BossRushItemIds.SkyIslandStarbrassVisorHelm, BossRushItemIds.SkyIslandStarfurnaceHarness,
            BossRushItemIds.SkyIslandStarfurnacePack, BossRushItemIds.SkyIslandStargazerLensHelm
        };

        /// <summary>这一组第 <paramref name="index"/> 名是不是头目 / 岛主。不是返回 null（走普通档次装饰）。</summary>
        internal static SkyIslandBossProfile Find(string encounterId, int index)
        {
            if (string.IsNullOrEmpty(encounterId)) return null;
            for (int i = 0; i < profiles.Length; i++)
                if (profiles[i].Index == index && string.Equals(profiles[i].EncounterId, encounterId, StringComparison.Ordinal))
                    return profiles[i];
            return null;
        }

        internal static SkyIslandBossGearSpec GearSpec(int typeId)
        {
            for (int i = 0; i < gearSpecs.Length; i++)
                if (gearSpecs[i].TypeId == typeId) return gearSpecs[i];
            return null;
        }

        /// <summary>首杀手记 id 是否登记过（剧情存档只收登记过的 id）。</summary>
        internal static bool IsBossNote(string id)
        {
            for (int i = 0; i < profiles.Length; i++)
                if (string.Equals(profiles[i].NoteId, id, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static SkyIslandBossProfile FindByNote(string id)
        {
            for (int i = 0; i < profiles.Length; i++)
                if (string.Equals(profiles[i].NoteId, id, StringComparison.Ordinal)) return profiles[i];
            return null;
        }

        /// <summary>中英名对照表的中文那一半（取用走 <see cref="Name"/>）。改名要连 Wiki 与首杀字幕一起改。</summary>
        internal static string NameCn(SkyIslandBossKind kind)
        {
            switch (kind)
            {
                case SkyIslandBossKind.Foreman: return "残星匠首";
                case SkyIslandBossKind.Stargazer: return "瞭台观星手";
                default: return "头目";
            }
        }

        internal static string NameEn(SkyIslandBossKind kind)
        {
            switch (kind)
            {
                case SkyIslandBossKind.Foreman: return "Starforge Foreman";
                case SkyIslandBossKind.Stargazer: return "Overlook Stargazer";
                default: return "Chief";
            }
        }

        /// <summary>按当前语言取名（玩家能在游戏里切语言，取用时解析）。</summary>
        internal static string Name(SkyIslandBossProfile profile)
        {
            return profile == null ? string.Empty : L10n.T(NameCn(profile.Kind), NameEn(profile.Kind));
        }

        /// <summary>
        /// 死后留哪一件：返回 <see cref="SkyIslandBossProfile.Gear"/> 的下标，-1 表示这一趟不掉专属装备。
        /// 权重顺序是「各件依次，最后是不掉」；<paramref name="roll"/> 取 [0, 1)，越界夹回。
        /// </summary>
        internal static int RollDrop(SkyIslandBossProfile profile, double roll)
        {
            if (profile == null || profile.Gear == null) return -1;
            int total = Math.Max(0, profile.NoDropWeight);
            for (int i = 0; i < profile.Gear.Length; i++) total += Math.Max(0, profile.Gear[i].Weight);
            if (total <= 0) return -1;
            if (double.IsNaN(roll) || roll < 0.0) roll = 0.0;
            double pick = Math.Min(roll, 0.999999999) * total;
            int accumulated = 0;
            for (int i = 0; i < profile.Gear.Length; i++)
            {
                accumulated += Math.Max(0, profile.Gear[i].Weight);
                if (pick < accumulated) return i;
            }
            return -1;
        }

        /// <summary>这一件的掉率（0..1）。</summary>
        internal static double DropChance(SkyIslandBossProfile profile, int index)
        {
            if (profile == null || profile.Gear == null || index < 0 || index >= profile.Gear.Length) return 0.0;
            int total = Math.Max(0, profile.NoDropWeight);
            for (int i = 0; i < profile.Gear.Length; i++) total += Math.Max(0, profile.Gear[i].Weight);
            return total <= 0 ? 0.0 : Math.Max(0, profile.Gear[index].Weight) / (double)total;
        }

        /// <summary>给定血量比例应处的相位序号（0 = 还没跨过第一档）。</summary>
        internal static int PhaseFor(float fraction, float[] thresholds)
        {
            if (thresholds == null) return 0;
            int result = 0;
            for (int i = 0; i < thresholds.Length; i++) if (fraction <= thresholds[i]) result = i + 1;
            return result;
        }

        /// <summary>逃出半径 <paramref name="radius"/> 的圈、预警 <paramref name="telegraph"/> 秒时的最低速度。</summary>
        internal static float EscapeSpeed(float radius, float telegraph)
        {
            return telegraph <= 0f ? float.PositiveInfinity : radius / telegraph;
        }

        /// <summary>官方护甲减伤系数（`Health.Hurt`）：2 / (clamp(护甲 - 穿甲, 0, 999) + 2)，没有护甲时为 1。</summary>
        internal static double ArmorFactor(double armor, double pierce)
        {
            if (armor <= 0.0) return 1.0;
            return 2.0 / (Math.Max(0.0, Math.Min(999.0, armor - pierce)) + 2.0);
        }

        /// <summary>按参考穿甲折算的有效血量倍率：血量倍率 ÷ 身甲减伤系数。档次排序用它，而不是只看血量倍率。</summary>
        internal static double EffectiveHealth(SkyIslandBossProfile profile)
        {
            if (profile == null) return 0.0;
            double bodyArmor = 0.0;
            if (profile.Gear != null)
                for (int i = 0; i < profile.Gear.Length; i++)
                {
                    SkyIslandBossGearSpec spec = GearSpec(profile.Gear[i].TypeId);
                    if (spec != null && spec.StatKey == "BodyArmor") bodyArmor += spec.StatValue;
                }
            return profile.Health / ArmorFactor(bodyArmor, ReferencePierce);
        }

        /// <summary>这份存档是否已经打倒过这位头目 / 岛主（首杀手记在不在）。只管首杀字幕与手记进度，不影响刷新与掉落。</summary>
        internal static bool Defeated(SkyIslandStoryData data, SkyIslandBossProfile profile)
        {
            return data != null && profile != null && data.discoveredNotes != null &&
                Array.IndexOf(data.discoveredNotes, profile.NoteId) >= 0;
        }

        internal static int DefeatedCount(SkyIslandStoryData data)
        {
            int count = 0;
            for (int i = 0; i < profiles.Length; i++) if (Defeated(data, profiles[i])) count++;
            return count;
        }

        /// <summary>手记「旅程进度」一行：「 · 岛主与头目 1/2」。</summary>
        internal static string ProgressLine(SkyIslandStoryData data)
        {
            return L10n.T(" · 岛主与头目 ", " · island lords and chiefs ") + DefeatedCount(data) + "/" + profiles.Length;
        }

        /// <summary>首杀那一刻的字幕：它是谁、身上那套装备以后怎么拿。</summary>
        internal static string FirstKillCaption(SkyIslandBossProfile profile)
        {
            if (profile == null) return string.Empty;
            switch (profile.Kind)
            {
                case SkyIslandBossKind.Foreman:
                    return L10n.T("第一次打倒残星匠首：它每趟都会回到工坊，每次都穿着全套星工装备，倒下时只留下其中一件。",
                        "The Starforge Foreman is down for the first time. It returns to the workshop every raid in its full Starworks set and leaves one piece behind each time it falls.");
                case SkyIslandBossKind.Stargazer:
                    return L10n.T("第一次打倒瞭台观星手：它每趟都会回到瞭台，倒下时有三成机会留下观星镜盔。",
                        "The overlook stargazer is down for the first time. It returns to the overlook every raid and leaves its lens helm behind three times in ten.");
                default:
                    return L10n.T("头目倒下了。", "The chief is down.");
            }
        }

        /// <summary>主角身上穿着几件星工装备（头盔 / 护甲 / 背包三槽各传当前 TypeID，没穿传 0）。</summary>
        internal static int StarworksPiecesWorn(int helmTypeId, int armorTypeId, int packTypeId)
        {
            int count = 0;
            if (helmTypeId == BossRushItemIds.SkyIslandStarbrassVisorHelm) count++;
            if (armorTypeId == BossRushItemIds.SkyIslandStarfurnaceHarness) count++;
            if (packTypeId == BossRushItemIds.SkyIslandStarfurnacePack) count++;
            return count;
        }

        /// <summary>星工两件套：渡口工台配方里的残铜片少耗 1 片（至少还要 1 片）。其余材料与其他台子不变。</summary>
        internal static int BrassScrapCost(int baseCost, int starworksPiecesWorn)
        {
            if (baseCost <= 1 || starworksPiecesWorn < 2) return baseCost;
            return baseCost - 1;
        }

        /// <summary>
        /// 居民说起头目 / 岛主：浮舟谈匠首和那身星工装备，苇白谈瞭台上的观星手。
        /// 没打倒过说「在哪、怎么躲」，打倒过说装备拿去做什么。浮舟那句以换行开头，苇白那句以换行结尾（与各自台词的拼接口径一致）。
        /// </summary>
        internal static string ResidentLine(string npcId, SkyIslandStoryData data)
        {
            switch (npcId)
            {
                case "sky_fuzhou":
                    return Defeated(data, Find("G", 0))
                        ? L10n.T("\n匠首那身星工装备，凑齐任意两件穿上再来：渡口工台做东西能省一片残铜片。",
                            "\nThat Starworks gear the Foreman wears — put on any two pieces and come back: the dock workbench takes one less brass scrap.")
                        : L10n.T("\n残星工坊的星灯边上守着一个穿全套星工装备的家伙，每趟都在。它立起供能桩就先拆桩，炉子过热那几秒最好打。",
                            "\nSomeone in a full Starworks outfit guards the star lamp at the Fallen Star Workshop, every raid. When it raises its pylons, break those first; hit it hardest while its furnace overheats.");
                case "sky_weibai":
                    return Defeated(data, Find("S4", 0))
                        ? L10n.T("观星手那顶镜盔要是落到你手里，戴上站定一会儿，远处的人影会自己亮起来。\n",
                            "If that stargazer's lens helm ever lands in your hands, wear it and stand still a moment — distant figures light up on their own.\n")
                        : L10n.T("残星瞭台上有个观星手，被它盯上脚下会亮一圈——躲到掩体后面断开视线，或者干脆冲到它跟前。\n",
                            "There is a stargazer up on the Starfall Overlook. When it marks you, a ring lights up at your feet — break its line of sight behind cover, or rush right up to it.\n");
                default:
                    return string.Empty;
            }
        }

        private static SkyIslandBossGearPiece Piece(string slot, int typeId, int weight)
        {
            return new SkyIslandBossGearPiece { Slot = slot, TypeId = typeId, Weight = weight };
        }
    }
}
