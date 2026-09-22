using System;
using System.Security.Cryptography;
using Iced.Intel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
namespace OhMyMods.MacCompatibility;
public static class ModuleLookup {
 [DllImport("/usr/lib/libSystem.B.dylib")] static extern uint _dyld_image_count();
 [DllImport("/usr/lib/libSystem.B.dylib")] static extern IntPtr _dyld_get_image_name(uint index);
 [DllImport("/usr/lib/libSystem.B.dylib")] static extern IntPtr _dyld_get_image_header(uint index);
 static bool IsGame(string n)=>n=="GameAssembly.dylib"||n=="GameAssembly.so"||n=="UserAssembly.dll"||string.Equals(n,"GameAssembly.dll",StringComparison.OrdinalIgnoreCase);
 public static ProcessModule Resolve() {
  var managed=Process.GetCurrentProcess().Modules.Cast<ProcessModule>().ToArray();
  var matches=managed.Where(x=>IsGame(x.ModuleName)).ToArray();
  if(matches.Length==1)return matches[0];
  if(matches.Length>1 || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) throw new InvalidOperationException("Ambiguous or unavailable game module.");
  Console.WriteLine("MAC_MODULE_LOOKUP managed modules: "+string.Join(", ",managed.Select(x=>x.ModuleName)));
  ProcessModule result=null;
  for(uint i=0;i<_dyld_image_count();i++) {
   string path=Marshal.PtrToStringUTF8(_dyld_get_image_name(i));
   if(path==null || !IsGame(Path.GetFileName(path)))continue;
   if(result!=null)throw new InvalidOperationException("Multiple native game modules.");
   IntPtr header=_dyld_get_image_header(i);
   if((uint)Marshal.ReadInt32(header)!=0xfeedfacf)throw new NotSupportedException("Expected loaded Mach-O 64 header.");
   int count=Marshal.ReadInt32(header,16), commandBytes=Marshal.ReadInt32(header,20), offset=32;
   if(count<1 || count>4096 || commandBytes<0)throw new InvalidDataException("Invalid Mach-O commands.");
   ulong textBase=0,maxEnd=0;
   for(int j=0;j<count;j++){
    uint cmd=(uint)Marshal.ReadInt32(header,offset);int size=Marshal.ReadInt32(header,offset+4);
    if(size<8 || offset+size>32+commandBytes)throw new InvalidDataException("Invalid Mach-O command range.");
    if(cmd==0x19){
     if(size<72)throw new InvalidDataException("Invalid segment command.");
     ulong vmaddr=(ulong)Marshal.ReadInt64(header,offset+24),vmsize=(ulong)Marshal.ReadInt64(header,offset+32);
     string segment=Marshal.PtrToStringAnsi(IntPtr.Add(header,offset+8),16).TrimEnd('\0');
     if(segment=="__TEXT")textBase=vmaddr;
     maxEnd=Math.Max(maxEnd,checked(vmaddr+vmsize));
    }
    offset+=size;
   }
   int memorySize=checked((int)(maxEnd-textBase));
   result=(ProcessModule)Activator.CreateInstance(typeof(ProcessModule),true);
   Set(result,"ModuleName",Path.GetFileName(path));Set(result,"FileName",path);Set(result,"BaseAddress",header);Set(result,"ModuleMemorySize",memorySize);
   Console.WriteLine($"MAC_MODULE_LOOKUP dyld={path} base=0x{header.ToInt64():X} size={memorySize}");
  }
  return result??throw new InvalidOperationException("Native GameAssembly not found.");
 }

 public static IntPtr FindMetadataIndexFunction() {
  if(!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)||RuntimeInformation.ProcessArchitecture!=Architecture.X64)
   throw new PlatformNotSupportedException("This compatibility probe targets macOS x64.");
  var module=Resolve();
  using(var stream=File.OpenRead(module.FileName)) {
   using var sha=SHA256.Create();
   var hash=Convert.ToHexString(sha.ComputeHash(stream));
   if(hash!="738FB98871DD6E2136474325EA3F7F4F81F2094873A6BC88F7A942D268660B1A")throw new NotSupportedException("Unverified GameAssembly build; refusing metadata hook. SHA256="+hash);
  }
  if(Marshal.ReadInt32(module.BaseAddress,4)!=0x01000007)throw new PlatformNotSupportedException("Loaded game image is not x86_64.");
  var handle=NativeLibrary.Load(module.FileName);
  var export=NativeLibrary.GetExport(handle,"il2cpp_image_get_class");
  var image=SingleBranch(export,module);
  var branches=DirectBranches(image,module);
  if(branches.Count!=2 || branches[0].flow!=FlowControl.Call || branches[1].flow!=FlowControl.UnconditionalBranch)
   throw new NotSupportedException("Unexpected Image::GetType shape; refusing a guessed hook.");
  var handleThunk=branches[1].address;
  var handleBody=SingleBranch(handleThunk,module);
  var indexBody=SingleBranch(handleBody,module);
  if(indexBody.ToInt64()-module.BaseAddress.ToInt64()!=0x5a4f10)throw new NotSupportedException("Unexpected metadata-index RVA.");
  byte[] expected=Convert.FromHexString("554889E54157415641554154534883EC1883FFFF");byte[] actual=new byte[expected.Length];Marshal.Copy(indexBody,actual,0,actual.Length);
  if(!actual.SequenceEqual(expected))throw new NotSupportedException("Unexpected metadata-index entry instructions.");
  Console.WriteLine($"MAC_METADATA_CHAIN export=0x{export.ToInt64():X} image=0x{image.ToInt64():X} handleThunk=0x{handleThunk.ToInt64():X} handleBody=0x{handleBody.ToInt64():X} index=0x{indexBody.ToInt64():X}");
  return indexBody;
 }
 static IntPtr SingleBranch(IntPtr p,ProcessModule m){var b=DirectBranches(p,m);if(b.Count!=1||b[0].flow!=FlowControl.UnconditionalBranch)throw new NotSupportedException("Unexpected tail thunk.");return b[0].address;}
 static List<(IntPtr address,FlowControl flow)> DirectBranches(IntPtr p,ProcessModule module){
  long offset=p.ToInt64()-module.BaseAddress.ToInt64();if(offset<0||offset>module.ModuleMemorySize-256)throw new InvalidOperationException("Hook target outside game module.");
  byte[] code=new byte[256];Marshal.Copy(p,code,0,code.Length);
  var decoder=Decoder.Create(64,new ByteArrayCodeReader(code));decoder.IP=(ulong)p.ToInt64();
  var found=new List<(IntPtr,FlowControl)>();
  while(decoder.IP<(ulong)p.ToInt64()+256){decoder.Decode(out var ins);
   if(ins.IsInvalid||ins.FlowControl==FlowControl.Return||ins.Mnemonic==Mnemonic.Int3)break;
   if(ins.FlowControl==FlowControl.Call||ins.FlowControl==FlowControl.UnconditionalBranch){
    if(ins.Op0Kind!=OpKind.NearBranch64)throw new NotSupportedException("Unexpected indirect branch.");
    found.Add((new IntPtr((long)ins.NearBranchTarget),ins.FlowControl));
    if(ins.FlowControl==FlowControl.UnconditionalBranch)break;
   }
  }
  return found;
 }
 static void Set(ProcessModule module,string name,object value)=>(typeof(ProcessModule).GetField("<"+name+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)??throw new MissingFieldException(name)).SetValue(module,value);
}
