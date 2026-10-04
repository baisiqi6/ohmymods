// Android replacement for BepInEx's BepInEx.Unity.IL2CPP.Utils.Collections.WrapToIl2Cpp for the
// one call shape the shared source uses: the loader's MonoEnumeratorWrapper keeps the ORIGINAL
// native owner's StartCoroutine. Device/ALC evidence is in the task record, not here.
using System.Collections;

namespace BepInEx.Unity.IL2CPP.Utils.Collections;

internal static class AndroidCoroutine
{
    internal static Il2CppSystem.Collections.IEnumerator WrapToIl2Cpp(this IEnumerator routine)
        => new Il2CppSystem.Collections.IEnumerator(new MelonLoader.Support.MonoEnumeratorWrapper(routine).Pointer);
}
