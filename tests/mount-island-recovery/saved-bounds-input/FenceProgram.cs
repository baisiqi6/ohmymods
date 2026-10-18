using System;namespace SavedBoundsFence {class Program {static int n,bad;static void Check(bool b,string s){n++;if(!b){bad++;Console.WriteLine("FAIL "+s);}}
static int Main(){
Host.Reset();var t=Host.Prepare();Check(t!=null,"before frozen");Host.Publish(t);Check(t.Ready,"post publishes exact bounds");Check(Actual.Recheck(t,out _),"host stable before activation");Actual.Consume();Check(Host.Ensure==1,"consume reaches Ensure only on exact frozen bounds");Actual.Consume();Check(Host.Ensure==1,"token consumed once");
Host.Reset();Host.AfterFirstMarkers=()=>Host.Island.Min++;Check(Actual.BeforePop(Host.Island,true)==null,"A/B raw integer drift refuses token");
Host.Reset();Host.Island.Throw=true;Check(Actual.BeforePop(Host.Island,true)==null,"A getter exception refuses token");
Host.Reset();Host.AfterFirstMarkers=()=>Host.Island.Throw=true;Check(Actual.BeforePop(Host.Island,true)==null,"B getter exception refuses token");
Host.Reset();Host.AfterFirstMarkers=()=>Host.ScopeValid=false;Check(Actual.BeforePop(Host.Island,true)==null,"second scope rejects despite bounds stable");
Host.Reset();t=Host.Prepare();Host.Island.Min++;Host.Publish(t);Host.Island.Min--;Host.Publish(t);Actual.Consume();Check(Host.Ensure==0,"post drift withdraws token and restore cannot restart");
Host.Reset();t=Host.Prepare();Host.Island.Throw=true;Host.Publish(t);Actual.Consume();Check(Host.Ensure==0,"post exception no grant");
Host.Reset();t=Host.Prepare();int reads=Host.Island.Reads;Host.ScopeValid=false;Host.Publish(t);Check(Host.Island.Reads==reads,"post scope rejects before bounds getter");
Host.Reset();t=Host.Prepare();Host.Publish(t);Host.Island.Min++;Actual.Consume();Check(Host.Ensure==0,"consume drift before Ensure");Host.Island.Min--;Actual.Consume();Check(Host.Ensure==0,"consume drift token not retryable");
Host.Reset();t=Host.Prepare();Host.Publish(t);Host.Island.Throw=true;Actual.Consume();Check(Host.Ensure==0,"consume exception before Ensure");
Host.Reset();t=Host.Prepare();Host.Publish(t);reads=Host.Island.Reads;Host.MarkersValid=false;Actual.Consume();Check(Host.Ensure==0&&Host.Island.Reads==reads,"consume marker gate short circuits bounds and business");
Host.Reset();t=Host.Prepare();Host.Publish(t);Host.Island.Max++;Check(!Actual.Recheck(t,out _),"host boundary drift before activation/commit");Host.Island.Max--;Host.Island.Throw=true;Check(!Actual.Recheck(t,out _),"host read exception rejects");Host.Island.Throw=false;reads=Host.Island.Reads;Host.ScopeValid=false;Check(!Actual.Recheck(t,out _)&&Host.Island.Reads==reads,"host full scope short circuits getter");
Host.Reset();Host.Grant=false;t=Actual.BeforePop(Host.Island,true);Host.Grant=true;Host.Island.objects=null;Host.Seed=934126577;Host.Publish(t);Actual.Consume();Check(Host.Ensure==0,"late valid live bounds cannot grant without Before capture");
Console.WriteLine($"{n-bad}/{n} real before/post/consume-prefix/host-fence SOURCE checks; native scope and values stubbed");return bad==0?0:1;}}}
