using System;
using System.Text.Json;
using KingdomEnhancedMod;

namespace HermesHeadwearTests
{
    /// <summary>纯编解码器：尾巴布局/边界、盘上收据注入的字节保真与 fail-closed 拒绝。</summary>
    internal static class CodecTests
    {
        internal static void Run()
        {
            Console.WriteLine("HermesHeadwearCodec");

            Case.Run("codec.tail.roundtripsChoiceAndRevision", () =>
            {
                int[] choices = { -1, 0, 21, 43 };
                for (int i = 0; i < choices.Length; i++)
                {
                    byte[] buffer = new byte[HermesHeadwearCodec.TailBytes];
                    Guid token = Guid.NewGuid();
                    Check.True(HermesHeadwearCodec.WriteTail(buffer, token, choices[i], i % 2 == 1, i),
                        "tail write accepted for choice " + choices[i]);
                    Check.Equal((byte)0x4B, buffer[0], "magic byte 0");
                    Check.Equal((byte)0x48, buffer[1], "magic byte 1");
                    Check.Equal((byte)0x4D, buffer[2], "magic byte 2");
                    Check.Equal((byte)0x31, buffer[3], "magic byte 3");
                    for (int b = 0; b < 4; b++)
                    {
                        Check.Equal(HermesHeadwearCodec.TailMagicBytes[b], buffer[b],
                            "magic byte table matches the written tail at " + b);
                    }

                    Check.True(HermesHeadwearCodec.TryReadTail(buffer, out HermesHeadwearCodec.Tail tail),
                        "tail must parse back");
                    Check.Equal(token, tail.Token, "token roundtrip");
                    Check.Equal(choices[i], tail.Choice, "choice roundtrip");
                    Check.Equal(i % 2 == 1, tail.HostEnabled, "hostEnabled roundtrip");
                    Check.Equal(i, tail.Revision, "revision roundtrip");
                }
            });

            Case.Run("codec.tail.rejectsOutOfRangeAndEmptyToken", () =>
            {
                byte[] buffer = new byte[HermesHeadwearCodec.TailBytes];
                Check.True(!HermesHeadwearCodec.WriteTail(buffer, Guid.Empty, 3, true, 1), "empty token rejected");
                Check.True(!HermesHeadwearCodec.WriteTail(buffer, Guid.NewGuid(), 44, true, 1), "choice 44 rejected");
                Check.True(!HermesHeadwearCodec.WriteTail(buffer, Guid.NewGuid(), -2, true, 1), "choice -2 rejected");
                Check.True(!HermesHeadwearCodec.WriteTail(new byte[10], Guid.NewGuid(), 3, true, 1), "short target rejected");
            });

            Case.Run("codec.tail.rejectsForeignAndCorruptedBytes", () =>
            {
                byte[] valid = new byte[HermesHeadwearCodec.TailBytes];
                Check.True(HermesHeadwearCodec.WriteTail(valid, Guid.NewGuid(), 30, true, 4), "baseline tail written");

                byte[] foreign = (byte[])valid.Clone();
                foreign[0] = 0xDE;
                Check.True(!HermesHeadwearCodec.TryReadTail(foreign, out _), "foreign magic rejected");

                byte[] wrongVersion = (byte[])valid.Clone();
                wrongVersion[4] = 2;
                Check.True(!HermesHeadwearCodec.TryReadTail(wrongVersion, out _), "unknown tail version rejected");

                Check.True(!HermesHeadwearCodec.TryReadTail(new byte[HermesHeadwearCodec.TailBytes], out _),
                    "all-zero payload rejected");

                byte[] truncated = new byte[HermesHeadwearCodec.TailBytes - 1];
                Array.Copy(valid, truncated, truncated.Length);
                Check.True(!HermesHeadwearCodec.TryReadTail(truncated, out _), "truncated tail rejected");

                byte[] badChoice = (byte[])valid.Clone();
                badChoice[21] = 44;
                Check.True(!HermesHeadwearCodec.TryReadTail(badChoice, out _), "out-of-range choice rejected");
            });

            Case.Run("codec.metadata.buildAndRead", () =>
            {
                Guid token = Guid.NewGuid();
                string json = HermesHeadwearCodec.BuildMetadataJson(token, -1);
                Check.Contains(json, "\"v\":1", "schema version present");
                Check.Contains(json, "\"choice\":-1", "choice present");

                HermesHeadwearCodec.ReceiptInfo receipt = HermesHeadwearCodec.ReadComponentMetadata(
                    "{\"maskIndex\":1,\"kemHermesHeadwear\":" + json + "}");
                Check.True(receipt.Found && receipt.Recognized, "v1 receipt recognized");
                Check.Equal(token, receipt.Token, "token parsed");
                Check.Equal(HermesHeadwearCodec.ChoiceNone, receipt.Choice, "choice -1 parsed");

                string nFormat = token.ToString("N");
                Check.Equal(32, nFormat.Length, "guid N format length");
                Check.Contains(json, nFormat, "guid N spelling used on disk");
            });

            Case.Run("codec.metadata.unknownSchemaKeptOpaque", () =>
            {
                Guid token = Guid.NewGuid();
                string[] opaque =
                {
                    "{\"v\":2,\"id\":\"" + token.ToString("N") + "\",\"choice\":5}",
                    "{\"v\":1,\"id\":\"not-a-guid\",\"choice\":5}",
                    "{\"v\":1,\"id\":\"" + token.ToString("N") + "\",\"choice\":44}",
                    "{\"v\":1,\"id\":\"" + token.ToString("N") + "\"}",
                    "{\"id\":\"" + token.ToString("N") + "\",\"choice\":5}",
                    "\"plain-string\"",
                    "17",
                };
                for (int i = 0; i < opaque.Length; i++)
                {
                    HermesHeadwearCodec.ReceiptInfo receipt = HermesHeadwearCodec.ReadComponentMetadata(
                        "{\"maskIndex\":1,\"kemHermesHeadwear\":" + opaque[i] + "}");
                    Check.True(receipt.Found, "metadata found for case " + i);
                    Check.True(!receipt.Recognized, "metadata must stay opaque for case " + i);
                    Check.Equal(Guid.Empty, receipt.Token, "no token for opaque case " + i);
                    Check.Equal(HermesHeadwearCodec.ChoiceNone, receipt.Choice, "no choice for opaque case " + i);
                }

                Check.True(!HermesHeadwearCodec.ReadComponentMetadata("{\"maskIndex\":1}").Found,
                    "absent metadata is not found");
                Check.True(!HermesHeadwearCodec.ReadComponentMetadata("{").Found, "malformed payload is not found");
            });

            Case.Run("codec.inject.preservesNativeBytesExactly", () =>
            {
                string payload = "{\"maskIndex\":3,\"trollHealth\":5,\"toughTroll\":false}";
                string metadata = HermesHeadwearCodec.BuildMetadataJson(Guid.NewGuid(), 21);
                string updated = HermesHeadwearCodec.InjectMetadata(payload, metadata);

                Check.NotNull(updated, "injection must succeed");
                Check.Equal("{\"maskIndex\":3,\"trollHealth\":5,\"toughTroll\":false,\"kemHermesHeadwear\":"
                    + metadata + "}", updated, "exact byte-level result (native part untouched)");
            });

            Case.Run("codec.inject.replacesInsteadOfAppendingTwice", () =>
            {
                string payload = "{\"maskIndex\":3,\"trollHealth\":5,\"toughTroll\":false}";
                Guid first = Guid.NewGuid();
                Guid second = Guid.NewGuid();
                string once = HermesHeadwearCodec.InjectMetadata(payload,
                    HermesHeadwearCodec.BuildMetadataJson(first, 4));
                string twice = HermesHeadwearCodec.InjectMetadata(once,
                    HermesHeadwearCodec.BuildMetadataJson(second, 8));

                Check.NotNull(twice, "second injection must succeed");
                Check.Equal("{\"maskIndex\":3,\"trollHealth\":5,\"toughTroll\":false,\"kemHermesHeadwear\":"
                    + HermesHeadwearCodec.BuildMetadataJson(second, 8) + "}", twice,
                    "the unique key is replaced, not duplicated");

                HermesHeadwearCodec.ReceiptInfo receipt = HermesHeadwearCodec.ReadComponentMetadata(twice);
                Check.Equal(second, receipt.Token, "new token wins");
                Check.Equal(8, receipt.Choice, "new choice wins");
            });

            Case.Run("codec.inject.handlesPrettyNativeJson", () =>
            {
                string payload = "{\n    \"maskIndex\": 3,\n    \"trollHealth\": 5,\n    \"toughTroll\": false\n}";
                string metadata = HermesHeadwearCodec.BuildMetadataJson(Guid.NewGuid(), 2);
                string updated = HermesHeadwearCodec.InjectMetadata(payload, metadata);

                Check.NotNull(updated, "pretty payload injection must succeed");
                Check.Contains(updated, "\"maskIndex\": 3", "pretty native text preserved");
                Check.True(updated.EndsWith("}", StringComparison.Ordinal), "stays an object");

                using (JsonDocument document = JsonDocument.Parse(updated))
                {
                    JsonElement root = document.RootElement;
                    Check.Equal(3, root.GetProperty("maskIndex").GetInt32(), "native value readable");
                    Check.Equal(2, root.GetProperty("kemHermesHeadwear").GetProperty("choice").GetInt32(),
                        "receipt readable");
                }
            });

            Case.Run("codec.inject.ignoresNestedLookalikeKeys", () =>
            {
                string payload = "{\"nested\":{\"kemHermesHeadwear\":{\"v\":9}},\"maskIndex\":1}";
                string metadata = HermesHeadwearCodec.BuildMetadataJson(Guid.NewGuid(), 6);
                string updated = HermesHeadwearCodec.InjectMetadata(payload, metadata);

                Check.NotNull(updated, "injection with nested lookalike must succeed");
                using (JsonDocument document = JsonDocument.Parse(updated))
                {
                    JsonElement root = document.RootElement;
                    Check.Equal(9, root.GetProperty("nested").GetProperty("kemHermesHeadwear")
                        .GetProperty("v").GetInt32(), "nested value untouched");
                    Check.Equal(6, root.GetProperty("kemHermesHeadwear").GetProperty("choice").GetInt32(),
                        "top-level receipt added");
                }
            });

            Case.Run("codec.inject.survivesBracesAndQuotesInsideStrings", () =>
            {
                string payload = "{\"maskIndex\":1,\"note\":\"a}b\\\"c\",\"toughTroll\":true}";
                string metadata = HermesHeadwearCodec.BuildMetadataJson(Guid.NewGuid(), 13);
                string updated = HermesHeadwearCodec.InjectMetadata(payload, metadata);

                Check.NotNull(updated, "injection with escaped string must succeed");
                Check.Contains(updated, "\"note\":\"a}b\\\"c\"", "escaped string kept byte-identical");

                HermesHeadwearCodec.ReceiptInfo receipt = HermesHeadwearCodec.ReadComponentMetadata(updated);
                Check.True(receipt.Recognized, "receipt readable after injection");
                Check.Equal(13, receipt.Choice, "choice readable after injection");
            });

            Case.Run("codec.inject.failsClosedOnBadPayloads", () =>
            {
                string metadata = HermesHeadwearCodec.BuildMetadataJson(Guid.NewGuid(), 1);
                string[] bad =
                {
                    null,
                    "",
                    "null",
                    "[]",
                    "{\"a\":1",
                    "{\"a\":1,}",
                    "{\"a\":01}",
                    "{\"a\":1}{\"b\":2}",
                    "{\"kemHermesHeadwear\":{\"v\":1},\"kemHermesHeadwear\":{\"v\":1}}",
                    "{not-json}",
                };
                for (int i = 0; i < bad.Length; i++)
                {
                    Check.Null(HermesHeadwearCodec.InjectMetadata(bad[i], metadata),
                        "payload " + i + " must be refused");
                }

                Check.Null(HermesHeadwearCodec.InjectMetadata("{\"a\":1}", new string('x', 2048)),
                    "oversized receipt text refused");
                Check.Null(HermesHeadwearCodec.InjectMetadata(new string('x', HermesHeadwearCodec.MaxComponentDataChars + 1),
                    metadata), "oversized payload refused");
            });

            Case.Run("codec.inject.handlesEmptyObject", () =>
            {
                string metadata = HermesHeadwearCodec.BuildMetadataJson(Guid.NewGuid(), 40);
                string updated = HermesHeadwearCodec.InjectMetadata("{}", metadata);
                Check.Equal("{\"kemHermesHeadwear\":" + metadata + "}", updated, "empty object gets the receipt");

                string spaced = HermesHeadwearCodec.InjectMetadata("{  }", metadata);
                Check.NotNull(spaced, "whitespace-only object accepted");
                Check.True(HermesHeadwearCodec.ReadComponentMetadata(spaced).Recognized,
                    "receipt readable from whitespace-only object");
            });
        }
    }
}
