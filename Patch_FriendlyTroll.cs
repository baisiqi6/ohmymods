using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using UnityEngine;
using Harmony;

namespace MyMod
{
    public static class Patch_FriendlyTroll
    {
        public static void Register(HarmonyInstance harmony)
        {
            // 临时禁用：Harmony 1.2 的 transpiler 在当前 IL 结构上崩溃
            // （CodeTranspiler → Activator.CreateInstance type=null），
            // 导致整个 Main.Load() return false，所有 patch 全部失效。
            // TODO: 用 Prefix 重实现"友好巨魔不追 CrownStealer"逻辑，
            //       或修复 transpiler 的 Label 注入（Brtrue_S+Label 在 Harmony1.2 不稳定）。
            Debug.Log("[MyMod] Patch_FriendlyTroll disabled (transpiler crash workaround)");
        }

        // ---- 原 transpiler 实现（已禁用，保留备查）----
        // public static void _Register(HarmonyInstance harmony)
        // {
        //     var friendlyTrollType = typeof(FriendlyTroll);
        //     var moveToTargetMethod = friendlyTrollType.GetMethod("MoveToTargetRoutine", BindingFlags.NonPublic | BindingFlags.Instance);
        //     if (moveToTargetMethod != null)
        //     {
        //         var transpiler = new HarmonyMethod(typeof(Patch_FriendlyTroll).GetMethod("Transpiler_MoveToTargetRoutine"));
        //         harmony.Patch(moveToTargetMethod, null, null, transpiler);
        //         Debug.Log("[MyMod] Patched FriendlyTroll.MoveToTargetRoutine with transpiler");
        //     }
        // }
        //
        // public static IEnumerable<CodeInstruction> Transpiler_MoveToTargetRoutine(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        // {
        //     var instrs = instructions.ToList();
        //     List<CodeInstruction> result = new List<CodeInstruction>();
        //     Label continueLabel = il.DefineLabel();
        //     bool foundContinueTarget = false;
        //     for (int i = 0; i < instrs.Count; i++)
        //     {
        //         if (!foundContinueTarget && i < instrs.Count - 1)
        //         {
        //             CodeInstruction instr = instrs[i];
        //             if (instr.opcode == OpCodes.Br || instr.opcode == OpCodes.Br_S)
        //             {
        //                 if (instr.operand != null && instr.operand.GetType() == typeof(Label))
        //                 {
        //                     Label targetLabel = (Label)instr.operand;
        //                     for (int j = 0; j < i; j++)
        //                     {
        //                         if (instrs[j].labels.Contains(targetLabel))
        //                         {
        //                             instrs[j].labels.Add(continueLabel);
        //             ...
        //     return result;
        // }
    }
}
