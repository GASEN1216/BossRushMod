using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    internal static class IdentityRegression
    {
        internal static void Run()
        {
            var cases = new[] {
                new object[]{"town","scav",TidegateEnemyRole.Regular,"突击队员",42,33,30,9,0xA4AE64},
                new object[]{"town","scav",TidegateEnemyRole.Captain,"突击队长",43,2,1,5,0x8E4659},
                new object[]{"docks","scav",TidegateEnemyRole.Regular,"码头游荡者",104,32,25,9,0x4385A2},
                new object[]{"reeds","scav",TidegateEnemyRole.Regular,"芦苇伏兵",1143,32,21,12,0x8BB556},
                new object[]{"dig","digger",TidegateEnemyRole.Regular,"淘沙帮伙计",138,33,26,13,0xC78369},
                new object[]{"dig","digger",TidegateEnemyRole.Gunner,"淘沙帮投掷手",59,33,29,5,0xD76235},
                new object[]{"ww","guard",TidegateEnemyRole.Regular,"水厂警卫",1146,2,15,13,0xB9DDD4},
                new object[]{"ww","guard",TidegateEnemyRole.Marksman,"水厂狙击手",104,2,23,12,0xA797BB},
                new object[]{"cape","guard",TidegateEnemyRole.Regular,"灯塔守卫",1145,1142,27,11,0x30465F},
                new object[]{"cape","scav",TidegateEnemyRole.Regular,"岬角巡哨",1144,32,24,10,0x5BBFB1},
                new object[]{"dig","boss",TidegateEnemyRole.Regular,"沙老大·铅锤",44,34,30,13,0x8E4134},
            };
            var names = new HashSet<string>();
            L10n.English = false; TidegateEnemyIdentity.InjectLabels();
            foreach (object[] c in cases)
            {
                var preset = new CharacterRandomPreset { nameKey = "Cname_Scav", isBoss = true };
                TidegateEnemyIdentity.Tune(preset, (string)c[0], (string)c[1], (TidegateEnemyRole)c[2]);
                Check(preset.showName && preset.showHealthBar && names.Add(preset.nameKey), "missing or shared enemy identity");
                Check(LocalizationHelper.Labels[preset.nameKey] == (string)c[3], "district role name mismatch");
                Check(preset.isBoss == ((string)c[1] == "boss"), "ordinary role became boss");
                var actor = CharacterMainControl.Create("actor");
                actor.characterModel.CustomFace = CustomFaceInstance.CreateBaseline();
                var baseline = actor.characterModel.CustomFace.ConvertToSaveData();
                var old = new Item(); actor.CharacterItem.Mounts["Helmat"].Content = old;
                TidegateEnemyIdentity.Dress(actor, (string)c[0], (string)c[1], (TidegateEnemyRole)c[2]);
                Check(actor.CharacterItem.Mounts["Helmat"].Content.TypeID == (int)c[4] &&
                    actor.CharacterItem.Mounts["Armor"].Content.TypeID == (int)c[5], "role did not equip its official silhouette");
                Check(old.Destroyed && actor.CharacterItem.Mounts["Helmat"].Refreshes == 1, "old equipment leaked or model not refreshed");
                var face = actor.characterModel.CustomFace.ConvertToSaveData();
                Check(actor.characterModel.Loads == 1 && face.mouthID == (int)c[6] && face.eyeID == (int)c[7]
                    && Rgb(face.headSetting.mainColor) == (int)c[8], "Dress did not apply the role's actual face/feather combination");
                Check(GeometrySame(face.hairInfo,baseline.hairInfo) && GeometrySame(face.eyeInfo,baseline.eyeInfo)
                    && GeometrySame(face.mouthInfo,baseline.mouthInfo) && GeometrySame(face.wingInfo,baseline.wingInfo)
                    && GeometrySame(face.tailInfo,baseline.tailInfo) && GeometrySame(face.footInfo,baseline.footInfo), "face geometry baseline was zeroed");
                Check(GeometrySame(CustomFaceInstance.SharedBaseline.mouthInfo,baseline.mouthInfo)
                    && CustomFaceInstance.SharedBaseline.mouthID == 2, "shared official face mutated");
                string description;
                Check(TidegateEnemyIdentity.DescribeAppearance(actor,(string)c[0],(string)c[1],(TidegateEnemyRole)c[2],out description)
                    && description.Contains("mouth="+(int)c[6]+"/"), "actual readback rejects applied profile");
                // Readback must observe live values, not a marker saying Apply once succeeded.
                face.headSetting.mainColor = new Color(0,0,0,1); actor.characterModel.CustomFace.Data = face;
                Check(!TidegateEnemyIdentity.DescribeAppearance(actor,(string)c[0],(string)c[1],(TidegateEnemyRole)c[2],out description), "F3 missed changed feather color");
            }
            L10n.English = true; TidegateEnemyIdentity.InjectLabels();
            Check(LocalizationHelper.Labels["BossRush_TidegateEnemy_Captain"] == "Assault Captain", "language reinjection failed");
            var dog = CharacterMainControl.Create("hound");
            dog.characterModel.CustomFace = null;
            TidegateEnemyIdentity.Dress(dog, "town", "scav", TidegateEnemyRole.Hound);
            Check(dog.CharacterItem.Mounts["Helmat"].Content == null, "wolf received humanoid equipment");
            string wolfDescription;
            Check(TidegateEnemyIdentity.DescribeAppearance(dog,"town","scav",TidegateEnemyRole.Hound,out wolfDescription)
                && wolfDescription.Contains("native_wolf"), "native wolf was treated as failed humanoid face");
            var failed = CharacterMainControl.Create("failed");
            var kept = new Item(); failed.CharacterItem.Mounts["Helmat"].Content = kept;
            ItemAssetsCollection.Missing = 42;
            TidegateEnemyIdentity.Dress(failed, "town", "scav", TidegateEnemyRole.Regular);
            Check(failed.CharacterItem.Mounts["Helmat"].Content == kept && !kept.Destroyed, "missing prefab deleted existing gear");
            ItemAssetsCollection.Missing = 0;
            failed.CharacterItem.Mounts["Helmat"].Reject = true;
            TidegateEnemyIdentity.Dress(failed, "town", "scav", TidegateEnemyRole.Regular);
            Check(failed.CharacterItem.Mounts["Helmat"].Content == kept && !kept.Destroyed && ItemAssetsCollection.Last.Destroyed,
                "rejected slot lost old gear or leaked new gear");
            FaceFailures();
            Console.WriteLine("Tidegate identities: 11 actual face/feather combinations + native wolf, geometry preservation, live readback and face/slot rollback PASS");
        }
        private static void FaceFailures()
        {
            var actor = CharacterMainControl.Create("face_failure");
            actor.characterModel.CustomFace = CustomFaceInstance.CreateBaseline();
            actor.characterModel.transform.localScale = Vector3.one;
            var baseline = actor.characterModel.CustomFace.ConvertToSaveData();
            Duckov.Utilities.GameplayDataSettings.CustomFaceData.Mouths.Missing = 30;
            Check(!TidegateEnemyAppearance.Apply(actor,"BossRush_TidegateEnemy_Trooper") && actor.characterModel.Loads == 0,
                "silent official missing-ID fallback was accepted or modified model");
            Duckov.Utilities.GameplayDataSettings.CustomFaceData.Mouths.Missing = -1;
            actor.characterModel.FailuresRemaining = 1;
            Check(!TidegateEnemyAppearance.Apply(actor,"BossRush_TidegateEnemy_Trooper") && actor.characterModel.Loads == 2,
                "partial face failure did not run rollback");
            Check(actor.characterModel.CustomFace.Data.mouthID == baseline.mouthID
                && Rgb(actor.characterModel.CustomFace.Data.headSetting.mainColor) == Rgb(baseline.headSetting.mainColor)
                && actor.characterModel.transform.localScale.x == 1f, "face rollback lost original face or scale");
            Check(TidegateEnemyAppearance.Apply(actor,"BossRush_TidegateEnemy_Trooper"),"face could not be applied after rollback");
            actor.characterModel.IgnoreWrites = true;
            var current = actor.characterModel.CustomFace.Data;
            Check(!TidegateEnemyAppearance.Apply(actor,"BossRush_TidegateEnemy_Captain")
                && actor.characterModel.CustomFace.Data.mouthID == current.mouthID,"silent SetFaceFromData no-op passed readback");
            actor.characterModel.CustomFace.ThrowOnRead = true;
            string broken;
            Check(!TidegateEnemyAppearance.Apply(actor,"BossRush_TidegateEnemy_Trooper")
                && !TidegateEnemyAppearance.Matches(actor,"BossRush_TidegateEnemy_Trooper",out broken),"broken source face escaped decorative failure boundary");
            actor.characterModel.CustomFace = null;
            Check(!TidegateEnemyAppearance.Apply(actor,"BossRush_TidegateEnemy_Trooper"),"missing CustomFace component reported success");
        }
        private static bool GeometrySame(CustomFacePartInfo a, CustomFacePartInfo b) { return a.radius == b.radius && a.heightOffset == b.heightOffset; }
        private static int Rgb(Color c) { return ((int)Math.Round(c.r*255)<<16) | ((int)Math.Round(c.g*255)<<8) | (int)Math.Round(c.b*255); }
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    }
    internal sealed class CharacterModel : Component
    {
        internal CustomFaceInstance CustomFace;
        internal int Loads, FailuresRemaining;
        internal bool IgnoreWrites;
        internal void SetFaceFromData(CustomFaceSettingData face)
        {
            Loads++;
            if (CustomFace == null || IgnoreWrites) return; // official null component is a silent no-op
            CustomFace.Data.headSetting = face.headSetting; // may fail after a partial mutation
            if (FailuresRemaining > 0) { FailuresRemaining--; throw new InvalidOperationException("injected part load failure"); }
            CustomFace.Data = face;
            CustomFace.Data.eyebrowInfo.heightOffset = face.eyeInfo.height; // official RefreshAll behavior
        }
    }
    internal struct CustomFacePartInfo { internal float radius,height,heightOffset,scale,twist,distanceAngle,leftRightAngle; internal Color color; }
    internal struct CustomFaceHeadSetting { internal Color mainColor; internal float headScaleOffset,foreheadHeight,foreheadRound; }
    internal struct CustomFaceSettingData
    {
        internal bool savedSetting; internal CustomFaceHeadSetting headSetting;
        internal int hairID,eyeID,eyebrowID,mouthID,tailID,wingID,footID;
        internal CustomFacePartInfo hairInfo,eyeInfo,eyebrowInfo,mouthInfo,tailInfo,wingInfo,footInfo;
    }
    internal sealed class CustomFaceInstance
    {
        internal CustomFaceSettingData Data;
        internal bool ThrowOnRead;
        internal CustomFaceSettingData ConvertToSaveData() { if (ThrowOnRead) throw new InvalidOperationException("broken part snapshot"); return Data; }
        internal static readonly CustomFaceSettingData SharedBaseline = Baseline();
        internal static CustomFaceInstance CreateBaseline() { return new CustomFaceInstance { Data=SharedBaseline }; }
        private static CustomFaceSettingData Baseline()
        {
            var part = new CustomFacePartInfo { radius=.23f,heightOffset=.09f,scale=1f,color=new Color(1,1,1,1) };
            return new CustomFaceSettingData { mouthID=2, hairInfo=part,eyeInfo=part,eyebrowInfo=part,mouthInfo=part,tailInfo=part,wingInfo=part,footInfo=part,
                headSetting=new CustomFaceHeadSetting {mainColor=new Color(.2f,.3f,.7f,1),foreheadRound=1f} };
        }
    }
    internal sealed class CustomFacePart { internal int id; }
    internal sealed class CustomFacePartCollection
    {
        internal int Missing=-1;
        internal int totalCount { get { return 34; } }
        internal CustomFacePart GetPartPrefab(int id) { return new CustomFacePart { id=id==Missing?0:id }; }
    }
    internal sealed class CustomFaceData
    {
        internal readonly CustomFacePartCollection Hairs=new CustomFacePartCollection(), Eyes=new CustomFacePartCollection(),
            Eyebrows=new CustomFacePartCollection(), Mouths=new CustomFacePartCollection(), Tails=new CustomFacePartCollection(),
            Wings=new CustomFacePartCollection(), Foots=new CustomFacePartCollection();
    }
    internal static class TidegateText { internal const string MapBossKey = "BossRush_TidegateMap_Boss"; }
    internal static class L10n { internal static bool English; internal static string T(string zh, string en) { return English ? en : zh; } }
    internal static class LocalizationHelper
    {
        internal static Dictionary<string,string> Labels = new Dictionary<string,string>();
        internal static void InjectLocalization(string key, string text) { Labels[key] = text; }
    }
    internal static class SkyIslandBossForge
    { internal static Slot FindSlot(Item item, string key) { return item == null ? null : item.Mounts[key]; } }
}
namespace Duckov.Utilities
{
    internal static class GameplayDataSettings
    {
        internal static BossRush.CustomFaceData CustomFaceData=new BossRush.CustomFaceData();
        internal static class Layers { internal const int wallLayerMask = 1; }
    }
}
namespace UnityEngine
{
    internal struct Color { internal float r,g,b,a; internal Color(float r,float g,float b,float a) { this.r=r;this.g=g;this.b=b;this.a=a; } }
}
namespace ItemStatsSystem
{
    internal sealed class Item
    {
        internal int TypeID;
        internal string DisplayNameRaw;
        internal bool UseDurability, Destroyed;
        internal float Durability, MaxDurability;
        internal Dictionary<string,Slot> Mounts = new Dictionary<string,Slot> {
            {"Helmat",new Slot()}, {"Armor",new Slot()}, {"FaceMask",new Slot()}, {"Headset",new Slot()}
        };
        internal void DestroyTree() { Destroyed = true; }
        internal void Detach() { }
    }
    internal static class ItemAssetsCollection
    {
        internal static int Missing;
        internal static Item Last;
        private static readonly Dictionary<int,string> Names = new Dictionary<int,string> {
            {2,"ArmorLV3"},{26,"GasMask_01"},{27,"Goggle"},{32,"ArmorLV1"},{33,"ArmorLV2"},{34,"ArmorLV4"},
            {42,"HelmatLV2"},{43,"HelmatLV3"},{44,"HelmatLV4"},{59,"Pot"},{104,"Hat"},{138,"HeadLantern"},
            {679,"HeadsetLV1"},{741,"Glass"},{744,"SunGlass"},{1142,"ArmorLV3_3"},{1143,"HelmatLV1_2"},
            {1144,"HelmatLV2_2"},{1145,"HelmatLV3_2"},{1146,"HelmatLV3_UN"}
        };
        internal static Item GetPrefab(int id) { return id == Missing ? null : new Item { TypeID=id, DisplayNameRaw="Item_"+Names[id] }; }
        internal static Item InstantiateSync(int id) { Last = GetPrefab(id); return Last; }
    }
}
namespace ItemStatsSystem.Items
{
    internal sealed class Slot
    {
        internal Item Content;
        internal bool Reject;
        internal int Refreshes;
        internal bool CanPlug(Item item) { return !Reject; }
        internal bool Plug(Item item, out Item previous) { previous = Content; if (Reject) return false; Content = item; return true; }
        internal void ForceInvokeSlotContentChangedEvent() { Refreshes++; }
    }
}
