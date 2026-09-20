using KingdomEnhancedMod;
using UnityEngine;
internal static partial class Program
{
 static void RedistributionTests()
 {
  Test("Native-style refill callback is suppressed for all three exits",()=>{
   var n=Ordinary();var s=Special();var g1=Garrison(n);var g2=Garrison(n);var g3=Garrison(n);
   var pairs=new[]{g1,g2,g3};int callbacks=0;
   foreach(var pair in pairs) pair.Item2.AfterSlotClear=_=>{
    callbacks++;
    if(SpecialTowerDuplicateCleanup.ShouldDistributeTowerArchers(Managers.Inst.kingdom))
     foreach(var target in pairs) if(!target.Item1.isArcherPresent)
      foreach(var source in pairs) if(source.Item2._guardSlot==null){target.Item1.archer=source.Item2;target.Item1.isArcherPresent=true;source.Item2._guardSlot=target.Item1;break;}
   };
   Removed(n,Run(n,s));Eq(3,callbacks,"all native exit callbacks occurred");
   foreach(var pair in pairs)ArcherSafe(pair.Item2,n);
   foreach(var pair in pairs)Check(Managers.Inst.kingdom.RemovedSlots.Contains(pair.Item1),"retired slot removed before scope ends");
   Check(SpecialTowerDuplicateCleanup.ShouldDistributeTowerArchers(Managers.Inst.kingdom),"distribution resumes after success");
  });
  Test("Scope targets exact kingdom and rejects reentrant cleanup",()=>{
   var n=Ordinary();var s=Special();var(_,a)=Garrison(n);var other=new Kingdom();
   a.BeforeExit=_=>{Check(!SpecialTowerDuplicateCleanup.ShouldDistributeTowerArchers(Managers.Inst.kingdom),"same kingdom gated");Check(SpecialTowerDuplicateCleanup.ShouldDistributeTowerArchers(other),"other kingdom allowed");Check(SpecialTowerDuplicateCleanup.ShouldDistributeTowerArchers(null),"null allowed");Eq(0,Run(n,s).Count,"reentrant cleanup refused");};
   Removed(n,Run(n,s));Eq(1,a.ExitCalls,"one exit only");
  });
  Test("Scope clears after partial native failure and next cleanup works",()=>{
   var n=Ordinary();var s=Special();var(_,a)=Garrison(n);a.AfterSlotClear=_=>throw new System.Exception("native partial failure");
   Eq(0,Run(n,s).Count,"first pass retains");Preserved(n);
   Eq(0,Managers.Inst.kingdom.RemovedSlots.Count,"retained root keeps slot registration");
   Check(SpecialTowerDuplicateCleanup.ShouldDistributeTowerArchers(Managers.Inst.kingdom),"failure clears scope");
   var n2=Ordinary(40);var s2=Special(x:40);Removed(n2,Run(n2,s2));
  });
  Test("Scope clears after post-exit eligibility rejection",()=>{
   var n=Ordinary();var s=Special();var(_,a)=Garrison(n);a.AfterExit=_=>n.GetComponent<PayableUpgrade>().selectedByP1=true;
   Eq(0,Run(n,s).Count,"payment retains");Preserved(n);
   Check(SpecialTowerDuplicateCleanup.ShouldDistributeTowerArchers(Managers.Inst.kingdom),"retention clears scope");
  });
 }
}
