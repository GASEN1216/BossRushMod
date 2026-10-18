using System;
using System.Collections.Generic;
using System.IO;
using BossRush;
internal sealed class LevelManager {internal static LevelManager Instance=new LevelManager();internal bool IsBaseLevel=true;}
namespace Saves
{
    internal static partial class ItemSavesUtilities
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void Save(ItemStatsSystem.Item item,string key)
        {
#if !WAYLAYER_HARMONY_FIXTURE
            if(key=="MainCharacterItemData"&&CoopWaylayerJournal.Current!=null)CoopWaylayerJournal.Current.BeforeNativeSave(item);
#endif
            ItemStatsSystem.Data.ItemTreeData value=ItemStatsSystem.Data.ItemTreeData.FromItem(item);
            SavesSystem.Save<ItemStatsSystem.Data.ItemTreeData>("Item/",key,value);
        }
    }
    internal static class SavesSystem
    {
        internal static int CurrentSlot=1;
        internal static string CurrentFilePath {get{return "isolated-fixture-slot-"+CurrentSlot;}}
        private static bool saving;
        internal static bool IsSaving {get{return saving;}}
        internal static event Action OnCollectSaveData,OnSetFile,OnSaveDeleted;
        internal static readonly Dictionary<string,Dictionary<string,object>> Cache=new Dictionary<string,Dictionary<string,object>>();
        internal static readonly Dictionary<string,Dictionary<string,object>> Disk=new Dictionary<string,Dictionary<string,object>>();
        internal static bool FailBeforeDisk,ThrowAfterDisk,FailRead;
        internal static int PhysicalWrites,CharacterWrites,FailReadAfterWrites,FailBeforeDiskAfterWrites;
        internal static string PhysicalRoot;
        internal static Action BeforeRefusedWrite;
        internal static Action<string> AfterPhysicalRead;
        private static string PhysicalPath {get{return Path.Combine(PhysicalRoot,"slot-"+CurrentSlot+".json");}}
        private static Dictionary<string,object> Values(Dictionary<string,Dictionary<string,object>> table,string path) {Dictionary<string,object> v;if(!table.TryGetValue(path,out v)){v=new Dictionary<string,object>();table[path]=v;}return v;}
        internal static bool KeyExisits(string key) {return Values(Cache,CurrentFilePath).ContainsKey(key);}
        internal static T Load<T>(string key) {object v;return Values(Cache,CurrentFilePath).TryGetValue(key,out v)?(T)v:default(T);}
        internal static void Save<T>(string key,T value) {Values(Cache,CurrentFilePath)[key]=value;}
        internal static void Save<T>(string prefix,string key,T value) {if(prefix=="Item/")CharacterWrites++;Save(prefix+key,value);}
        internal static void SaveFile(bool writeTime)
        {
            saving=true;
            if(FailBeforeDisk||(FailBeforeDiskAfterWrites>0&&PhysicalWrites>=FailBeforeDiskAfterWrites)) {if(BeforeRefusedWrite!=null)BeforeRefusedWrite();saving=false;throw new Exception("physical write refused");}
            Disk[CurrentFilePath]=new Dictionary<string,object>(Values(Cache,CurrentFilePath));PhysicalWrites++;
            if(PhysicalRoot!=null)
            {
                // Native storage boundary: this fixture owns only the Mod journal key.
                File.WriteAllText(PhysicalPath,(string)Disk[CurrentFilePath]["BossRush_CoopWaylayerJournal"]);
                object character;if(Disk[CurrentFilePath].TryGetValue("Item/MainCharacterItemData",out character))
                    File.WriteAllText(PhysicalPath+".native.json",BossRushCoopJson.ToJson(character));
            }
            saving=false;
            if(ThrowAfterDisk)throw new Exception("post-write exception");
        }
        internal static object ReadDisk(string path,string key) {if(FailRead||(FailReadAfterWrites>0&&PhysicalWrites>=FailReadAfterWrites))throw new Exception("disk unreadable");if(PhysicalRoot!=null){if(key=="Item/MainCharacterItemData")return File.Exists(PhysicalPath+".native.json")?BossRushCoopJson.FromJson<ItemStatsSystem.Data.ItemTreeData>(File.ReadAllText(PhysicalPath+".native.json")):null;return File.Exists(PhysicalPath)?File.ReadAllText(PhysicalPath):null;}object v;return Values(Disk,path).TryGetValue(key,out v)?v:null;}
        internal static void Reload()
        {
            if(PhysicalRoot!=null&&File.Exists(PhysicalPath))Disk[CurrentFilePath]=new Dictionary<string,object>{{"BossRush_CoopWaylayerJournal",File.ReadAllText(PhysicalPath)}};
            if(PhysicalRoot!=null&&File.Exists(PhysicalPath+".native.json"))Values(Disk,CurrentFilePath)["Item/MainCharacterItemData"]=BossRushCoopJson.FromJson<ItemStatsSystem.Data.ItemTreeData>(File.ReadAllText(PhysicalPath+".native.json"));
            Cache[CurrentFilePath]=new Dictionary<string,object>(Values(Disk,CurrentFilePath));if(OnSetFile!=null)OnSetFile();
        }
        internal static void Reset(){Cache.Clear();Disk.Clear();FailBeforeDisk=ThrowAfterDisk=FailRead=false;saving=false;PhysicalWrites=CharacterWrites=FailReadAfterWrites=FailBeforeDiskAfterWrites=0;BeforeRefusedWrite=null;AfterPhysicalRead=null;BossRushSaveFileThrottle.ResetStaticCaches();}
        internal static void Collect(){if(OnCollectSaveData!=null)OnCollectSaveData();}
        internal static void Delete(){if(OnSaveDeleted!=null)OnSaveDeleted();}
    }
}
internal sealed class ES3Settings {internal ES3.Location location;internal ES3Settings(string path,ES3Settings parent){}}
internal static class ES3
{
    internal enum Location {File,Cache}
    internal static bool KeyExists(string key,string path,ES3Settings settings){if(settings.location!=Location.File)throw new Exception("must bypass cache");return Saves.SavesSystem.ReadDisk(path,key)!=null;}
    internal static T Load<T>(string key,string path,ES3Settings settings){if(settings.location!=Location.File)throw new Exception("must bypass cache");var value=(T)Saves.SavesSystem.ReadDisk(path,key);if(Saves.SavesSystem.AfterPhysicalRead!=null)Saves.SavesSystem.AfterPhysicalRead(key);return value;}
}
