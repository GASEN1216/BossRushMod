using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BossRush.Utils;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string ZombieModeAttributeMaxHealthKey = "MaxHealth";
        private const string ZombieModeAttributeMoveSpeedKey = "MoveSpeed";
        private const string ZombieModeAttributeWalkSpeedKey = "WalkSpeed";
        private const string ZombieModeAttributeRunSpeedKey = "RunSpeed";
        private const string ZombieModeAttributeMeleeDamageKey = "MeleeDamageMultiplier";
        private const string ZombieModeAttributeRangedDamageKey = "GunDamageMultiplier";
        // 官方 stat 名是 ReloadSpeedGain（CharacterMainControl.reloadSpeedGainHash）；
        // 曾误写成 "ReloadSpeedMultiplier"，该 stat 不存在，奖励被静默丢弃。
        // AttributeBonuses 是纯运行时字典、不落盘，改 key 不影响存档。
        private const string ZombieModeAttributeReloadSpeedKey = "ReloadSpeedGain";
        private const string ZombieModeAttributeDamageReductionKey = "ElementFactor_Physics";
        private const int ZombieModeContractGearDealMinQuality = 5;

        private GameObject zombieModeRewardUiRoot;
    }
    // 奖励选择面板在 ZombieModeRewardSelectionView.cs，补给 / 医疗终端的交互体与服务面板在
    // ZombieModeTemporaryNpcServiceView.cs（2026-09-23 审美审查时从宿主 partial 拆出：partial 行数预算没有余量，
    // 视图与交互体也不是宿主职责，AGENTS §4.15）。
}
