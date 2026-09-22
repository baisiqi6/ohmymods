using System;using System.IO;using System.Security.Cryptography;using System.Linq;using Mono.Cecil;using Mono.Cecil.Cil;
class Program{static void Main(string[] args){
 using(var input=File.OpenRead(args[0])) if(Convert.ToHexString(SHA256.HashData(input))!="65AB051A681C2C1E52DF7596FF738BE401AAE72CFF882FF18241CF54A3EA38A4")throw new InvalidOperationException("Unknown input assembly; refusing offset patch.");
 using var a=AssemblyDefinition.ReadAssembly(args[0]);using var helper=AssemblyDefinition.ReadAssembly(args[1]);
 var type=a.MainModule.Types.Single(x=>x.Name=="InjectorHelpers");var m=type.Methods.Single(x=>x.Name==".cctor");
 if(type.FullName!="Il2CppInterop.Runtime.Injection.InjectorHelpers"||m.Parameters.Count!=0||!m.IsStatic)throw new InvalidOperationException("Unexpected initializer.");
 var list=m.Body.Instructions;var start=list.Single(x=>x.Offset==0x14);var end=list.Single(x=>x.Offset==0x38);
 if(start.OpCode!=OpCodes.Call||start.Operand is not MethodReference call||call.FullName!="System.Diagnostics.Process System.Diagnostics.Process::GetCurrentProcess()"||end.OpCode!=OpCodes.Stsfld||end.Operand is not FieldReference fld||fld.Name!="Il2CppModule")throw new InvalidOperationException("Unexpected replacement interval.");
 var resolve=a.MainModule.ImportReference(helper.MainModule.Types.Single(x=>x.Name=="ModuleLookup").Methods.Single(x=>x.Name=="Resolve"));
 int from=list.IndexOf(start),to=list.IndexOf(end);
 start.OpCode=OpCodes.Call;start.Operand=resolve;
 for(int i=from+1;i<to;i++){list[i].OpCode=OpCodes.Nop;list[i].Operand=null;}

 var hook=a.MainModule.Types.Single(x=>x.Name=="MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook");
 var finder=hook.Methods.Single(x=>x.Name=="FindTargetMethod");
 finder.Body.Instructions.Clear();finder.Body.ExceptionHandlers.Clear();finder.Body.Variables.Clear();
 var find=a.MainModule.ImportReference(helper.MainModule.Types.Single(x=>x.Name=="ModuleLookup").Methods.Single(x=>x.Name=="FindMetadataIndexFunction"));
 finder.Body.Instructions.Add(Instruction.Create(OpCodes.Call,find));finder.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
 a.Write(args[2]);Console.WriteLine("Injected dyld fallback for InjectorHelpers game-module lookup.");
}}
