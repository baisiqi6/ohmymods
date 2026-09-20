using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
// Reproduce exact ClassInjector v29 emitted invoker shape without loading any game assembly.
unsafe class Program {
 enum Reason { NotLocked }
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
 delegate void Invoker(IntPtr method, IntPtr info, IntPtr obj, IntPtr* args, IntPtr* result);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
 delegate byte Target(IntPtr self, IntPtr player, IntPtr reason);
 static void Main() {
   foreach(var t in new[]{typeof(IntPtr), typeof(int), typeof(Reason).MakeByRefType(), typeof(int).MakeByRefType()}) {
     string stage="Emit";
     try {
       var dm=new DynamicMethod("Invoker_"+t.Name,typeof(void),new[]{typeof(IntPtr),typeof(IntPtr),typeof(IntPtr),typeof(IntPtr*),typeof(IntPtr*)},typeof(Program),true);
       var il=dm.GetILGenerator();
       il.Emit(OpCodes.Ldarg_2);
       foreach(var pair in new[]{(typeof(IntPtr),0),(t,1)}) {
         il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldc_I4,pair.Item2*IntPtr.Size); il.Emit(OpCodes.Add_Ovf_Un);
         il.Emit(OpCodes.Ldobj,typeof(IntPtr));
         if(pair.Item1!=typeof(IntPtr)) il.Emit(OpCodes.Ldobj,pair.Item1);
       }
       il.Emit(OpCodes.Ldarg_0);
       il.EmitCalli(OpCodes.Calli,CallingConvention.Cdecl,typeof(byte),new[]{typeof(IntPtr),typeof(IntPtr),t});
       var local=il.DeclareLocal(typeof(byte)); il.Emit(OpCodes.Stloc,local); il.Emit(OpCodes.Ldarg_S,(byte)4); il.Emit(OpCodes.Ldloc,local); il.Emit(OpCodes.Stobj,typeof(byte)); il.Emit(OpCodes.Ret);
       stage="CreateDelegate"; var d=dm.CreateDelegate<Invoker>(); Console.WriteLine(t+" CreateDelegate OK");
       stage="Marshal.GetFunctionPointerForDelegate"; Marshal.GetFunctionPointerForDelegate(d); Console.WriteLine(t+" Marshal OK");
       stage="PrepareDelegate"; RuntimeHelpers.PrepareDelegate(d); Console.WriteLine(t+" PrepareDelegate OK");
       if(t==typeof(IntPtr)) {
         // Exercise only a locally created managed callback through its CLR thunk.
         // No game/Unity assembly is loaded, no external native library is called.
         Target target=(self,player,reason)=>{Marshal.WriteInt32(reason,0); return 0;};
         int* reason=stackalloc int[3]; reason[0]=12345; reason[1]=98765; reason[2]=54321;
         IntPtr* args=stackalloc IntPtr[2]; args[0]=IntPtr.Zero; args[1]=(IntPtr)reason;
         byte result=255;
         d(Marshal.GetFunctionPointerForDelegate(target),IntPtr.Zero,IntPtr.Zero,args,(IntPtr*)&result);
         if(reason[0]!=0||reason[1]!=98765||reason[2]!=54321||result!=0)throw new Exception("pointer ABI sentinel failure");
         GC.KeepAlive(target); Console.WriteLine("IntPtr ABI invoke PASS; writes 4 bytes, adjacent sentinels unchanged");
       }
     } catch(Exception e) { Console.WriteLine(t+" "+stage+" FAIL "+e); }
   }
 }
}
