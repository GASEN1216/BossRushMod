// ============================================================================
// SkyIslandSessionBosses.cs - 头目 / 岛主给岛上其它 owner 的只读查询（R1：残星匠首、瞭台观星手）
// ============================================================================
// 从 SkyIslandSession.cs 拆出来单独放：会话主文件卡在 1200 行预算上。
// 只读：不改遭遇状态、不刷怪、不写存档。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        /// <summary>
        /// 观星镜盔的「站定标敌」：把 <paramref name="radius"/> 米内活着的敌人本体填进 <paramref name="into"/>（先清空），返回个数。
        /// 调用方复用同一个列表，不在节流路径上分配。
        /// </summary>
        internal int CopyLivingEnemies(Vector3 center, float radius, List<Transform> into)
        {
            if (into == null) return 0;
            into.Clear();
            return encounters == null ? 0 : encounters.CopyLivingEnemies(center, radius, into);
        }
    }
}
