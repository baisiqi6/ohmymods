{
  "case": "controlled-profession133-placement-isolated-20261018-single1",
  "objective": "profession-placement",
  "serial": "emulator-5582",
  "avd": "ohmymods_android35_inprocess",
  "emulator": "/opt/homebrew/share/android-commandlinetools/emulator/emulator",
  "launchFlags": [
    "-avd",
    "ohmymods_android35_inprocess",
    "-port",
    "5582",
    "-memory",
    "4096",
    "-cores",
    "4",
    "-gpu",
    "swiftshader",
    "-read-only",
    "-no-snapshot",
    "-no-boot-anim",
    "-no-window",
    "-crash-report-mode",
    "never"
  ],
  "candidateMain": {
    "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-1453/shared-pixel-adaptation/product-build/release/OhMyMods.AndroidProbe.dll",
    "sha256": "0cd539392b23ca0cea9b13e85879f77803890cbec00f9a9fb9cc9e471cf30344",
    "dest": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.AndroidProbe.dll",
    "originalSHA256": "5460efd9652731ce50b3d69d10570f3f599cce1935c007e0afe71a0669da156b"
  },
  "candidateCfg": {
    "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-1453/profession-activation-config/MelonPreferences.activation4.cfg",
    "sha256": "16b68641cedac18f8bfeea9fc19948c11d6aaed16331b643a0623904d93d324c",
    "dest": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/UserData/MelonPreferences.cfg",
    "originalSHA256": "9758bda80f7834063ba3bdee0146e2c93df4d3ccbb63a71a382e30c7f4d7c882",
    "preferenceKeys": 58
  },
  "addon": {
    "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-1453/profession-placement-root-build/bin/OhMyMods.S1InputSurfaceProbeV3.dll",
    "sha256": "26bc4bd9a860f3aabfe0d141ead60a0dac5b2a3be260d40fff4be93e399de2e6",
    "dest": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.S1InputSurfaceProbeV3.dll"
  },
  "runtimeRefs": [
    {
      "remote": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/MelonLoader/net6/MelonLoader.dll",
      "local": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g1-memory-software-proposal/runtime-pins/MelonLoader.dll",
      "sha256": "f6ce6a5421ed8a9c55221144c6c7c9b5a26ab2c874bf1e426b49c878dcb6d85a"
    },
    {
      "remote": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/MelonLoader/net6/Il2CppInterop.Runtime.dll",
      "local": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g1-memory-software-proposal/runtime-pins/Il2CppInterop.Runtime.dll",
      "sha256": "c80e8e88b50604c0d77853b3d7913692428502d176fb48a7a5f1faf05b351826"
    },
    {
      "remote": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll",
      "local": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g1-memory-software-proposal/runtime-pins/Assembly-CSharp.dll",
      "sha256": "43801062e6334f4ece1fdd303bacc685238bb93b778566cfe77e5ffa0feb8704"
    },
    {
      "remote": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/MelonLoader/Il2CppAssemblies/Il2Cppmscorlib.dll",
      "local": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g1-memory-software-proposal/runtime-pins/Il2Cppmscorlib.dll",
      "sha256": "df29ddfe5030624b0d161b3d157a497c9328ecf6f670590e70f3284167463f48"
    },
    {
      "remote": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/MelonLoader/Il2CppAssemblies/UnityEngine.CoreModule.dll",
      "local": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g1-native-memory-apkbudget/cases/swiftshader-memory-20261015-apkbudget-single1/original-logs/Il2CppAssemblies/UnityEngine.CoreModule.dll",
      "sha256": "4f50e9afbd69f692b4347a3ed12a548e2882c35f8dc3063dbdcf2cb9d10274f2"
    },
    {
      "remote": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/MelonLoader/Il2CppAssemblies/Il2CppRewired_Core.dll",
      "local": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g1-native-memory-apkbudget/cases/swiftshader-memory-20261015-apkbudget-single1/original-logs/Il2CppAssemblies/Il2CppRewired_Core.dll",
      "sha256": "b4ff7e723cecb579c0ab1df58d90ef6624ff2458f4b6cde20b72c7089d172366"
    }
  ],
  "hostProtection": [
    {
      "label": "normalDLL",
      "path": "/Applications/ohmymods/arm64/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll",
      "sha256": "8345f086c2a93ec6202f3e6c292733661b4f81e0e7fd28dff8993e51ec9d9e1b"
    },
    {
      "label": "localChecklist",
      "path": "/Users/Admin/projects/ohmymods/docs/project-harness/harness-checklist.json",
      "sha256": "efb52c89c3348b3c73a24ddd204fe30bca04a218a239e9262fe8f25003bf69af"
    }
  ],
  "hostDisks": [
    {
      "label": "userdata-base",
      "path": "/Applications/ohmymods/android/avd/ohmymods_android35_inprocess.avd/userdata-qemu.img"
    },
    {
      "label": "userdata-overlay",
      "path": "/Applications/ohmymods/android/avd/ohmymods_android35_inprocess.avd/userdata-qemu.img.qcow2"
    },
    {
      "label": "cache-base",
      "path": "/Applications/ohmymods/android/avd/ohmymods_android35_inprocess.avd/cache.img"
    },
    {
      "label": "cache-overlay",
      "path": "/Applications/ohmymods/android/avd/ohmymods_android35_inprocess.avd/cache.img.qcow2"
    },
    {
      "label": "encryptionkey-base",
      "path": "/Applications/ohmymods/android/avd/ohmymods_android35_inprocess.avd/encryptionkey.img"
    },
    {
      "label": "encryptionkey-overlay",
      "path": "/Applications/ohmymods/android/avd/ohmymods_android35_inprocess.avd/encryptionkey.img.qcow2"
    }
  ],
  "budgets": {
    "bootSecondsMax": 90.0,
    "prepareSecondsMax": 60.0,
    "worldSecondsMax": 900.0,
    "totalSecondsMax": 1200.0,
    "cleanupSecondsMax": 30.0,
    "windowMinRemainingSeconds": 1230.0,
    "leaseMarginMinutes": 15
  },
  "targetFiles": {
    "main": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.AndroidProbe.dll",
    "cfg": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/UserData/MelonPreferences.cfg",
    "userData": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/UserData",
    "mods": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods",
    "release": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/Release",
    "latestLog": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/MelonLoader/Latest.log",
    "logs": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/MelonLoader/Logs",
    "package": "com.rawfury.kingdom2crowns",
    "launchActivity": "eagle.simple.sdks.manage.FakeActivity",
    "addon": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.S1InputSurfaceProbeV3.dll"
  },
  "gateSHA256": "089e1f823057c73f53015d13c03e7a3dd9e7978d6095df5a14fbf072a60dddfc",
  "sourceReviewSHA256": "9186ee499723cce91c1d863931c54c60e03dc44b8b0c7fd433fe4eb249e250c6",
  "resourceWindowSHA256": "bbcbb3fd34279f417e27e8dd447a8f6812b6ca951b3d2ef2f8729306fede1042",
  "taskIdentity": {
    "issue": 232,
    "owner": "mac-codex-ohmymods-android-operator",
    "session": "codex-android-profession-archery-20261014",
    "operation": "a7459a58-3446-4856-aaa3-5f02e1115de2"
  }
}
