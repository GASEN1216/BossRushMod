#!/bin/bash
# sync_wiki_zh.sh — 将 WikiContent/zh 源文件转换为 wiki-site 中文版格式
# 转换规则：
#   1. ##/###/#### 标题全部扁平化为 #
#   2. [tip] 文本 → ::: tip 块
#   3. [warn] 文本 → ::: warning 块
#   4. 其他内容保持不变

set -euo pipefail

BASE="/mnt/d/sofrware/steam/steamapps/common/Escape from Duckov/Duckov_Data/Mods/ykf/BossRushMod"
SRC="$BASE/WikiContent/zh"
DST="$BASE/wiki-site/docs"

# 创建目标目录
mkdir -p "$DST/getting-started"
mkdir -p "$DST/game-modes"
mkdir -p "$DST/bosses"
mkdir -p "$DST/npcs"
mkdir -p "$DST/equipment"
mkdir -p "$DST/items"
mkdir -p "$DST/systems"
mkdir -p "$DST/achievements"
mkdir -p "$DST/guides"
mkdir -p "$DST/maps"

# 转换函数
convert() {
    local src_file="$1"
    local dst_file="$2"

    if [ ! -f "$src_file" ]; then
        echo "WARNING: Source file not found: $src_file"
        return 1
    fi

    # 用 awk 处理转换
    awk '
    {
        # 标题扁平化: ####/###/## → #
        if (/^####+ /) {
            sub(/^####+ /, "# ")
            print
        } else if (/^### /) {
            sub(/^### /, "# ")
            print
        } else if (/^## /) {
            sub(/^## /, "# ")
            print
        }
        # [tip] 转换为 ::: tip 块
        else if (/^\[tip\] /) {
            text = $0
            sub(/^\[tip\] /, "", text)
            print "::: tip"
            print text
            print ":::"
        }
        # [warn] 转换为 ::: warning 块
        else if (/^\[warn\] /) {
            text = $0
            sub(/^\[warn\] /, "", text)
            print "::: warning"
            print text
            print ":::"
        }
        else {
            print
        }
    }
    ' "$src_file" > "$dst_file"

    echo "OK: $dst_file"
}

echo "=== 开始同步 WikiContent/zh → wiki-site/docs/ ==="
echo ""

# getting-started/ (3)
echo "--- getting-started/ ---"
convert "$SRC/start__overview.md"       "$DST/getting-started/overview.md"
convert "$SRC/start__how_to_enter.md"   "$DST/getting-started/installation.md"
convert "$SRC/start__first_run.md"      "$DST/getting-started/first-steps.md"

# game-modes/ (6)
echo "--- game-modes/ ---"
convert "$SRC/mode/mode__overview.md"   "$DST/game-modes/index.md"
convert "$SRC/mode/mode__mode_a.md"     "$DST/game-modes/standard.md"
convert "$SRC/mode/mode__mode_c.md"     "$DST/game-modes/infinite-hell.md"
convert "$SRC/mode/mode__mode_d.md"     "$DST/game-modes/mode-d.md"
convert "$SRC/mode/mode__mode_e.md"     "$DST/game-modes/mode-e.md"
convert "$SRC/mode/mode__mode_f.md"     "$DST/game-modes/mode-f.md"

# bosses/ (3)
echo "--- bosses/ ---"
convert "$SRC/boss/boss__overview.md"           "$DST/bosses/index.md"
convert "$SRC/boss/boss__dragon_descendant.md"  "$DST/bosses/dragon-descendant.md"
convert "$SRC/boss/boss__dragon_king.md"        "$DST/bosses/dragon-king.md"

# npcs/ (4)
echo "--- npcs/ ---"
convert "$SRC/npc/npc__overview.md"     "$DST/npcs/index.md"
convert "$SRC/npc/npc__goblin.md"       "$DST/npcs/goblin.md"
convert "$SRC/npc/npc__nurse.md"        "$DST/npcs/nurse.md"
convert "$SRC/npc/npc__courier.md"      "$DST/npcs/courier.md"

# equipment/ (8)
echo "--- equipment/ ---"
convert "$SRC/equipment/equipment__overview.md"         "$DST/equipment/index.md"
convert "$SRC/equipment/equipment__dragon_set.md"       "$DST/equipment/dragon-set.md"
convert "$SRC/equipment/equipment__dragon_king_set.md"  "$DST/equipment/dragon-king-set.md"
convert "$SRC/equipment/equipment__flight_totem.md"     "$DST/equipment/flight-totem.md"
convert "$SRC/equipment/equipment__reverse_scale.md"    "$DST/equipment/reverse-scale.md"
convert "$SRC/equipment/equipment__halberd.md"          "$DST/equipment/halberd.md"
convert "$SRC/equipment/equipment__dragon_breath.md"    "$DST/equipment/dragon-breath.md"
convert "$SRC/equipment/equipment__dragon_cannon.md"    "$DST/equipment/dragon-cannon.md"

# items/ (5)
echo "--- items/ ---"
convert "$SRC/item/item__overview.md"       "$DST/items/index.md"
convert "$SRC/item/item__key_items.md"      "$DST/items/key-items.md"
convert "$SRC/item/item__npc_items.md"      "$DST/items/npc-items.md"
convert "$SRC/item/item__consumables.md"    "$DST/items/consumables.md"
convert "$SRC/item/item__mode_f_items.md"   "$DST/items/mode-items.md"

# systems/ (5)
echo "--- systems/ ---"
convert "$SRC/system__rewards_and_loot.md"          "$DST/systems/loot-rewards.md"
convert "$SRC/system__reforge_and_achievements.md"  "$DST/systems/reforge.md"
convert "$SRC/system__boss_filter_and_wiki.md"      "$DST/systems/boss-filter.md"
convert "$SRC/npc/npc__affinity_and_marriage.md"    "$DST/systems/affinity-marriage.md"
convert "$SRC/config__overview.md"                  "$DST/systems/configuration.md"

# achievements/ (1)
echo "--- achievements/ ---"
convert "$SRC/system__achievements_list.md"  "$DST/achievements/index.md"

# guides/ (5)
echo "--- guides/ ---"
convert "$SRC/tips/tips__new_player_route.md"   "$DST/guides/beginner-route.md"
convert "$SRC/tips/tips__boss_fights.md"        "$DST/guides/boss-fights.md"
convert "$SRC/tips/tips__hell_and_mode_d.md"    "$DST/guides/hell-and-mode-d.md"
convert "$SRC/tips/tips__mode_e_strategy.md"    "$DST/guides/mode-e-strategy.md"
convert "$SRC/tips/tips__mode_f_strategy.md"    "$DST/guides/mode-f-strategy.md"

# maps/ (1)
echo "--- maps/ ---"
convert "$SRC/map__overview.md"  "$DST/maps/index.md"

# easter-eggs.md (1)
echo "--- other ---"
convert "$SRC/easter__kunkun.md"  "$DST/easter-eggs.md"

echo ""
echo "=== 同步完成！共处理 42 个文件 ==="
echo ""
echo "注意：changelog/index.md 保持不变（已有适合网站的版本）"
