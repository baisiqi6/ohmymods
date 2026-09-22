using System.Security.Cryptography;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
if(args.Length==2 && args[0]=="--unknown-key-test") {
 var a=System.Reflection.Assembly.LoadFrom(Path.GetFullPath(args[1]));
 try {a.GetType("OhMyMods.Arm64Injection.PinnedTargets")!.GetMethod("Resolve")!.Invoke(null,new object[]{"UNKNOWN"});throw new Exception("Unknown key accepted");}
 catch(TargetInvocationException e) when(e.InnerException is NotSupportedException && e.InnerException.Message=="Unknown pinned injection key."){Console.WriteLine("PASS unknown key rejected before native reads");return;}
}
if(args.Length!=3)throw new ArgumentException("input helper output");
if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0])))!="9342B39FEA1009A755A8FE1CB21F01E26FB923B79A58BF3ED312AF0247564534")throw new InvalidDataException("Unverified Runtime input SHA256");
using var original=AssemblyDefinition.ReadAssembly(args[0]);
using var asm=AssemblyDefinition.ReadAssembly(args[0]);
using var helper=AssemblyDefinition.ReadAssembly(args[1]);
static IEnumerable<MethodDefinition> All(TypeDefinition t)=>t.Methods.Concat(t.NestedTypes.SelectMany(All));
var methods=asm.MainModule.Types.SelectMany(All).ToArray();
var allowed=new HashSet<uint>();
void Allow(MethodDefinition m)=>allowed.Add(m.MetadataToken.ToUInt32());
var resolver=asm.MainModule.ImportReference(helper.MainModule.Types.Single(t=>t.Name=="PinnedTargets").Methods.Single(m=>m.Name=="Resolve"));
string[] keys={"GenericMethod_GetMethod_Hook","MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook","Class_FromIl2CppType_Hook","Class_FromName_Hook","Class_GetFieldDefaultValue_Hook"};
foreach(string key in keys){
 var m=asm.MainModule.Types.Single(t=>t.Name==key).Methods.Single(m=>m.Name=="FindTargetMethod");
 if(m.Parameters.Count!=0||m.ReturnType.FullName!="System.IntPtr"||!m.HasBody)throw new Exception("Unexpected FindTargetMethod shape");
 Allow(m);m.Body=new Mono.Cecil.Cil.MethodBody(m);var il=m.Body.GetILProcessor();
 il.Append(Instruction.Create(OpCodes.Ldstr,key));il.Append(Instruction.Create(OpCodes.Call,resolver));il.Append(Instruction.Create(OpCodes.Ret));
}
var legacy=asm.MainModule.Types.Single(t=>t.Name==keys[0]);
var hook=legacy.Methods.Single(m=>m.Name=="Hook");
var del=legacy.NestedTypes.Single(t=>t.Name=="MethodDelegate");
var invoke=del.Methods.Single(m=>m.Name=="Invoke");var begin=del.Methods.Single(m=>m.Name=="BeginInvoke");
var detour=legacy.Methods.Single(m=>m.Name=="GetDetour");
foreach(var m in new[]{hook,invoke,begin}){
 if(m.Parameters.Count!=(m==begin?4:2)||m.Parameters[0].ParameterType.FullName!="Il2CppInterop.Runtime.Runtime.Il2CppGenericMethod*"||m.Parameters[1].ParameterType.FullName!="System.Boolean")throw new Exception("Unexpected legacy ABI");
 Allow(m);m.Parameters.RemoveAt(1);
}
Allow(detour);
var arg2=hook.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldarg_2).ToArray();
if(arg2.Length!=2||arg2.Any(i=>i.Next.OpCode!=OpCodes.Callvirt||i.Next.Operand is not MethodReference mr||mr.Name!="Invoke"||mr.DeclaringType.FullName!=del.FullName))throw new Exception("Unexpected bool argument sites");
foreach(var i in arg2){i.OpCode=OpCodes.Nop;i.Operand=null;}
// MethodDef operands update in place; update any separate MemberRef signatures as well.
int updatedRefs=0;
foreach(var m in methods.Where(m=>m.HasBody))foreach(var i in m.Body.Instructions){
 if(i.Operand is not MethodReference r)continue;
 bool match=(r.DeclaringType.FullName==legacy.FullName&&r.Name=="Hook")||(r.DeclaringType.FullName==del.FullName&&(r.Name=="Invoke"||r.Name=="BeginInvoke"));
 if(match&&r.Parameters.Count>1&&r.Parameters[1].ParameterType.FullName=="System.Boolean"){
  if(m!=hook&&m!=detour)throw new Exception("Unexpected external legacy ABI reference: "+m.FullName);
  r.Parameters.RemoveAt(1);updatedRefs++;
 }
}
var tail=hook.Body.Instructions.Single(i=>i.Offset==0x108);
if(tail.OpCode!=OpCodes.Ldarg_0)throw new Exception("Unexpected Original tail");
var store=hook.Body.Instructions.Single(i=>i.Offset==0x44);
if(store.OpCode!=OpCodes.Stloc_1||store.Previous.Operand is not FieldReference f||f.Name!="method_inst")throw new Exception("Unexpected method_inst site");
var hp=hook.Body.GetILProcessor();var cursor=store;
foreach(var ins in new[]{Instruction.Create(OpCodes.Ldloc_1),Instruction.Create(OpCodes.Ldc_I4_0),Instruction.Create(OpCodes.Conv_U),Instruction.Create(OpCodes.Beq,tail)}){hp.InsertAfter(cursor,ins);cursor=ins;}
var setup=asm.MainModule.Types.Single(t=>t.Name=="InjectorHelpers").Methods.Single(m=>m.Name=="Setup");Allow(setup);
var newField=setup.Body.Instructions.Single(i=>i.Operand is FieldReference f&&f.Name=="GenericMethodGetMethodHook_Unity6");
var oldField=setup.Body.Instructions.Single(i=>i.Operand is FieldReference f&&f.Name=="GenericMethodGetMethodHook");
if(newField.OpCode!=OpCodes.Ldsfld||newField.Next.OpCode!=OpCodes.Callvirt||oldField.Next.OpCode!=OpCodes.Callvirt||newField.Next.Operand is not MethodReference nr||nr.Name!="ApplyHook"||oldField.Next.Operand is not MethodReference orr||orr.Name!="ApplyHook")throw new Exception("Unexpected Setup shape");
newField.Operand=oldField.Operand;newField.Next.Operand=oldField.Next.Operand;
static string Shape(MethodDefinition m){
 string signature=m.FullName;
 if(!m.HasBody)return signature;
 var ins=m.Body.Instructions;
 string Operand(object o)=>o switch {null=>"",Instruction i=>"@"+ins.IndexOf(i),Instruction[] ar=>string.Join(",",ar.Select(ins.IndexOf)),_=>o.ToString()!};
 return signature+"|"+m.Body.InitLocals+"|"+m.Body.MaxStackSize+"|"+string.Join(";",m.Body.Variables.Select(v=>v.VariableType.FullName))+"|"+string.Join(";",ins.Select(i=>i.OpCode+" "+Operand(i.Operand)))+"|"+string.Join(";",m.Body.ExceptionHandlers.Select(e=>$"{e.HandlerType}:{Operand(e.TryStart)}:{Operand(e.TryEnd)}:{Operand(e.HandlerStart)}:{Operand(e.HandlerEnd)}:{Operand(e.FilterStart)}:{e.CatchType}"));
}
var before=original.MainModule.Types.SelectMany(All).ToArray();
foreach(var m in methods)if(!allowed.Contains(m.MetadataToken.ToUInt32())&&Shape(m)!=Shape(before.Single(b=>b.MetadataToken==m.MetadataToken)))throw new Exception("Unapproved change: "+m.FullName);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);asm.Write(args[2]);
using var output=AssemblyDefinition.ReadAssembly(args[2]);var after=output.MainModule.Types.SelectMany(All).ToArray();
if(after.Length!=before.Length)throw new Exception("Method count changed");
foreach(var m in after){var b=before.Single(b=>b.MetadataToken==m.MetadataToken);if(!allowed.Contains(m.MetadataToken.ToUInt32())&&Shape(m)!=Shape(b))throw new Exception("Unapproved output change: "+m.FullName);}
var ah=after.Single(m=>m.DeclaringType.FullName==legacy.FullName&&m.Name=="Hook");
if(ah.Parameters.Count!=1||ah.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldarg_2))throw new Exception("Residual bool ABI");
foreach(var m in after.Where(m=>m.HasBody))foreach(var i in m.Body.Instructions)if(i.Operand is MethodReference r&&((r.DeclaringType.FullName==legacy.FullName&&r.Name=="Hook")||(r.DeclaringType.FullName==del.FullName&&(r.Name=="Invoke"||r.Name=="BeginInvoke"))))if(r.Parameters.Count!=(r.Name=="BeginInvoke"?3:1))throw new Exception("Residual method reference ABI");
var asetup=after.Single(m=>m.MetadataToken==setup.MetadataToken);
if(asetup.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.Name=="ApplyHook"&&r.DeclaringType.FullName.Contains("Unity6")))throw new Exception("Unity6 ApplyHook remains");
Console.WriteLine($"PASS {after.Length} methods; {allowed.Count} allowed methods/signatures; only Setup field+host replaced; {updatedRefs} separate ABI refs updated.");
foreach(var m in after.Where(m=>allowed.Contains(m.MetadataToken.ToUInt32()))){Console.WriteLine(m.FullName);if(m.HasBody)foreach(var i in m.Body.Instructions)Console.WriteLine(i);}
Console.WriteLine("OUTPUT SHA256="+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[2]))));
