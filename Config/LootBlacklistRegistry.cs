// ============================================================================
// LootBlacklistRegistry.cs - 掉落物黑名单注册系统
// ============================================================================
// 模块说明：
//   统一管理所有物品的掉落黑名单，这些物品将从Boss随机掉落池中排除
//   
// 使用方式：
//   在 RegisterBlacklist() 方法的列表中添加物品ID和注释即可
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 掉落物黑名单注册系统
    /// </summary>
    public static class LootBlacklistRegistry
    {
        private static readonly HashSet<int> _blacklist = new HashSet<int>();
        private static bool _initialized = false;
        
        /// <summary>
        /// 检查物品ID是否在黑名单中
        /// </summary>
        public static bool Contains(int itemId)
        {
            EnsureInitialized();
            return _blacklist.Contains(itemId);
        }
        
        /// <summary>
        /// 确保黑名单已初始化
        /// </summary>
        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            RegisterBlacklist();
        }
        
        /// <summary>
        /// 注册所有黑名单物品
        /// </summary>
        private static void RegisterBlacklist()
        {
            int[] blacklistIds = new int[]
            {
                // ========== 原版未实装物品 ==========
                153,  284,  292,  293,  294,  295,  297,  299,  300,
                313,  314,  315,  316,  317,  366,  373,  375,  376,
                385,  386,  397,  427,  672,  745,  747,  750,  751,
                753,  757,  760,  761,  762,  763,  766,  774,  775,
                778,  809,  811,  814,  816,  817,  823,  824,  825,
                835,  910,  913,  1046, 1047, 1048, 1049, 1050, 1051,
                1052, 1053, 1054, 1062, 1064, 1065, 1066, 1067, 1068,
                1069, 1073, 1092, 1158, 1164, 1214, 1225, 1249, 1273,
                
                // ========== 龙裔遗族装备（Boss专属掉落） ==========
                500003,  // 赤龙首（龙头）
                500004,  // 焰鳞甲（龙甲）
                500005,  // 龙息武器（龙枪）
                
                // ========== 特殊图腾 ==========
                500010,  // 腾云驾雾 I（飞行图腾）

                // ========== 龙王专属装备 ==========
                500011,  // 龙王之冕（龙王头盔）
                500012,  // 龙王鳞铠（龙王护甲）
                500013,  // 逆鳞（龙王图腾）
            };
            
            foreach (int id in blacklistIds)
            {
                _blacklist.Add(id);
            }
        }
    }
}
