using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace KingdomEnhancedMod;

// Unwired R4 environment adapter. The future PalaceCourtScene.Create/CourtBinding owner
// supplies one measured calibration and explicit absolute sort orders. No hooks, actors,
// camera access, ticks, native-parent edits, payment or save writes live in this module.
// All three layers stay in one game-parent plane at zero relative parallax. The reviewed
// rear +/-16px plates do not authorize inheriting the old native 0.7 parallax ratio.
internal sealed class PalaceScrollScene
{
    private const string ResourcePrefix = "KingdomEnhancedMod.KEM_PalaceScrollR4/";
    private const int MaxResourceBytes = 4 * 1024 * 1024;
    private static PalaceScrollScene _retainedAfterFailedClear;
    private readonly List<GameObject> _children = new List<GameObject>(3);
    private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>(3);
    private readonly List<Sprite> _sprites = new List<Sprite>(3);
    private readonly List<Texture2D> _textures = new List<Texture2D>(3);
    private GameObject _root;
    private Transform _parent;
    private IntPtr _parentPointer;
    private int _sceneHandle;
    private Vector3 _anchor;
    private bool _ready, _active, _cleared, _clearFailed;

    internal Transform GateAnchor => _ready && !_cleared && _root != null ? _root.transform : null;

    internal static bool Create(CourtBinding binding, PalaceScrollCalibration calibration,
        int rearSortOrder, int architectureSortOrder, int bankObjectSortOrder,
        out PalaceScrollScene result, out string reason)
    {
        result = null;
        reason = null;
        if (_retainedAfterFailedClear != null)
        { reason = "previous R4 owner retained after incomplete clear; no new allocation"; return false; }
        var self = new PalaceScrollScene();
        try
        {
            if (binding?.Parent == null || binding.Reference == null || calibration == null)
                throw new InvalidOperationException("R4 binding/calibration unavailable");
            Transform parent = binding.Parent;
            SpriteRenderer reference = binding.Reference;
            if (parent.gameObject.scene.handle != reference.gameObject.scene.handle)
                throw new InvalidOperationException("R4 binding spans different scenes");
            RequireTransformBasis(parent);
            calibration.RequireUnityCanvas();
            float x = UnityFloat(calibration.WorldX(PalaceScrollCalibration.AnchorPixelX));
            float y = UnityFloat(calibration.WorldY(PalaceScrollCalibration.FootlinePixelY));
            float z = reference.transform.position.z;
            if (!Finite(z)) throw new InvalidOperationException("R4 reference plane is not finite");
            RequireSort(rearSortOrder); RequireSort(architectureSortOrder); RequireSort(bankObjectSortOrder);
            if (rearSortOrder >= architectureSortOrder)
                throw new InvalidOperationException("R4 rear must sort behind front architecture");
            PalaceScrollCalibration.RequireReviewedRearEnvelope(0d, 0d, 0d);
            self._parent = parent;
            self._parentPointer = parent.Pointer;
            self._sceneHandle = parent.gameObject.scene.handle;
            self._root = new GameObject("KEM_PalaceScrollR4"); // register before subsequent mutations
            self._root.SetActive(false);
            self._root.layer = binding.GoLayer;
            self._root.transform.SetParent(parent, false);
            // Parent is a translated court root, not an identity matrix. Shared pivot is world-authored.
            self._anchor = new Vector3(x, y, z);
            self._root.transform.position = self._anchor;
            int[] orders = { rearSortOrder, architectureSortOrder, bankObjectSortOrder };
            for (int i = 0; i < Layers.Length; i++)
                self.AddLayer(binding, calibration, Layers[i].File, Layers[i].Width, Layers[i].OriginX,
                    orders[i], Layers[i].Hash);
            self._ready = true;
            result = self;
            return true;
        }
        catch (Exception error)
        {
            reason = "R4 create rejected: " + error.Message;
            if (!self.Clear(out string clearReason))
            {
                _retainedAfterFailedClear = self;
                result = self; // retain the owner as well as the static allocation guard
                reason += "; " + clearReason;
            }
            return false;
        }
    }

    /// <summary>R4 三层字面契约（Create 与 precheck 共用唯一表；hash/宽度/裁剪原点不复制到第二处）。</summary>
    private readonly struct R4Layer
    {
        internal readonly string File;
        internal readonly int Width;
        internal readonly int OriginX;
        internal readonly string Hash;

        internal R4Layer(string file, int width, int originX, string hash)
        {
            File = file;
            Width = width;
            OriginX = originX;
            Hash = hash;
        }
    }

    private static readonly R4Layer[] Layers =
    {
        new R4Layer("layers/rear-amodal-with-bleed.png", 1512, -32,
            "a719369bcc8f9383577fd43677f51b401954a3b211ae4063389444161a4b0810"),
        new R4Layer("layers/front-architecture.png", 1448, 0,
            "bff08dfaaaa8c99b8a3d91e78eafd6e656ac9e26d5f5b37d538888a2133e0777"),
        new R4Layer("layers/near-bank-objects.png", 1448, 0,
            "dfe0a59823676950da661305dd0181f372077b4c9676a6ad26df63f261d47e9e"),
    };

    /// <summary>R4 付前资源契约（只读；与 AddLayer 消费同一表；高度 = 共享画布高 252）。</summary>
    internal static void DescribeResourceContracts(List<PalaceResourceContract> into, out string reason)
    {
        reason = "";
        if (into == null) { reason = "into-null"; return; }
        for (int i = 0; i < Layers.Length; i++)
            into.Add(new PalaceResourceContract(ResourcePrefix + Layers[i].File, Layers[i].Hash, Layers[i].Width,
                PalaceScrollCalibration.CanvasHeight, MaxResourceBytes, "r4-layer:" + i));
    }

    private void AddLayer(CourtBinding binding, PalaceScrollCalibration calibration,
        string file, int width, int originX, int absoluteSortOrder, string expectedHash)
    {
        byte[] bytes = Read(file, expectedHash);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        _textures.Add(texture);
        if (!ImageConversion.LoadImage(texture, bytes, false))
            throw new InvalidDataException("R4 PNG decode failed: " + file);
        if (texture.width != width || texture.height != PalaceScrollCalibration.CanvasHeight)
            throw new InvalidDataException("R4 PNG dimensions differ: " + file);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.anisoLevel = 0;
        PalaceScrollPivot pivot = calibration.SharedPivot(originX, 0, width, texture.height);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, texture.height),
            new Vector2(UnityFloat(pivot.X), UnityFloat(pivot.Y)), UnityFloat(calibration.PixelsPerUnit), 0u, SpriteMeshType.FullRect);
        if (sprite == null) throw new InvalidOperationException("R4 Sprite.Create returned null: " + file);
        _sprites.Add(sprite);
        var child = new GameObject("palace-scroll-" + Path.GetFileNameWithoutExtension(file));
        _children.Add(child); // register even if parenting fails
        child.transform.SetParent(_root.transform, false);
        child.layer = binding.GoLayer;
        SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
        _renderers.Add(renderer);
        renderer.sprite = sprite;
        renderer.sortingLayerID = binding.LayerId;
        renderer.sortingOrder = absoluteSortOrder;
        renderer.color = Color.white;
        if (binding.Material != null) renderer.sharedMaterial = binding.Material;
    }

    internal bool Activate(out string reason)
    {
        reason = null;
        if (_cleared || _clearFailed)
        { reason = "R4 owner is not ready for activation"; return false; }
        try
        {
            // A scene may destroy the root between Create and Activate while owned
            // textures still live. Enter the one rollback path rather than dropping it.
            if (!_ready || _root == null)
                throw new InvalidOperationException("R4 root disappeared before activation");
            if (_parent == null || _parent.Pointer != _parentPointer
                || _parent.gameObject.scene.handle != _sceneHandle || !_parent.gameObject.activeInHierarchy
                || _root.gameObject.scene.handle != _sceneHandle || _root.transform.parent == null
                || _root.transform.parent.Pointer != _parentPointer)
                throw new InvalidOperationException("R4 parent/scene epoch changed before activation");
            Vector3 now = _root.transform.position;
            if (!Finite(now.x) || !Finite(now.y) || !Finite(now.z) || (now - _anchor).sqrMagnitude > .00000001f)
                throw new InvalidOperationException("R4 world anchor changed before activation");
            RequireTransformBasis(_root.transform);
            if (_active)
            {
                if (!_root.activeInHierarchy) throw new InvalidOperationException("activated R4 root unexpectedly became inactive");
                return true;
            }
            _root.SetActive(true);
            if (!_root.activeInHierarchy) throw new InvalidOperationException("R4 root did not become active");
            _active = true;
            return true;
        }
        catch (Exception error)
        {
            reason = "R4 activation rejected: " + error.Message;
            if (!Clear(out string clearReason)) reason += "; " + clearReason;
            return false;
        }
    }

    // Success means these checked Destroy calls returned; Unity destruction is deferred.
    // On an unknown failure retain the lists/owner and prohibit further allocation, without retries.
    internal bool Clear(out string reason)
    {
        reason = null;
        if (_cleared) return true;
        if (_clearFailed) { reason = "R4 cleanup responsibility retained; no blind retry"; return false; }
        _ready = false; _active = false;
        try
        {
            if (_root != null)
            {
                _root.SetActive(false);
                if (_root.activeSelf) throw new InvalidOperationException("R4 root remained active");
            }
            foreach (SpriteRenderer renderer in _renderers)
            {
                if (renderer == null) continue;
                renderer.sprite = null;
                if (renderer.sprite != null) throw new InvalidOperationException("R4 renderer retained its sprite");
            }
            foreach (GameObject child in _children) if (child != null) UnityEngine.Object.Destroy(child);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            foreach (Sprite sprite in _sprites) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            foreach (Texture2D texture in _textures) if (texture != null) UnityEngine.Object.Destroy(texture);
            _root = null; _parent = null;
            _children.Clear(); _renderers.Clear(); _sprites.Clear(); _textures.Clear();
            _cleared = true;
            return true;
        }
        catch (Exception error)
        {
            _clearFailed = true;
            _retainedAfterFailedClear = this;
            reason = "R4 cleanup call failed; owner and assets retained: " + error.Message;
            return false;
        }
    }

    private static byte[] Read(string file, string expectedHash)
    {
        using Stream stream = typeof(PalaceScrollScene).Assembly.GetManifestResourceStream(ResourcePrefix + file);
        if (stream == null || stream.Length <= 0 || stream.Length > MaxResourceBytes)
            throw new InvalidDataException("R4 embedded resource missing or out of bounds: " + file);
        byte[] bytes = new byte[checked((int)stream.Length)];
        int read = 0;
        while (read < bytes.Length)
        {
            int step = stream.Read(bytes, read, bytes.Length - read);
            if (step <= 0) throw new EndOfStreamException("R4 embedded resource truncated: " + file);
            read += step;
        }
        using SHA256 sha = SHA256.Create();
        if (!string.Equals(Convert.ToHexString(sha.ComputeHash(bytes)), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("R4 embedded resource hash differs: " + file);
        return bytes;
    }

    private static float UnityFloat(double value)
    {
        float result = (float)value;
        if (!Finite(result)) throw new ArgumentOutOfRangeException(nameof(value), "R4 value cannot be represented by Unity float");
        return result;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void RequireTransformBasis(Transform transform)
    {
        Vector3 scale = transform.lossyScale;
        float angle = Quaternion.Angle(transform.rotation, Quaternion.identity);
        if (!Finite(scale.x) || !Finite(scale.y) || !Finite(scale.z) || !Finite(angle)
            || Math.Abs(scale.x - 1f) > .0001f || Math.Abs(scale.y - 1f) > .0001f || Math.Abs(scale.z - 1f) > .0001f
            || angle > .0001f)
            throw new InvalidOperationException("R4 transform must have unit world scale and no rotation");
    }
    private static void RequireSort(int value)
    {
        if (value < -32768 || value > 32767) throw new ArgumentOutOfRangeException(nameof(value), "R4 absolute sorting order outside Unity range");
    }
}
