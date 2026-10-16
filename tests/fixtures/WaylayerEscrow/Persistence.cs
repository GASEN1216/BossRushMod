using System;
using System.Collections.Generic;
using BossRush;
internal sealed class LevelManager {internal static LevelManager Instance=new LevelManager();internal bool IsBaseLevel=true;}
namespace Saves
{
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
        internal static int PhysicalWrites,CharacterWrites,FailReadAfterWrites;
        private static Dictionary<string,object> Values(Dictionary<string,Dictionary<string,object>> table,string path) {Dictionary<string,object> v;if(!table.TryGetValue(path,out v)){v=new Dictionary<string,object>();table[path]=v;}return v;}
        internal static bool KeyExisits(string key) {return Values(Cache,CurrentFilePath).ContainsKey(key);}
        internal static T Load<T>(string key) {object v;return Values(Cache,CurrentFilePath).TryGetValue(key,out v)?(T)v:default(T);}
        internal static void Save<T>(string key,T value) {Values(Cache,CurrentFilePath)[key]=value;}
        internal static void SaveFile(bool writeTime)
        {
            saving=true;
            if(FailBeforeDisk)throw new Exception("physical write refused");
            Disk[CurrentFilePath]=new Dictionary<string,object>(Values(Cache,CurrentFilePath));PhysicalWrites++;
            saving=false;
            if(ThrowAfterDisk)throw new Exception("post-write exception");
        }
        internal static object ReadDisk(string path,string key) {if(FailRead||(FailReadAfterWrites>0&&PhysicalWrites>=FailReadAfterWrites))throw new Exception("disk unreadable");object v;return Values(Disk,path).TryGetValue(key,out v)?v:null;}
        internal static void Reload() {Cache[CurrentFilePath]=new Dictionary<string,object>(Values(Disk,CurrentFilePath));if(OnSetFile!=null)OnSetFile();}
        internal static void Reset(){Cache.Clear();Disk.Clear();FailBeforeDisk=ThrowAfterDisk=FailRead=false;saving=false;PhysicalWrites=CharacterWrites=FailReadAfterWrites=0;}
        internal static void Collect(){if(OnCollectSaveData!=null)OnCollectSaveData();}
        internal static void Delete(){if(OnSaveDeleted!=null)OnSaveDeleted();}
    }
}
internal sealed class ES3Settings {internal ES3.Location location;internal ES3Settings(string path,ES3Settings parent){}}
internal static class ES3
{
    internal enum Location {File,Cache}
    internal static bool KeyExists(string key,string path,ES3Settings settings){if(settings.location!=Location.File)throw new Exception("must bypass cache");return Saves.SavesSystem.ReadDisk(path,key)!=null;}
    internal static T Load<T>(string key,string path,ES3Settings settings){if(settings.location!=Location.File)throw new Exception("must bypass cache");return (T)Saves.SavesSystem.ReadDisk(path,key);}
}
