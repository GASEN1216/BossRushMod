using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using UnityEngine;

namespace BossRush
{
    /// <summary>保存并返还官方地图数据引用；若后来已有其他 owner 接管，不覆盖其新数据。</summary>
    internal sealed class StoneOutpostMapDataLease : IDisposable
    {
        private MiniMapSettings settings;
        private readonly List<MiniMapSettings.MapEntry> previousMaps;
        private readonly List<MiniMapSettings.MapEntry> ownedMaps;
        private readonly Sprite previousSprite;
        private readonly Vector3 previousCenter;
        private readonly float previousSize;
        private readonly Vector3 ownedCenter;
        private readonly float ownedSize;

        internal StoneOutpostMapDataLease(MiniMapSettings target, List<MiniMapSettings.MapEntry> maps, Vector3 center, float size)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (maps == null || maps.Count == 0) throw new ArgumentException("地图条目不能为空", nameof(maps));
            settings = target;
            previousMaps = target.maps;
            previousSprite = target.combinedSprite;
            previousCenter = target.combinedCenter;
            previousSize = target.combinedSize;
            ownedMaps = maps;
            ownedCenter = center;
            ownedSize = size;
            target.maps = maps;
            target.combinedSprite = null;
            target.combinedCenter = center;
            target.combinedSize = size;
        }

        public void Dispose()
        {
            MiniMapSettings target = settings;
            settings = null;
            if (target == null || !ReferenceEquals(target.maps, ownedMaps)) return;
            target.maps = previousMaps;
            if (target.combinedSprite == null) target.combinedSprite = previousSprite;
            if (target.combinedCenter == ownedCenter) target.combinedCenter = previousCenter;
            if (target.combinedSize == ownedSize) target.combinedSize = previousSize;
        }
    }
}
