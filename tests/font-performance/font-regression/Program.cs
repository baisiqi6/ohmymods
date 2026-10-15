using KingdomEnhancedMod;
using P=KingdomEnhancedMod.StaticBestFitPolicy;
int failures=0,checks=0;
void Check(bool ok,string name,object actual) {checks++;Console.WriteLine((ok?"PASS ":"FAIL ")+name+" actual="+actual);if(!ok)failures++;}
var life=new P.Life(100,1);var other=new P.Life(200,2);var owner=new object();Func<object,long,int,P.Liveness> live=(_,_,_)=>P.Liveness.Alive;
P.ResetAll();
var ticket=P.BeginFontWrite(life,owner,false,P.Capability.Dynamic,live,out _);
bool rewrite=P.NoteExternalBestFitSet(life,owner,true,P.Capability.Dynamic,live,out _);
var action=P.FinishFontWrite(ticket,P.Capability.Dynamic,!rewrite,live,out _);
Check(action!=P.WriteAction.WriteFalse,"later dynamic true survives ticket which began without record",action);
P.ResetAll();
ticket=P.BeginFontWrite(life,owner,true,P.Capability.Static,live,out _);
P.NoteExternalBestFitSet(life,owner,false,P.Capability.Dynamic,live,out _);
rewrite=P.NoteExternalBestFitSet(life,owner,true,P.Capability.Dynamic,live,out _);
action=P.FinishFontWrite(ticket,P.Capability.Dynamic,!rewrite,live,out _);
Check(action!=P.WriteAction.WriteFalse&&P.TryGetWanted(life,out var w)&&w,"false then true in open font transaction keeps latest true",action);
P.ResetAll();
P.NoteEnable(life,owner,P.Capability.Static,true,live,out _);
int removed=P.Prune((_,_,_)=>throw new InvalidOperationException("transient native access"),1);
Check(removed==0&&P.RecordCount==1,"throwing liveness preserves intent",P.RecordCount);
removed=P.Prune((_,_,_)=>P.Liveness.Unknown,1);
Check(removed==0&&P.RecordCount==1,"unknown liveness preserves intent",P.RecordCount);
P.ResetAll();
P.NoteEnable(life,owner,P.Capability.Static,true,live,out _);
using(P.EnterInternalWrite(life))
{
    Check(P.TryConsumeInternalWrite(life),"own direct setter consumes one-shot permission",true);
    Check(!P.TryConsumeInternalWrite(life),"same-life nested setter has no own permission",false);
    P.NoteExternalBestFitSet(life,owner,false,P.Capability.Static,live,out _);
}
Check(!P.TryGetWanted(life,out var stillWanted)||!stillWanted,"OwnConsumed then nested external false clears wanted",P.RecordCount);
Check(P.NoteEnable(life,owner,P.Capability.Dynamic,false,live,out _)==P.WriteAction.None,"dynamic enable does not resurrect after explicit false",P.RecordCount);
using(P.EnterInternalWrite(life))
{
    Check(!P.TryConsumeInternalWrite(other),"other life cannot borrow pending permission",false);
    Check(P.TryConsumeInternalWrite(life),"original life permission remains after foreign request",true);
}
Check(!P.TryConsumeInternalWrite(life),"scope exit cannot leak permission",false);
P.ResetAll();
ticket=P.BeginFontWrite(life,owner,true,P.Capability.Static,live,out _);
action=P.FinishFontWrite(ticket,P.Capability.Dynamic,false,live,out _);
Check(action==P.WriteAction.WriteTrue,"throw before assignment restores old dynamic intent",action);
Check(P.FinishFontWrite(ticket,P.Capability.Dynamic,false,live,out _)==P.WriteAction.None,"ticket finalizes once",ticket.Closed);
P.ResetAll();
ticket=P.BeginFontWrite(life,owner,true,P.Capability.Static,live,out _);
action=P.FinishFontWrite(ticket,P.Capability.Static,false,live,out _);
Check(action==P.WriteAction.None&&P.TryGetWanted(life,out w)&&w,"throw after static assignment keeps false and wanted",action);
Check(P.Prune((_,_,_)=>P.Liveness.Dead,1)==1,"confirmed dead record prunes",P.RecordCount);
P.ResetAll();
for(int i=1;i<=P.Capacity;i++)P.NoteEnable(new P.Life(i,i),owner,P.Capability.Static,true,live,out _);
action=P.NoteEnable(new P.Life(99999,99999),owner,P.Capability.Static,true,live,out bool coverage);
Check(action==P.WriteAction.None&&coverage&&P.RecordCount==P.Capacity,"capacity does not erase live intent or normalize without record",action);
P.ResetAll();
// Adapter handoff model, distinct from the direct policy tests above. Late action is revalidated by production ApplyDecisionGate.
ticket=P.BeginFontWrite(life,owner,true,P.Capability.Static,live,out _);
bool effective=false;
action=P.FinishFontWrite(ticket,P.Capability.Dynamic,effective,live,out _);
P.NoteExternalBestFitSet(life,owner,false,P.Capability.Dynamic,live,out _);effective=false;
action=P.ApplyDecisionGate(life,action,false,P.Capability.Dynamic,effective);
if(action!=P.WriteAction.None)using(P.EnterInternalWrite(life))
{
    if(!P.TryConsumeInternalWrite(life))throw new Exception("own permit failed");
    effective=action==P.WriteAction.WriteTrue;
}
Console.WriteLine("HANDOFF_MODEL late_false effective="+effective+" record="+P.RecordCount+" oldAction="+action);
Check(!effective,"adapter handoff must not apply stale true after late external false",effective);
P.ResetAll();
P.NoteEnable(life,owner,P.Capability.Static,true,live,out _);
P.NoteExternalBestFitSet(life,owner,true,P.Capability.Dynamic,live,out _);
Check(P.ApplyDecisionGate(life,P.WriteAction.WriteFalse,false,P.Capability.Dynamic,false)==P.WriteAction.WriteTrue,"latest true recalculates stale false at apply",P.RecordCount);
Check(P.ApplyDecisionGate(life,P.WriteAction.WriteTrue,false,P.Capability.Static,false)==P.WriteAction.None,"current static already false cancels earlier restore",P.RecordCount);
Check(P.ApplyDecisionGate(life,P.WriteAction.WriteTrue,false,P.Capability.Unknown,false)==P.WriteAction.None,"unknown current ability never guessed for restore",P.RecordCount);
Check(P.ApplyDecisionGate(life,P.WriteAction.WriteFalse,true,P.Capability.Dynamic,true)==P.WriteAction.WriteFalse,"known incoming-static normalization keeps false before old dynamic changes",P.RecordCount);
Console.WriteLine("checks="+checks+" failures="+failures);return failures==0?0:1;
