using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace OhMyMods.Arm64Injection;
public static class PinnedTargets
{
    const string ExpectedHash = "738FB98871DD6E2136474325EA3F7F4F81F2094873A6BC88F7A942D268660B1A";
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern uint _dyld_image_count();
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern IntPtr _dyld_get_image_name(uint index);
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern IntPtr _dyld_get_image_header(uint index);
    static (long Start, long End) GetTextRange()
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
        // Validate both entries are in the readable executable __TEXT mapping before reading code.
        int commands = Marshal.ReadInt32(header, 16), bytes = Marshal.ReadInt32(header, 20), offset = 32;
        if (commands < 1 || commands > 4096 || bytes < 8 || bytes > 1048576) throw new InvalidDataException("Invalid Mach-O command table.");
        bool validText = false; long textEnd = 0;
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
                    if (validText || vmaddr != 0 || vmsize < 4 || (protection & 5) != 5)
                        throw new NotSupportedException("Unexpected executable __TEXT segment.");
                    validText = true; textEnd = checked(header.ToInt64() + vmsize);
                }
            }
            offset += size;
        }
        if (!validText || offset != 32 + bytes) throw new NotSupportedException("Unexpected Mach-O layout.");
        return (header.ToInt64(), textEnd);
    }
    public static IntPtr Resolve(string key)
    {
        (int Rva, string Prefix) selected = key switch
        {
            "GenericMethod_GetMethod_Hook" => (0x5598ac, "FF0301D1F44F02A9FD7B03A9FDC30091080040F9E80100B4"),
            "MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook" => (0x58e754, "FF8301D1FA6701A9F85F02A9F65703A9F44F04A9FD7B05A9"),
            "Class_FromIl2CppType_Hook" => (0x5a4060, "F657BDA9F44F01A9FD7B02A9FD830091F40301AAF30300AA"),
            "Class_FromName_Hook" => (0x59891c, "FF8302D1FA6705A9F85F06A9F65707A9F44F08A9FD7B09A9"),
            "Class_GetFieldDefaultValue_Hook" => (0x58dd8c, "FF0301D1F65701A9F44F02A9FD7B03A9FDC30091F30301AA"),
            _ => throw new NotSupportedException("Unknown pinned injection key.")
        };
        var range = GetTextRange();
        byte[] expected = Convert.FromHexString(selected.Prefix);
        long address = checked(range.Start + selected.Rva);
        if ((address & 3) != 0 || address < range.Start || address > range.End - expected.Length)
            throw new NotSupportedException("Pinned target outside executable __TEXT.");
        var pointer = new IntPtr(address);
        byte[] actual = new byte[expected.Length];
        Marshal.Copy(pointer, actual, 0, actual.Length);
        if (!actual.SequenceEqual(expected)) throw new NotSupportedException("Pinned injection target fingerprint mismatch: " + key);
        Console.WriteLine($"ARM64_PINNED_INJECTION {key} target=0x{address:X}");
        return pointer;
    }
}
