{
  "case": "controlled-loader-only-fixture-20261019-single1",
  "objective": "loader-control",
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
  "originalMain": {
    "path": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.AndroidProbe.dll",
    "dest": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.AndroidProbe.dll",
    "sha256": "5460efd9652731ce50b3d69d10570f3f599cce1935c007e0afe71a0669da156b"
  },
  "originalCfg": {
    "path": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/UserData/MelonPreferences.cfg",
    "sha256": "9758bda80f7834063ba3bdee0146e2c93df4d3ccbb63a71a382e30c7f4d7c882"
  },
  "mainDisable": {
    "source": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.AndroidProbe.dll",
    "directory": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/UserData/NativeControlDisabled",
    "target": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/UserData/NativeControlDisabled/OhMyMods.AndroidProbe.dll",
    "sha256": "5460efd9652731ce50b3d69d10570f3f599cce1935c007e0afe71a0669da156b",
    "operation": "single-mv-game0-no-restore"
  },
  "addon": {
    "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-20261019/loader-control-probe-worker/root-build/bin/OhMyMods.NativeSceneControlProbe.dll",
    "sha256": "609efed2b058d398f3333c06d39d2c8318a72bbc827f0b68fa781927932b74f8",
    "dest": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.NativeSceneControlProbe.dll",
    "assembly": "OhMyMods.NativeSceneControlProbe"
  },
  "nativeFixture": {
    "path": "/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/resume-20261019/fixture-codec-audit/evidence/global-v35.day32.original.gz",
    "sha256": "35faea071dd59332d61e80e9136ffa2c190f8bbb76f00e4e21cca169f4580fb5",
    "bytes": 260337,
    "target": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/Release/global-v35",
    "expectedOriginalSHA256": "7ba5f975c098f07fe3e4b13dfe10a32a9575508d22ca5a8a82941cc063236bf8",
    "expectedOriginalBytes": 82978
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
    "worldSecondsMax": 360.0,
    "totalSecondsMax": 720.0,
    "cleanupSecondsMax": 30.0,
    "windowMinRemainingSeconds": 750.0,
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
    "disabledMain": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/UserData/NativeControlDisabled/OhMyMods.AndroidProbe.dll",
    "probe": "/sdcard/Android/data/com.rawfury.kingdom2crowns/files/MelonLoader/Mods/OhMyMods.NativeSceneControlProbe.dll"
  },
  "gateSHA256": "5416b6bf2e836e97ab1a71c144e7eec5cf4de3ec6ef9bf780de81c1210b8de7b",
  "sourceReviewSHA256": "9eb3dc659bec0a7c5810512a6cf2ed94e9619e817c259ed999e4271aa83dde46",
  "resourceWindowSHA256": "5ae74a57973e8808f834ef632b52ccfb445aa1ded0419fadfac17e5f700cbeeb",
  "taskIdentity": {
    "issue": 232,
    "owner": "mac-codex-ohmymods-android-operator",
    "session": "codex-android-profession-archery-20261014",
    "operation": "a7459a58-3446-4856-aaa3-5f02e1115de2"
  }
}
