// Standard compiler metadata for net6 only: the pinned Unity interop exports a public empty namesake.
// Keep nullable checking/annotations enabled; never edit the shared generated Unity assembly.
#if NET6_0
#nullable enable
namespace System.Runtime.CompilerServices
{
    internal sealed class NullableAttribute : System.Attribute
    {
        public readonly byte[] NullableFlags;
        public NullableAttribute(byte flag) { NullableFlags = new byte[] { flag }; }
        public NullableAttribute(byte[] flags) { NullableFlags = flags; }
    }
}
#endif
