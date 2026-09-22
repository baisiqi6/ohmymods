using System;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
class Program {
 static void LongBranches(MethodDefinition m) {
 foreach(var i in m.Body.Instructions) i.OpCode=i.OpCode.Code switch {
 Code.Br_S=>OpCodes.Br, Code.Brfalse_S=>OpCodes.Brfalse, Code.Brtrue_S=>OpCodes.Brtrue,
 Code.Ble_S=>OpCodes.Ble, Code.Blt_S=>OpCodes.Blt, Code.Leave_S=>OpCodes.Leave, _=>i.OpCode};
 }
 static void Main(string[] args) {
 if(args.Length!=4)throw new ArgumentException("Expected stock Cpp2IL.Core, stock LibCpp2IL and two output paths.");
 if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0])))!="A7AE866298472BFA5176C1B5B71ADFA9FE6E2F1DEEF7B8D50F063C0E2C0BA258" || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])))!="DD3BD5417CAC3FD203B906A90DBAE6719136E61F43C2B4EED55F56242BB10A14")throw new InvalidOperationException("Unknown Cpp2IL input pair; refusing offset patch.");
 using var a=AssemblyDefinition.ReadAssembly(args[0]);
 using var lib=AssemblyDefinition.ReadAssembly(args[1]);
 var t=a.MainModule.Types.Single(x=>x.Name=="AsmResolverAssemblyPopulator");
 var p=a.MainModule.Types.Single(x=>x.Name=="PropertyAnalysisContext");
 var def=p.Methods.Single(x=>x.Name=="get_Definition");
 var raw=a.MainModule.ImportReference(lib.MainModule.Types.Single(x=>x.Name=="Il2CppPropertyDefinition").Methods.Single(x=>x.Name=="get_RawPropertyType"));
 var m=t.Methods.Single(x=>x.Name=="CopyPropertiesInType");
 var start=m.Body.Instructions.Single(x=>x.Offset==0x19);
 var end=m.Body.Instructions.Single(x=>x.Offset==0x155);
 var il=m.Body.GetILProcessor();
 foreach(var i in new[]{Instruction.Create(OpCodes.Ldloc_1),Instruction.Create(OpCodes.Callvirt,def),Instruction.Create(OpCodes.Brfalse,end),Instruction.Create(OpCodes.Ldloc_1),Instruction.Create(OpCodes.Callvirt,def),Instruction.Create(OpCodes.Callvirt,raw),Instruction.Create(OpCodes.Brfalse,end)}) il.InsertBefore(start,i);
 LongBranches(m);
 m=t.Methods.Single(x=>x.Name=="PopulateCustomAttributes");
 var call=m.Body.Instructions.Single(x=>x.Offset==0x152);
 start=call.Next; end=m.Body.Instructions.Single(x=>x.Offset==0x161);
 il=m.Body.GetILProcessor();
 // Stack contains PropertyAnalysisContext, nullable AsmResolverProperty. Skip this property if absent.
 foreach(var i in new[]{Instruction.Create(OpCodes.Dup),Instruction.Create(OpCodes.Brtrue,start),Instruction.Create(OpCodes.Pop),Instruction.Create(OpCodes.Pop),Instruction.Create(OpCodes.Br,end)}) il.InsertBefore(start,i);
 LongBranches(m);
 a.Write(args[2]);
 var prop=lib.MainModule.Types.Single(x=>x.Name=="Il2CppPropertyDefinition");
 var rm=prop.Methods.Single(x=>x.Name=="get_RawPropertyType");
 var old=rm.Body.Instructions.ToArray();
 var metadata=(FieldReference)old.Single(x=>x.OpCode==OpCodes.Ldsfld).Operand;
 var getter=prop.Methods.Single(x=>x.Name=="get_Getter");
 var setter=prop.Methods.Single(x=>x.Name=="get_Setter");
 var returns=(MethodReference)old.First(x=>x.Operand is MethodReference r && r.Name=="get_RawReturnType").Operand;
 var parameters=(MethodReference)old.First(x=>x.Operand is MethodReference r && r.Name=="get_Parameters").Operand;
 var rawField=(FieldReference)old.First(x=>x.OpCode==OpCodes.Ldfld).Operand;
 rm.Body.Instructions.Clear(); rm.Body.ExceptionHandlers.Clear();
 il=rm.Body.GetILProcessor();
 var nullRet=Instruction.Create(OpCodes.Ldnull);
 var setterStart=Instruction.Create(OpCodes.Ldarg_0);
 var haveSetter=Instruction.Create(OpCodes.Callvirt,parameters);
 var haveParams=Instruction.Create(OpCodes.Dup);
 var haveElement=Instruction.Create(OpCodes.Ldfld,rawField);
 var nonEmpty=Instruction.Create(OpCodes.Ldc_I4_0);
 foreach(var i in new[]{
 Instruction.Create(OpCodes.Ldsfld,metadata),Instruction.Create(OpCodes.Brfalse,nullRet),
 Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,getter),Instruction.Create(OpCodes.Brfalse,setterStart),
 Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,getter),Instruction.Create(OpCodes.Callvirt,returns),Instruction.Create(OpCodes.Ret),
 setterStart,Instruction.Create(OpCodes.Call,setter),Instruction.Create(OpCodes.Dup),Instruction.Create(OpCodes.Brtrue,haveSetter),Instruction.Create(OpCodes.Pop),Instruction.Create(OpCodes.Br,nullRet),
 haveSetter,Instruction.Create(OpCodes.Dup),Instruction.Create(OpCodes.Brtrue,haveParams),Instruction.Create(OpCodes.Pop),Instruction.Create(OpCodes.Br,nullRet),
 haveParams,Instruction.Create(OpCodes.Ldlen),Instruction.Create(OpCodes.Brtrue,nonEmpty),Instruction.Create(OpCodes.Pop),Instruction.Create(OpCodes.Br,nullRet),
 nonEmpty,Instruction.Create(OpCodes.Ldelem_Ref),Instruction.Create(OpCodes.Dup),Instruction.Create(OpCodes.Brtrue,haveElement),Instruction.Create(OpCodes.Pop),Instruction.Create(OpCodes.Br,nullRet),
 haveElement,Instruction.Create(OpCodes.Ret),nullRet,Instruction.Create(OpCodes.Ret)}) il.Append(i);
 lib.Write(args[3]);
 Console.WriteLine("Applied issue-471 property guards to stock Cpp2IL.Core (AsmResolver 6 compatible).");
 }
}
