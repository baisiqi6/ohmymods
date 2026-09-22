using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
if(args.Length==3 && args[0]=="--test") {
 if(RuntimeInformation.ProcessArchitecture!=Architecture.Arm64)throw new Exception("ARM64 sentinel test requires a native ARM64 process");
 static MethodInfo Load(string path){var ctx=new AssemblyLoadContext(Path.GetFileName(path)+Guid.NewGuid());ctx.Resolving+=(c,n)=>{string p=Path.Combine("/Applications/ohmymods-arm64-lab/BepInEx/core",n.Name+".dll");return File.Exists(p)?c.LoadFromAssemblyPath(p):null;};return ctx.LoadFromAssemblyPath(Path.GetFullPath(path)).GetType("BepInEx.Unity.IL2CPP.Hook.INativeDetour")!.GetMethod("FollowExportThunks",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!;}
 var patchedTest=Load(args[1]);var originalTest=Load(args[2]);
 IntPtr memory=Marshal.AllocHGlobal(128);
 try {
  void Fill(){Marshal.Copy(new byte[128],0,memory,128);}
  IntPtr Run(MethodInfo method,IntPtr p)=>(IntPtr)method.Invoke(null,new object[]{p})!;
  Fill();Marshal.Copy(Convert.FromHexString("E923BA6DFA6701A9F85F02A9"),0,memory,12); // Real ARM64 game entry at RVA 0x7d9710 begins E9; do not run the old parser on this fixture.
  if(Run(patchedTest,memory)!=memory)throw new Exception("ARM64 E9 instruction was followed");Console.WriteLine("PASS native ARM64 actual patched method leaves real game E9 23 BA 6D FA 67 01 A9 F8 5F 02 A9 untouched");
  Fill();Marshal.WriteByte(memory,0xff);Marshal.WriteByte(memory,1,0x25);if(Run(patchedTest,memory)!=memory)throw new Exception("ARM64 FF25 bytes were followed");Console.WriteLine("PASS native ARM64 FF25 sentinel untouched");
  if(Run(patchedTest,IntPtr.Zero)!=IntPtr.Zero)throw new Exception("null changed");Console.WriteLine("PASS null untouched");
  // Execute unchanged original parser on safe synthetic x86 bytes to establish its retained semantics.
  Fill();Marshal.WriteByte(memory,0xe9);Marshal.WriteInt32(memory,1,11);Marshal.WriteByte(memory,16,0xc3);
  if(Run(originalTest,memory)!=IntPtr.Add(memory,16))throw new Exception("Original E9 fixture mismatch");Console.WriteLine("PASS original x86 E9 parser fixture; identical body retained in output");
  Fill();Marshal.WriteByte(memory,0xff);Marshal.WriteByte(memory,1,0x25);Marshal.WriteInt32(memory,2,10);Marshal.WriteIntPtr(memory,16,IntPtr.Add(memory,32));Marshal.WriteByte(memory,32,0xc3);
  if(Run(originalTest,memory)!=IntPtr.Add(memory,32))throw new Exception("Original FF25 fixture mismatch");Console.WriteLine("PASS original x64 FF25 parser fixture; identical body retained in output (not a native x64 run)");
 }finally{Marshal.FreeHGlobal(memory);}return;
}
if(args.Length!=2)throw new ArgumentException("input output");
if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0])))!="C6F8D361D6715C8E92BF9E643B99D9397605557CAAEB33A593318A864B93786D")throw new InvalidDataException("Unverified entry-patched BepInEx input");
using var original=AssemblyDefinition.ReadAssembly(args[0]);using var asm=AssemblyDefinition.ReadAssembly(args[0]);
static IEnumerable<MethodDefinition> All(TypeDefinition t)=>t.Methods.Concat(t.NestedTypes.SelectMany(All));
var all=asm.MainModule.Types.SelectMany(All).ToArray();
var method=all.Single(m=>m.Name=="FollowExportThunks"&&m.DeclaringType.Name=="INativeDetour");
if(!method.IsStatic||method.Parameters.Count!=1||method.ReturnType.FullName!="System.IntPtr"||method.Body.Instructions.Count!=79||method.Body.ExceptionHandlers.Count!=1)throw new Exception("Unexpected thunk method shape");
var runtimeType=all.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>().First(r=>r.DeclaringType.FullName=="System.Runtime.InteropServices.RuntimeInformation").DeclaringType;
var arch=new TypeReference("System.Runtime.InteropServices","Architecture",asm.MainModule,runtimeType.Scope,true);
var getArch=new MethodReference("get_ProcessArchitecture",arch,runtimeType){HasThis=false};
var first=method.Body.Instructions[0];
var prefix=new[]{Instruction.Create(OpCodes.Call,getArch),Instruction.Create(OpCodes.Ldc_I4_0),Instruction.Create(OpCodes.Beq,first),Instruction.Create(OpCodes.Call,getArch),Instruction.Create(OpCodes.Ldc_I4_1),Instruction.Create(OpCodes.Beq,first),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ret)};
var il=method.Body.GetILProcessor();foreach(var i in prefix)il.InsertBefore(first,i);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);asm.Write(args[1]);
using var result=AssemblyDefinition.ReadAssembly(args[1]);
static string Shape(MethodDefinition m,int skip=0){if(!m.HasBody)return m.FullName;var ins=m.Body.Instructions.Skip(skip).ToList();string Op(object o)=>o switch{null=>"",Instruction i=>"@"+ins.IndexOf(i),Instruction[] ar=>string.Join(",",ar.Select(x=>ins.IndexOf(x))),_=>o.ToString()!};return m.FullName+"|"+m.Body.InitLocals+"|"+string.Join(";",m.Body.Variables.Select(v=>v.VariableType.FullName))+"|"+string.Join(";",ins.Select(i=>i.OpCode+" "+Op(i.Operand)))+"|"+string.Join(";",m.Body.ExceptionHandlers.Select(e=>$"{e.HandlerType}:{Op(e.TryStart)}:{Op(e.TryEnd)}:{Op(e.HandlerStart)}:{Op(e.HandlerEnd)}:{Op(e.FilterStart)}:{e.CatchType}"));}
var before=original.MainModule.Types.SelectMany(All).ToArray();var after=result.MainModule.Types.SelectMany(All).ToArray();
if(before.Length!=after.Length)throw new Exception("Method count changed");
for(int i=0;i<before.Length;i++)if(Shape(before[i])!=Shape(after[i],before[i].MetadataToken==method.MetadataToken?8:0))throw new Exception("Unexpected IL/EH/locals change: "+before[i].FullName);
var patched=after.Single(m=>m.MetadataToken==method.MetadataToken);
if(patched.Body.Instructions[2].Operand!=patched.Body.Instructions[8]||patched.Body.Instructions[5].Operand!=patched.Body.Instructions[8])throw new Exception("Wrong guard branch target");
var chain=after.Single(m=>m.Name=="Initialize"&&m.DeclaringType.Name=="IL2CPPChainloader");if(chain.Body.Instructions.Count(i=>i.Operand is MethodReference r&&r.Name=="ResolveRuntimeInvoke")!=1)throw new Exception("Entry resolver lost");
var typed=after.Single(m=>m.Name=="GenerateTrampoline"&&m.HasGenericParameters&&m.DeclaringType.Name=="BaseNativeDetour`1");if(!typed.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.Name=="Prepare")||!typed.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.Name=="GetDelegateForFunctionPointer"))throw new Exception("Typed trampoline lost");
Console.WriteLine($"PASS {after.Length} methods: only 8-instruction architecture prefix added; all original IL/locals/EH preserved; entry resolver and typed trampoline preserved.");foreach(var i in patched.Body.Instructions)Console.WriteLine(i);Console.WriteLine("SHA256="+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1]))));
