// 英雄弓箭手·红飘带纯逻辑（无 Unity 依赖：本文件同时被 net8 逻辑测试直接编译）。
//
// 契约（operator 2026-09-14，含 reviewer 修订）：
// - 两条链，每条 8 节点（index 0 = 肩部锚点），段长固定 → 总长恒定（跟随约束，不是弹簧）；
// - 30Hz 固定步：delta 先钳到 MaxTickDelta，再按 30Hz 消耗、catch-up 上限 4 步；
//   每个子步用**内部 simulationTime**（首次调用以传入风钟锚定，之后每子步 +1/30，
//   不再重复采样调用方的风钟）→ 60Hz/30Hz/10Hz 采样在共同时刻得到同一姿态；
// - 每步受力：重力（父 local -y）+ 微弱 sin 风（两条链相位不同）+ 角色实际速度带来的
//   向后拖曳（速度越快越展开，有上限，绝不水平锁定）+ 阻尼（Verlet 隐式速度）；
// - 地面：肩部离地 GroundPixels=14px（肩部 local y = -14/32），链长 24/22px 会穿过地面；
//   前向投影时若节点将低于地面，则解 y=floor（含自身半宽离地）且
//   x = previousX + 持久折叠符号 × sqrt(segmentLength² - dy²)（段长严格保持）；
//   Reset 生成「先垂直下垂、到地后沿地折叠到身后」的自然姿态；折叠符号持久，
//   上一段 x≈0 时绝不随机翻转；
// - 静止 → 垂到地面后折在身后（不水平悬浮、不穿地），折叠段保留小幅颤动而不是硬直线；
//   行走 → 略偏后 + 波纹更强；跑动 → 展开在身后 + 波纹最强；
//   停下 → 数秒内平滑回落；与人物动画帧无关（射击不强制水平）；
// - 非法输入（NaN/±Inf/负 dt、NaN 风钟）不改变状态；瞬时速度超 TeleportSpeed（瞬移/池重置）
//   与坐标越界 → 自动回落到静止姿态，无坐标爆炸 / 无 NaN；
// - 零逐帧分配：全部状态在构造时分配的定长数组里原地推进，无扫描、无物理组件、无碰撞。
//
// 与 view 的分工：本文件只产出「链 local、以自身锚点为原点」的点；
// 呈现量化（1/32 格）、锚点偏移与朝向换算在 HeroArcherClothMath，绝不回写模拟状态。

using System;

namespace KingdomEnhancedMod;

/// <summary>一条固定段长的 8 节点飘带链（纯逻辑，30Hz 固定步 + 地面折叠）。</summary>
internal sealed class HeroArcherClothChain
{
    internal const int NodeCount = 8;
    internal const float StepSeconds = 1f / 30f;
    internal const int MaxCatchUpSteps = 4;
    internal const float MaxTickDelta = 0.1f;

    /// <summary>重力（父 local -y，单位 u/s²；1u = 32px）。</summary>
    internal const float Gravity = 9f;

    // ---- 速度展开包络（operator 2026-09-15：跑动逐渐充分扬到身后、停下缓缓落回）----
    /// <summary>包络上升/下降时间常数（秒）：起跑 0.5s 时间常数、停下 ~1s 缓慢释放
    /// → 「静止→跑」不瞬间跳到最终形，「跑→静止」保留 1–3s 惯性再逐渐落回。</summary>
    internal const float FlowAttackSeconds = 0.5f;
    internal const float FlowReleaseSeconds = 0.95f;

    /// <summary>包络上限（u/s）：展开力只看包络 → 天然有界（超过部分不参与，瞬移另由 TeleportSpeed 拦）。</summary>
    internal const float FlowMax = 4f;

    /// <summary>
    /// 展开力（自身气流拖曳）：q = DragLinear·flow + DragQuadratic·flow²（走轻微、跑充分展开），
    /// 再软饱和 drag = MaxDrag·(1 − e^(−q/MaxDrag))：光滑、有界、flow→0 时→0（静止绝无水平推力）。
    /// </summary>
    internal const float DragLinear = 1f;
    internal const float DragQuadratic = 24f;
    internal const float MaxDrag = 19.5f;

    /// <summary>
    /// 气流抬升（把落地的折叠尾巴带离地）：lift = MaxLift·flow²/(flow² + LiftHalfSpeed²)。
    /// 严格小于 Gravity → 静止时恒为 0，绝不永久抵消重力、绝不悬空；跑得越快越接近上限（有界）。
    /// </summary>
    internal const float MaxLift = 4.2f;
    internal const float LiftHalfSpeed = 0.9f;

    /// <summary>展开时风只轻微调形（自身气流主导）：风的水平分量 × 1/(1+WindFade·flow)。</summary>
    internal const float WindFade = 0.6f;

    /// <summary>
    /// 气流对「节点自身运动」的额外阻尼（每 u/s 的保留衰减）：跑动时自身气流更快吸收起跑甩尾，
    /// 让尾巴是「被吹起来」而不是「被甩起来」。flow→0 时严格为 0 → 静止/待机行为不变。
    /// </summary>
    internal const float AirDampingPerFlow = 0.3f;

    /// <summary>每 30Hz 步的隐式速度保留比例（阻尼）。</summary>
    internal const float DampingPerStep = 0.90f;

    /// <summary>贴地判定带（链 local，2px）：节点落在这个厚度里视为与地面接触（不吃风/拖曳水平推力）。</summary>
    internal const float GroundContactBand = 2f / 32f;

    /// <summary>贴地节点的水平隐式速度保留比例（地面摩擦）：贴地的尾巴不会被风推着滑到身前。</summary>
    internal const float GroundFrictionPerStep = 0.72f;

    // ---- 沿线传播的柔和波纹（替代「整条链同相位的弱 sin」）----
    /// <summary>波纹频率（Hz）与相邻节点相位滞后（rad）：同一时刻各节点相位不同 → 带子弯曲/起波，而不是整条平移。</summary>
    internal const float WaveHz = 0.55f;
    internal const float WaveNodeLag = 0.55f;

    /// <summary>静止也保留的基础波纹（量化后仍可见 ~1px 级柔和变化），并随速度平滑加幅。</summary>
    internal const float WaveIdleGain = 1f;
    internal const float WaveSpeedGain = 0.9f;
    internal const float WaveReferenceSpeed = 3.5f;
    /// <summary>波形用「受控的局部弯折角」而不是外力：每个节点把该段的朝向转一点，
    /// 沿线相位滞后 → 行波弯曲；重力/地面/段长约束原样生效（贴地段也不会被推走）。</summary>
    internal const float WaveBendIdleRadians = 0.015f;
    internal const float WaveBendSpeedRadians = 0.05f;
    internal const float WaveGroundedScale = 0.75f;

    /// <summary>贴地折叠段的行波抬升幅度（像素，波峰）：折叠线沿长度 1-2px 起伏，不是硬直线。</summary>
    internal const float WaveLiftPixels = 2f;

    /// <summary>幅度包络每子步的平滑比例：走/跑加幅、停下减幅都不是硬开关。</summary>
    internal const float WaveEnvelopeSmoothPerStep = 0.05f;

    /// <summary>原生世界风（方向/强度，signed -1..1，非物理 m/s）：水平推力 + 平滑（operator 4Hz 采样）。</summary>
    internal const float WindForceX = 1.6f;
    internal const float WindSmoothPerStep = 0.12f;

    /// <summary>超过此 local 速度（u/s）视为瞬移/重置，而不是真实移动。</summary>
    internal const float TeleportSpeed = 40f;

    /// <summary>坐标上限（u）：越界视为数值事故，回落到静止姿态。</summary>
    internal const float MaxCoordinate = 8f;

    private const float StepEpsilon = 1e-5f;
    private const float DegenerateLength = 1e-6f;
    /// <summary>折叠方向只在上一段明显偏斜（|x| ≥ 25% 段长）时才跟随它；近竖直的微小摆动绝不翻转折叠方向。</summary>
    private const float FoldSignMinRatio = 0.25f;

    /// <summary>只有真的在朝后走（local 速度 &lt; -此值）时才允许折叠到身前；否则持久折叠到身后。</summary>
    private const float BackpedalSpeed = 0.2f;

    /// <summary>包络每 30Hz 子步的推进比例（由时间常数换算 → 固定步，与采样率无关）。</summary>
    private static readonly float FlowAttackPerStep = 1f - (float)Math.Exp(-StepSeconds / FlowAttackSeconds);
    private static readonly float FlowReleasePerStep = 1f - (float)Math.Exp(-StepSeconds / FlowReleaseSeconds);

    private readonly float _segmentLength;
    private readonly float _phase;
    private readonly float _dragScale;
    private readonly float _windScale;
    private readonly float[] _x = new float[NodeCount];
    private readonly float[] _y = new float[NodeCount];
    private readonly float[] _previousX = new float[NodeCount];
    private readonly float[] _previousY = new float[NodeCount];
    private readonly float[] _floorY = new float[NodeCount];
    private float _accumulator;
    private float _simulationTime;
    private bool _simulationTimeSeeded;
    private float _foldSign = -1f;
    private bool _backpedaling;
    private float _windTarget;
    private float _windInput;
    /// <summary>有符号速度展开包络（u/s）：加速快、减速慢；停下后残余展开力仍指向刚才的运动反方向。</summary>
    private float _flowX;
    private float _waveEnvelope = WaveIdleGain;

    /// <param name="length">总长（链 local 单位，1u = 32px）。</param>
    /// <param name="phase">风的相位偏移（两条链不同 → 不同步摆动）。</param>
    /// <param name="dragScale">拖曳比例（副链略低）。</param>
    /// <param name="windScale">风比例（副链略高）。</param>
    /// <param name="groundY">地面在链 local 的 y（肩部离地 -14/32 减去本链锚点偏移）。</param>
    internal HeroArcherClothChain(float length, float phase, float dragScale, float windScale, float groundY)
    {
        if (!HeroArcherClothMath.IsFinite(length) || length <= 0f) length = 0.5f;
        _segmentLength = length / (NodeCount - 1);
        _phase = HeroArcherClothMath.IsFinite(phase) ? phase : 0f;
        _dragScale = HeroArcherClothMath.IsFinite(dragScale) ? dragScale : 1f;
        _windScale = HeroArcherClothMath.IsFinite(windScale) ? windScale : 1f;
        if (!HeroArcherClothMath.IsFinite(groundY)) groundY = -0.5f;
        for (int i = 0; i < NodeCount; i++)
        {
            // 地面含该节点自身半宽（1px→0.5px 收窄），保证整条带子不穿地。
            _floorY[i] = i == 0 ? groundY : groundY + HeroArcherClothMath.HalfWidthUnits(i);
        }
        Reset();
    }

    internal float Length => _segmentLength * (NodeCount - 1);

    internal float SegmentLength => _segmentLength;

    /// <summary>节点 X（链 local，节点 0 = 锚点 = 0）。view 只读。</summary>
    internal float[] PointsX => _x;

    /// <summary>节点 Y（链 local，向下为负，节点 0 = 锚点 = 0）。view 只读。</summary>
    internal float[] PointsY => _y;

    /// <summary>地面 Y（链 local，含折叠判定用）。</summary>
    internal float FloorY => _floorY[NodeCount - 1];

    /// <summary>
    /// 回到静止姿态：先垂直下垂、到地后沿地折叠到身后；previous = current（零速度）。
    /// 池复用 / world 重置 / 数值事故共用；同时清除风钟锚点（下次调用重新锚定）。
    /// </summary>
    internal void Reset()
    {
        _accumulator = 0f;
        _simulationTime = 0f;
        _simulationTimeSeeded = false;
        _foldSign = -1f; // 静止自然折向身后（-x = 背向）
        _backpedaling = false;
        _flowX = 0f;
        _windInput = _windTarget;
        _waveEnvelope = WaveIdleGain;
        _x[0] = 0f;
        _y[0] = 0f;
        _previousX[0] = 0f;
        _previousY[0] = 0f;
        for (int i = 1; i < NodeCount; i++)
        {
            float previousX = _x[i - 1];
            float previousY = _y[i - 1];
            float y = previousY - _segmentLength;
            float floor = _floorY[i];
            if (y < floor) FoldToFloor(i, previousX, previousY, floor, out _x[i], out _y[i]);
            else { _x[i] = previousX; _y[i] = y; }
            _previousX[i] = _x[i]; // 零速度
            _previousY[i] = _y[i];
        }
    }

    /// <summary>
    /// 原生世界风输入（视觉方向/强度，不是物理速度）：signedNormalized ∈ [-1,1]，NaN/±Inf 忽略。
    /// 内部再按 WindSmoothPerStep 平滑，避免 4Hz 采样跳变直接砸进物理。
    /// </summary>
    // Change local coordinates after a facing reversal while preserving the already trailing world shape.
    // The anchor remains pinned; subsequent fixed steps pull the cloth toward its new resting direction.
    internal void Rebase(float scaleX, float offsetX)
    {
        if (!HeroArcherClothMath.IsFinite(scaleX) || !HeroArcherClothMath.IsFinite(offsetX) || Math.Abs(scaleX)>4f || Math.Abs(offsetX)>2f) { Reset(); return; }
        for (int i=1;i<NodeCount;i++) { _x[i]=_x[i]*scaleX+offsetX; _previousX[i]=_previousX[i]*scaleX+offsetX; }
        // 节点镜像保留世界位置；展开量按角色朝向驱动，不镜像成转向立停后残留的向前拉力。
        if (scaleX<0) { _foldSign=-_foldSign; _windInput=-_windInput; }
    }

    internal void SetWind(float signedNormalized)
    {
        if (!HeroArcherClothMath.IsFinite(signedNormalized)) return;
        if (signedNormalized > 1f) signedNormalized = 1f;
        else if (signedNormalized < -1f) signedNormalized = -1f;
        _windTarget = signedNormalized;
    }

    /// <summary>
    /// 纯输入推进：dt 有界、30Hz 固定步、catch-up 有限；零/非法 dt 与 NaN 风钟不破坏状态。
    /// 内部 simulationTime 只在首次调用时用传入风钟锚定，之后每子步 +1/30。
    /// </summary>
    /// <param name="dt">自上次 Tick 的秒数（NaN/±Inf/≤0 → 完全不动）。</param>
    /// <param name="velocityLocal">角色实际速度在父 local 的 X 分量（u/s，由 HeroArcherClothMath 换算）。</param>
    /// <param name="windClock">风钟（秒）；只用于首次锚定内部 simulationTime，NaN/±Inf → 锚 0。</param>
    internal void Step(float dt, float velocityLocal, float windClock)
    {
        if (!HeroArcherClothMath.IsFinite(dt) || dt <= 0f) return;
        if (dt > MaxTickDelta) dt = MaxTickDelta;
        if (!_simulationTimeSeeded)
        {
            _simulationTimeSeeded = true;
            _simulationTime = HeroArcherClothMath.IsFinite(windClock) ? windClock : 0f;
        }
        _accumulator += dt;

        int steps = 0;
        while (_accumulator >= StepSeconds - StepEpsilon && steps < MaxCatchUpSteps)
        {
            _simulationTime += StepSeconds; // 每个子步精确 1/30，与调用频率无关
            FixedStep(velocityLocal, _simulationTime);
            _accumulator -= StepSeconds;
            steps++;
        }
    }

    /// <summary>单个 30Hz 步：受力 → 隐式速度积分 → 前向段长投影（含地面折叠）→ 数值守卫。</summary>
    private void FixedStep(float velocityLocal, float windClock)
    {
        // 非法速度：本步当作静止，不带脏状态
        float velocity = HeroArcherClothMath.IsFinite(velocityLocal) ? velocityLocal : 0f;
        float speed = velocity < 0f ? -velocity : velocity;
        if (speed > TeleportSpeed)
        {
            Reset(); // 瞬移/池重置：不是真实移动，回落到静止姿态
            return;
        }
        if (speed > FlowMax) speed = FlowMax; // 展开力只看包络上限内的速度（有界）

        // 速度展开包络：加速快、减速慢。包络有符号 → 停下后残余展开力仍指向「刚才的运动反方向」，
        // 尾部保留惯性拖在身后（不会因为瞬时速度归零而反向推向前方），约 1s 后落回。
        float flowTarget = velocity < 0f ? -speed : speed;
        float flowMagnitude = _flowX < 0f ? -_flowX : _flowX;
        bool expanding = speed > flowMagnitude;
        _flowX += (flowTarget - _flowX) * (expanding ? FlowAttackPerStep : FlowReleasePerStep);
        float flow = _flowX < 0f ? -_flowX : _flowX;

        // 自身气流：拖曳把尾部带到身后（软饱和有界），抬升把落地的折叠尾巴带离地（恒 < Gravity）
        float lift = MaxLift * (flow * flow) / (flow * flow + LiftHalfSpeed * LiftHalfSpeed);
        float unsaturatedDrag = DragLinear * flow + DragQuadratic * flow * flow;
        float drag = MaxDrag * (1f - (float)Math.Exp(-unsaturatedDrag / MaxDrag)) * _dragScale;
        float accelerationX = _flowX > 0f ? -drag : drag; // 向后 = 与最近的运动方向相反
        float accelerationY = -Gravity + lift;
        _backpedaling = velocity <= -BackpedalSpeed;      // 只有朝后走才允许尾巴落在身前

        // 沿线传播的波纹：频率固定、每子步推进 1/30（与渲染频率无关）；幅度包络随展开量平滑变化
        float speedRatio = flow / WaveReferenceSpeed;
        if (speedRatio > 1f) speedRatio = 1f;
        _waveEnvelope += (WaveIdleGain + WaveSpeedGain * speedRatio - _waveEnvelope) * WaveEnvelopeSmoothPerStep;
        _windInput += (_windTarget - _windInput) * WindSmoothPerStep;
        bool waveTimed = HeroArcherClothMath.IsFinite(windClock);
        double wavePhase = waveTimed ? windClock * (2.0 * Math.PI * WaveHz) + _phase : 0.0;
        // 展开后自身气流主导：风的水平分量被 1/(1+WindFade·flow) 衰减 → 跑动时只轻微调形，吹不到身前
        float windForce = WindForceX * _windScale * _windInput / (1f + WindFade * flow);
        float waveBend = (_waveEnvelope * WaveBendIdleRadians + speedRatio * WaveBendSpeedRadians) * _windScale;

        float deltaSquared = StepSeconds * StepSeconds;
        // 悬空节点：气流速度越大，自身运动越被空气吸收（起跑甩尾更快收敛；flow=0 → 与原阻尼完全一致）
        float airborneRetention = DampingPerStep / (1f + AirDampingPerFlow * flow);
        for (int i = 1; i < NodeCount; i++)
        {
            float x = _x[i];
            float y = _y[i];
            // 贴地接触：不再接受风/拖曳的水平推力，横向加摩擦（否则整条落地的带子会被风推着翻到身前）
            bool grounded = y <= _floorY[i] + GroundContactBand;
            // 贴地段：不吃风/拖曳的水平推力（避免被推着滑到身前）；行波由下面的局部弯折角实现
            float drivenX = accelerationX + windForce;
            float nodeX = grounded ? 0f : drivenX;
            float nodeY = accelerationY;
            float implicitX = (x - _previousX[i]) * (grounded ? GroundFrictionPerStep : airborneRetention);
            float implicitY = (y - _previousY[i]) * airborneRetention;
            _previousX[i] = x;
            _previousY[i] = y;
            _x[i] = x + implicitX + nodeX * deltaSquared;
            _y[i] = y + implicitY + nodeY * deltaSquared;
        }

        _x[0] = 0f;
        _y[0] = 0f;
        for (int i = 1; i < NodeCount; i++)
        {
            float previousX = _x[i - 1];
            float previousY = _y[i - 1];
            float dx = _x[i] - previousX;
            float dy = _y[i] - previousY;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (!(length > DegenerateLength))
            {
                dx = 0f;
                dy = -1f;
                length = 1f;
            }
            float scale = _segmentLength / length;
            float directionX = dx * scale;
            float directionY = dy * scale;
            // 行波：第 i 个节点按自己的相位说话（沿线滞后 → 起波/弯曲而不是整条平移）。
            bool nodeGrounded = _y[i] <= _floorY[i] + GroundContactBand;
            float nodePhase = waveTimed ? (float)(wavePhase + i * WaveNodeLag) : 0f;
            float floor = _floorY[i];
            if (waveTimed)
            {
                if (nodeGrounded)
                {
                    // 贴地段：不动折叠方向，只把本节点的地面线抬高一点 → 落地折痕沿线 1-2px 起伏（绝不穿地）
                    floor += WaveLiftPixels / HeroArcherClothMath.PixelsPerUnit * _waveEnvelope * _windScale
                        * 0.5f * (1f + (float)Math.Sin(nodePhase));
                }
                else
                {
                    // 悬空段：把该段朝向转一个很小的角（旋转保长）；贴地段的弯折更小
                    float bend = waveBend * WaveGroundedScale * (float)Math.Sin(nodePhase);
                    if (bend != 0f)
                    {
                        float bendSin = (float)Math.Sin(bend);
                        float bendCos = (float)Math.Cos(bend);
                        float rotatedX = directionX * bendCos - directionY * bendSin;
                        directionY = directionX * bendSin + directionY * bendCos;
                        directionX = rotatedX;
                    }
                }
            }
            float x = previousX + directionX;
            float y = previousY + directionY;
            if (y < floor)
            {
                // 会穿地：钉在地面线上，横向用持久折叠符号补足段长（长度严格不变）
                FoldToFloor(i, previousX, previousY, floor, out x, out y);
            }
            _x[i] = x;
            _y[i] = y;
        }

        Guard();
    }

    /// <summary>
    /// 地面折叠：y = floor；x = previousX + foldSign × sqrt(segmentLength² - dy²)。
    /// foldSign 跟随上一段的 x 方向；上一段 x≈0（上一段竖直、或 i=1 无上一段）时保持上一次的符号，
    /// 绝不随机翻转。
    /// </summary>
    private void FoldToFloor(int index, float previousX, float previousY, float floor, out float x, out float y)
    {
        // 折叠方向只在上一段还悬空时跟随它；一旦落在地面上，折叠方向就定住（不随机翻转、也不被风翻到身前）
        if (index >= 2 && _y[index - 1] > _floorY[index - 1] + GroundContactBand)
        {
            float previousSegmentX = _x[index - 1] - _x[index - 2];
            float threshold = _segmentLength * FoldSignMinRatio;
            if (previousSegmentX > threshold && _backpedaling) _foldSign = 1f;
            else if (previousSegmentX < -threshold) _foldSign = -1f; // 否则保持上一次符号（近竖直段不翻转）
        }
        float dy = floor - previousY;
        float square = _segmentLength * _segmentLength - dy * dy;
        if (!(square > 0f)) square = 0f;
        x = previousX + _foldSign * (float)Math.Sqrt(square);
        y = floor;
    }

    /// <summary>数值守卫：任何非有限值/越界坐标/穿地 → 回落到静止姿态。</summary>
    private void Guard()
    {
        for (int i = 0; i < NodeCount; i++)
        {
            float x = _x[i];
            float y = _y[i];
            if (!HeroArcherClothMath.IsFinite(x) || !HeroArcherClothMath.IsFinite(y)
                || x > MaxCoordinate || x < -MaxCoordinate
                || y > MaxCoordinate || y < -MaxCoordinate)
            {
                Reset();
                return;
            }
            if (y < _floorY[i] - 1e-3f)
            {
                Reset();
                return;
            }
        }
    }
}

/// <summary>纯换算/呈现辅助（无 Unity 依赖，测试直接覆盖）。</summary>
internal static class HeroArcherClothMath
{
    internal const float PixelsPerUnit = 32f;

    /// <summary>主体最大半宽 2px；实际地面为整数像素，Round(y-halfWidth) 不会落到地面以下。</summary>
    internal const float RootHalfWidthPixels = 2f;

    /// <summary>尾部最大半宽 1.5px，对应最大 3px 的尾部布面。</summary>
    internal const float TipHalfWidthPixels = 1.5f;

    /// <summary>肩部离地高度（像素）——地面 = 肩部 local y = -14/32。</summary>
    internal const float ShoulderHeightPixels = 14f;

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>节点 i 的最大呈现半宽（链 local 单位）；为动态布面预留地面间隙。</summary>
    internal static float HalfWidthUnits(int nodeIndex)
    {
        if (nodeIndex < 0) nodeIndex = 0;
        int last = HeroArcherClothChain.NodeCount - 1;
        if (nodeIndex > last) nodeIndex = last;
        float pixels = nodeIndex < 5 ? RootHalfWidthPixels : TipHalfWidthPixels;
        return pixels / PixelsPerUnit;
    }

    /// <summary>
    /// 呈现量化：对齐 1/32 格。只用于写渲染顶点，绝不回写模拟状态（避免抖动）。
    /// 非有限值 → 0。
    /// </summary>
    internal static float Quantize(float units)
    {
        if (!IsFinite(units)) return 0f;
        return (float)Math.Floor(units * PixelsPerUnit + 0.5f) / PixelsPerUnit;
    }

    /// <summary>
    /// 世界 X 速度 → 父 local 的 X 速度：除以父链真实 scale.x（同时完成朝向符号翻转：
    /// scale.x &lt; 0 = 朝左）——每帧只算一次。缺少这一步会让飘带在朝左时拖到面前（方向反转）。
    /// 非有限输入 → 0；scale.x 退化（|x| &lt; 1e-4）→ 只按朝向符号翻转。
    /// </summary>
    internal static float LocalVelocityXFromWorld(float worldVelocityX, float parentScaleX)
    {
        if (!IsFinite(worldVelocityX) || !IsFinite(parentScaleX)) return 0f;
        if (parentScaleX > -1e-4f && parentScaleX < 1e-4f)
            return parentScaleX < 0f ? -worldVelocityX : worldVelocityX;
        return worldVelocityX / parentScaleX;
    }
}
