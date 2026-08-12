@echo off
REM 编译 MyMod（多文件版本）
REM GAME_DIR 指向游戏 Managed 目录（E 盘绝对路径，ohmymods 与游戏目录分离）
set GAME_DIR=E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Call.of.Olympus-P2P\KingdomTwoCrowns_Data\Managed
set UMM_DIR=%GAME_DIR%\UnityModManager
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

%CSC% /target:library /out:MyMod.dll ^
    /reference:%GAME_DIR%\UnityEngine.dll ^
    /reference:%GAME_DIR%\UnityEngine.CoreModule.dll ^
    /reference:%GAME_DIR%\UnityEngine.Physics2DModule.dll ^
    /reference:%GAME_DIR%\UnityEngine.IMGUIModule.dll ^
    /reference:%GAME_DIR%\UnityEngine.PhysicsModule.dll ^
    /reference:%GAME_DIR%\Assembly-CSharp.dll ^
    /reference:%UMM_DIR%\UnityModManager.dll ^
    /reference:%UMM_DIR%\0Harmony-1.2.dll ^
    /reference:%GAME_DIR%\netstandard.dll ^
    Main.cs ^
    Patch_Shop.cs ^
    Patch_Mover.cs ^
    Patch_Construction.cs ^
    Patch_Kingdom.cs ^
    Patch_Holder.cs ^
    Patch_FriendlyTroll.cs ^
    Patch_Enemy.cs ^
    Patch_Knight.cs ^
    Patch_Banker.cs ^
    Patch_Worker.cs ^
    Patch_Character.cs ^
    Patch_World.cs ^
    Patch_Castle.cs ^
    Patch_SidedShop.cs ^
    Patch_PoolManager.cs ^
    Patch_Probe.cs ^
    Patch_BeggarCamp.cs ^
    Patch_Artemis.cs ^
    Patch_HermesStaff.cs

if %ERRORLEVEL% EQU 0 (
    echo 编译成功！MyMod.dll 已生成
) else (
    echo 编译失败！
    pause
)
