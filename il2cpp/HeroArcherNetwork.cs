// 英雄弓箭手·联机 slice：本版整体 fail-closed（不注册 RPC 槽、不发包、客户端不自行挑英雄）。
//
// 结论（如实）：15 分钟预算内无法把「每侧至多 1 名英雄 + 与射击/箭数 slice 联动的收据」做到可靠联机，
// 按 operator 契约选择 fail-closed：**在线会话（含主机）一律不启用英雄**，因此不存在主客显示/战斗分歧，
// 也不会因为客户端本地 F5 开关造成主机掉战斗。单机（离线）完整可用。
//
// 为什么不是「主机启用、客户端不启用」：英雄改变射击行为（射速/箭数 slice），主机单边启用会让客机
// 缺少英雄的箭与视觉 → 主客表现分歧（契约明确禁止）。所以 gate 必须在会话层（HasWorldAuth && !IsOnline）。
//
// 待实现协议（下一 slice，双端同版本才可放开；本文件不注册槽位也不发包）：
//   * 槽位：只在既有 CRPCHeader.RegisterComponents 真实 hook 点上，为「含 Archer 的 header」在原生
//     RemoteMethodList 末尾追加 1 个自有槽（同一个 hook 点已有 KnightIdentityNetwork / Hermes 两个
//     追加者；追加顺序取决于 Harmony patch 顺序，必须与对端一致，因此绝不能出现「一边追加一边不追加」）。
//     绝不复用原生槽、绝不短 getter/未审 native 入口、绝不在 RPC 回调里写 ByteBuffer。
//   * 握手（照抄 KnightIdentityNetwork 已验证的 nonce 形态）：
//       请求 kind=1：magic 'HA1' | version(1) | kind(1) | nonce int64 LE | side int8 | lifeLocal int32
//       响应 kind=2：magic 'HA1' | version(1) | kind(2) | echoNonce int64 | side int8 | lifeHost int32 | heroFlag(1)
//     客户端在「绑定 + 本地 life 就绪」时生成随机非零 nonce，只在 header 注册且已追平时按拍重试（有界队列）；
//     **host 决定 hero receipt**（client 不能自己挑）；旧 life/旧 nonce/旧 world 的迟到包一律拒绝并撤销本地表现。
//   * 上限：全局 2（每侧 1）；客户端本地开关只影响它自己是否请求，绝不影响主机战斗。
//   * 撤销：停用/换 world/池复用（life 变化）/模组关闭 → 立即撤销（与 HeroArcherRuntime 同一套生命周期）。

using System;

namespace KingdomEnhancedMod;

internal static class HeroArcherNetwork
{
    /// <summary>协议版本（真正实现时与包内 magic 一起发；双方不同版本不握手）。</summary>
    internal const int ProtocolVersion = 1;

    /// <summary>本 slice 明确未实现联机收据：保持 false，任何在线会话都不放开英雄。</summary>
    internal const bool OnlineProtocolImplemented = false;

    /// <summary>失败说明（日志/报告用；不参与逻辑）。</summary>
    internal const string Status = "fail-closed: online hero sync not implemented (ProtocolVersion 1)";

    /// <summary>
    /// 本机是否允许存在英雄：必须拿到世界权威（离线即真）**且**不在联机会话中。
    /// 线上判定不可用（探测抛错）时按「在线」处理（fail-closed），绝不让未知状态放开英雄。
    /// </summary>
    internal static bool AllowsLocalHero
    {
        get
        {
            try
            {
                if (!NetworkBigBoss.HasWorldAuth) return false;
                if (NetworkBigBoss.IsOnline) return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
