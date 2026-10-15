using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

// caller-proven 原生 adapter 的 host 替身：
// - 每次 Read/写都以 Access() 创建时的身份快照 Bound 做 token 同一性证明（模拟 adapter 契约）；
// - 统计读写交错（控制器每次写前/写后都必须读）；
// - 单次触发 hook 注入认领突变、外来覆写、写前/写后抛异常、move 写入丢失。
// 本替身不能证明任何 native/earlyAI 行为。
internal sealed class FakeStockItem
{
    internal string PaidId;
    internal long World = 3, Life = 11, Pointer = 0x100;
    internal int Instance = 1, Slot;
    internal object Owner = new object();
    internal bool Valid = true, PickedUp, FriendlyClaimed, EnemyClaimed, StockOwned = true;
    internal bool VisualParked;
    internal int FriendlyPolicy;   // 原生 pickUpPolicy 当前值（0=Anybody … 5=Blocked）
    internal int EnemyPolicy;      // 原生 CurrentEnemyPolicy 当前值（独立字段，可为任意值）
    internal float X, Y, Z;

    // 单次触发 hook（触发即移除）
    internal readonly List<string> FailBeforeWrite = new();          // 写入前抛异常（不写入）
    internal readonly List<string> FailAfterWrite = new();           // 写入生效后抛异常
    internal readonly List<string> ForeignBeforeWrite = new();       // 写前外来者覆写该字段
    internal readonly List<string> ForeignAfterWrite = new();        // 写后（验证读之前）外来者覆写该字段
    // 按写入次数定位的单次 hook：仅在该字段的第 N 次写入尝试时触发（field, attempt）
    internal readonly List<(string Field, int Attempt)> FailBeforeWriteAt = new();
    internal readonly Dictionary<string, int> WriteAttempts = new();

    internal readonly List<string> FriendlyClaimBeforeWrite = new(); // 写前出现友方认领
    internal readonly List<string> FriendlyClaimAfterWrite = new();  // 写后出现友方认领
    internal bool LoseMoveWrite;                                      // move 写入丢失（位置不变）

    internal int Reads, Writes;
    internal int ReadsSinceLastWrite;
    internal bool ViolatedReadBeforeWrite;
    internal readonly List<string> Log = new();
    internal ShopStockToken Bound;

    internal FakeStockItem(string paidId = "gun-1", int slot = 0, long pointer = 0x100)
    {
        PaidId = paidId;
        Slot = slot;
        Pointer = pointer;
    }

    internal ShopStockToken Token => new(PaidId, World, Life, Pointer, Instance, Slot, Owner);

    // 写前每次都有读，且最后一次写之后仍有读（协议：写前/写后都读）
    internal bool ProtocolOk => !ViolatedReadBeforeWrite && (Writes == 0 || ReadsSinceLastWrite > 0);

    internal ShopStockObservation Snapshot() => new()
    {
        Valid = Valid,
        PickedUp = PickedUp,
        FriendlyClaimed = FriendlyClaimed,
        EnemyClaimed = EnemyClaimed,
        StockOwned = StockOwned,
        VisualParked = VisualParked,
        FriendlyPolicy = FriendlyPolicy,
        EnemyPolicy = EnemyPolicy,
        X = X,
        Y = Y,
        Z = Z,
    };

    internal ShopStockAccess Access()
    {
        var t = Token;
        Bound = t;
        return new ShopStockAccess
        {
            Token = t,
            Read = req => { RequireSame(req); OnRead(); return Snapshot(); },
            SetFriendlyPolicy = (req, v) => { RequireSame(req); Write("friendly", () => FriendlyPolicy = v, v); },
            SetEnemyPolicy = (req, v) => { RequireSame(req); Write("enemy", () => EnemyPolicy = v, v); },
            SetVisualParked = (req, v) => { RequireSame(req); Write("visual", () => VisualParked = v, v); },
            Move = (req, x, y, z) => { RequireSame(req); Write("move", () => { X = x; Y = y; Z = z; }, null); },
        };
    }

    private void RequireSame(ShopStockToken t)
    {
        if (!t.Equals(Bound))
            throw new InvalidOperationException("fake adapter: token identity mismatch");
    }

    private void OnRead()
    {
        Reads++;
        ReadsSinceLastWrite++;
        Log.Add("read");
    }

    private void Write(string field, Action apply, object value)
    {
        if (ReadsSinceLastWrite == 0) ViolatedReadBeforeWrite = true;
        WriteAttempts.TryGetValue(field, out var prior);
        int attempt = prior + 1;
        WriteAttempts[field] = attempt;
        if (FailBeforeWriteAt.Remove((field, attempt)))
        {
            Log.Add("throw-before:" + field + "#" + attempt);
            ReadsSinceLastWrite = 0;
            throw new InvalidOperationException("fake pre-write failure on " + field + " attempt " + attempt);
        }
        if (FriendlyClaimBeforeWrite.Remove(field)) FriendlyClaimed = true;
        if (ForeignBeforeWrite.Remove(field))
        {
            if (field == "friendly") FriendlyPolicy = 0;
            else if (field == "enemy") EnemyPolicy = 3;
            else if (field == "visual") VisualParked = false;
        }
        if (FailBeforeWrite.Remove(field))
        {
            Log.Add("throw-before:" + field);
            ReadsSinceLastWrite = 0;
            throw new InvalidOperationException("fake pre-write failure on " + field);
        }
        if (field == "move" && LoseMoveWrite)
        {
            Log.Add("lost:move");
            ReadsSinceLastWrite = 0;
            return;
        }
        apply();
        Log.Add("write:" + field + (value != null ? "=" + value : ""));
        Writes++;
        ReadsSinceLastWrite = 0;
        if (FriendlyClaimAfterWrite.Remove(field)) FriendlyClaimed = true;
        if (ForeignAfterWrite.Remove(field))
        {
            if (field == "friendly") FriendlyPolicy = 2;
            else if (field == "enemy") EnemyPolicy = 3;
            else if (field == "visual") VisualParked = false;
        }
        if (FailAfterWrite.Remove(field))
            throw new InvalidOperationException("fake post-write failure on " + field);
    }
}
