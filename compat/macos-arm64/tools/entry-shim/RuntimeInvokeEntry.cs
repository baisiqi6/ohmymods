using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace OhMyMods.Arm64Entry;
public static class RuntimeInvokeEntry
{
    const string ExpectedHash = "738FB98871DD6E2136474325EA3F7F4F81F2094873A6BC88F7A942D268660B1A";
    const int ExportRva = 0x536494, TargetRva = 0x5a1ee8;
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern uint _dyld_image_count();
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern IntPtr _dyld_get_image_name(uint index);
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern IntPtr _dyld_get_image_header(uint index);
    public static IntPtr ResolveRuntimeInvoke(IntPtr export)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            throw new PlatformNotSupportedException("RuntimeInvoke entry experiment requires macOS ARM64.");
        string path = Environment.GetEnvironmentVariable("BEPINEX_GAME_ASSEMBLY_PATH");
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("An absolute BEPINEX_GAME_ASSEMBLY_PATH is required.");
        path = Path.GetFullPath(path);
        using (var input = File.OpenRead(path))
        using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(input)) != ExpectedHash)
                throw new NotSupportedException("Unverified GameAssembly SHA256.");
        IntPtr header = IntPtr.Zero;
        uint count = _dyld_image_count();
        for (uint i = 0; i < count; i++)
        {
            string loaded = Marshal.PtrToStringUTF8(_dyld_get_image_name(i));
            if (loaded == null || Path.GetFullPath(loaded) != path) continue;
            if (header != IntPtr.Zero) throw new InvalidOperationException("Ambiguous loaded GameAssembly.");
            header = _dyld_get_image_header(i);
        }
        if (header == IntPtr.Zero || unchecked((uint)Marshal.ReadInt32(header)) != 0xfeedfacf || Marshal.ReadInt32(header, 4) != 0x0100000c)
            throw new NotSupportedException("Expected loaded ARM64 Mach-O image.");
        if (export != IntPtr.Add(header, ExportRva)) throw new NotSupportedException("Unexpected runtime_invoke export RVA.");
        // Validate both entries are in the readable executable __TEXT mapping before reading code.
        int commands = Marshal.ReadInt32(header, 16), bytes = Marshal.ReadInt32(header, 20), offset = 32;
        if (commands < 1 || commands > 4096 || bytes < 8 || bytes > 1048576) throw new InvalidDataException("Invalid Mach-O command table.");
        bool validText = false;
        for (int i = 0; i < commands; i++)
        {
            if (offset > 32 + bytes - 8) throw new InvalidDataException("Truncated load command.");
            int cmd = Marshal.ReadInt32(header, offset), size = Marshal.ReadInt32(header, offset + 4);
            if (size < 8 || size > 32 + bytes - offset) throw new InvalidDataException("Invalid load command.");
            if (cmd == 0x19)
            {
                if (size < 72) throw new InvalidDataException("Invalid segment.");
                string name = Marshal.PtrToStringAnsi(IntPtr.Add(header, offset + 8), 16).TrimEnd('\0');
                if (name == "__TEXT")
                {
                    long vmaddr = Marshal.ReadInt64(header, offset + 24), vmsize = Marshal.ReadInt64(header, offset + 32);
                    int protection = Marshal.ReadInt32(header, offset + 60);
                    if (validText || vmaddr != 0 || vmsize < TargetRva + 24 || (protection & 5) != 5)
                        throw new NotSupportedException("Unexpected executable __TEXT segment.");
                    validText = true;
                }
            }
            offset += size;
        }
        if (!validText || offset != 32 + bytes) throw new NotSupportedException("Unexpected Mach-O layout.");
        uint instruction = unchecked((uint)Marshal.ReadInt32(export));
        if ((instruction & 0xfc000000) != 0x14000000 || instruction != 0x1401ae95)
            throw new NotSupportedException("Unexpected runtime_invoke branch instruction.");
        // Sign-extend imm26 before multiplying by four; ARM64 B is relative to this instruction.
        long displacement = ((long)((int)(instruction << 6) >> 6)) << 2;
        IntPtr target = new IntPtr(checked(export.ToInt64() + displacement));
        if (target != IntPtr.Add(header, TargetRva)) throw new NotSupportedException("Unexpected Runtime::Invoke target RVA.");
        byte[] expected = Convert.FromHexString("F657BDA9F44F01A9FD7B02A9FD830091F30303AAF40302AA");
        byte[] actual = new byte[expected.Length];
        Marshal.Copy(target, actual, 0, actual.Length);
        if (!actual.SequenceEqual(expected)) throw new NotSupportedException("Unexpected Runtime::Invoke entry fingerprint.");
        Console.WriteLine($"ARM64_RUNTIME_INVOKE_ENTRY export=0x{export.ToInt64():X} target=0x{target.ToInt64():X}");
        return target;
    }
}
