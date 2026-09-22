using System;using System.IO;using System.Linq;using System.Security.Cryptography;using Mono.Cecil;using Mono.Cecil.Cil;
class Program{static void Main(string[] a){
if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a[0])))!="46CF1EF802BDF8CB6587FC1E4D98F7AF1073A60487B39A64E230E6F6E4C23AEC")throw new Exception("Unexpected input");
using var asm=AssemblyDefinition.ReadAssembly(a[0]);var t=asm.MainModule.Types.Single(t=>t.Name=="BaseNativeDetour`1");
var typed=t.Methods.Single(m=>m.Name=="GenerateTrampoline"&&m.HasGenericParameters);var untyped=t.Methods.Single(m=>m.Name=="GenerateTrampoline"&&!m.HasGenericParameters);
var prepare=(MethodReference)untyped.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.Name=="Prepare").Operand;
var list=typed.Body.Instructions;var start=list.Single(i=>i.Offset==0x5c);var end=list.Single(i=>i.Offset==0x75);
if(start.OpCode!=OpCodes.Ldtoken||end.OpCode!=OpCodes.Pop||list.Single(i=>i.Offset==0x70).Operand is not MethodReference r||r.Name!="GenerateTrampoline")throw new Exception("Unexpected original proxy call shape");
int first=list.IndexOf(start),last=list.IndexOf(end);start.OpCode=OpCodes.Call;start.Operand=prepare;
for(int i=first+1;i<=last;i++){list[i].OpCode=OpCodes.Nop;list[i].Operand=null;}
if(!typed.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.Name=="GetDelegateForFunctionPointer")||typed.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.Name=="GenerateTrampoline"))throw new Exception("Invalid patched path");
Directory.CreateDirectory(Path.GetDirectoryName(a[1]));asm.Write(a[1]);Console.WriteLine("Typed native trampoline: Prepare + Marshal; non-generic proxy path preserved.");
}}
