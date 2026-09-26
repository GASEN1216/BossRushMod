// ============================================================================
// SkyIslandSessionGnatBounty.cs - 苇白的「夜里驱蚋」委托：可完成量门控
// ============================================================================
// 内容批次四把云蚋、灭蚊灯、蒲扇、纱笠和蛙卵都做了，唯独没给「打下来」这条路任何回报：
// 云蚋死了不掉东西，也没有任何目标在数它。结果是驱风香（材料价值 300、整夜不来蚊子、
// 还挡风加耐力）把风晶灭蚊灯（材料价值 2790、要先点两盏灯、还要把蚋引过来）完全压住，
// 风灯「招蚋、照着的不躲」那半句也没有兑现对象——两件东西做出来没人会用。
//
// 这里把云蚋接进已有的航务委托：打下来的蚋算进苇白的第四类单子。灭蚊灯成了「铺陷阱刷进度」，
// 蒲扇成了「贴脸清场」，风灯的招蚋从缺点变成主动选择，夜里也第一次有了「值得留在岛上」的理由。
//
// 从 SkyIslandSession.cs 拆出来单独放（会话主文件贴着 1200 行预算，写法同 SkyIslandSessionRecall）：
// 主文件只在 AvailableBountyProgress 的末尾多一句转发。
//
// 纪律：
// - **只派做得完的单**（与另外三类同一条硬规则）：可完成量按「天亮之前保守还能打下几只」算，
//   不是夜里就是 0，于是白天根本不会挂出这一单。
// - 委托按出击计、不进存档：加一个方向不碰任何存档面（SCHEMA 不变）。
// - 计数只有 SkyIslandGnats.Remove(killed: true) 一个上报点，枪、灭蚊灯、蒲扇三条路都汇到那里。
// ============================================================================

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        /// <summary>
        /// 驱蚋委托这一趟还能推进多少（<see cref="AvailableBountyProgress"/> 的 <see cref="SkyIslandBountyKind.Gnats"/> 分支）。
        ///
        /// 取保守下界：没有云蚋 owner、精灵表缺失（整趟不刷蚋）或此刻不是夜里一律返回 0；
        /// 夜里按 <see cref="SkyIslandMosquitoRules.CullableBeforeDawn"/> 用「距天亮的现实秒」折算。
        /// 天亮之后这一单做不下去，玩家仍可用苇白面板上的「退掉这一单」让出委托槽。
        ///
        /// **它是四类里唯一的软门，不满足 <see cref="AvailableBountyProgress"/> 主注释里那条硬不变式**
        /// （「可完成量只随本趟进度单调递减，所以接单时 available ≥ target 就保证做得完」）：
        /// - 刷新是概率事件（`SpawnChance`），场上只数 `swarm.Alive` 还会随新群**回升**，所以这个量不单调递减；
        /// - 更实际的是**玩家自己就能把供给压没**：接单后整夜焚驱风香或站在灶火烟里，`SpawnWeight` 直接归零、
        ///   已经围上来的也全 `Scatter`（`killed=false`，不计数），预测的那些蚋就永远不来。
        ///
        /// 纯概率下的风险很小（基础权重下整夜期望供给远超一单 12 只），Wiki 也如实写了「想安静过夜就焚香，
        /// 想赚这一单就反过来把蚋引出来」。真正的兜底是 `SkyIslandBounty.TryAbandon`，那条出口必须一直挂着。
        /// </summary>
        private int AvailableGnatCull()
        {
            SkyIslandGnats swarm = SkyIslandGnats.Current;
            if (swarm == null || !swarm.Usable) return 0;
            double hours = SkyIslandLighting.ClockHours();
            if (!SkyIslandNight.IsNight(hours)) return 0;
            return SkyIslandMosquitoRules.CullableBeforeDawn(
                SkyIslandNight.RealSecondsUntilDawn(hours, SkyIslandLighting.ClockScale()), swarm.Alive);
        }

        /// <summary>
        /// 这一趟到底会不会起蚋（与此刻是不是夜里无关）。
        ///
        /// 白天四类单都挂不出来时，苇白那句「暂时没有能接的活」要靠它决定说不说「天黑再来」：
        /// 云蚋精灵表缺失的那一趟根本不刷蚋（<see cref="SkyIslandGnats.Usable"/> 为假），
        /// 那时候承诺夜里有活就是骗人。
        /// </summary>
        internal bool HasGnatBountyThisRaid
        {
            get
            {
                SkyIslandGnats swarm = SkyIslandGnats.Current;
                return swarm != null && swarm.Usable;
            }
        }

        /// <summary>此刻是不是夜里——驱蚋单只在夜里挂得出来，读钟仍只经 <see cref="SkyIslandLighting"/> 一处。</summary>
        internal bool IsNightNow { get { return SkyIslandNight.IsNight(SkyIslandLighting.ClockHours()); } }
    }
}
