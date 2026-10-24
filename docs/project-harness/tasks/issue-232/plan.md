{
  "case": "controlled-candidate136-save-load4-isolated-20261025-r13-single1",
  "objective": "save-load4-probe",
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
    "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/engine-gaps-20261025/root-build/release/OhMyMods.AndroidProbe.dll",
    "sha256": "6b14879dee633f67a3e1853058579e1c7143310986bc866f7a4b040298a532b3",
    "dest": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.AndroidProbe.dll",
    "originalSHA256": "5460efd9652731ce50b3d69d10570f3f599cce1935c007e0afe71a0669da156b"
  },
  "candidateCfg": {
    "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/restock-economy-config-20261025/MelonPreferences.restock7.cfg",
    "sha256": "feefad796591f2043a671085d84d55735f74f3bbac40f19fc680ac0ce82c2fc1",
    "dest": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/UserData/MelonPreferences.cfg",
    "originalSHA256": "9758bda80f7834063ba3bdee0146e2c93df4d3ccbb63a71a382e30c7f4d7c882",
    "preferenceKeys": 58
  },
  "addon": {
    "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/prefs-probe-v2/root-build/bin/OhMyMods.NativeSceneControlProbe.dll",
    "sha256": "9339efaafe3d5bf0ccb82b81c546ed64161b2071051afc41f00be0ddf5e01c12",
    "dest": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.NativeSceneControlProbe.dll",
    "assembly": "OhMyMods.NativeSceneControlProbe"
  },
  "nativeFixture": {
    "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/save-load-inputs-20261025/Release/global-v35",
    "sha256": "e6e6e81d7564cc722c57c5bd6862e1c850c0b948b265a2ea1def79958c61cd61",
    "bytes": 263366,
    "target": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/Release/global-v35",
    "expectedOriginalSHA256": "7ba5f975c098f07fe3e4b13dfe10a32a9575508d22ca5a8a82941cc063236bf8",
    "expectedOriginalBytes": 82978
  },
  "modArchiveSeed": {
    "dir": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/UserData/KingdomEnhancedMod/ModSave",
    "state": "captured",
    "files": [
      {
        "rel": "hero-identities.v1.json",
        "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/save-load-inputs-20261025/ModSave/hero-identities.v1.json",
        "sha256": "9d90a1096223ef2d8bf4da73da222941a4264ed451200c7cbacaf98519f2fd86",
        "bytes": 1189
      },
      {
        "rel": "hero-identities.v1.json.bak",
        "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/save-load-inputs-20261025/ModSave/hero-identities.v1.json.bak",
        "sha256": "352a864cf6eef558375b1b9c80f47052b712075712cb63ab5d5375be161604ce",
        "bytes": 562
      },
      {
        "rel": "musketeer-identities.v2.json",
        "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/save-load-inputs-20261025/ModSave/musketeer-identities.v2.json",
        "sha256": "7e53e04504e7bb73702d468aa72aadfbfcdd10a20ba469bed9c9158f3387aa84",
        "bytes": 51446
      },
      {
        "rel": "musketeer-pair-global-v35.json",
        "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/save-load-inputs-20261025/ModSave/musketeer-pair-global-v35.json",
        "sha256": "e7583f6c676f19069a5a47698856cd4be25d8f6e2c3c8e58ac53255c29a3a499",
        "bytes": 380656
      }
    ],
    "expectedBaseline": [
      {
        "rel": "hero-identities.v1.json",
        "sha256": "352a864cf6eef558375b1b9c80f47052b712075712cb63ab5d5375be161604ce",
        "bytes": 562
      }
    ]
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
    }
  ],
  "hostProtection": [
    {
      "label": "normalDLL",
      "path": "/Applications/ohmymods/arm64/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll",
      "sha256": "055368a3964d307b9a3155160d6a2036cd554f8765c7ea79bdac198b6e59fcaf"
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
    "prepareSecondsMax": 120.0,
    "worldSecondsMax": 360.0,
    "totalSecondsMax": 900.0,
    "cleanupSecondsMax": 30.0,
    "windowMinRemainingSeconds": 930.0,
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
    "probe": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.NativeSceneControlProbe.dll"
  },
  "gateSHA256": "465e2db9a0124f4bf3626ac663256c64b2ec8f60be644ed90b801062847ac5fe",
  "sourceReviewSHA256": "df40dc8b8f00704e15e98aec20d9ad5f81a48212356d3b15fc9a2a53a6d098e3",
  "resourceWindowSHA256": "cb510b5dc0b037852e07e1949fb47af38d7c57b1fc2268fb5015797d7e336fba",
  "taskIdentity": {
    "issue": 232,
    "owner": "mac-dsh-ohmymods-android-operator",
    "session": "dsh-android-profession-archery-20261023",
    "operation": "a7459a58-3446-4856-aaa3-5f02e1115de2"
  }
}
