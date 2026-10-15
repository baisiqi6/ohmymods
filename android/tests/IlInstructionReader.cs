using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace OhMyMods.AndroidProbe.Tests;

// Single instruction-boundary IL reader shared by the three metadata scanners in Program.cs
// (BodyCallsMethod / BodyReferencedMembers / CountBodyCalls). The previous scanners walked the raw
// body bytes hunting for 0x28/0x6F/0x73, so an operand byte that happened to equal a call opcode
// turned the following operand bytes into a fake metadata token — the real adapter run died with
// BadImageFormatException reading an out-of-range MemberRef row from a false instruction boundary. This
// reader walks real instruction boundaries using the BCL System.Reflection.Emit.OpCode table as
// the single source of operand widths (0xFE two-byte prefix, full operand widths, InlineSwitch
// target arrays) and reports truncated / unknown encodings instead of guessing a width or skipping
// bytes. The reader is a scanner only: it never executes IL and never interprets tokens.

internal sealed class IlInstructionReaderException : Exception
{
    internal IlInstructionReaderException(string message) : base(message) { }
}

internal readonly struct IlInstruction
{
    internal readonly int Offset;        // IL offset of the opcode byte
    internal readonly OpCode OpCode;
    internal readonly int OperandOffset; // -1 when the instruction has no operand
    internal readonly int OperandSize;   // bytes consumed by the operand (InlineSwitch: 4 + 4*count)
    internal readonly int Size;          // opcode bytes + operand bytes
    internal readonly int OperandToken;  // little-endian 4-byte operand when OperandSize == 4, else 0

    internal IlInstruction(int offset, OpCode opCode, int operandOffset, int operandSize, int size, int operandToken)
    {
        Offset = offset;
        OpCode = opCode;
        OperandOffset = operandOffset;
        OperandSize = operandSize;
        Size = size;
        OperandToken = operandToken;
    }
}

internal static class IlInstructionReader
{
    private static readonly Dictionary<int, OpCode> OpCodeByValue = BuildOpCodeMap();

    private static Dictionary<int, OpCode> BuildOpCodeMap()
    {
        var map = new Dictionary<int, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode)) continue;
            var opcode = (OpCode)field.GetValue(null);
            // The BCL table also carries the reserved one-byte slots 0xF8..0xFF, published as
            // OpCodeType.Nternal (Prefix7..Prefix2, the 0xFE escape entry Prefix1, and Prefixref).
            // They are not instructions: keeping them in the map would accept a reserved byte as an
            // operand-less instruction instead of failing like every other unknown encoding. The
            // 0xFE escape itself is resolved by Read before this map is consulted, and the legal
            // 0xFE-prefixed instructions live in their own non-Nternal entries, so filtering by type
            // keeps every real prefix (tail./constrained./unaligned./readonly./…) intact.
            if (opcode.OpCodeType == OpCodeType.Nternal) continue;
            map[(ushort)opcode.Value] = opcode;
        }
        return map;
    }

    // Parses one method body's IL bytes into instructions. Throws IlInstructionReaderException on
    // every encoding the ECMA-335 boundary rules cannot resolve (truncated instruction, truncated
    // or unbounded InlineSwitch target array, unknown main byte, unknown byte after the 0xFE
    // prefix); the message carries the IL offset and the caller's context string.
    internal static List<IlInstruction> Read(byte[] il, string context = null)
    {
        if (il == null) throw new IlInstructionReaderException(Describe(context, 0, "null IL buffer"));
        var instructions = new List<IlInstruction>();
        int index = 0;
        while (index < il.Length)
        {
            int offset = index;
            int first = il[index++];
            int key = first;
            if (first == 0xFE)
            {
                if (index >= il.Length)
                    throw new IlInstructionReaderException(Describe(context, offset, "0xFE prefix byte ends the method body"));
                key = 0xFE00 | il[index++];
            }
            if (!OpCodeByValue.TryGetValue(key, out OpCode opcode))
                throw new IlInstructionReaderException(Describe(context, offset, "unknown opcode 0x" + key.ToString("X4")));

            int operandOffset = -1, operandSize = 0, token = 0;
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                case OperandType.ShortInlineBrTarget:
                    operandSize = 1;
                    break;
                case OperandType.InlineVar:
                    operandSize = 2;
                    break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    operandSize = 4;
                    break;
                case OperandType.InlineR:
                case OperandType.InlineI8:
                    operandSize = 8;
                    break;
                case OperandType.InlineSwitch:
                    if (index + 4 > il.Length)
                        throw new IlInstructionReaderException(Describe(context, offset,
                            "InlineSwitch target count is truncated (" + (il.Length - index) + " of 4 bytes present)"));
                    int count = il[index] | (il[index + 1] << 8) | (il[index + 2] << 16) | (il[index + 3] << 24);
                    int available = (il.Length - index - 4) / 4;
                    if (count < 0)
                        throw new IlInstructionReaderException(Describe(context, offset,
                            "InlineSwitch target count is negative (" + count + ")"));
                    if (count > available)
                        throw new IlInstructionReaderException(Describe(context, offset,
                            "InlineSwitch declares " + count + " targets but only " + available + " remain"));
                    operandSize = 4 + count * 4;
                    break;
                default:
                    throw new IlInstructionReaderException(Describe(context, offset,
                        "unsupported operand type " + opcode.OperandType + " for " + opcode.Name));
            }

            if (operandSize > 0)
            {
                operandOffset = index;
                if (index + operandSize > il.Length)
                    throw new IlInstructionReaderException(Describe(context, offset,
                        opcode.Name + " needs " + operandSize + " operand bytes but only " + (il.Length - index) + " remain"));
                if (operandSize == 4)
                    token = il[index] | (il[index + 1] << 8) | (il[index + 2] << 16) | (il[index + 3] << 24);
                index += operandSize;
            }
            instructions.Add(new IlInstruction(offset, opcode, operandOffset, operandSize, index - offset, token));
        }
        return instructions;
    }

    private static string Describe(string context, int offset, string detail)
        => "IL_" + offset.ToString("X4") + ": " + detail + (context == null ? "" : " [" + context + "]");

    // The call-class opcode selections the three scanners keep from the original byte walk.
    internal static bool IsCallOrCallvirt(IlInstruction instruction)
        => instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt;

    internal static bool IsCallOrCallvirtOrNewobj(IlInstruction instruction)
        => IsCallOrCallvirt(instruction) || instruction.OpCode == OpCodes.Newobj;
}

// Pure regression cases for the reader's boundary rules, driven with legal encoded instruction
// bytes: the operand-embedded fake call+MemberRef token shapes that made the old byte walk report
// phantom references (and crash on the out-of-range row), the real call/callvirt/newobj and 0xFE
// boundaries that must still be found, and the truncated / unknown / unbounded encodings that must
// fail loudly. Consumers pass their own assertion sink; no product state is touched.
internal static class IlInstructionReaderCases
{
    internal static void Run(Action<bool, string> check)
    {
        // 1) ldc.i8 whose 8-byte operand embeds 0x28 followed by a MemberRef-shaped token with the
        //    out-of-range row 0x0F0000: the old scan read that operand as `call 0x0AF00000` and the
        //    MemberRef lookup then threw ReadOutOfBounds. The boundary reader must see one ldc.i8
        //    plus the terminating ret, and the call-class token scan must stay empty.
        byte[] fakeInLdcI8 = { 0x21, 0x28, 0x00, 0x00, 0xF0, 0x0A, 0x00, 0x00, 0x00, 0x2A };
        check(CallClassTokens(fakeInLdcI8).Count == 0,
            "ldc.i8 operand containing a fake call+MemberRef token yields no call-class instruction");
        var ldcI8 = IlInstructionReader.Read(fakeInLdcI8, "case ldc.i8");
        check(ldcI8.Count == 2 && ldcI8[0].OpCode == OpCodes.Ldc_I8 && ldcI8[0].OperandSize == 8
            && ldcI8[0].OperandOffset == 1 && ldcI8[0].Size == 9 && ldcI8[1].OpCode == OpCodes.Ret,
            "ldc.i8 boundary consumes exactly its 8-byte operand before the next instruction");

        // 2) ldc.r8 (0x23) operand with an embedded 0x6F (callvirt) + fake MemberRef token.
        byte[] fakeInLdcR8 = { 0x23, 0x6F, 0x00, 0x00, 0xF0, 0x0A, 0x00, 0x00, 0x00, 0x2A };
        check(CallClassTokens(fakeInLdcR8).Count == 0,
            "ldc.r8 operand containing a fake callvirt+MemberRef token yields no call-class instruction");

        // 3) InlineSwitch whose target array embeds 0x28 + a MemberRef-shaped token, and a switch
        //    whose target array is followed by a real call: the target bytes are operand data and
        //    the following call must still be found at its real offset.
        byte[] fakeInSwitch = { 0x45, 0x02, 0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0xF0, 0x0A, 0x00, 0x00, 0x00, 0x2A };
        var switchOnly = IlInstructionReader.Read(fakeInSwitch, "case switch");
        check(switchOnly.Count == 2 && switchOnly[0].OpCode == OpCodes.Switch && switchOnly[0].OperandSize == 12
            && switchOnly[0].OperandOffset == 1 && switchOnly[1].OpCode == OpCodes.Ret,
            "InlineSwitch boundary consumes the 4-byte count plus 4 bytes per target");
        check(CallClassTokens(fakeInSwitch).Count == 0,
            "InlineSwitch target bytes containing a fake call+MemberRef token yield no call-class instruction");
        byte[] switchThenCall = { 0x45, 0x01, 0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0xF0, 0x28, 0x02, 0x00, 0x00, 0x0A, 0x2A };
        var switchCallTokens = CallClassTokens(switchThenCall);
        check(switchCallTokens.Count == 1 && switchCallTokens[0] == 0x0A000002,
            "a real call after an InlineSwitch operand is still found by token");

        // 4) Real call / callvirt / newobj MemberRef tokens are all recognized, in order.
        byte[] realCalls =
        {
            0x28, 0x01, 0x00, 0x00, 0x0A, // call    0x0A000001
            0x6F, 0x02, 0x00, 0x00, 0x0A, // callvirt 0x0A000002
            0x73, 0x03, 0x00, 0x00, 0x0A, // newobj  0x0A000003
            0x2A,                         // ret
        };
        var realTokens = CallClassTokens(realCalls);
        check(realTokens.Count == 3 && realTokens[0] == 0x0A000001 && realTokens[1] == 0x0A000002 && realTokens[2] == 0x0A000003,
            "call/callvirt/newobj token operands are read at their own boundaries");

        // 5) 0xFE two-byte boundaries: ldftn (InlineMethod, 4-byte operand) followed by a real call,
        //    and an operand-less FE opcode (arglist) directly followed by ret.
        byte[] feThenCall = { 0xFE, 0x06, 0x01, 0x00, 0x00, 0x06, 0x28, 0x01, 0x00, 0x00, 0x0A, 0x2A };
        var fe = IlInstructionReader.Read(feThenCall, "case FE");
        check(fe.Count == 3 && fe[0].OpCode == OpCodes.Ldftn && fe[0].Size == 6
            && fe[0].OperandToken == 0x06000001 && fe[1].OpCode == OpCodes.Call && fe[1].Offset == 6
            && fe[2].OpCode == OpCodes.Ret,
            "ldftn (0xFE06) is a two-byte opcode with a 4-byte operand");
        var feTokens = CallClassTokens(feThenCall);
        check(feTokens.Count == 1 && feTokens[0] == 0x0A000001,
            "the call after a 0xFE instruction is found at its real offset");
        byte[] feNoOperand = { 0xFE, 0x00, 0x2A };
        var arglist = IlInstructionReader.Read(feNoOperand, "case FE no operand");
        check(arglist.Count == 2 && arglist[0].OpCode == OpCodes.Arglist && arglist[0].Size == 2 && arglist[1].OpCode == OpCodes.Ret,
            "an operand-less 0xFE opcode consumes exactly two bytes");

        // 6) Truncated and unknown encodings fail loudly instead of guessing a width.
        check(ThrowsReader(() => IlInstructionReader.Read(new byte[] { 0x28, 0x01, 0x02 }, "case truncated call")),
            "a truncated call operand reports an explicit reader error");
        check(ThrowsReader(() => IlInstructionReader.Read(new byte[] { 0x21, 0x01, 0x02, 0x03 }, "case truncated ldc.i8")),
            "a truncated ldc.i8 operand reports an explicit reader error");
        check(ThrowsReader(() => IlInstructionReader.Read(new byte[] { 0xFE }, "case truncated FE")),
            "a method body ending on the 0xFE prefix reports an explicit reader error");
        check(ThrowsReader(() => IlInstructionReader.Read(new byte[] { 0xFE, 0x99 }, "case unknown FE")),
            "an unknown byte after the 0xFE prefix reports an explicit reader error");
        check(ThrowsReader(() => IlInstructionReader.Read(new byte[] { 0x45 }, "case truncated switch")),
            "a switch without its target count reports an explicit reader error");
        check(ThrowsReader(() => IlInstructionReader.Read(new byte[] { 0x45, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 }, "case short switch")),
            "a switch declaring more targets than the body holds reports an explicit reader error");
        check(ThrowsReader(() => IlInstructionReader.Read(new byte[] { 0x45, 0xFF, 0xFF, 0xFF, 0xFF }, "case negative switch count")),
            "a negative switch target count reports an explicit reader error");
        check(ThrowsReader(() => IlInstructionReader.Read(new byte[] { 0x45, 0xFF, 0xFF, 0xFF, 0x7F }, "case huge switch count")),
            "an unbounded switch target count reports an explicit reader error without allocating");

        // 7) The BCL's reserved one-byte slots (OpCodeType.Nternal) are not instructions: 0xF8/0xFD/0xFF
        //    must fail through the same unknown-opcode error as any other unassigned byte, and the key
        //    they report must be the single-byte key, never a 0xFE-prefixed double-byte key.
        foreach (byte reserved in new byte[] { 0xF8, 0xFD, 0xFF })
        {
            string key = "0x00" + reserved.ToString("X2");
            bool failed = ReadError(new byte[] { reserved }, out string message);
            check(failed && message.Contains(key, StringComparison.Ordinal)
                && !message.Contains("0xFE" + reserved.ToString("X2"), StringComparison.Ordinal),
                "reserved opcode slot " + key + " fails explicitly with its own single-byte key");
        }

        // 8) The reserved-slot filter must not touch the legal 0xFE prefixes: the operand-less
        //    tail./readonly. step two bytes, constrained. still consumes its 4-byte InlineType
        //    operand and unaligned. its 1-byte ShortInlineI operand, and a real call after them is
        //    still found.
        byte[] tailReadonly = { 0xFE, 0x14, 0xFE, 0x1E, 0x2A };
        var prefixes = IlInstructionReader.Read(tailReadonly, "case tail/readonly");
        check(prefixes.Count == 3 && prefixes[0].OpCode == OpCodes.Tailcall && prefixes[0].Size == 2
            && prefixes[1].OpCode == OpCodes.Readonly && prefixes[1].Size == 2 && prefixes[2].OpCode == OpCodes.Ret,
            "tail./readonly. stay two-byte operand-less instructions");
        byte[] constrained = { 0xFE, 0x16, 0x01, 0x00, 0x00, 0x0B, 0x28, 0x01, 0x00, 0x00, 0x0A, 0x2A };
        var constrainedInstructions = IlInstructionReader.Read(constrained, "case constrained");
        check(constrainedInstructions.Count == 3 && constrainedInstructions[0].OpCode == OpCodes.Constrained
            && constrainedInstructions[0].Size == 6 && constrainedInstructions[0].OperandSize == 4
            && constrainedInstructions[1].OpCode == OpCodes.Call && constrainedInstructions[1].Offset == 6
            && constrainedInstructions[1].OperandToken == 0x0A000001 && constrainedInstructions[2].OpCode == OpCodes.Ret,
            "constrained. (0xFE16) still steps over its 4-byte InlineType operand");
        var constrainedTokens = CallClassTokens(constrained);
        check(constrainedTokens.Count == 1 && constrainedTokens[0] == 0x0A000001,
            "the call after constrained. is still found by token");
        byte[] unaligned = { 0xFE, 0x12, 0x01, 0x28, 0x01, 0x00, 0x00, 0x0A, 0x2A };
        var unalignedInstructions = IlInstructionReader.Read(unaligned, "case unaligned");
        check(unalignedInstructions.Count == 3 && unalignedInstructions[0].OpCode == OpCodes.Unaligned
            && unalignedInstructions[0].Size == 3 && unalignedInstructions[0].OperandSize == 1
            && unalignedInstructions[1].OpCode == OpCodes.Call && unalignedInstructions[1].OperandToken == 0x0A000001
            && unalignedInstructions[2].OpCode == OpCodes.Ret,
            "unaligned. (0xFE12) still steps over its 1-byte ShortInlineI operand");
    }

    // The call-class token selection the three scanners share: call/callvirt/newobj operands only,
    // decoded at real instruction boundaries.
    private static List<int> CallClassTokens(byte[] il)
    {
        var tokens = new List<int>();
        foreach (IlInstruction instruction in IlInstructionReader.Read(il, "cases"))
        {
            if (IlInstructionReader.IsCallOrCallvirtOrNewobj(instruction))
                tokens.Add(instruction.OperandToken);
        }
        return tokens;
    }

    private static bool ThrowsReader(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (IlInstructionReaderException)
        {
            return true;
        }
    }

    // Same explicit-failure contract as ThrowsReader, but hands the message to the case so the
    // unknown-opcode key itself can be asserted (single-byte key, not a 0xFE-prefixed one).
    private static bool ReadError(byte[] il, out string message)
    {
        try
        {
            IlInstructionReader.Read(il, "cases");
            message = null;
            return false;
        }
        catch (IlInstructionReaderException error)
        {
            message = error.Message;
            return true;
        }
    }
}
