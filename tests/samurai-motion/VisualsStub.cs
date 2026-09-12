namespace KingdomEnhancedMod;
// Records the visual API boundary only. Rendering behavior is tested separately against the real helper.
internal static class SamuraiDashVisuals
{
 internal sealed class Token {internal Knight Owner;internal bool Ended,Immediate;}
 internal static readonly Dictionary<int,int> Begins=new();
 internal static readonly Dictionary<int,Token> Current=new();
 internal static readonly List<Token> EndCalls=new();
 internal static readonly List<Knight> Clears=new();
 internal static int BeginCount(Knight k)=>Begins.TryGetValue(k.gameObject.GetInstanceID(),out int n)?n:0;
 internal static Token Begin(Knight k)
 {int id=k.gameObject.GetInstanceID();Begins[id]=BeginCount(k)+1;var token=new Token{Owner=k};Current[id]=token;return token;}
 internal static void End(Token token,bool immediate=false)
 {if(token==null)return;EndCalls.Add(token);token.Ended=true;token.Immediate=immediate;int id=token.Owner.gameObject.GetInstanceID();if(Current.TryGetValue(id,out var current)&&ReferenceEquals(current,token))Current.Remove(id);}
 internal static void Clear(Knight k)
 {if(k==null)return;Clears.Add(k);int id=k.gameObject.GetInstanceID();if(Current.TryGetValue(id,out var token)){token.Ended=true;token.Immediate=true;Current.Remove(id);}}
 internal static void Reset(){Begins.Clear();Current.Clear();EndCalls.Clear();Clears.Clear();}
}
