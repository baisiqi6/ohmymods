// 岸线冷启动准备 + owner 租约行为测试（source-linked：编译 ../../il2cpp/MapExtensionIslandArt.cs）。
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

            // ---- 9) 真实岸线 mask + 生产 planner（联合）：300x200/400x200 三行 ≥0.6、16/16 逐像素岸内
            {
                float[] reqW = { 25, 24, 21, 25, 21, 21, 29, 29, 26, 28, 28, 40, 22, 24, 40, 20 };
                float[] reqH = { 16, 22, 26, 17, 18, 18, 13, 13, 14, 18, 13, 25, 19, 32, 26, 20 };
                var sixteen = new List<KingdomEnhancedMod.MapIconRequest>(16);
                for (int i = 0; i < 16; i++)
                {
                    sixteen.Add(new KingdomEnhancedMod.MapIconRequest(KingdomEnhancedMod.MapIconKind.Steed,
                        100 + i, i, reqW[i], reqH[i]));
                }
                float aspect = prep.Width / (float)prep.Height;
                var liveMask = new KingdomEnhancedMod.MapShoreMask(prep.Width, prep.Height, prep.Mask);
                foreach (float worldW in new[] { 300f, 400f })
                {
                    Check(KingdomEnhancedMod.MapWorldLayout.ComposeDomains(
                            new KingdomEnhancedMod.MapIconBox(0f, 0f, worldW, 200f), 18f,
                            KingdomEnhancedMod.MapWorldLayout.DefaultExtensionReserve,
                            out _, out KingdomEnhancedMod.MapIconBox band), "live/domains-" + worldW);
                    Check(KingdomEnhancedMod.MapExtensionShapePlan.TryPlanWorldBox(band,
                        KingdomEnhancedMod.MapExtensionShapePlan.Margin, aspect,
                        out KingdomEnhancedMod.MapIconBox shape), "live/shape-" + worldW);
                    KingdomEnhancedMod.MapIconBox area = KingdomEnhancedMod.MapWorldLayout.IconAreaOf(shape, true, 20f);
                    var livePlacements = new List<KingdomEnhancedMod.MapIconPlacement>();
                    bool liveOk = KingdomEnhancedMod.MapExtensionIslandLayout.TryPlan(area, sixteen,
                        new List<KingdomEnhancedMod.MapIconBox>(), shape, liveMask, livePlacements,
                        out float liveScale, out int liveFailed);
                    Check(liveOk && liveFailed == 0 && livePlacements.Count == 16 && liveScale >= 0.6f,
                        "live/16-at-ge06-" + worldW + " scale=" + liveScale + " n=" + livePlacements.Count);
                    int inside = 0;
                    for (int i = 0; i < livePlacements.Count; i++)
                    {
                        float w = livePlacements[i].Request.Width * livePlacements[i].Scale;
                        float h = livePlacements[i].Request.Height * livePlacements[i].Scale;
                        var box = new KingdomEnhancedMod.MapIconBox(livePlacements[i].X, livePlacements[i].Y,
                            livePlacements[i].X + w, livePlacements[i].Y + h);
                        if (KingdomEnhancedMod.MapExtensionIslandLayout.FootprintInsideShore(box, shape, liveMask))
                        {
                            inside++;
                        }
                    }
                    Check(inside == 16, "live/16-inside-" + worldW + " inside=" + inside);
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
    }
}
