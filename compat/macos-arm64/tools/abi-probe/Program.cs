using System;using System.IO;using System.Reflection;using System.Runtime.Loader;using System.Runtime.InteropServices;
class Program{
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr CallOne(IntPtr fn,IntPtr arg);
 static void Main(string[] a){
 string root="/Applications/ohmymods-arm64-lab";
 AssemblyLoadContext.Default.Resolving+=(ctx,n)=>{foreach(string d in new[]{root+"/BepInEx/core",root+"/BepInEx/interop",root+"/dotnet"}){string f=Path.Combine(d,n.Name+".dll");if(File.Exists(f))return ctx.LoadFromAssemblyPath(f);}return null;};
 var asm=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(a[0]));
 var type=asm.GetType("Il2CppInterop.Runtime.Injection.Hooks.GenericMethod_GetMethod_Hook",true);var instance=Activator.CreateInstance(type,true);
 var dt=type.GetNestedType("MethodDelegate",BindingFlags.NonPublic);if(dt.GetMethod("Invoke").GetParameters().Length!=1)throw new Exception("ABI not single-argument");
 var lib=NativeLibrary.Load(Path.GetFullPath(a[1]));var original=Marshal.GetDelegateForFunctionPointer(NativeLibrary.GetExport(lib,"identity"),dt);
 type.BaseType.GetField("_original",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(instance,original);
 var hook=(Delegate)type.GetMethod("GetDetour").Invoke(instance,null);IntPtr fn=Marshal.GetFunctionPointerForDelegate(hook);
 var call=Marshal.GetDelegateForFunctionPointer<CallOne>(NativeLibrary.GetExport(lib,"call_one"));
 if(call(fn,IntPtr.Zero)!=new IntPtr(0x1234))throw new Exception("null passthrough failed");
 IntPtr g=Marshal.AllocHGlobal(24);for(int i=0;i<24;i++)Marshal.WriteByte(g,i,0);
 if(call(fn,g)!=g)throw new Exception("null-definition struct passthrough failed");Marshal.FreeHGlobal(g);GC.KeepAlive(hook);GC.KeepAlive(original);
 Console.WriteLine("ABI-PASS arch="+RuntimeInformation.ProcessArchitecture+" null + 24-byte struct direct native call through actual patched Hook and Original delegate");
 }}
