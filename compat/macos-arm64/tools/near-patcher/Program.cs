using System.Security.Cryptography;using Mono.Cecil;using Mono.Cecil.Cil;
if(args.Length!=2)throw new ArgumentException("input output");
if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0])))!="34DB10F713F09A665B3A4843E7D702AF717537628A4C0655D7DE2BE99BC52DC3")throw new Exception("Unverified ARM64 thunk-guard input");
using var original=AssemblyDefinition.ReadAssembly(args[0]);using var asm=AssemblyDefinition.ReadAssembly(args[0]);
static IEnumerable<MethodDefinition> All(TypeDefinition t)=>t.Methods.Concat(t.NestedTypes.SelectMany(All));
var type=asm.MainModule.Types.Single(t=>t.Name=="DobbyDetour");var prepare=type.Methods.Single(m=>m.Name=="PrepareImpl");var apply=type.Methods.Single(m=>m.Name=="ApplyImpl");
var exception=new TypeReference("System","InvalidOperationException",asm.MainModule,asm.MainModule.TypeSystem.Object.Scope);
var ctor=new MethodReference(".ctor",asm.MainModule.TypeSystem.Void,exception){HasThis=true};ctor.Parameters.Add(new ParameterDefinition(asm.MainModule.TypeSystem.String));
var extras=new Dictionary<string,HashSet<Instruction>>();
foreach(var pair in new[]{(prepare,"Prepare"),(apply,"Commit")}){
 var m=pair.Item1;var call=m.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.DeclaringType.Name=="DobbyLib"&&r.Name==pair.Item2);
 var pop=call.Next;if(call.OpCode!=OpCodes.Call||pop.OpCode!=OpCodes.Pop)throw new Exception("Unexpected return-code discard");
 var resume=pop.Next;var added=new HashSet<Instruction>();extras[m.FullName]=added;var il=m.Body.GetILProcessor();
 var checks=new List<Instruction>();
 if(m==prepare){if(m.Body.Variables.Count!=1||m.Body.Variables[0].VariableType.FullName!="System.IntPtr")throw new Exception("Unexpected trampoline local");checks.Add(Instruction.Create(OpCodes.Ldloc_0));checks.Add(Instruction.Create(OpCodes.Brtrue,resume));checks.Add(Instruction.Create(OpCodes.Ldstr,"DobbyPrepare succeeded without an original trampoline."));checks.Add(Instruction.Create(OpCodes.Newobj,ctor));checks.Add(Instruction.Create(OpCodes.Throw));}
 pop.OpCode=OpCodes.Brfalse;pop.Operand=checks.Count>0?checks[0]:resume;
 var cursor=pop;foreach(var i in new[]{Instruction.Create(OpCodes.Ldstr,"Dobby"+pair.Item2+" failed; refusing unsafe detour state."),Instruction.Create(OpCodes.Newobj,ctor),Instruction.Create(OpCodes.Throw)}.Concat(checks)){il.InsertAfter(cursor,i);cursor=i;added.Add(i);}
}
static string Shape(MethodDefinition m){if(!m.HasBody)return m.FullName;var ins=m.Body.Instructions;string Op(object o)=>o switch{null=>"",Instruction i=>"@"+ins.IndexOf(i),Instruction[] ar=>string.Join(",",ar.Select(i=>ins.IndexOf(i))),_=>o.ToString()!};return m.FullName+"|"+m.Body.InitLocals+"|"+string.Join(";",m.Body.Variables.Select(v=>v.VariableType.FullName))+"|"+string.Join(";",ins.Select(i=>i.OpCode+" "+Op(i.Operand)))+"|"+string.Join(";",m.Body.ExceptionHandlers.Select(e=>$"{e.HandlerType}:{Op(e.TryStart)}:{Op(e.TryEnd)}:{Op(e.HandlerStart)}:{Op(e.HandlerEnd)}:{Op(e.FilterStart)}:{e.CatchType}"));}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);asm.Write(args[1]);using var result=AssemblyDefinition.ReadAssembly(args[1]);
var before=original.MainModule.Types.SelectMany(All).ToArray();var after=result.MainModule.Types.SelectMany(All).ToArray();if(before.Length!=after.Length)throw new Exception("Method count changed");
for(int n=0;n<before.Length;n++)if(before[n].FullName!=prepare.FullName&&before[n].FullName!=apply.FullName&&Shape(before[n])!=Shape(after[n]))throw new Exception("Unrelated IL change "+before[n].FullName);
foreach(var m in after.Where(m=>m.FullName==prepare.FullName||m.FullName==apply.FullName)){var call=m.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.DeclaringType.Name=="DobbyLib");if(call.Next.OpCode!=OpCodes.Brfalse)throw new Exception("Native failure unchecked");Console.WriteLine(m.FullName);foreach(var i in m.Body.Instructions)Console.WriteLine(i);}
Console.WriteLine($"PASS {after.Length} methods; only PrepareImpl/ApplyImpl changed. Existing runtime-invoke, thunk architecture guard, typed trampoline unchanged.");Console.WriteLine("SHA256="+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1]))));
