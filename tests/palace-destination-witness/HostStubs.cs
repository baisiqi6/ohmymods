using System;
using System.Security.Cryptography;
using System.Text;
namespace KingdomEnhancedMod;
internal sealed class PalacePaidScope {
 public string Profile {get;} public Guid CampaignGuid {get;} public int CampaignSlot {get;} public int Biome {get;} public int Reign {get;} public int Start {get;} public int PlayerSlot {get;}
 public PalacePaidScope(string profile,Guid guid,int slot,int biome,int reign,int start,int player){Profile=profile;CampaignGuid=guid;CampaignSlot=slot;Biome=biome;Reign=reign;Start=start;PlayerSlot=player;}
 public string ScopeKey()=>CampaignGuid.ToString("D");
 public bool ContentEquals(PalacePaidScope other)=>other!=null&&Profile==other.Profile&&CampaignGuid==other.CampaignGuid&&CampaignSlot==other.CampaignSlot&&Biome==other.Biome&&Reign==other.Reign&&Start==other.Start&&PlayerSlot==other.PlayerSlot;
}
internal static class PalacePaidText {public static bool IsLowerHex(string s,int n)=>s!=null&&s.Length==n&&System.Linq.Enumerable.All(s,c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'));public static string ToLowerHex(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(bytes).ToLowerInvariant();}
internal sealed class PalacePaidContractException:Exception {public PalacePaidContractException(string reason):base(reason){}}

internal static class PalacePaidCurrencies {public static int[] CopyValidated(int[] values,string path)=>(int[])values.Clone();public static bool ContentEquals(System.Collections.Generic.IReadOnlyList<int> a,System.Collections.Generic.IReadOnlyList<int>b)=>System.Linq.Enumerable.SequenceEqual(a,b);}
