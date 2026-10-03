// 岸线冷启动准备 + owner 租约行为测试（source-linked：编译 ../../src/MapExtensionIslandArt.cs）。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace ShoreArtTests
{
    internal static class Program
    {
        private static int _checks;
        private static int _fails;

        private static void Check(bool ok, string label)
        {
            _checks++;
            if (ok) Console.WriteLine("ok   " + label);
            else { _fails++; Console.WriteLine("FAIL " + label); }
        }

        private static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "--layout-report")
            {
                return LayoutReport.Run(args[1], args[2], args.Length > 3 ? args[3] : null);
            }
            if (args.Length >= 2 && args[0] == "--self-check")
            {
                return LayoutReport.SelfCheck(args[1]);
            }
            if (args.Length >= 2 && args[0] == "--self-check-legacy")
            {
                return LayoutReport.SelfCheckLegacy(args[1]);
            }
            // 默认读**已嵌入的自有 PNG**（任意 cwd 可复跑；不依赖 repo 相对路径或用户绝对目录）。
            // 显式路径参数可选（第一个位置参数），公开输出只记 basename，不导出本地绝对路径。
            byte[] png;
            string assetLabel;
            if (args.Length > 0)
            {
                string asset = args[0];
                if (!File.Exists(asset)) { Console.WriteLine("asset missing (explicit): " + asset); return 2; }
                png = File.ReadAllBytes(asset);
                assetLabel = Path.GetFileName(asset);
            }
            else
            {
                png = KingdomEnhancedMod.MapExtensionIslandArt.ReadResourceBytes();
                if (png == null || png.Length == 0)
                {
                    Console.WriteLine("embedded own asset missing; pass an explicit asset path instead");
                    return 2;
                }
                assetLabel = KingdomEnhancedMod.MapExtensionIslandArt.ResourceName;
            }
            Console.WriteLine("asset " + assetLabel + " bytes=" + png.Length + " sha256 " + Sha(png));

            Png.DecodeBottomUp(png, out int sw, out int sh, out Color32[] source);   // Unity 行序（bottom-up），与 fake LoadImage 同一转换
            var prep = KingdomEnhancedMod.MapExtensionIslandArt.BuildPrep(source, sw, sh,
                KingdomEnhancedMod.MapExtensionIslandArt.TargetLogicalWidth);
            Check(prep != null, "prep/not-null");
            if (prep == null) { Console.WriteLine("FAILURES checks=" + _checks + " fails=" + _fails); return 1; }
            Console.WriteLine("src " + sw + "x" + sh + " bbox=" + prep.BboxWidth + "x" + prep.BboxHeight +
                " aspect=" + prep.Aspect.ToString("0.####") + " canvas=" + prep.Width + "x" + prep.Height +
                " island=" + prep.IslandPixels + " comps=" + prep.ComponentCount +
                " gray=[" + prep.GrayMin + "," + prep.GrayMax + "] levels=" + prep.LevelsUsed);
            Check(Math.Abs(prep.Aspect - prep.BboxWidth / (float)prep.BboxHeight) < 1e-4f, "prep/aspect-equals-real-bbox-ratio");
            Check(prep.Width - 4 == KingdomEnhancedMod.MapExtensionIslandArt.TargetLogicalWidth, "prep/target-width-224");
            float scale = KingdomEnhancedMod.MapExtensionIslandArt.TargetLogicalWidth / (float)prep.BboxWidth;
            Check(Math.Abs((prep.Height - 4) - Math.Round(prep.BboxHeight * scale)) <= 1, "prep/uniform-scale-height");
            int nonBinary = 0, inMask = 0, edgeOnMask = 0, edge = 0;
            var tones = new HashSet<byte>();
            for (int i = 0; i < prep.Shore.Length; i++)
            {
                if (prep.Shore[i].a != 0 && prep.Shore[i].a != 255) nonBinary++;
                if (prep.Shore[i].a != 0) { tones.Add(prep.Shore[i].r); inMask++; }
                if (prep.Outline[i].a != 0) { edge++; if (prep.Mask[i]) edgeOnMask++; }
            }
            Check(nonBinary == 0, "prep/binary-alpha");
            Check(tones.Count <= KingdomEnhancedMod.MapExtensionIslandArt.ToneLevels && tones.Count >= 1, "prep/tone-levels<=6");
            Check(edge > 0 && edgeOnMask == 0, "prep/outline-ring-outside-mask");
            Check(inMask == prep.IslandPixels, "prep/mask==island-pixels");
            int border = 0;
            for (int x = 0; x < prep.Width; x++)
            {
                if (prep.Shore[x].a != 0) border++;
                if (prep.Shore[(prep.Height - 1) * prep.Width + x].a != 0) border++;
            }
            for (int y = 0; y < prep.Height; y++)
            {
                if (prep.Shore[y * prep.Width].a != 0) border++;
                if (prep.Shore[y * prep.Width + prep.Width - 1].a != 0) border++;
            }
            Check(border == 0, "prep/transparent-padding-no-clip");

            // ---- 1b) 视觉合同：nearest 六档锚点（227/245/189/255）+ native 白色 outline
            byte[] palette = KingdomEnhancedMod.MapExtensionIslandArt.TonePalette;
            Check(Array.IndexOf(palette, (byte)227) >= 0 && Array.IndexOf(palette, (byte)245) >= 0 &&
                Array.IndexOf(palette, (byte)189) >= 0 && Array.IndexOf(palette, (byte)255) >= 0,
                "prep/palette-native-anchors");
            var toneProbe = new Color32[100];
            for (int i = 0; i < 100; i++) toneProbe[i] = new Color32(254, 254, 254, 255);
            var tonePrep = KingdomEnhancedMod.MapExtensionIslandArt.BuildPrep(toneProbe, 10, 10, 20);
            byte firstTone = 0;
            for (int i = 0; i < tonePrep.Shore.Length; i++)
            {
                if (tonePrep.Shore[i].a > 0) { firstTone = tonePrep.Shore[i].r; break; }
            }
            bool whiteOutline = true;
            int outlineCount = 0;
            for (int i = 0; i < tonePrep.Outline.Length; i++)
            {
                if (tonePrep.Outline[i].a == 0) continue;
                outlineCount++;
                if (tonePrep.Outline[i].r != 255 || tonePrep.Outline[i].g != 255 || tonePrep.Outline[i].b != 255)
                {
                    whiteOutline = false;
                }
            }
            Check(firstTone == 255 && whiteOutline && outlineCount > 0,
                "prep/nearest-254-to-255-white-outline tone=" + firstTone + " white=" + whiteOutline +
                " n=" + outlineCount);
            var anchorProbe = new Color32[3];
            anchorProbe[0] = new Color32(227, 227, 227, 255);
            anchorProbe[1] = new Color32(245, 245, 245, 255);
            anchorProbe[2] = new Color32(189, 189, 189, 255);
            var anchorPrep = KingdomEnhancedMod.MapExtensionIslandArt.BuildPrep(anchorProbe, 3, 1, 8);
            var anchorTones = new HashSet<byte>();
            for (int i = 0; i < anchorPrep.Shore.Length; i++)
            {
                if (anchorPrep.Shore[i].a > 0) anchorTones.Add(anchorPrep.Shore[i].r);
            }
            Check(anchorTones.Contains(227) && anchorTones.Contains(245) && anchorTones.Contains(189),
                "prep/palette-anchors-lossless");

            // ---- 1c) Unity 行序：LoadImage 输出 bottom-up（index 0 = 图像左下）；BuildPrep 保持同序
            var texProbe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(texProbe, png, false);
            Png.Decode(png, out int pw, out int ph, out Color32[] topDown);
            bool bottomUp = texProbe.width == pw && texProbe.height == ph;
            for (int y = 0; y < ph && bottomUp; y += 37)
            {
                for (int x = 0; x < pw; x += 97)
                {
                    Color32 a = texProbe.Pixels[y * pw + x];
                    Color32 b = topDown[(ph - 1 - y) * pw + x];
                    if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) { bottomUp = false; break; }
                }
            }
            Check(bottomUp, "prep/loadimage-unity-bottom-up");
            // 非对称上下 fixture：数组低半（=图像底部）亮 245、高半（=图像顶部）暗 116，
            // BuildPrep 输出必须保持同一行序（低 y=底部=亮、高 y=顶部=暗）。
            const int aw = 8, ah = 8;
            var asymmetric = new Color32[aw * ah];
            for (int y = 0; y < ah; y++)
            {
                for (int x = 0; x < aw; x++)
                {
                    byte tone = y < ah / 2 ? (byte)245 : (byte)116;
                    asymmetric[y * aw + x] = new Color32(tone, tone, tone, 255);
                }
            }
            var orientationPrep = KingdomEnhancedMod.MapExtensionIslandArt.BuildPrep(asymmetric, aw, ah, 8);
            byte lowTone = 0;
            byte highTone = 0;
            for (int y = 0; y < orientationPrep.Height && lowTone == 0; y++)
            {
                for (int x = 0; x < orientationPrep.Width; x++)
                {
                    if (orientationPrep.Shore[y * orientationPrep.Width + x].a > 0)
                    {
                        lowTone = orientationPrep.Shore[y * orientationPrep.Width + x].r;
                        break;
                    }
                }
            }
            for (int y = orientationPrep.Height - 1; y >= 0 && highTone == 0; y--)
            {
                for (int x = 0; x < orientationPrep.Width; x++)
                {
                    if (orientationPrep.Shore[y * orientationPrep.Width + x].a > 0)
                    {
                        highTone = orientationPrep.Shore[y * orientationPrep.Width + x].r;
                        break;
                    }
                }
            }
            Check(lowTone == 245 && highTone == 116,
                "prep/unity-bottom-up-row-order low=" + lowTone + " high=" + highTone);

            // ---- 1d) 非对称真实文件字节联合：Encode(文件上半=暗116=图像顶) → fake LoadImage(bottom-up)
            // → 生产 BuildPrep。不共享"解码方向"假设，三入口（联合/LoadImage/导出）必须同序。
            const int fw = 8, fh = 8;
            var fileOrder = new Color32[fw * fh];
            for (int y = 0; y < fh; y++)
            {
                for (int x = 0; x < fw; x++)
                {
                    byte tone = y < fh / 2 ? (byte)116 : (byte)245;
                    fileOrder[y * fw + x] = new Color32(tone, tone, tone, 255);
                }
            }
            byte[] asymmetricPng = Png.Encode(fw, fh, fileOrder);
            var asymmetricTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Check(ImageConversion.LoadImage(asymmetricTex, asymmetricPng, false), "yfix/asymmetric-loadimage-ok");
            Check(asymmetricTex.Pixels.Length == fw * fh && asymmetricTex.Pixels[0].r == 245,
                "yfix/loadimage-index0-is-image-bottom got=" +
                (asymmetricTex.Pixels.Length > 0 ? asymmetricTex.Pixels[0].r.ToString() : "empty"));
            var jointPrep = KingdomEnhancedMod.MapExtensionIslandArt.BuildPrep(
                asymmetricTex.GetPixels32(), fw, fh, 8);
            byte jointLow = 0, jointHigh = 0;
            for (int y = 0; y < jointPrep.Height && jointLow == 0; y++)
            {
                for (int x = 0; x < jointPrep.Width; x++)
                {
                    if (jointPrep.Shore[y * jointPrep.Width + x].a > 0)
                    {
                        jointLow = jointPrep.Shore[y * jointPrep.Width + x].r;
                        break;
                    }
                }
            }
            for (int y = jointPrep.Height - 1; y >= 0 && jointHigh == 0; y--)
            {
                for (int x = 0; x < jointPrep.Width; x++)
                {
                    if (jointPrep.Shore[y * jointPrep.Width + x].a > 0)
                    {
                        jointHigh = jointPrep.Shore[y * jointPrep.Width + x].r;
                        break;
                    }
                }
            }
            Check(jointLow == 245 && jointHigh == 116,
                "yfix/joint-loadimage-buildprep-row-order low=" + jointLow + " high=" + jointHigh);

            int cw = 40, ch = 24;
            var synth = new Color32[cw * ch];
            for (int y = 4; y < 20; y++) for (int x = 4; x < 30; x++) synth[y * cw + x] = new Color32(120, 120, 120, 255);
            synth[1 * cw + 37] = new Color32(255, 255, 255, 255);
            var sp = KingdomEnhancedMod.MapExtensionIslandArt.BuildPrep(synth, cw, ch, 20);
            Check(sp != null && sp.ComponentCount == 2 && sp.BboxX == 4 && sp.BboxWidth == 26,
                "prep/largest-island-excludes-speck");

            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseAllBorrowed();
            var img = new Image { sprite = new Sprite() };
            var original = img.sprite;
            var own = new Sprite(); var ownB = new Sprite();
            var ownerA = new object(); var ownerB = new object();
            var leaseA = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(img, own, ownerA);
            Check(leaseA != null && img.sprite == own, "lease/borrow-applies");
            Check(leaseA.OriginalSprite == original, "lease/snapshot-original-once");
            var leaseA2 = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(img, own, ownerA);
            Check(ReferenceEquals(leaseA, leaseA2) && leaseA.OriginalSprite == original, "lease/rebind-same-owner-keeps-snapshot");
            Check(KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1, "lease/rebind-no-duplicate");
            var leaseB = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(img, ownB, ownerB);
            Check(leaseB != null && img.sprite == ownB && KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1, "lease/owner-switch-supersedes-old-bounded");
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(ownerA);
            Check(img.sprite == ownB, "lease/A-cleanup-keeps-B");
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(ownerB);
            Check(img.sprite == original, "lease/B-release-restores-original");

            var img2 = new Image { sprite = new Sprite() };
            var ext = new Sprite();
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(img2, own, ownerA);
            img2.sprite = ext;
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(ownerA);
            Check(img2.sprite == ext, "lease/external-swap-not-overwritten");
            Check(KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores == 0, "lease/external-swap-finalized");

            var img3 = new Image { sprite = new Sprite() };
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(img3, own, ownerA);
            img3.ThrowOnSet = true;
            int destroyBefore = UnityEngine.Object.DestroyCount;
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(ownerA);
            Check(img3._sprite != null && KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores >= 1, "lease/throwing-release-keeps-responsibility");
            Check(UnityEngine.Object.DestroyCount == destroyBefore, "lease/no-destroy-on-failed-release");
            img3.ThrowOnSet = false;
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(ownerA);
            Check(img3._sprite != own, "lease/retry-after-failure-restores");

            var img4 = new Image { sprite = new Sprite() };
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(img4, own, ownerA);
            int leasesBeforeDead = KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount;
            UnityEngine.Object.Destroy(img4);
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(ownerA);
            Check(KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount < leasesBeforeDead, "lease/dead-wrapper-cleared");

            // ---- 4) 共享同一 own sprite：A→B 接管后旧 A 不得撤 B（代际门，不靠 sprite 相等）
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseAllBorrowed();
            var imgS = new Image { sprite = new Sprite() };
            var native = imgS.sprite;
            var shared = new Sprite();
            var oA = new object(); var oB = new object();
            var lA = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgS, shared, oA);
            var lB = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgS, shared, oB);
            Check(lA != null && lB != null && imgS.sprite == shared, "lease/shared-own-b-binds");
            Check(KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1, "lease/shared-own-supersedes-old");
            Check(lA != null && lA.RestorePending == false, "lease/superseded-no-responsibility");
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oA);
            Check(imgS.sprite == shared, "lease/A-cleanup-same-own-keeps-B");
            var lAOld = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgS, shared, oA);
            Check(KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1, "lease/late-A-bind-bounded");
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oB);
            Check(imgS.sprite == native, "lease/shared-own-b-release-restores-native");
            Check(KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 0, "lease/shared-own-clears");

            // ---- 5) 真实 TryEnsure + Reset 故障路径：恢复未完成前不得销毁仍被使用的自有 Sprite/Texture
            int createBeforeEnsure = UnityEngine.Object.CreateCount;
            bool ensured = KingdomEnhancedMod.MapExtensionIslandArt.TryEnsure();
            Check(ensured, "reset/ensure-real-tryensure");
            int cacheAlive = UnityEngine.Object.AliveCount;
            var imgR = new Image { sprite = new Sprite() };
            var nativeR = imgR.sprite;
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgR, KingdomEnhancedMod.MapExtensionIslandArt.Shore, oA);
            imgR.ThrowOnSet = true;
            int destroyBeforeReset = UnityEngine.Object.DestroyCount;
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            Check(KingdomEnhancedMod.MapExtensionIslandArt.Shore != null, "reset/keeps-shore-while-restore-pending");
            Check(KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores >= 1, "reset/keeps-responsibility");
            Check(UnityEngine.Object.DestroyCount == destroyBeforeReset, "reset/no-destroy-while-pending");
            imgR.ThrowOnSet = false;
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            Check(imgR.sprite == nativeR, "reset/restores-after-retry");
            Check(KingdomEnhancedMod.MapExtensionIslandArt.Shore == null, "reset/destroys-after-all-restored");
            Check(nativeR.Destroyed == false, "reset/native-sprite-never-destroyed");
            var imgX = new Image { sprite = new Sprite() };
            var ext2 = new Sprite();
            KingdomEnhancedMod.MapExtensionIslandArt.TryEnsure();
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgX, KingdomEnhancedMod.MapExtensionIslandArt.Shore, oA);
            imgX.sprite = ext2;
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            Check(imgX.sprite == ext2, "reset/external-swap-not-overwritten");
            Check(KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 0 && KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores == 0,
                "reset/external-swap-finalized-not-piled");
            Check(ext2.Destroyed == false, "reset/external-sprite-untouched");

            // ---- 6) TryEnsure 临时对象生命周期：成功/GetPixels 抛异常/SpriteCreate 失败 每个创建必对应清理
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            int c0 = UnityEngine.Object.CreateCount, d0 = UnityEngine.Object.DestroyCount;
            bool ok1 = KingdomEnhancedMod.MapExtensionIslandArt.TryEnsure();
            int c1 = UnityEngine.Object.CreateCount, d1 = UnityEngine.Object.DestroyCount;
            Check(ok1 && (c1 - c0) == 5 && (d1 - d0) == 1, "lifecycle/success-created5-destroyed1(raw) got c=" + (c1 - c0) + " d=" + (d1 - d0));
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            int c2, d2;
            bool ok2;
            {
                var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false) { GetPixelsThrows = true };
                UnityEngine.Object.Alive.Remove(probe); // probe 本体仅测试用，不计入
                ImageConversion.GetPixelsThrowsNext = true;
                c2 = UnityEngine.Object.CreateCount; d2 = UnityEngine.Object.DestroyCount;
                ok2 = KingdomEnhancedMod.MapExtensionIslandArt.TryEnsure();
                ImageConversion.GetPixelsThrowsNext = false;
            }
            Check(!ok2 && (UnityEngine.Object.CreateCount - c2) == (UnityEngine.Object.DestroyCount - d2),
                "lifecycle/getpixels-failure-all-temporaries-destroyed");
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            UnityEngine.Sprite.FailNextCreate = true;
            bool ok3 = KingdomEnhancedMod.MapExtensionIslandArt.TryEnsure();
            UnityEngine.Sprite.FailNextCreate = false;
            Check(!ok3, "lifecycle/sprite-create-failure-fails-closed");
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            // ---- 7) dead wrapper 严格递减；读异常不产生成功租约
            KingdomEnhancedMod.MapExtensionIslandArt.TryEnsure();
            var imgD = new Image { sprite = new Sprite() };
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgD, KingdomEnhancedMod.MapExtensionIslandArt.Shore, oA);
            int beforeDead = KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount;
            UnityEngine.Object.Destroy(imgD);
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oA);
            Check(KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == beforeDead - 1, "lease/dead-wrapper-strict-decrease");
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();

            // ---- 8) 租约 v2：归还读故障/事务式接管/原生 wrapper 身份/写后抛（独立 review 反例的 source-linked 复现）
            var ownV2A = new Sprite();
            var ownV2B = new Sprite();

            // 8.1 归还读故障 = unknown：保留租约与 pending（不得当“外部已替换”注销；资源不得销毁）
            var imgRF = new Image { sprite = new Sprite() };
            var nativeRF = imgRF.sprite;
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgRF, ownV2A, oA);
            imgRF.ThrowOnGet = true;
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oA);
            Check(KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1 &&
                KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores >= 1,
                "leasev2/read-fault-keeps-responsibility leases=" + KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount +
                " pending=" + KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores);
            imgRF.ThrowOnGet = false;
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oA);
            Check(imgRF.sprite == nativeRF && KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 0 &&
                KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores == 0, "leasev2/read-fault-recovers");

            // 8.2 接管写前抛：事务回滚——旧租约/责任原样保留，新接管不谎报成功
            var imgT1 = new Image { sprite = new Sprite() };
            var nativeT1 = imgT1.sprite;
            var leaseT1A = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgT1, ownV2A, oA);
            imgT1.ThrowOnSet = true;
            var leaseT1B = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgT1, ownV2B, oB);
            Check(leaseT1B == null && KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1 &&
                imgT1._sprite == ownV2A && KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores >= 1,
                "leasev2/takeover-prethrow-rollback b=" + (leaseT1B != null) +
                " leases=" + KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount +
                " pending=" + KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores);
            imgT1.ThrowOnSet = false;
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oB);
            Check(imgT1.sprite == ownV2A && KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1,
                "leasev2/takeover-old-owner-cleanup-safe");
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oA);
            Check(imgT1.sprite == nativeT1 && leaseT1A != null && leaseT1A.OriginalSprite == nativeT1,
                "leasev2/takeover-original-snapshot-preserved");

            // 8.3 接管写后抛（setter 非原子）：不谎报成功，但状态收敛为已接管；原快照不丢
            var imgT2 = new Image { sprite = new Sprite() };
            var nativeT2 = imgT2.sprite;
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgT2, ownV2A, oA);
            imgT2.WriteThenThrowSet = true;
            var leaseT2B = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgT2, ownV2B, oB);
            Check(leaseT2B == null && imgT2._sprite == ownV2B &&
                KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1 &&
                KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores >= 1,
                "leasev2/takeover-write-then-throw-not-success leases=" +
                KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount);
            imgT2.WriteThenThrowSet = false;
            var leaseT2Reb = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgT2, ownV2B, oB);
            Check(leaseT2Reb != null && leaseT2Reb.OriginalSprite == nativeT2 &&
                !ReferenceEquals(leaseT2Reb.OriginalSprite, ownV2A),
                "leasev2/write-then-throw-keeps-native-original");
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oB);
            Check(imgT2.sprite == nativeT2, "leasev2/write-then-throw-restores-native");

            // 8.4 同 native 不同 wrapper（alias）：识别为同一 Image，不得第二租约、不得把 own 当原值
            var imgA4 = new Image { sprite = new Sprite() };
            var nativeA4 = imgA4.sprite;
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgA4, ownV2A, oA);
            var alias4 = new Image { Native = imgA4.Native };
            var leaseAlias4 = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(alias4, ownV2A, oA);
            Check(leaseAlias4 != null && KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1 &&
                leaseAlias4.OriginalSprite == nativeA4 && !ReferenceEquals(leaseAlias4.OriginalSprite, ownV2A),
                "leasev2/alias-wrapper-single-lease leases=" + KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount);
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oA);
            Check(imgA4.sprite == nativeA4 && alias4.sprite == nativeA4, "leasev2/alias-wrapper-restores-native");

            // 8.5 身份读取故障 = unknown：拒绝接管/新建，旧租约与写入值不动
            var imgU5 = new Image { sprite = new Sprite() };
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgU5, ownV2A, oA);
            imgU5.ThrowOnIdentity = true;
            var leaseU5 = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgU5, ownV2B, oB);
            Check(leaseU5 == null && imgU5._sprite == ownV2A &&
                KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1,
                "leasev2/identity-unknown-fail-closed");
            imgU5.ThrowOnIdentity = false;
            KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oA);
            Check(imgU5.sprite != ownV2A, "leasev2/identity-unknown-then-release");

            // 8.6 读故障期间 Reset 不得销毁仍被引用的自有资源；恢复可读后正常收尾
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            bool ready6 = KingdomEnhancedMod.MapExtensionIslandArt.TryEnsure();
            var img6 = new Image { sprite = new Sprite() };
            KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(img6, KingdomEnhancedMod.MapExtensionIslandArt.Shore, oA);
            img6.ThrowOnGet = true;
            int destroy6 = UnityEngine.Object.DestroyCount;
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            Check(ready6 && KingdomEnhancedMod.MapExtensionIslandArt.Shore != null &&
                KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores >= 1 &&
                UnityEngine.Object.DestroyCount == destroy6,
                "leasev2/reset-readfault-keeps-shore pending=" +
                KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores);
            img6.ThrowOnGet = false;
            KingdomEnhancedMod.MapExtensionIslandArt.Reset();
            Check(KingdomEnhancedMod.MapExtensionIslandArt.Shore == null && img6.sprite != null,
                "leasev2/reset-readfault-finalizes-after-recovery");

            // ---- 9) 真实顶面 mask + 生产 planner（联合）：
            //      (a) 两个正常 world 视口 16/16、scale ≥ 0.6（调用点显式下限，与生产一致）、
            //          每 footprint 逐像素位于顶面 PlacementMask + 自由错落诊断（右侧利用/无长水平带/非微抖动）；
            //      (b2) detail 框（230×84.74）+ 真实船标：16/16 ≥0.6；锚点=优选位置 + 有界修正，
            //          fallback 锚路径在船标挡住主锚邻域时被实际使用（可观测锚距离）；
            //      (c) 判别性负例：alpha=true 但 PlacementMask=false 的崖面矩形 —— 同一几何下 alpha mask 会放下、
            //          运行期顶面 mask 必须拒绝（证明不是改图假通过）。
            {
                float[] reqW = { 25, 24, 21, 25, 21, 21, 29, 29, 26, 28, 28, 40, 22, 24, 40, 20 };
                float[] reqH = { 16, 22, 26, 17, 18, 18, 13, 13, 14, 18, 13, 25, 19, 32, 26, 20 };
                var sixteen = new List<KingdomEnhancedMod.MapIconRequest>(16);
                for (int i = 0; i < 16; i++)
                {
                    sixteen.Add(new KingdomEnhancedMod.MapIconRequest(KingdomEnhancedMod.MapIconKind.Steed,
                        100 + i, i, reqW[i], reqH[i]));
                }
                Check(prep.PlacementMask != null && prep.PlacementPixels > 0 && prep.PlacementPixels < prep.IslandPixels,
                    "live/placement-mask-nonempty-subset placement=" + prep.PlacementPixels +
                    " island=" + prep.IslandPixels);
                Check(KingdomEnhancedMod.MapExtensionIslandArt.PlacementCutX.Length ==
                        KingdomEnhancedMod.MapExtensionIslandArt.PlacementCutY.Length &&
                    KingdomEnhancedMod.MapExtensionIslandArt.PlacementCutX.Length >= 2,
                    "live/placement-cut-table");
                bool cutMonotonic = true;
                for (int i = 1; i < KingdomEnhancedMod.MapExtensionIslandArt.PlacementCutX.Length; i++)
                {
                    if (KingdomEnhancedMod.MapExtensionIslandArt.PlacementCutX[i] <=
                        KingdomEnhancedMod.MapExtensionIslandArt.PlacementCutX[i - 1]) cutMonotonic = false;
                }
                Check(cutMonotonic, "live/placement-cut-monotonic");
                var liveMask = new KingdomEnhancedMod.MapShoreMask(prep.Width, prep.Height, prep.PlacementMask);
                var alphaMask = new KingdomEnhancedMod.MapShoreMask(prep.Width, prep.Height, prep.Mask);
                float aspect = prep.Width / (float)prep.Height;
                KingdomEnhancedMod.MapIconBox lastShape = default;
                foreach (float worldW in new[] { 300f, 400f })
                {
                    Check(KingdomEnhancedMod.MapWorldLayout.ComposeDomains(
                            new KingdomEnhancedMod.MapIconBox(0f, 0f, worldW, 200f), 18f,
                            KingdomEnhancedMod.MapWorldLayout.DefaultExtensionReserve,
                            out _, out KingdomEnhancedMod.MapIconBox band), "live/domains-" + worldW);
                    Check(KingdomEnhancedMod.MapExtensionShapePlan.TryPlanWorldBox(band,
                        KingdomEnhancedMod.MapExtensionShapePlan.Margin, aspect,
                        out KingdomEnhancedMod.MapIconBox shape), "live/shape-" + worldW);
                    lastShape = shape;
                    KingdomEnhancedMod.MapIconBox area = KingdomEnhancedMod.MapWorldLayout.IconAreaOf(shape);
                    var livePlacements = new List<KingdomEnhancedMod.MapIconPlacement>();
                    bool liveOk = KingdomEnhancedMod.MapExtensionIslandLayout.TryPlan(area, sixteen,
                        new List<KingdomEnhancedMod.MapIconBox>(), shape, liveMask, livePlacements,
                        out float liveScale, out int liveFailed, KingdomEnhancedMod.MapExtensionIslandLayout.PreferScale);
                    Check(liveOk && liveFailed == 0 && livePlacements.Count == 16 && liveScale >= 0.6f,
                        "live/16-at-ge06-" + worldW + " scale=" + liveScale + " n=" + livePlacements.Count);
                    int inside = 0, indexOk = 0;
                    foreach (KingdomEnhancedMod.MapIconPlacement p in livePlacements)
                    {
                        float w = p.Request.Width * p.Scale;
                        float h = p.Request.Height * p.Scale;
                        var box = new KingdomEnhancedMod.MapIconBox(p.X, p.Y, p.X + w, p.Y + h);
                        if (KingdomEnhancedMod.MapExtensionIslandLayout.FootprintInsideShore(box, shape, liveMask))
                        {
                            inside++;
                        }
                        if (p.RequestIndex >= 0 && p.RequestIndex < 16 &&
                            p.Request.Width == sixteen[p.RequestIndex].Width) indexOk++;
                    }
                    Check(inside == 16, "live/16-inside-" + worldW + " inside=" + inside);
                    Check(indexOk == 16, "live/request-index-" + worldW + " ok=" + indexOk);
                    AddScatterChecks(livePlacements, shape, liveMask, "live" + worldW);
                }
                // (b2) detail 框 + 真实船标 blocker：16/16 ≥0.6；锚点=优选位置 + 有界修正；
                //      fallback 锚路径被实际使用（每个 footprint 中心须在 24 UI 内命中 primary/fallback 之一）。
                {
                    var detailBox = new KingdomEnhancedMod.MapIconBox(-92f, -46.3684211f, 138f, 38.3684211f);
                    var boat = new List<KingdomEnhancedMod.MapIconBox>
                    {
                        new KingdomEnhancedMod.MapIconBox(-64f, 2f, -32f, 34f),
                    };
                    var detailOut = new List<KingdomEnhancedMod.MapIconPlacement>();
                    bool detailOk = KingdomEnhancedMod.MapExtensionIslandLayout.TryPlan(detailBox, sixteen, boat,
                        detailBox, liveMask, detailOut, out float detailScale, out int detailFailed,
                        KingdomEnhancedMod.MapExtensionIslandLayout.PreferScale);
                    Check(detailOk && detailFailed == 0 && detailOut.Count == 16 && detailScale >= 0.6f,
                        "detail/16-with-boat scale=" + detailScale + " n=" + detailOut.Count);
                    var rankD = new List<int>();
                    for (int i = 0; i < sixteen.Count; i++) rankD.Add(i);
                    rankD.Sort((x, y) =>
                    {
                        float ax = sixteen[x].Width * sixteen[x].Height, ay = sixteen[y].Width * sixteen[y].Height;
                        if (ax != ay) return ay.CompareTo(ax);
                        if (sixteen[x].Height != sixteen[y].Height) return sixteen[y].Height.CompareTo(sixteen[x].Height);
                        if (sixteen[x].Width != sixteen[y].Width) return sixteen[y].Width.CompareTo(sixteen[x].Width);
                        return x.CompareTo(y);
                    });
                    int anchored = 0, fallbackUsed = 0;
                    float worst = 0f;
                    foreach (KingdomEnhancedMod.MapIconPlacement p in detailOut)
                    {
                        int slot2 = rankD.IndexOf(p.RequestIndex);
                        float px2 = KingdomEnhancedMod.MapExtensionIslandLayout.PreferredAnchorX[slot2];
                        float py2 = KingdomEnhancedMod.MapExtensionIslandLayout.PreferredAnchorY[slot2];
                        float fx2 = KingdomEnhancedMod.MapExtensionIslandLayout.FallbackAnchorX[slot2];
                        float fy2 = KingdomEnhancedMod.MapExtensionIslandLayout.FallbackAnchorY[slot2];
                        float cx2 = p.X + p.Request.Width * p.Scale * 0.5f;
                        float cy2 = p.Y + p.Request.Height * p.Scale * 0.5f;
                        float dP = Math.Max(Math.Abs(cx2 - (detailBox.X0 + px2 * detailBox.Width)),
                            Math.Abs(cy2 - (detailBox.Y0 + py2 * detailBox.Height)));
                        float dF = float.IsNaN(fx2) ? float.MaxValue : Math.Max(
                            Math.Abs(cx2 - (detailBox.X0 + fx2 * detailBox.Width)),
                            Math.Abs(cy2 - (detailBox.Y0 + fy2 * detailBox.Height)));
                        float nearest = Math.Min(dP, dF);
                        if (nearest <= 24f) anchored++;
                        if (dF < dP && dF <= 24f) fallbackUsed++;
                        if (nearest > worst) worst = nearest;
                    }
                    Check(anchored == 16, "detail/anchored-within-24 n=" + anchored + " worst=" + worst.ToString("0.#"));
                    Check(fallbackUsed >= 1, "detail/fallback-path-used n=" + fallbackUsed);
                }
                // (c) 判别性负例：固定 12×6 画布像素崖面矩形（65,34)-(76,39)（全部 alpha 且非顶面）
                {
                    int cx0 = 65, cy0 = 34, cx1 = 76, cy1 = 39;
                    _cliffAlpha = prep.Mask;
                    _cliffPlacement = prep.PlacementMask;
                    Check(prep.Mask[cy0 * prep.Width + cx0] && !prep.PlacementMask[cy0 * prep.Width + cx0],
                        "cliff/sample-is-alpha-not-placement");
                    KingdomEnhancedMod.MapIconBox cliff = CanvasRectToPaper(lastShape, prep.Width, prep.Height,
                        cx0, cy0, cx1, cy1);
                    Check(KingdomEnhancedMod.MapExtensionIslandLayout.FootprintInsideShore(cliff, lastShape, alphaMask),
                        "cliff/alpha-inside-true");
                    Check(!KingdomEnhancedMod.MapExtensionIslandLayout.FootprintInsideShore(cliff, lastShape, liveMask),
                        "cliff/placement-inside-false");
                    var probe = new List<KingdomEnhancedMod.MapIconRequest>
                    {
                        new KingdomEnhancedMod.MapIconRequest(KingdomEnhancedMod.MapIconKind.Steed, 199, 0, 14f, 7f),
                    };
                    var alphaOut = new List<KingdomEnhancedMod.MapIconPlacement>();
                    bool alphaPlaced = KingdomEnhancedMod.MapExtensionIslandLayout.TryPlan(cliff, probe,
                        new List<KingdomEnhancedMod.MapIconBox>(), lastShape, alphaMask, alphaOut, out _, out _);
                    var liveOut = new List<KingdomEnhancedMod.MapIconPlacement>();
                    bool livePlaced = KingdomEnhancedMod.MapExtensionIslandLayout.TryPlan(cliff, probe,
                        new List<KingdomEnhancedMod.MapIconBox>(), lastShape, liveMask, liveOut, out _, out int liveFailed);
                    Check(alphaPlaced && alphaOut.Count == 1 && AlphaOutOnCliff(alphaOut[0], lastShape, prep.Width, prep.Height),
                        "cliff/alpha-control-places n=" + alphaOut.Count);
                    Check(!livePlaced && liveOut.Count == 0 && liveFailed == 1,
                        "cliff/placement-rejects n=" + liveOut.Count);
                }
            }

            // ---- 10) 独立回归反例：alias 释放 / 首绑写后未知 / sprite wrapper 身份（native 身份判定）
            {
                var own3A = new Sprite();
                KingdomEnhancedMod.MapExtensionIslandArt.Reset();

                // 10.1 ReleaseBorrowed(alias)：同 native 不同 wrapper 必须能释放（不能只修借用入口）
                var imgRb = new Image { sprite = new Sprite() };
                var nativeRb = imgRb.sprite;
                KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgRb, own3A, oA);
                var aliasRb = new Image { Native = imgRb.Native };
                KingdomEnhancedMod.MapExtensionIslandArt.ReleaseBorrowed(aliasRb);
                Check(imgRb.sprite == nativeRb && KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 0,
                    "leasev3/release-borrowed-alias leases=" + KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount +
                    " restored=" + (imgRb.sprite == nativeRb));

                // 10.2 首绑：setter 写后抛 + 随后 getter unknown —— 无旧租约可留，
                //      必须保留原值与“可能已写入”的责任；Reset 不得销毁仍挂在 Image 上的 own
                KingdomEnhancedMod.MapExtensionIslandArt.TryEnsure();
                var shore3 = KingdomEnhancedMod.MapExtensionIslandArt.Shore;
                var imgWU = new Image { sprite = new Sprite() };
                var nativeWU = imgWU.sprite;
                imgWU.WriteThenThrowSet = true;
                imgWU.ReadFaultAfterWriteThrow = true;
                var leaseWU = KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgWU, shore3, oA);
                bool ownWritten = imgWU._sprite == shore3;
                int destroyWU = UnityEngine.Object.DestroyCount;
                KingdomEnhancedMod.MapExtensionIslandArt.Reset();
                Check(leaseWU == null && ownWritten && KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 1 &&
                    KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores >= 1 &&
                    UnityEngine.Object.DestroyCount == destroyWU && !shore3.Destroyed,
                    "leasev3/first-write-unknown-keeps-responsibility lease=" + (leaseWU != null) +
                    " leases=" + KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount +
                    " pending=" + KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores +
                    " ownDestroyed=" + shore3.Destroyed);
                imgWU.ThrowOnGet = false;
                imgWU.WriteThenThrowSet = false;
                KingdomEnhancedMod.MapExtensionIslandArt.Reset();
                Check(CompareSpriteNative(imgWU._sprite, nativeWU) && shore3.Destroyed,
                    "leasev3/first-write-unknown-recovers destroyAfterRecovery=" + shore3.Destroyed);
                KingdomEnhancedMod.MapExtensionIslandArt.Reset();

                // 10.3 归还读回 wrapper alias（同 native 不同 managed wrapper）：不得误判外部替换
                var imgSW = new Image { sprite = new Sprite() };
                var nativeSW = imgSW.sprite;
                var own3B = new Sprite();
                KingdomEnhancedMod.MapExtensionIslandArt.TryBorrow(imgSW, own3B, oA);
                imgSW.FreshSpriteWrappers = true;
                KingdomEnhancedMod.MapExtensionIslandArt.ReleaseOwner(oA);
                Check(KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount == 0 &&
                    KingdomEnhancedMod.MapExtensionIslandArt.PendingRestores == 0 &&
                    CompareSpriteNative(imgSW._sprite, nativeSW),
                    "leasev3/sprite-wrapper-alias-not-foreign leases=" +
                    KingdomEnhancedMod.MapExtensionIslandArt.LeaseCount +
                    " restoredNative=" + CompareSpriteNative(imgSW._sprite, nativeSW));
            }

            Console.WriteLine((_fails == 0 ? "ALL PASS" : "FAILURES") + " checks=" + _checks + " fails=" + _fails);
            return _fails == 0 ? 0 : 1;
        }

        private static bool CompareSpriteNative(Sprite a, Sprite b)
        {
            if (a == null || b == null) return false;
            try { return a.Pointer == b.Pointer && a.GetInstanceID() == b.GetInstanceID(); }
            catch (Exception) { return false; }
        }

        private static string Sha(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
        }

        /// <summary>画布像素矩形 → paper box（0.25/0.75 偏移保证 floor/ceil 覆盖 = [x0..x1]×[y0..y1]）。</summary>
        private static KingdomEnhancedMod.MapIconBox CanvasRectToPaper(
            in KingdomEnhancedMod.MapIconBox frame, int canvasW, int canvasH, int x0, int y0, int x1, int y1)
        {
            float sx = canvasW / frame.Width;
            float sy = canvasH / frame.Height;
            return new KingdomEnhancedMod.MapIconBox(
                frame.X0 + (x0 + 0.25f) / sx, frame.Y0 + (y0 + 0.25f) / sy,
                frame.X0 + (x1 + 0.75f) / sx, frame.Y0 + (y1 + 0.75f) / sy);
        }

        private static bool[] _cliffAlpha;
        private static bool[] _cliffPlacement;

        /// <summary>alpha 对照放置是否真的落在崖面（画布像素全部 alpha=true 且非 placement）。</summary>
        private static bool AlphaOutOnCliff(in KingdomEnhancedMod.MapIconPlacement p,
            in KingdomEnhancedMod.MapIconBox frame, int canvasW, int canvasH)
        {
            if (_cliffAlpha == null || _cliffPlacement == null) return false;
            float sx = canvasW / frame.Width;
            float sy = canvasH / frame.Height;
            int x0 = (int)Math.Floor((p.X - frame.X0) * sx);
            int y0 = (int)Math.Floor((p.Y - frame.Y0) * sy);
            int x1 = (int)Math.Ceiling((p.X + p.Request.Width * p.Scale - frame.X0) * sx) - 1;
            int y1 = (int)Math.Ceiling((p.Y + p.Request.Height * p.Scale - frame.Y0) * sy) - 1;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || y < 0 || x >= canvasW || y >= canvasH) return false;
                    if (!_cliffAlpha[y * canvasW + x]) return false;
                    if (_cliffPlacement[y * canvasW + x]) return false;
                }
            }
            return true;
        }

        /// <summary>自由错落诊断（真实视觉合同，非微抖动）：右半≥6、最大同带≤4、最长近水平链≤3、
        /// y 中心带≥8、y 范围≥20；旧三行布局（3 带 5/6 项）必然失败。</summary>
        private static void AddScatterChecks(List<KingdomEnhancedMod.MapIconPlacement> placements,
            in KingdomEnhancedMod.MapIconBox shape, KingdomEnhancedMod.MapShoreMask mask, string tag)
        {
            float fx = mask.Width / shape.Width;
            float bandTol = 1.5f * fx;
            var centers = new List<float>();
            var xs = new List<float>();
            foreach (KingdomEnhancedMod.MapIconPlacement p in placements)
            {
                centers.Add(p.Y + p.Request.Height * p.Scale * 0.5f);
                xs.Add(p.X + p.Request.Width * p.Scale * 0.5f);
            }
            var sorted = new List<float>(centers);
            sorted.Sort();
            int bands = 0, maxBand = 0, run = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i == 0 || sorted[i] - sorted[i - 1] > bandTol)
                {
                    if (run > maxBand) maxBand = run;
                    bands++;
                    run = 0;
                }
                run++;
            }
            if (run > maxBand) maxBand = run;
            int rightHalf = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                if (xs[i] > shape.X0 + shape.Width * 0.5f) rightHalf++;
            }
            var order = new List<int>();
            for (int i = 0; i < placements.Count; i++) order.Add(i);
            order.Sort((a, b) => xs[a] != xs[b] ? xs[a].CompareTo(xs[b]) : centers[a].CompareTo(centers[b]));
            int longest = 1;
            for (int i = 0; i < order.Count; i++)
            {
                int chain = 1;
                float lastX = xs[order[i]];
                for (int j = i + 1; j < order.Count; j++)
                {
                    if (Math.Abs(centers[order[j]] - centers[order[i]]) <= bandTol &&
                        xs[order[j]] - lastX < 12f * fx)
                    {
                        chain++;
                        lastX = xs[order[j]];
                    }
                }
                if (chain > longest) longest = chain;
            }
            float yMin = float.MaxValue, yMax = float.MinValue;
            foreach (float c in centers)
            {
                if (c < yMin) yMin = c;
                if (c > yMax) yMax = c;
            }
            Check(rightHalf >= 6, "scatter/" + tag + "-right-half n=" + rightHalf);
            Check(maxBand <= 4, "scatter/" + tag + "-max-band n=" + maxBand);
            Check(longest <= 3, "scatter/" + tag + "-longest-chain n=" + longest);
            Check(bands >= 8, "scatter/" + tag + "-y-bands n=" + bands);
            Check(yMax - yMin >= 20f, "scatter/" + tag + "-y-range r=" + (yMax - yMin).ToString("0.#"));
        }

    }
}
