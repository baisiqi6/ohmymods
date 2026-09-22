using System;using System.IO;using System.Linq;using System.Security.Cryptography;using Mono.Cecil;using Mono.Cecil.Cil;
class Program{static void Main(string[] a){
if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a[0])))!="1A2824268A24D3ECA2931DAA7B7B378D7338A542502BCF9E2261919095B1EB97")throw new Exception("Unexpected input");
using var asm=AssemblyDefinition.ReadAssembly(a[0]);
var m=asm.MainModule.Types.Single(t=>t.Name=="ConsoleSetOutFix").Methods.Single(m=>m.Name=="Apply");
var tail=m.Body.Instructions.Single(i=>i.Offset==0x1f);
if(tail.OpCode!=OpCodes.Ldtoken||m.Body.Instructions.Single(i=>i.Offset==0x2a).Operand is not MethodReference r||r.Name!="CreateAndPatchAll")throw new Exception("Unexpected IL shape");
// Diagnostic ARM64 lab only: retain writer initialization and Console.SetOut;
// omit the managed method hook preventing later SetOut calls.
var ix=m.Body.Instructions.IndexOf(tail);while(m.Body.Instructions.Count>ix)m.Body.Instructions.RemoveAt(ix);
m.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));asm.Write(a[1]);Console.WriteLine("ARM64 lab: Console.SetOut writer retained; managed interception omitted.");
}}
