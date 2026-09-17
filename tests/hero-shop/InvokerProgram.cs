using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using KingdomEnhancedMod;

// Exact metadata29 invoker emitter path from installed ClassInjector. No game/native DLL.
unsafe class InvokerProgram
{
    enum Reason { Invalid=0, NotLocked=21 }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void Invoker(IntPtr method, IntPtr info, IntPtr obj, IntPtr* args, IntPtr* result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate byte Target(IntPtr self, IntPtr player, IntPtr reason);
    static int checks;
    static void Check(bool value,string title) { if(!value)throw new Exception(title); checks++; }
    static Invoker Emit(Type reason)
    {
        var dm=new DynamicMethod("OwnerInvoker_"+reason.Name,typeof(void),new[]{typeof(IntPtr),typeof(IntPtr),typeof(IntPtr),typeof(IntPtr*),typeof(IntPtr*)},typeof(InvokerProgram),true);
        var il=dm.GetILGenerator();il.Emit(OpCodes.Ldarg_2);
        Type[] parameters={typeof(IntPtr),reason};
        for(int i=0;i<parameters.Length;i++)
        {
            il.Emit(OpCodes.Ldarg_3);il.Emit(OpCodes.Ldc_I4,i*IntPtr.Size);il.Emit(OpCodes.Add_Ovf_Un);
            il.Emit(OpCodes.Ldobj,typeof(IntPtr));
            if(parameters[i]!=typeof(IntPtr))il.Emit(OpCodes.Ldobj,parameters[i]);
        }
        il.Emit(OpCodes.Ldarg_0);il.EmitCalli(OpCodes.Calli,CallingConvention.Cdecl,typeof(byte),new[]{typeof(IntPtr),typeof(IntPtr),reason});
        var result=il.DeclareLocal(typeof(byte));il.Emit(OpCodes.Stloc,result);il.Emit(OpCodes.Ldarg_S,(byte)4);il.Emit(OpCodes.Ldloc,result);il.Emit(OpCodes.Stobj,typeof(byte));il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<Invoker>();
    }
    static void Main()
    {
        var broken=Emit(typeof(Reason).MakeByRefType());
        Check(Marshal.GetFunctionPointerForDelegate(broken)!=IntPtr.Zero,"old adapter registration succeeds");
        bool invalid=false;try{RuntimeHelpers.PrepareDelegate(broken);}catch(InvalidProgramException){invalid=true;}
        Check(invalid,"old ref-enum invoker fails at JIT, after registration");
        var fixedInvoker=Emit(typeof(IntPtr));RuntimeHelpers.PrepareDelegate(fixedInvoker);
        Target target=(self,player,reason)=>(byte)(HeroShopOwnerInterop.WriteUnlocked(reason,(int)Reason.NotLocked)?1:0);
        RuntimeHelpers.PrepareDelegate(target);
        int* words=stackalloc int[3];words[0]=123456;words[1]=-9876;words[2]=654321;
        Check(!HeroShopOwnerInterop.WriteUnlocked((IntPtr)(words+1),21)&&words[1]==21,"direct production output adapter succeeds");
        Check(words[0]==123456&&words[2]==654321,"direct adapter writes only four bytes");
        words[1]=-9876;
        IntPtr* args=stackalloc IntPtr[2];args[0]=IntPtr.Zero;args[1]=(IntPtr)(words+1);
        byte* result=stackalloc byte[3];result[0]=12;result[1]=255;result[2]=34;
        fixedInvoker(Marshal.GetFunctionPointerForDelegate(target),IntPtr.Zero,IntPtr.Zero,args,(IntPtr*)(result+1));
        Check(words[1]==21&&result[1]==0,"native-equivalent argv roundtrip writes NotLocked and returns false");
        Check(words[0]==123456&&words[2]==654321&&result[0]==12&&result[2]==34,"enum and bool result guard bytes unchanged");
        Check(HeroShopOwnerInterop.WriteUnlocked(IntPtr.Zero,21),"null output refuses unlocked result");
        GC.KeepAlive(target);GC.KeepAlive(fixedInvoker);GC.KeepAlive(broken);
        Console.WriteLine("PASS "+checks+" invoker/JIT/production pointer adapter assertions; no game/native library loaded.");
    }
}
