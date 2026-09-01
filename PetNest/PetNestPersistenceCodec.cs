// ============================================================================
// PetNestPersistenceCodec.cs - 遗种巢 DTO <-> JSON 编解码（实施计划 步骤 2）
// ============================================================================
// 与 PetNestPersistence.cs 拆开只为单文件行数预算；契约是同一份：
//   - 手写编解码，不用反射：字段名是存档契约的一部分，必须能被 grep 与 guard 看见；
//   - 解码一律给默认值兜底，缺字段不算错误（SCHEMA+ 向后兼容扩展的基础）；
//   - 解码出的对象立刻 Normalize()，容器不留 null；
//   - 解码抛异常 -> 返回 null -> 上层进写屏障，绝不覆盖原 key。
//
// 字段名一旦发布即冻结（只增不改）。新增字段必须是可选的，老档缺失时走默认值。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>遗种巢三个 payload 的编解码。无状态。</summary>
    internal static class PetNestCodec
    {
        #region 默认值

        internal static PetNestNestData CreateDefaultNest()
        {
            PetNestNestData data = new PetNestNestData();
            data.Normalize();
            return data;
        }

        internal static PetNestExpeditionData CreateDefaultExpedition()
        {
            PetNestExpeditionData data = new PetNestExpeditionData();
            data.Normalize();
            return data;
        }

        internal static PetNestMuseumData CreateDefaultMuseum()
        {
            PetNestMuseumData data = new PetNestMuseumData();
            data.Normalize();
            return data;
        }

        internal static PetNestBundleData CreateDefaultBundle()
        {
            PetNestBundleData data = new PetNestBundleData();
            data.generation = 0;
            data.nest = CreateDefaultNest();
            data.expedition = CreateDefaultExpedition();
            data.museum = CreateDefaultMuseum();
            return data;
        }

        #endregion

        #region v2 聚合包

        internal static string EncodeBundle(PetNestBundleData data)
        {
            if (data == null) return null;
            data.Normalize();
            PetNestJsonBuilder sb = new PetNestJsonBuilder();
            sb.BeginObject()
              .Int("schemaVersion", PetNestTuning.BundleSchemaVersion)
              .Int("generation", data.generation)
              .Raw("nest", EncodeNest(data.nest))
              .Raw("expedition", EncodeExpedition(data.expedition))
              .Raw("museum", EncodeMuseum(data.museum))
              .EndObject();
            return sb.ToString();
        }

        internal static PetNestBundleData DecodeBundle(PetNestJsonNode root)
        {
            if (root == null || root.Kind != PetNestJsonKind.Object) return null;
            if (root.GetInt("schemaVersion", -1) != PetNestTuning.BundleSchemaVersion) return null;
            PetNestBundleData data = new PetNestBundleData();
            data.generation = root.GetInt("generation", 0);
            data.nest = DecodeNest(root.GetObject("nest"));
            data.expedition = DecodeExpedition(root.GetObject("expedition"));
            data.museum = DecodeMuseum(root.GetObject("museum"));
            if (data.nest == null || data.expedition == null || data.museum == null) return null;
            data.Normalize();
            return data;
        }

        internal static PetNestBundleData CloneBundle(PetNestBundleData source)
        {
            if (source == null) return CreateDefaultBundle();
            string json = EncodeBundle(source);
            PetNestBundleData clone = DecodeBundle(PetNestJson.Parse(json));
            return clone ?? CreateDefaultBundle();
        }

        internal static PetNestNestData CloneNest(PetNestNestData source)
        {
            PetNestNestData clone = DecodeNest(PetNestJson.Parse(EncodeNest(source)));
            return clone ?? CreateDefaultNest();
        }

        internal static PetNestExpeditionData CloneExpedition(PetNestExpeditionData source)
        {
            PetNestExpeditionData clone = DecodeExpedition(PetNestJson.Parse(EncodeExpedition(source)));
            return clone ?? CreateDefaultExpedition();
        }

        internal static PetNestMuseumData CloneMuseum(PetNestMuseumData source)
        {
            PetNestMuseumData clone = DecodeMuseum(PetNestJson.Parse(EncodeMuseum(source)));
            return clone ?? CreateDefaultMuseum();
        }

        #endregion

        #region 巢

        internal static string EncodeNest(PetNestNestData data)
        {
            PetNestJsonBuilder sb = new PetNestJsonBuilder();
            sb.BeginObject()
              .Str("deployedPetId", data.deployedPetId)
              .Int("capacity", data.capacity)
              .Int("nameSerial", data.nameSerial);

            sb.BeginArray("pets");
            List<PetNestPetRecord> pets = data.pets;
            if (pets != null)
            {
                for (int i = 0; i < pets.Count; i++)
                {
                    EncodePet(sb, pets[i]);
                }
            }
            sb.EndArray();

            sb.BeginArray("soulLedger");
            List<PetNestSoulLedgerEntry> ledger = data.soulLedger;
            if (ledger != null)
            {
                for (int i = 0; i < ledger.Count; i++)
                {
                    PetNestSoulLedgerEntry e = ledger[i];
                    if (e == null) continue;
                    sb.BeginObject()
                      .Str("lineageKey", e.lineageKey)
                      .Int("souls", e.souls)
                      .EndObject();
                }
            }
            sb.EndArray();

            sb.EndObject();
            return sb.ToString();
        }

        private static void EncodePet(PetNestJsonBuilder sb, PetNestPetRecord pet)
        {
            if (pet == null) return;
            sb.BeginObject()
              .Str("id", pet.id)
              .Str("lineageKey", pet.lineageKey)
              .Str("displayName", pet.displayName)
              .Long("birthTicks", pet.birthTicks)
              .Int("level", pet.level)
              .Int("exp", pet.exp)
              .Bool("shiny", pet.shiny)
              .Str("personalityId", pet.personalityId)
              .Int("state", pet.state)
              .Str("lockedByExpeditionId", pet.lockedByExpeditionId)
              .Int("careerCount", pet.careerCount)
              .Int("expeditionCount", pet.expeditionCount)
              .Int("mergedOldScarCount", pet.mergedOldScarCount);

            sb.BeginArray("talents");
            if (pet.talents != null)
            {
                for (int i = 0; i < pet.talents.Count; i++)
                {
                    PetNestTalentEntry t = pet.talents[i];
                    if (t == null) continue;
                    sb.BeginObject()
                      .Str("id", t.id)
                      .Str("statKey", t.statKey)
                      .Num("value", t.value)
                      .Bool("percentage", t.percentage)
                      .EndObject();
                }
            }
            sb.EndArray();

            sb.BeginArray("scars");
            if (pet.scars != null)
            {
                for (int i = 0; i < pet.scars.Count; i++)
                {
                    PetNestScarRecord s = pet.scars[i];
                    if (s == null) continue;
                    sb.BeginObject()
                      .Long("ticks", s.ticks)
                      .Str("place", s.place)
                      .Str("killer", s.killer)
                      .Str("statKey", s.statKey)
                      .Num("percent", s.percent)
                      .EndObject();
                }
            }
            sb.EndArray();

            if (pet.adultSnapshot != null)
            {
                PetNestAdultSnapshot a = pet.adultSnapshot;
                sb.BeginObject("adultSnapshot")
                  .Long("ticks", a.ticks)
                  .Int("level", a.level)
                  .Num("maxHealth", a.maxHealth)
                  .Num("damageFactor", a.damageFactor)
                  .Str("personalityId", a.personalityId)
                  .Int("careerCount", a.careerCount)
                  .Int("scarCount", a.scarCount)
                  .EndObject();
            }

            sb.EndObject();
        }

        internal static PetNestNestData DecodeNest(PetNestJsonNode payload)
        {
            if (payload == null) return null;
            PetNestNestData data = new PetNestNestData();
            data.deployedPetId = payload.GetString("deployedPetId", null);
            data.capacity = payload.GetInt("capacity", PetNestTuning.DefaultNestCapacity);
            data.nameSerial = payload.GetInt("nameSerial", 0);

            data.pets = new List<PetNestPetRecord>();
            List<PetNestJsonNode> petNodes = payload.GetArray("pets");
            for (int i = 0; i < petNodes.Count; i++)
            {
                PetNestPetRecord pet = DecodePet(petNodes[i]);
                if (pet != null) data.pets.Add(pet);
            }

            data.soulLedger = new List<PetNestSoulLedgerEntry>();
            List<PetNestJsonNode> ledgerNodes = payload.GetArray("soulLedger");
            for (int i = 0; i < ledgerNodes.Count; i++)
            {
                PetNestJsonNode n = ledgerNodes[i];
                if (n == null || n.Kind != PetNestJsonKind.Object) continue;
                string lineageKey = n.GetString("lineageKey", null);
                if (string.IsNullOrEmpty(lineageKey)) continue;
                PetNestSoulLedgerEntry e = new PetNestSoulLedgerEntry();
                e.lineageKey = lineageKey;
                e.souls = n.GetInt("souls", 0);
                data.soulLedger.Add(e);
            }

            data.Normalize();
            return data;
        }

        private static PetNestPetRecord DecodePet(PetNestJsonNode node)
        {
            if (node == null || node.Kind != PetNestJsonKind.Object) return null;
            string id = node.GetString("id", null);
            if (string.IsNullOrEmpty(id)) return null;

            PetNestPetRecord pet = new PetNestPetRecord();
            pet.id = id;
            pet.lineageKey = node.GetString("lineageKey", null);
            pet.displayName = node.GetString("displayName", null);
            pet.birthTicks = node.GetLong("birthTicks", 0L);
            pet.level = node.GetInt("level", 1);
            pet.exp = node.GetInt("exp", 0);
            pet.shiny = node.GetBool("shiny", false);
            pet.personalityId = node.GetString("personalityId", null);
            pet.state = node.GetInt("state", (int)PetNestPetState.InNest);
            pet.lockedByExpeditionId = node.GetString("lockedByExpeditionId", null);
            pet.careerCount = node.GetInt("careerCount", 0);
            pet.expeditionCount = node.GetInt("expeditionCount", 0);
            pet.mergedOldScarCount = node.GetInt("mergedOldScarCount", 0);

            pet.talents = new List<PetNestTalentEntry>();
            List<PetNestJsonNode> talentNodes = node.GetArray("talents");
            for (int i = 0; i < talentNodes.Count; i++)
            {
                PetNestJsonNode t = talentNodes[i];
                if (t == null || t.Kind != PetNestJsonKind.Object) continue;
                PetNestTalentEntry entry = new PetNestTalentEntry();
                entry.id = t.GetString("id", null);
                entry.statKey = t.GetString("statKey", null);
                entry.value = t.GetFloat("value", 0f);
                entry.percentage = t.GetBool("percentage", true);
                pet.talents.Add(entry);
            }

            pet.scars = new List<PetNestScarRecord>();
            List<PetNestJsonNode> scarNodes = node.GetArray("scars");
            for (int i = 0; i < scarNodes.Count; i++)
            {
                PetNestJsonNode s = scarNodes[i];
                if (s == null || s.Kind != PetNestJsonKind.Object) continue;
                PetNestScarRecord scar = new PetNestScarRecord();
                scar.ticks = s.GetLong("ticks", 0L);
                scar.place = s.GetString("place", null);
                scar.killer = s.GetString("killer", null);
                scar.statKey = s.GetString("statKey", null);
                scar.percent = s.GetFloat("percent", 0f);
                pet.scars.Add(scar);
            }

            PetNestJsonNode adult = node.GetObject("adultSnapshot");
            if (adult != null)
            {
                PetNestAdultSnapshot a = new PetNestAdultSnapshot();
                a.ticks = adult.GetLong("ticks", 0L);
                a.level = adult.GetInt("level", 1);
                a.maxHealth = adult.GetFloat("maxHealth", 0f);
                a.damageFactor = adult.GetFloat("damageFactor", 1f);
                a.personalityId = adult.GetString("personalityId", null);
                a.careerCount = adult.GetInt("careerCount", 0);
                a.scarCount = adult.GetInt("scarCount", 0);
                pet.adultSnapshot = a;
            }

            pet.Normalize();
            return pet;
        }

        #endregion

        #region 远征

        internal static string EncodeExpedition(PetNestExpeditionData data)
        {
            PetNestJsonBuilder sb = new PetNestJsonBuilder();
            sb.BeginObject()
              .Int("idSerial", data.idSerial);

            sb.BeginArray("records");
            List<PetNestExpeditionRecord> records = data.records;
            if (records != null)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    PetNestExpeditionRecord r = records[i];
                    if (r == null) continue;
                    sb.BeginObject()
                      .Str("id", r.id)
                      .Str("petId", r.petId)
                      .Str("petDisplayName", r.petDisplayName)
                      .Str("destinationId", r.destinationId)
                      .Int("riskTier", r.riskTier)
                      .Long("departTicks", r.departTicks)
                      .Long("returnTicks", r.returnTicks)
                      .Num("deathRate", r.deathRate)
                      .Num("successRate", r.successRate)
                      .Bool("settled", r.settled)
                      .Bool("revealed", r.revealed)
                      .Bool("rewardsGranted", r.rewardsGranted)
                      // 按条目记账三格（schemaVersion 不变：老档缺这三个键时
                      // 解码回落到默认值，语义与旧行为一致，属向后兼容扩展）
                      .Bool("cashGranted", r.cashGranted)
                      .Int("grantedLootUnits", r.grantedLootUnits)
                      .Int("rewardGrantAttempts", r.rewardGrantAttempts)
                      .Bool("outcomeDead", r.outcomeDead)
                      .Bool("outcomeInjured", r.outcomeInjured)
                      .Long("outcomeCash", r.outcomeCash);

                    sb.BeginArray("outcomeLootTypeIds");
                    if (r.outcomeLootTypeIds != null)
                    {
                        for (int k = 0; k < r.outcomeLootTypeIds.Count; k++)
                        {
                            sb.ItemInt(r.outcomeLootTypeIds[k]);
                        }
                    }
                    sb.EndArray();

                    sb.BeginArray("outcomeLootCounts");
                    if (r.outcomeLootCounts != null)
                    {
                        for (int k = 0; k < r.outcomeLootCounts.Count; k++)
                        {
                            sb.ItemInt(r.outcomeLootCounts[k]);
                        }
                    }
                    sb.EndArray();

                    sb.EndObject();
                }
            }
            sb.EndArray();

            sb.EndObject();
            return sb.ToString();
        }

        internal static PetNestExpeditionData DecodeExpedition(PetNestJsonNode payload)
        {
            if (payload == null) return null;
            PetNestExpeditionData data = new PetNestExpeditionData();
            data.idSerial = payload.GetInt("idSerial", 0);
            data.records = new List<PetNestExpeditionRecord>();

            List<PetNestJsonNode> nodes = payload.GetArray("records");
            for (int i = 0; i < nodes.Count; i++)
            {
                PetNestJsonNode n = nodes[i];
                if (n == null || n.Kind != PetNestJsonKind.Object) continue;
                string id = n.GetString("id", null);
                if (string.IsNullOrEmpty(id)) continue;

                PetNestExpeditionRecord r = new PetNestExpeditionRecord();
                r.id = id;
                r.petId = n.GetString("petId", null);
                r.petDisplayName = n.GetString("petDisplayName", null);
                r.destinationId = n.GetString("destinationId", null);
                r.riskTier = n.GetInt("riskTier", (int)PetNestRiskTier.Safe);
                r.departTicks = n.GetLong("departTicks", 0L);
                r.returnTicks = n.GetLong("returnTicks", 0L);
                r.deathRate = n.GetFloat("deathRate", 0f);
                r.successRate = n.GetFloat("successRate", 0f);
                r.settled = n.GetBool("settled", false);
                r.revealed = n.GetBool("revealed", false);
                r.rewardsGranted = n.GetBool("rewardsGranted", false);
                // 老档没有这三个键：默认值等价于"整笔都还没发过"，
                // 而老档的已发放记录 rewardsGranted 已是 true，补发通道不会重入。
                r.cashGranted = n.GetBool("cashGranted", false);
                r.grantedLootUnits = n.GetInt("grantedLootUnits", 0);
                r.rewardGrantAttempts = n.GetInt("rewardGrantAttempts", 0);
                r.outcomeDead = n.GetBool("outcomeDead", false);
                r.outcomeInjured = n.GetBool("outcomeInjured", false);
                r.outcomeCash = n.GetLong("outcomeCash", 0L);

                r.outcomeLootTypeIds = new List<int>();
                List<PetNestJsonNode> ids = n.GetArray("outcomeLootTypeIds");
                for (int k = 0; k < ids.Count; k++)
                {
                    r.outcomeLootTypeIds.Add(ids[k].AsInt(0));
                }

                r.outcomeLootCounts = new List<int>();
                List<PetNestJsonNode> counts = n.GetArray("outcomeLootCounts");
                for (int k = 0; k < counts.Count; k++)
                {
                    r.outcomeLootCounts.Add(counts[k].AsInt(0));
                }

                r.Normalize();
                data.records.Add(r);
            }

            data.Normalize();
            return data;
        }

        #endregion

        #region 博物馆

        internal static string EncodeMuseum(PetNestMuseumData data)
        {
            PetNestJsonBuilder sb = new PetNestJsonBuilder();
            sb.BeginObject()
              .Int("mergedMemorialCount", data.mergedMemorialCount);

            sb.BeginArray("lineages");
            List<PetNestLineageStats> lineages = data.lineages;
            if (lineages != null)
            {
                for (int i = 0; i < lineages.Count; i++)
                {
                    PetNestLineageStats s = lineages[i];
                    if (s == null) continue;
                    sb.BeginObject()
                      .Str("lineageKey", s.lineageKey)
                      .Int("kills", s.kills)
                      .Int("hatched", s.hatched)
                      .Int("shinyHatched", s.shinyHatched)
                      .Int("maxLevel", s.maxLevel)
                      .Int("expeditions", s.expeditions)
                      .Bool("unlocked", s.unlocked)
                      .EndObject();
                }
            }
            sb.EndArray();

            sb.BeginArray("memorials");
            List<PetNestMemorialEntry> memorials = data.memorials;
            if (memorials != null)
            {
                for (int i = 0; i < memorials.Count; i++)
                {
                    PetNestMemorialEntry m = memorials[i];
                    if (m == null) continue;
                    sb.BeginObject()
                      .Str("displayName", m.displayName)
                      .Str("lineageKey", m.lineageKey)
                      .Str("destinationId", m.destinationId)
                      .Int("riskTier", m.riskTier)
                      .Num("deathRate", m.deathRate)
                      .Long("deathTicks", m.deathTicks)
                      .Int("careerCount", m.careerCount)
                      .Bool("shiny", m.shiny)
                      .EndObject();
                }
            }
            sb.EndArray();

            sb.EndObject();
            return sb.ToString();
        }

        internal static PetNestMuseumData DecodeMuseum(PetNestJsonNode payload)
        {
            if (payload == null) return null;
            PetNestMuseumData data = new PetNestMuseumData();
            data.mergedMemorialCount = payload.GetInt("mergedMemorialCount", 0);

            data.lineages = new List<PetNestLineageStats>();
            List<PetNestJsonNode> lineageNodes = payload.GetArray("lineages");
            for (int i = 0; i < lineageNodes.Count; i++)
            {
                PetNestJsonNode n = lineageNodes[i];
                if (n == null || n.Kind != PetNestJsonKind.Object) continue;
                string key = n.GetString("lineageKey", null);
                if (string.IsNullOrEmpty(key)) continue;
                PetNestLineageStats s = new PetNestLineageStats();
                s.lineageKey = key;
                s.kills = n.GetInt("kills", 0);
                s.hatched = n.GetInt("hatched", 0);
                s.shinyHatched = n.GetInt("shinyHatched", 0);
                s.maxLevel = n.GetInt("maxLevel", 0);
                s.expeditions = n.GetInt("expeditions", 0);
                s.unlocked = n.GetBool("unlocked", false);
                data.lineages.Add(s);
            }

            data.memorials = new List<PetNestMemorialEntry>();
            List<PetNestJsonNode> memorialNodes = payload.GetArray("memorials");
            for (int i = 0; i < memorialNodes.Count; i++)
            {
                PetNestJsonNode n = memorialNodes[i];
                if (n == null || n.Kind != PetNestJsonKind.Object) continue;
                PetNestMemorialEntry m = new PetNestMemorialEntry();
                m.displayName = n.GetString("displayName", null);
                m.lineageKey = n.GetString("lineageKey", null);
                m.destinationId = n.GetString("destinationId", null);
                m.riskTier = n.GetInt("riskTier", (int)PetNestRiskTier.Safe);
                m.deathRate = n.GetFloat("deathRate", 0f);
                m.deathTicks = n.GetLong("deathTicks", 0L);
                m.careerCount = n.GetInt("careerCount", 0);
                m.shiny = n.GetBool("shiny", false);
                data.memorials.Add(m);
            }

            data.Normalize();
            return data;
        }

        #endregion
    }
}
