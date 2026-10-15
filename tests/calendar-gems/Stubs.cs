using System;
using System.Collections.Generic;
namespace UnityEngine { public partial class Object {} public partial class Transform : Object {} }
public class Kingdom { public Player playerOne,playerTwo; }
public class Player { public Wallet wallet; }
public class Wallet { public static int Reads,Writes; public int Gems { get { Reads++;throw new Exception("wallet must not be read"); } set {Writes++;} } }
public static class NetworkBigBoss { public static bool Online,ThrowOnRead; public static bool IsOnline {get {if(ThrowOnRead)throw new Exception("network fault");return Online;}} }
public class CampaignSaveData {
 public IntPtr Pointer=(IntPtr)2; public static int Reads,Writes; public bool ThrowOnStored; public int Stored;
 public int storedGems {get {Reads++;if(ThrowOnStored)throw new Exception("stored fault");return Stored;} set {Writes++;Stored=value;}}
}
public class GlobalSaveData {
 public static GlobalSaveData Raw; public static bool ThrowOnRaw; public static int RawReads,LoadedReads,SelectorReads;
 public static GlobalSaveData _loaded {get {RawReads++;if(ThrowOnRaw)throw new Exception("raw field fault");return Raw;}}
 public static GlobalSaveData loaded {get {LoadedReads++;throw new Exception("loaded may create a save");}}
 public CampaignSaveData GetCurrentCampaign(){SelectorReads++;throw new Exception("implicit selector forbidden");}
 public IntPtr Pointer=(IntPtr)1; public int _currentCampaign,_currentChallenge; public List<CampaignSaveData> campaigns=new();
}
namespace BepInEx.Logging {public partial class ManualLogSource {public static readonly List<string> Warnings=new();public void LogWarning(string s){Warnings.Add(s);}}}
namespace KingdomEnhancedMod { public class KingdomEnhancedPlugin {public static KingdomEnhancedPlugin Instance=new();public BepInEx.Logging.ManualLogSource LogSource=new();}}
