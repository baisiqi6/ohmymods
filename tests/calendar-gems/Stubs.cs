using System;
using UnityEngine;

// 测试独立 stubs：只模拟 candidate 实际使用的现代 2.4 接口形态
// （Player.wallet/hasLocalAuthority/gameObject/transform/Pointer、Wallet.Gems/_playerRef/Pointer、
//  Kingdom.playerOne/playerTwo、Transform.IsChildOf、GameObject.activeInHierarchy、
//  KingdomEnhancedPlugin.Instance.LogSource.LogWarning）。
namespace UnityEngine
{
    public partial class Object
    {
    }

    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
    }

    public partial class Transform : Object
    {
        // 测试模型：Parent 指向所属 scene 根，IsChildOf 仅认同一 scene 根。
        public Transform Parent;

        public bool IsChildOf(Transform parent) => parent != null && Parent == parent;
    }
}

public class Wallet
{
    public static int GemWrites;
    public static int GemReads;

    public int gems;
    public bool ThrowOnGems;
    public Player _playerRef;
    public IntPtr Pointer;

    public int Gems
    {
        get
        {
            if (ThrowOnGems) throw new InvalidOperationException("stub gems getter fault");
            GemReads++;
            return gems;
        }
        set
        {
            GemWrites++;
            gems = value;
        }
    }
}

public class Player
{
    public bool hasLocalAuthority = true;
    public bool TunnelInput;
    public Wallet wallet;
    public GameObject gameObject;
    public Transform transform;
    public IntPtr Pointer;
}

public class Kingdom
{
    private Player _one, _two;
    public bool ThrowOnOne, ThrowOnTwo;

    public Player playerOne
    {
        get
        {
            if (ThrowOnOne) throw new InvalidOperationException("stub kingdom.playerOne fault");
            return _one;
        }
        set => _one = value;
    }

    public Player playerTwo
    {
        get
        {
            if (ThrowOnTwo) throw new InvalidOperationException("stub kingdom.playerTwo fault");
            return _two;
        }
        set => _two = value;
    }
}

namespace BepInEx.Logging
{
    public partial class ManualLogSource
    {
        public static readonly System.Collections.Generic.List<string> Warnings =
            new System.Collections.Generic.List<string>();

        public void LogWarning(string message) => Warnings.Add(message);
    }
}

namespace KingdomEnhancedMod
{
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance =
            new KingdomEnhancedPlugin();

        public BepInEx.Logging.ManualLogSource LogSource =
            new BepInEx.Logging.ManualLogSource();
    }
}
