// 动态测试用统一视图：把「本次修改后的实现」与「repo 旧版实现」放在同一套只读坐标接口后面，
// 让验收断言与 A/B 对比跑同一份合成输入序列，只用可观察坐标/输入评估（不镜像实现细节）。
using System;
using NewChain = KingdomEnhancedMod.HeroArcherClothChain;

namespace KingdomEnhancedMod.Dynamics;

internal interface IChain
{
    int NodeCount { get; }
    float SegmentLength { get; }
    float FloorY { get; }
    float[] PointsX { get; }
    float[] PointsY { get; }
    void Step(float dt, float velocityLocal, float windClock);
    void SetWind(float signedNormalized);
    void Reset();
    void Rebase(float scaleX, float offsetX);
}

internal sealed class NewChainView : IChain
{
    private readonly NewChain _chain;
    internal NewChainView(NewChain chain) => _chain = chain;

    public int NodeCount => NewChain.NodeCount;
    public float SegmentLength => _chain.SegmentLength;
    public float FloorY => _chain.FloorY;
    public float[] PointsX => _chain.PointsX;
    public float[] PointsY => _chain.PointsY;
    public void Step(float dt, float velocityLocal, float windClock) => _chain.Step(dt, velocityLocal, windClock);
    public void SetWind(float signedNormalized) => _chain.SetWind(signedNormalized);
    public void Reset() => _chain.Reset();
    public void Rebase(float scaleX, float offsetX) => _chain.Rebase(scaleX, offsetX);
}

