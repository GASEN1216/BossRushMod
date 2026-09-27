using System;
using System.Collections.Generic;
using Duckov.UI.DialogueBubbles;
using UnityEngine;

namespace BossRush
{
    public enum BossRushTrackedLootboxMode
    {
        None = 0,
        ModeE = 1,
        ModeF = 2
    }

    internal sealed class AwenLootSweepTarget
    {
        public InteractableLootbox Lootbox;
        public Vector3 VisitPosition;
    }


}
