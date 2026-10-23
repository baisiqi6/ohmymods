using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using KingdomEnhancedMod;
internal static class Program {
 static int passed,failed;
 static void Check(bool b,string why){if(b)passed++;else{failed++;Console.WriteLine("FAIL "+why);}}
 static byte[] Zip(string s){using var ms=new MemoryStream();using(var gzip=new GZipStream(ms,CompressionLevel.Optimal,true)){var b=Encoding.UTF8.GetBytes(s);gzip.Write(b);}return ms.ToArray();}
 static string Global(string islands,int land=8)=>"{\"serializedSaveDataVersion\":16,\"_currentCampaign\":0,\"campaigns\":[{\"currentLand\":"+land+",\"biomeIndex\":5,\"reign\":3,\"realStartDateTime\":262,\"_islands\":"+islands+"}]}";
 static int Main(string[] args){var owner=new PalacePaidScope("test",Guid.Parse("11111111-1111-1111-1111-111111111111"),0,5,3,262,1);
  var reader=typeof(PalacePaidSourceProof).GetMethod("TryReadNativeDestinationSnapshot",BindingFlags.Public|BindingFlags.Static);
  if(reader==null){Console.WriteLine("FAIL actual production source has no complete target12 projection; source-only reader cannot establish fresh absence without P1/wallet");return 1;}
  bool Read(string json,out object facts,out string reason){object[] a={Zip(json),owner,8,12,null,null};bool ok=(bool)reader.Invoke(null,a);facts=a[4];reason=(string)a[5];return ok;}
  Check(Read(Global("[]"),out var absent,out var why),"out-of-range target is explicit absent: "+why);
  Check(absent?.GetType().GetProperty("SlotKind")?.GetValue(absent)?.ToString()=="OutOfRange","absent kind");
  Check(Read(Global("["+string.Join(",",Enumerable.Repeat("null",13))+"]"),out var nul,out _),"explicit null target accepted");
  Check(nul?.GetType().GetProperty("SlotKind")?.GetValue(nul)?.ToString()=="Null","null kind");
  Check(!Read(Global("null"),out _,out _),"null islands is unknown");
  Check(!Read(Global("{}").Replace("\"_islands\":{}","\"other\":[]"),out _,out _),"missing islands is unknown");
  Check(!Read(Global("{}"),out _,out _),"wrong islands type is unknown");
  Check(!Read(Global("[]",9),out _,out _),"wrong current land refuses");
  Check(!Read(Global("[]").Replace("\"reign\":3","\"reign\":4"),out _,out _),"owner drift refuses");
  Check(!Read(Global("[]").Replace("\"reign\":3","\"reign\":3,\"reign\":3"),out _,out _),"duplicate refuses");
  string island="{\"land\":0,\"_levelEdges\":{\"min\":-200,\"max\":200},\"objects\":[]}";
  Check(Read(Global("["+string.Join(",",Enumerable.Repeat("null",12).Append(island))+"]"),out var saved,out _),"land0 saved object projected without P1/wallet");
  Check(saved?.GetType().GetProperty("IslandJson")?.GetValue(saved)?.ToString()==island,"exact island projection");
  if(args.Length>2){foreach(var sample in new[]{(args[1],8,"OutOfRange"),(args[2],12,"Object")}){byte[] raw=File.ReadAllBytes(sample.Item1);object[] nativeArgs={raw,owner,sample.Item2,12,null,null};bool ok=(bool)reader.Invoke(null,nativeArgs);Check(ok,"actual isolated gzip target projection: "+nativeArgs[5]);Check(nativeArgs[4]?.GetType().GetProperty("SlotKind")?.GetValue(nativeArgs[4])?.ToString()==sample.Item3,"actual isolated target kind "+sample.Item3);}}
  byte[] broken=Zip(Global("[]"));broken[^1]^=1;object[] invalid={broken,owner,8,12,null,null};Check(!(bool)reader.Invoke(null,invalid),"gzip trailer corruption rejected");
  string root=args[0];Directory.CreateDirectory(root);
  Check(PalaceDestinationWitness.TryObserveFile(root,"target12",out var missing,out why)&&missing.Kind==PalaceDestinationFileKind.Missing,"exact FileNotFound missing "+why);
  File.WriteAllText(Path.Combine(root,"target12"),"private-test");
  Check(PalaceDestinationWitness.TryObserveFile(root,"target12",out var present,out why)&&present.Kind==PalaceDestinationFileKind.Present,"opened file present "+why);
  Check(!PalaceDestinationWitness.TryObserveFile(root,"../target12",out _,out _),"leaf escape rejected");
  Check(!PalaceDestinationWitness.TryObserveFile(Path.Combine(root,"absent-parent"),"target12",out _,out _),"missing parent unknown");
  Directory.CreateSymbolicLink(Path.Combine(root,"linked-parent"),root);
  Check(!PalaceDestinationWitness.TryObserveFile(Path.Combine(root,"linked-parent"),"target12",out _,out _),"linked parent unknown");
  File.CreateSymbolicLink(Path.Combine(root,"linked-target"),Path.Combine(root,"target12"));
  Check(!PalaceDestinationWitness.TryObserveFile(root,"linked-target",out _,out _),"linked target unknown");
  File.CreateSymbolicLink(Path.Combine(root,"dangling"),Path.Combine(root,"absent-target"));
  Check(!PalaceDestinationWitness.TryObserveFile(root,"dangling",out _,out _),"dangling target unknown");
  Check(PalaceDestinationWitness.TryReadFile(root,"target12",1024,out var bytes,out _)&&Encoding.UTF8.GetString(bytes)=="private-test","bounded read actual private bytes");
  Check(!PalaceDestinationWitness.TryReadFile(root,"target12",1,out _,out _),"over-budget bytes refuse");
  if(!OperatingSystem.IsWindows()){
   string denied=Path.Combine(root,"denied");File.WriteAllText(denied,"private");File.SetUnixFileMode(denied,UnixFileMode.None);
   try {Check(!PalaceDestinationWitness.TryObserveFile(root,"denied",out _,out var denial)&&denial.Contains("UnauthorizedAccessException"),"permission denial is Unknown, not Missing");}
   finally {File.SetUnixFileMode(denied,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
  }
  Console.WriteLine($"Destination snapshot/witness production source: passed={passed} failed={failed}");return failed==0?0:1;
 }
}
