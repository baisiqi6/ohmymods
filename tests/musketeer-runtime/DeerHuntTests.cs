using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// 鹿猎手 slice 的行为回归（全部走生产路径，不做源码字符串断言）：
    /// * wildlife 扫描器组合判据 = **原生先验 AND 白天普通鹿**（任一侧异常 fail-closed，
    ///   先验绝不允许把目标放宽到兔子等小动物）；
    /// * 发射最终门复核昼夜/编队/骑士/乘船 + 鹿根自有 Damageable（兔子/坐骑/石化物/无效鹿拒绝），
    ///   敌怪门完全不受鹿状态影响；
    /// * 弹道侧小动物/坐骑/友军完全透明（不吸弹不吃伤害），鹿按原生 ReceiveDamage 吃 2 点箭伤，
    ///   没有任何手工掉钱/写 HP 的路径；夜间即使鹿被其他 mod 挪到 Enemies 层也绝不死。
    /// </summary>
    internal static class DeerHuntTests
    {
        internal static void Run()
        {
            Case.Run("wildlife condition accepts only a daytime normal deer", ConditionAcceptsOnlyDeer);
            Case.Run("wildlife condition ANDs the native prior; a throwing prior fails closed", ConditionAndsPrior);
            Case.Run("invalid deer (dead/petrified/non-arrow/invulnerable/disabled) are never huntable", InvalidDeerRejected);
            Case.Run("the deer launch gate re-checks day, formation, knight and embark", LaunchGateRechecks);
            Case.Run("formation members emit no deer bullet while enemy fire stays native", FormationNeverHunts);
            Case.Run("a daytime deer takes damage 2 through the native ReceiveDamage path", DeerTakesNativeDamage);
            Case.Run("rabbits/mount/friendlies are transparent: the deer behind them is still hit", SmallAnimalsTransparent);
            Case.Run("a deer moved to the Enemies layer never dies at night (transparent deer)", ForeignLayerDeer);
            Case.Run("a deer below the flat line is aimed at its real collider centre (straight line)", DeerAimBelowLine);
            Case.Run("flat-line deer stay flat; missing body evidence blocks the deer shot", DeerAimKeepsFlat);
            Case.Run("close-range and mirrored deer aim; never a backwards shot", CloseAndFlippedDeerAim);
            Case.Run("a tilted step is clipped at the real ground before the sweep", TiltedStepClipsAtGround);
            Case.Run("tilted segment hit.distance boundary is the clipped/step length", TiltedSegmentHitDistance);
            Case.Run("an in-flight deer hit re-checks source/identity/formation/night; enemies do not", DeerHitRechecksSourceState);
            Case.Run("a dead/grabbed/disabled shooter never lands a deer hit (enemy branch unaffected)", LivenessGates);
            Case.Run("a re-armed same GO gets a fresh lease: the old life's bullet cannot kill deer", NewLifeLease);
            Case.Run("the deer hit resolves through the complete-list path too", DeerHitThroughCompleteList);
            Case.Run("a released tilted record is re-planned flat for the next shooter", ReleaseClearsTiltedDirection);
            Case.Run("the third-party wildlife predicate is restored exactly after the strip", RestoresThirdPartyPrior);
            Case.Run("night flips the installed filter live (the cache is never trusted)", NightFlipsLive);
        }

        // ---- fixtures ------------------------------------------------------------

        private static Archer Arm()
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            Fixture.SetDaytime(true);
            Archer archer = Fixture.NewArcher();
            MusketeerIdentity.Units.Add(archer);
            MusketeerRuntime.Tick();
            return archer;
        }

        private static Archer ArmWithPrior(Scanner.ObjectCondition prior)
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            Fixture.SetDaytime(true);
            Archer archer = Fixture.NewArcher();
            archer._wildlifeScanner.additionalRequirements = prior;
            MusketeerIdentity.Units.Add(archer);
            MusketeerRuntime.Tick();
            return archer;
        }

        private static Scanner.ObjectCondition Filter(Archer archer)
            => archer._wildlifeScanner.additionalRequirements;

        private static int DamageCount(GameObject target) => target.GetComponent<Damageable>().DamageLog.Count;

        // ---- scanner condition ---------------------------------------------------

        private static void ConditionAcceptsOnlyDeer()
        {
            Archer archer = Arm();
            Scanner.ObjectCondition filter = Filter(archer);
            Check.True(filter != null, "the wildlife filter is installed");

            GameObject deer = Fixture.NewDeer();
            Check.True(filter.Invoke(deer), "a daytime deer is a hunt target");

            // 其他 mod 加的子 collider：候选是子物体，也必须经 Deer 根组件确认（绝不认名字/tag/层）。
            var hitbox = new GameObject("deer-hitbox");
            hitbox.transform.SetParent(deer.transform, false);
            hitbox.AddComponent<Collider2D>();
            Check.True(filter.Invoke(hitbox), "a sub-collider resolves through the Deer root component");

            Check.False(filter.Invoke(Fixture.NewCritter()), "a rabbit is never a hunt target");
            Check.False(filter.Invoke(Fixture.NewHind()), "the Hind mount is never a hunt target");
            Check.False(filter.Invoke(Fixture.NewEnemy()), "enemies are never wildlife targets");
            Check.False(filter.Invoke(new GameObject("unrelated")), "an unrelated object is never a hunt target");

            // 带 Deer 组件的坐骑（最坏情况）：Steed/Hind 组件存在就不是普通鹿。
            GameObject steedDeer = Fixture.NewDeer("steed-deer");
            steedDeer.AddComponent<Steed>();
            Check.False(filter.Invoke(steedDeer), "a Deer that is also a Steed is a mount, not a hunt target");
            GameObject hindDeer = Fixture.NewDeer("hind-deer");
            hindDeer.AddComponent<Hind>();
            Check.False(filter.Invoke(hindDeer), "a Deer that is also a Hind mount is never hunted");

            var bare = new GameObject("bare-deer");     // 只有装饰 collider 的"鹿"：没有鹿根自己的 Damageable
            bare.layer = Fixture.WildlifeLayer;
            bare.tag = "Wildlife";
            bare.AddComponent<Deer>();
            Check.False(filter.Invoke(bare), "a deer without its own Damageable is rejected");

            var root = new GameObject("deer-root");     // Damageable 挂在子物体上：不算鹿自己的
            root.layer = Fixture.WildlifeLayer;
            root.tag = "Wildlife";
            root.AddComponent<Deer>();
            var child = new GameObject("other-hitbox");
            child.transform.SetParent(root.transform, false);
            child.AddComponent<Damageable>();
            Check.False(filter.Invoke(child), "a Damageable on another object is not the deer's own Damageable");
        }

        private static void ConditionAndsPrior()
        {
            GameObject vetoed = null;
            Archer archer = ArmWithPrior(new Scanner.ObjectCondition(candidate => candidate != vetoed));
            vetoed = Fixture.NewDeer("vetoed");
            GameObject allowed = Fixture.NewDeer("allowed");
            Scanner.ObjectCondition filter = Filter(archer);

            Check.True(filter.Invoke(allowed), "the prior allows this deer → the deer condition decides");
            Check.False(filter.Invoke(vetoed), "the native prior still vetoes its deer (AND, never OR)");
            Check.False(filter.Invoke(Fixture.NewCritter()), "a permissive prior cannot widen the set to small animals");

            // 先验抛异常：必须 fail-closed，且异常绝不泄进原生扫描器调用链。
            Archer throwing = ArmWithPrior(new Scanner.ObjectCondition(
                _ => throw new InvalidOperationException("third-party prior failed")));
            Scanner.ObjectCondition throwingFilter = Filter(throwing);
            GameObject deer = Fixture.NewDeer("deer-throwing-prior");
            bool escaped = false;
            bool result = true;
            try { result = throwingFilter.Invoke(deer); }
            catch (Exception) { escaped = true; }
            Check.False(escaped, "a throwing prior never escapes into the native scanner call chain");
            Check.False(result, "fail-closed: a throwing prior yields no candidate");
        }

        private static void InvalidDeerRejected()
        {
            Archer archer = Arm();
            Scanner.ObjectCondition filter = Filter(archer);

            GameObject dead = Fixture.NewDeer("dead-deer");
            dead.GetComponent<Damageable>().isDead = true;
            Check.False(filter.Invoke(dead), "a dead deer is not huntable");

            GameObject petrified = Fixture.NewDeer("petrified-deer");
            petrified.GetComponent<Petrifiable>().IsPetrified = true;
            Check.False(filter.Invoke(petrified), "a petrified deer is never hunted (petrified things stay untouched)");

            GameObject nonArrow = Fixture.NewDeer("non-arrow-deer");
            nonArrow.GetComponent<Damageable>().acceptsArrow = false;
            Check.False(filter.Invoke(nonArrow), "a deer that does not take arrow damage is rejected");

            GameObject invulnerable = Fixture.NewDeer("invulnerable-deer");
            invulnerable.GetComponent<Damageable>().invulnerable = true;
            invulnerable.GetComponent<Damageable>().ignoredWhenInvulnerable = true;
            Check.False(filter.Invoke(invulnerable), "invulnerable+ignored deer is rejected");

            GameObject disabled = Fixture.NewDeer("disabled-deer");
            disabled.GetComponent<Damageable>().enabled = false;
            Check.False(filter.Invoke(disabled), "a disabled Damageable is rejected");

            GameObject inactive = Fixture.NewDeer("inactive-deer");
            inactive.SetActive(false);
            Check.False(filter.Invoke(inactive), "an inactive deer is rejected");
        }

        private static void NightFlipsLive()
        {
            Archer archer = Arm();
            GameObject deer = Fixture.NewDeer();
            Scanner.ObjectCondition filter = Filter(archer);

            Check.True(filter.Invoke(deer), "daytime deer passes");
            Fixture.SetDaytime(false);
            Check.False(filter.Invoke(deer), "the same installed filter reads night live (no stale cache trust)");
            Fixture.SetDaytime(true);
            Check.True(filter.Invoke(deer), "daytime flips it back without a reinstall");
        }

        // ---- launch gate / firing -------------------------------------------------

        private static void LaunchGateRechecks()
        {
            Archer archer = Arm();
            GameObject deer = Fixture.NewDeer();
            Check.True(MusketeerFoeFilter.IsDeerShotAllowed(archer, deer), "day + free archer: deer shot allowed");
            Check.True(MusketeerFoeFilter.IsValidShotTarget(archer, deer), "the launch gate accepts the daytime deer");
            Check.True(MusketeerFoeFilter.IsValidShotTarget(archer, Fixture.NewEnemy()),
                "the launch gate keeps enemy fire unchanged");

            archer._currentFormation = new Formation();
            Check.False(MusketeerFoeFilter.IsDeerShotAllowed(archer, deer), "formation members never hunt deer");
            archer._currentFormation = null;

            archer._knight = new Knight();
            Check.False(MusketeerFoeFilter.IsDeerShotAllowed(archer, deer), "knight followers never hunt deer");
            archer._knight = null;

            archer._embarkee = new Embarkee { IsEmbarked = true };
            Check.False(MusketeerFoeFilter.IsDeerShotAllowed(archer, deer), "embarked archers never hunt deer");
            archer._embarkee = null;

            Check.True(MusketeerFoeFilter.IsDeerShotAllowed(archer, deer), "clearing the native exclusions reopens the gate");

            Fixture.SetDaytime(false);
            Check.False(MusketeerFoeFilter.IsDeerShotAllowed(archer, deer), "night never allows a deer shot");
            Check.False(MusketeerFoeFilter.IsValidShotTarget(archer, deer), "the launch gate rejects the night deer");
            Check.True(MusketeerFoeFilter.IsValidShotTarget(archer, Fixture.NewEnemy()), "enemy fire stays night-legal");
            Fixture.SetDaytime(true);
            Check.True(MusketeerFoeFilter.IsDeerShotAllowed(archer, deer), "day reopens it (no reinstall)");

            // 敌人门与鹿状态无关：编队中也照常有效（原生战斗/夜战不受影响）。
            archer._currentFormation = new Formation();
            Check.True(MusketeerFoeFilter.IsValidGroundFoe(Fixture.NewEnemy(), archer),
                "the enemy gate is independent of formation/deer state");
        }

        private static void FormationNeverHunts()
        {
            Archer archer = Arm();
            GameObject deer = Fixture.NewDeer();
            Fixture.AddFlatBody(deer);
            archer._shootingTarget = deer;

            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject),
                "an armed musketeer always suppresses the native arrow");
            Check.Equal(1, MusketeerCombat.LiveCount, "a daytime deer shot emits one bullet");

            Time.time = 5f;
            archer._currentFormation = new Formation();
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "still suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "no deer bullet while in formation");

            // 敌人不受编队门影响：同样条件下换敌人目标照常发弹。
            archer._shootingTarget = Fixture.NewEnemy(EnemyType.TrollWeak);
            Time.time = 10f;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(2, MusketeerCombat.LiveCount, "enemy fire still works in formation");
        }

        private static void DeerTakesNativeDamage()
        {
            Archer archer = Arm();
            GameObject deer = Fixture.NewDeer();
            Fixture.AddFlatBody(deer);                 // 平射可命中的鹿身 collider（缺它不发鹿枪）
            Damageable damageable = deer.GetComponent<Damageable>();
            int nativeCalls = 0;
            int nativeDamage = 0;
            DamageSource nativeKind = default;
            damageable.OnReceiveDamage = (multiplier, damager, source) =>
            {
                // 原生 Deer.HandleOnReceiveDamage/HandleOnDeath 的替身订阅：只有它们负责掉落/死亡。
                nativeCalls++;
                nativeDamage = multiplier;
                nativeKind = source;
            };
            archer._shootingTarget = deer;

            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "bullet in flight");
            Fixture.QueueHit(deer, 0.6f);
            MusketeerCombat.Tick(0.05f, true);

            Check.Equal(1, nativeCalls, "exactly one native ReceiveDamage application (never a manual kill)");
            Check.Equal(2, nativeDamage, "damage stays 2");
            Check.Equal(DamageSource.Arrow, nativeKind, "the native arrow damage source is used");
            Check.Equal("2:Arrow", damageable.DamageLog[0], "the native damage entry matches (no manual coin path)");
            Check.False(damageable.isDead, "no HP/death field is written by the musketeer code");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed on the deer");
        }

        private static void SmallAnimalsTransparent()
        {
            Archer archer = Arm();
            GameObject deer = Fixture.NewDeer();
            Fixture.AddFlatBody(deer);                 // 平射可命中的鹿身 collider
            GameObject rabbit = Fixture.NewCritter();
            GameObject mount = Fixture.NewHind();
            var friend = new GameObject("peasant");
            friend.layer = Fixture.CitizensLayer;
            friend.AddComponent<Character>();
            friend.AddComponent<Damageable>();
            archer._shootingTarget = deer;

            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Fixture.QueueHit(friend, 0.2f);     // 前排友军：跳过、不阻挡
            Fixture.QueueHit(rabbit, 0.3f);     // 最近的兔子：完全透明、不吸弹
            Fixture.QueueHit(mount, 0.45f);     // 坐骑同样透明
            Fixture.QueueHit(deer, 0.6f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(friend), "a friendly unit takes no damage");
            Check.Equal(0, DamageCount(rabbit), "the rabbit takes no damage");
            Check.Equal(0, DamageCount(mount), "the mount takes no damage");
            Check.Equal(1, DamageCount(deer), "the deer behind the small animals is still hit");
            Check.Equal(0, MusketeerCombat.LiveCount, "the bullet is consumed on the deer");

            // 反向顺序：鹿最近时同样只有鹿吃弹，兔子在后面不受影响。
            Time.time = 5f;
            Physics2D.QueuedHits.Clear();
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Fixture.QueueHit(deer, 0.3f);
            Fixture.QueueHit(rabbit, 0.6f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(2, DamageCount(deer), "the nearest deer is hit once more");
            Check.Equal(0, DamageCount(rabbit), "the rabbit behind stays untouched");

            // 夜里同一只鹿对子弹透明：最终门不发弹（绝不夜猎）。
            Fixture.SetDaytime(false);
            Time.time = 10f;
            Physics2D.QueuedHits.Clear();
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(0, MusketeerCombat.LiveCount, "a night deer target emits no bullet (final gate)");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(2, DamageCount(deer), "no night damage to the deer");
            Check.Equal(0, DamageCount(rabbit), "small animals stay untouched at night");
        }

        private static void ForeignLayerDeer()
        {
            Archer archer = Arm();
            GameObject stray = Fixture.NewDeer("stray-deer");
            stray.layer = Fixture.EnemiesLayer;    // 其他 mod 把鹿挪到 Enemies 层
            Fixture.AddFlatBody(stray);            // 白天发射需要鹿根自己的 body collider
            GameObject enemy = Fixture.NewEnemy(EnemyType.TrollWeak);

            // 1) 夜间发射门：鹿候选绝不走敌人门 → 不发弹（不夜猎、也不浪费一颗弹）。
            Fixture.SetDaytime(false);
            archer._shootingTarget = stray;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(0, MusketeerCombat.LiveCount, "a night deer never produces a bullet, even on the Enemies layer");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(stray), "never a night deer kill");
            Check.Equal(0, DamageCount(enemy), "and the deer never leaks a shot through as an enemy");

            // 2) 白天发射、入夜后仍在半空：命中门复核 → 鹿透明、敌怪照打。
            Fixture.SetDaytime(true);
            Time.time = 5f;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "a daytime deer shot is in flight");
            Fixture.SetDaytime(false);                 // 飞行途中入夜：缓存判定必须复核
            Fixture.QueueHit(stray, 0.4f);
            Fixture.QueueHit(enemy, 0.9f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(stray), "the in-flight bullet re-checks night: no deer damage");
            Check.Equal(1, DamageCount(enemy), "the transparent deer does not absorb the in-flight shot");

            // 3) 白天：同一只鹿按鹿处理 → 原生伤害（2 点箭伤）。
            Fixture.SetDaytime(true);
            Time.time = 10f;
            Physics2D.QueuedHits.Clear();
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Fixture.QueueHit(stray, 0.4f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, DamageCount(stray), "daytime: the stray deer is hit as a deer");
            Check.Equal("2:Arrow", stray.GetComponent<Damageable>().DamageLog[0], "damage 2 from DamageSource.Arrow");
        }

        private static void DeerAimBelowLine()
        {
            Archer archer = Arm();
            // 测试世界的"地面"降到枪口线以下（真实世界这由 GroundCollider 顶面给出；绝不硬编码）。
            Fixture.SetGroundTop(0.3f);

            GameObject deer = Fixture.NewDeer("low-deer");
            Collider2D body = Fixture.AddLowBody(deer);   // 鹿根自己的 body：完全低于枪口水平线
            archer._shootingTarget = deer;

            Check.True(MusketeerCombat.TryComputeMuzzle(archer, out Vector2 origin, out float facing), "muzzle computes");
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerBullet bullet = MusketeerCombat.LiveRecordForTests(0);
            Check.True(bullet != null, "bullet in flight");
            Check.True(bullet.Direction.y < 0f, "the shot aims down towards the real body centre");
            Check.True(bullet.Direction.x > 0f, "it still travels towards the target side");
            Check.Near(1d, Math.Sqrt(bullet.Direction.x * bullet.Direction.x + bullet.Direction.y * bullet.Direction.y),
                1e-4d, "the direction stays a unit vector");
            Check.Near(12d, bullet.MaxDistance, 1e-4d, "the tilt never extends the range budget");

            // 直线瞄准（无 homing/曲线）：方向斜率必须等于"枪口 → 碰撞体中心"的斜率。
            float expectedX = body.bounds.center.x - origin.x;
            float expectedY = body.bounds.center.y - origin.y;
            Check.Near(expectedY / expectedX, bullet.Direction.y / bullet.Direction.x, 1e-4d,
                "the tilt is the straight line to the collider centre");

            // 推进一帧：位置沿同一单位方向直线下降（不是抛物线，也不加射程）。
            Vector2 before = bullet.Position;
            MusketeerCombat.Tick(0.05f, true);
            Check.True(bullet.Alive && MusketeerCombat.LiveCount == 1, "the bullet is still in flight after one clean sweep");
            Check.Near(before.x + bullet.Direction.x * 1.5d, bullet.Position.x, 1e-4d, "horizontal step follows the direction");
            Check.Near(before.y + bullet.Direction.y * 1.5d, bullet.Position.y, 1e-4d, "vertical step follows the direction");
            Check.True(bullet.Position.y < before.y, "the bullet descends along the straight line");

            // 命中：鹿照常吃原生 2 点箭伤（掉钱路径不变）。
            Fixture.QueueHit(body, 0.5f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, DamageCount(deer), "the deer still takes the native 2-damage arrow hit");
            Check.Equal("2:Arrow", deer.GetComponent<Damageable>().DamageLog[0], "damage 2 from DamageSource.Arrow");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed on the deer");
        }

        private static void DeerAimKeepsFlat()
        {
            Archer archer = Arm();
            GameObject crossing = Fixture.NewDeer("crossing-deer");
            Fixture.AddFlatBody(crossing);                // 枪口水平线穿过真实碰撞体 → 照旧平射
            archer._shootingTarget = crossing;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerBullet flat = MusketeerCombat.LiveRecordForTests(0);
            Check.True(flat != null, "bullet in flight");
            Check.Near(0d, flat.Direction.y, 1e-6d, "a deer the flat line crosses is shot flat");
            Check.Near(1d, flat.Direction.x, 1e-6d, "the flat shot keeps the facing direction");

            // 无 body 证据（鹿根没有自己的 collider）：不开鹿枪（fail-closed），也不发"猜"的平弹。
            Time.time = 5f;
            archer._shootingTarget = Fixture.NewDeer("no-collider-deer");
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "no body evidence → the deer shot is skipped");

            // 子物体上的 collider（例如物理脚圈）不算鹿根自己的 body：同样不开枪。
            Time.time = 10f;
            GameObject childBody = Fixture.NewDeer("child-body-deer");
            var foot = new GameObject("deer-foot-circle");
            foot.transform.SetParent(childBody.transform, false);
            var footCollider = foot.AddComponent<Collider2D>();
            footCollider.bounds = new Bounds
            {
                center = new Vector3(3f, 0.60f, 0f),
                extents = new Vector3(0.2f, 0.2f, 0f),
            };
            archer._shootingTarget = childBody;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "a child collider is not the deer's own body evidence");

            // 鹿根自己的 collider 被禁用 → 不开枪。
            Time.time = 15f;
            GameObject disabledBody = Fixture.NewDeer("disabled-body-deer");
            Collider2D disabled = Fixture.AddLowBody(disabledBody);
            disabled.enabled = false;
            archer._shootingTarget = disabledBody;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "a disabled body collider blocks the deer shot");

            // 空 bounds（没有真实几何）→ 不开枪。
            Time.time = 20f;
            GameObject emptyBody = Fixture.NewDeer("empty-body-deer");
            emptyBody.AddComponent<Collider2D>();         // 默认 bounds：center 0 / extents 0
            archer._shootingTarget = emptyBody;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "empty bounds are no aim evidence");

            // 带 Steed/Hind 的鹿根（坐骑）：不开枪。
            Time.time = 25f;
            GameObject mountDeer = Fixture.NewDeer("mount-deer");
            Fixture.AddLowBody(mountDeer);
            mountDeer.AddComponent<Hind>();
            archer._shootingTarget = mountDeer;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "a Deer carrying Hind/Steed is a mount, never a shot target");

            // 敌方子弹永远原水平（即使它的碰撞体低于枪口线）。
            Time.time = 30f;
            GameObject enemy = Fixture.NewEnemy(EnemyType.TrollWeak);
            var enemyBody = enemy.AddComponent<Collider2D>();
            enemyBody.bounds = new Bounds
            {
                center = new Vector3(3f, 0.60f, 0f),
                extents = new Vector3(0.45f, 0.18f, 0f),
            };
            archer._shootingTarget = enemy;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerBullet foe = MusketeerCombat.LiveRecordForTests(1);
            Check.True(foe != null, "the enemy bullet follows the crossing-deer bullet");
            Check.Near(0d, foe.Direction.y, 1e-6d, "enemy bullets stay horizontal (no deer tilt)");
            Check.Near(1d, foe.Direction.x, 1e-6d, "the enemy bullet keeps the facing direction");
        }

        private static void CloseAndFlippedDeerAim()
        {
            Archer archer = Arm();
            Check.True(MusketeerCombat.TryComputeMuzzle(archer, out Vector2 rightOrigin, out float rightFacing),
                "right-facing muzzle computes");

            // 左向（原生 renderer.flipX）：几何镜像，方向必须 (-x, -y) 且斜率仍指向 body 中心。
            archer._spriteRenderer.flipX = true;
            Check.True(MusketeerCombat.TryComputeMuzzle(archer, out Vector2 leftOrigin, out float leftFacing),
                "left-facing muzzle computes");
            Check.Near(-1d, leftFacing, 1e-6d, "the flip mirrors the facing");
            GameObject left = Fixture.NewDeer("left-deer");
            left.transform.position = new Vector3(-3f, 0.5f, 0f);
            Collider2D leftBody = Fixture.AddLowBody(left, -3f);
            archer._shootingTarget = left;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerBullet mirrored = MusketeerCombat.LiveRecordForTests(0);
            Check.True(mirrored != null, "left-facing deer bullet in flight");
            Check.True(mirrored.Direction.x < 0f, "the bullet travels left");
            Check.True(mirrored.Direction.y < 0f, "and still aims down at the body centre");
            Check.Near((leftBody.bounds.center.y - leftOrigin.y) / (leftBody.bounds.center.x - leftOrigin.x),
                mirrored.Direction.y / mirrored.Direction.x, 1e-4d, "the mirrored tilt is the same straight line");

            // Δx 近 0（鹿几乎贴脸）：不做近垂直/反向射击，等原生转身后的下一次决策。
            archer._spriteRenderer.flipX = false;
            Time.time = 5f;
            GameObject tooClose = Fixture.NewDeer("too-close-deer");
            Collider2D closeBody = tooClose.AddComponent<Collider2D>();
            closeBody.bounds = new Bounds
            {
                center = new Vector3(rightOrigin.x + 0.01f, 0.60f, 0f),
                extents = new Vector3(0.30f, 0.18f, 0f),
            };
            archer._shootingTarget = tooClose;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "a near-zero Δx deer is rejected, never shot near-vertically");

            // 目标在枪口背后（面向右而鹿在左）：绝不背射。
            Time.time = 10f;
            GameObject behind = Fixture.NewDeer("behind-deer");
            Fixture.AddLowBody(behind, -3f);
            archer._shootingTarget = behind;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "a deer behind the muzzle is never shot backwards");
        }

        private static void TiltedStepClipsAtGround()
        {
            Archer archer = Arm();
            Fixture.SetGroundTop(0.70f);                 // 真实地面顶面（斜向弹必须先裁到它）
            GameObject deer = Fixture.NewDeer("low-deer");
            Collider2D body = Fixture.AddLowBody(deer);  // 倾斜弹道：约 -0.116 斜率
            archer._shootingTarget = deer;

            // 1) 超大 dt（0.5s）：命中排在地面交点之后（distance 5 > 裁剪段长≈1.67）→ 绝不被采信，子弹落地终止。
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "tilted bullet in flight");
            Fixture.QueueHit(body, 5.0f);
            MusketeerCombat.Tick(0.5f, true);
            Check.Equal(0, DamageCount(deer), "a hit past the ground intersection is never taken (no through-ground hit)");
            Check.Equal(0, MusketeerCombat.LiveCount, "the bullet terminates at the real ground intersection");

            // 2) 同一斜线、地面交点之前有真实地上敌怪：大 dt 也先命中它（不穿地、不越界）。
            Time.time = 5f;
            Physics2D.QueuedHits.Clear();
            GameObject enemy = Fixture.NewEnemy(EnemyType.TrollWeak);
            Fixture.QueueHit(enemy, 1.0f);               // 1.0 < 1.67：裁剪段内的合法命中
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.5f, true);
            Check.Equal(1, enemy.GetComponent<Damageable>().DamageLog.Count, "an above-ground enemy inside the clipped segment is hit");
            Check.Equal(0, DamageCount(deer), "the deer behind the ground line stays untouched");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed on the enemy");

            // 3) 地上 Crusher（!IsStunned）先消费子弹：绝不穿透到后面的鹿。
            Time.time = 10f;
            Physics2D.QueuedHits.Clear();
            var crusherGo = new GameObject("crusher");
            crusherGo.layer = Fixture.EnemiesLayer;
            crusherGo.AddComponent<Damageable>();
            var crusher = crusherGo.AddComponent<Crusher>();
            crusher.Type = EnemyType.Crusher;
            crusher.IsStunned = false;
            Fixture.QueueHit(crusherGo, 0.8f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.5f, true);
            Check.Equal(0, crusherGo.GetComponent<Damageable>().DamageLog.Count, "the !IsStunned crusher consumes without damage");
            Check.Equal(0, DamageCount(deer), "no pierce to the deer behind the crusher");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed on the crusher");

            // 4) 裁剪段内的合法鹿命中照常结算（地面之前）。
            Time.time = 15f;
            Physics2D.QueuedHits.Clear();
            Fixture.QueueHit(body, 1.0f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.5f, true);
            Check.Equal(1, DamageCount(deer), "a deer inside the above-ground segment is still hit");
        }

        private static void TiltedSegmentHitDistance()
        {
            Archer archer = Arm();
            Fixture.SetGroundTop(0.60f);                 // 单帧 1.5 的步段尚未触地：只检验斜线段距离语义
            GameObject deer = Fixture.NewDeer("low-deer");
            Collider2D body = Fixture.AddLowBody(deer);
            archer._shootingTarget = deer;

            // 恰好落在步段端点（distance == 段长 1.5，欧氏）→ 有效。
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Fixture.QueueHit(body, 1.5f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, DamageCount(deer), "a hit at exactly the tilted segment end is taken");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed at the segment end");

            // 越界 0.01 → 绝不采信，子弹继续飞。
            Time.time = 5f;
            Physics2D.QueuedHits.Clear();
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Fixture.QueueHit(body, 1.51f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, DamageCount(deer), "an out-of-segment tilted hit never damages");
            Check.Equal(1, MusketeerCombat.LiveCount, "the bullet keeps flying (no false consumption)");
        }

        private static void DeerHitRechecksSourceState()
        {
            // 1) 发射后身份/战斗包失效：鹿命中作废（透明），同一颗弹的敌怪命中照常。
            Archer archer = Arm();
            GameObject deer = Fixture.NewDeer("deer-identity");
            Fixture.AddFlatBody(deer);
            archer._shootingTarget = deer;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "deer bullet in flight");
            MusketeerIdentity.Units.Clear();
            // Reproduce same-frame identity release before the runtime has reconciled.
            Check.True(MusketeerRuntime.IsArmedMusketeer(archer), "the stale package is still applied");
            Check.False(Filter(archer).Invoke(deer), "identity release immediately closes the scanner condition");
            GameObject enemy = Fixture.NewEnemy(EnemyType.TrollWeak);
            Fixture.QueueHit(deer, 0.3f);
            Fixture.QueueHit(enemy, 0.6f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(deer), "a deer hit after the shooter lost its package is transparent");
            Check.Equal(1, enemy.GetComponent<Damageable>().DamageLog.Count, "the enemy branch ignores identity/deer state");
            MusketeerRuntime.Tick();
            Check.False(MusketeerRuntime.IsArmedMusketeer(archer), "the next runtime pass releases the package");

            // 2) 发射后加入编队 + 骑士随从 + 乘船 + 入夜：鹿命中全部作废；敌怪命中不受任何一项影响。
            Archer second = Arm();
            GameObject deer2 = Fixture.NewDeer("deer-state");
            Fixture.AddFlatBody(deer2);
            second._shootingTarget = deer2;
            Check.True(MusketeerCombat.TryHandleShot(second.ActiveArrowAttack, second.gameObject), "suppressed");
            second._currentFormation = new Formation();
            second._knight = new Knight();
            second._embarkee = new Embarkee { IsEmbarked = true };
            Fixture.SetDaytime(false);
            GameObject enemy2 = Fixture.NewEnemy(EnemyType.TrollWeak);
            Fixture.QueueHit(deer2, 0.3f);
            Fixture.QueueHit(enemy2, 0.6f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(deer2), "formation/knight/embark/night invalidate the in-flight deer hit");
            Check.Equal(1, enemy2.GetComponent<Damageable>().DamageLog.Count,
                "enemy hits ignore day/formation/knight (night defence keeps working)");

            // 3) 射手被停用（失活）：鹿命中同样作废，敌怪命中照常。
            Archer third = Arm();
            GameObject deer3 = Fixture.NewDeer("deer-inactive");
            Fixture.AddFlatBody(deer3);
            third._shootingTarget = deer3;
            Check.True(MusketeerCombat.TryHandleShot(third.ActiveArrowAttack, third.gameObject), "suppressed");
            third.gameObject.SetActive(false);
            GameObject enemy3 = Fixture.NewEnemy(EnemyType.TrollWeak);
            Fixture.QueueHit(deer3, 0.3f);
            Fixture.QueueHit(enemy3, 0.6f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(deer3), "a deactivated shooter never lands a deer hit");
            Check.Equal(1, enemy3.GetComponent<Damageable>().DamageLog.Count, "the enemy branch still lands with a deactivated shooter");

            // 4) 发射后换世界：旧世界的鹿弹被丢弃，绝不跨世界命中。
            Archer fourth = Arm();
            GameObject deer4 = Fixture.NewDeer("deer-world");
            Fixture.AddFlatBody(deer4);
            fourth._shootingTarget = deer4;
            Check.True(MusketeerCombat.TryHandleShot(fourth.ActiveArrowAttack, fourth.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "deer bullet in the old world");
            Transform oldLayer = MusketeerAccess.WorldValue;
            UnityEngine.Object.Destroy(oldLayer.gameObject);
            Fixture.NewWorld();
            Time.time = 5f;
            MusketeerRuntime.Tick();
            Check.Equal(0, MusketeerCombat.LiveCount, "no deer bullet survives the world change");
            Fixture.QueueHit(deer4, 0.3f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(deer4), "the old-world deer takes no cross-world damage");
        }

        private static void LivenessGates()
        {
            // 1) Damageable 死亡（GO 仍 active、包仍 Applied）：鹿命中作废；敌怪命中照常。
            Archer archer = Arm();
            GameObject deer = Fixture.NewDeer("deer-dead-source");
            Fixture.AddFlatBody(deer);
            archer._shootingTarget = deer;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            archer._damageable.isDead = true;
            Check.True(MusketeerRuntime.IsArmedMusketeer(archer), "the package is still applied after death");
            Check.False(MusketeerFoeFilter.IsUsableSource(archer), "a dead shooter is not a usable source");
            Check.False(Filter(archer).Invoke(deer), "the scanner condition also rejects the dead shooter's deer");
            GameObject enemy = Fixture.NewEnemy(EnemyType.TrollWeak);
            Fixture.QueueHit(deer, 0.3f);
            Fixture.QueueHit(enemy, 0.6f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(deer), "a dead shooter (GO still active) never lands a deer hit");
            Check.Equal(1, enemy.GetComponent<Damageable>().DamageLog.Count, "the enemy branch ignores shooter liveness");

            // 2) Character 被抓：鹿命中作废。
            Archer grabbed = Arm();
            GameObject deer2 = Fixture.NewDeer("deer-grabbed-source");
            Fixture.AddFlatBody(deer2);
            grabbed._shootingTarget = deer2;
            Check.True(MusketeerCombat.TryHandleShot(grabbed.ActiveArrowAttack, grabbed.gameObject), "suppressed");
            grabbed._character.grabbed = true;
            Fixture.QueueHit(deer2, 0.3f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(deer2), "a grabbed shooter never lands a deer hit");

            // 3) Damageable 被禁用（Character inert）：鹿命中作废。
            Archer inert = Arm();
            GameObject deer3 = Fixture.NewDeer("deer-inert-source");
            Fixture.AddFlatBody(deer3);
            inert._shootingTarget = deer3;
            Check.True(MusketeerCombat.TryHandleShot(inert.ActiveArrowAttack, inert.gameObject), "suppressed");
            inert._character.inert = true;
            inert._damageable.enabled = false;
            Fixture.QueueHit(deer3, 0.3f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(deer3), "an inert/disabled shooter never lands a deer hit");
        }

        private static void NewLifeLease()
        {
            Archer archer = Arm();
            GameObject deer = Fixture.NewDeer("deer-lease");
            Fixture.AddFlatBody(deer);
            archer._shootingTarget = deer;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "old-life deer bullet in flight");
            long oldLease = MusketeerRuntime.BindingLease(archer);
            Check.True(oldLease != 0L, "an armed life exposes a binding lease");
            Check.True(MusketeerRuntime.MatchesBindingLease(archer, oldLease), "the live bullet's lease matches now");

            // 池回收 → 同一 GO/Pointer 作为**新 life** 重新武装（strip + re-arm）：lease 必须变化。
            MusketeerIdentity.Units.Clear();
            MusketeerRuntime.Tick();
            Check.False(MusketeerRuntime.MatchesBindingLease(archer, oldLease), "a stripped unit never matches its old lease");
            MusketeerIdentity.Units.Add(archer);
            MusketeerRuntime.Tick();
            long newLease = MusketeerRuntime.BindingLease(archer);
            Check.True(MusketeerRuntime.IsArmedMusketeer(archer), "the same GO/Pointer is re-armed as a new life");
            Check.True(newLease != 0L && newLease != oldLease, "the re-armed life got a fresh monotonic lease");
            Check.False(MusketeerRuntime.MatchesBindingLease(archer, oldLease), "the previous life's lease is never revived");
            Check.True(MusketeerRuntime.MatchesBindingLease(archer, newLease), "the new life's lease matches");

            GameObject enemy = Fixture.NewEnemy(EnemyType.TrollWeak);
            Fixture.QueueHit(deer, 0.3f);
            Fixture.QueueHit(enemy, 0.6f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, DamageCount(deer), "a bullet from the previous life never damages the deer");
            Check.Equal(1, enemy.GetComponent<Damageable>().DamageLog.Count,
                "the enemy branch keeps its old semantics across life changes");
        }

        private static void DeerHitThroughCompleteList()
        {
            Archer archer = Arm();
            GameObject deer = Fixture.NewDeer("list-deer");
            Collider2D body = Fixture.AddFlatBody(deer);
            archer._shootingTarget = deer;
            Physics2D.Saturate = true;                   // 数组饱和 → 强制完整 List 路径
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Fixture.QueueHit(body, 0.5f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, Physics2D.ListCastCount, "the complete-list overload is authoritative at saturation");
            Check.Equal((1 << Fixture.EnemiesLayer) | (1 << Fixture.WildlifeLayer), Physics2D.LastListLayerMask,
                "the list path queries enemies + wildlife");
            Check.Equal(1, DamageCount(deer), "the deer hit resolves through the complete-list path too");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed on the deer");
        }

        private static void ReleaseClearsTiltedDirection()
        {
            Archer archer = Arm();
            Fixture.SetGroundTop(0.3f);
            GameObject deer = Fixture.NewDeer("low-deer");
            Collider2D body = Fixture.AddLowBody(deer);
            archer._shootingTarget = deer;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerBullet tilted = MusketeerCombat.LiveRecordForTests(0);
            Check.True(tilted != null && tilted.Direction.y < 0f, "a tilted record is in flight");

            Fixture.QueueHit(body, 0.5f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, DamageCount(deer), "the tilted deer hit landed");
            Check.Equal(0, MusketeerCombat.LiveCount, "the record returned to the pool");

            // 回收后的记录被下一发（敌人）复用时：方向/射程/来源全部是新值，绝不残留旧倾角或旧身份。
            Time.time = 5f;
            archer._shootingTarget = Fixture.NewEnemy(EnemyType.TrollWeak);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerBullet reused = MusketeerCombat.LiveRecordForTests(0);
            Check.True(ReferenceEquals(tilted, reused), "the record instance is reused from the pool");
            Check.Near(1d, reused.Direction.x, 1e-6d, "the reused record carries the new facing");
            Check.Near(0d, reused.Direction.y, 1e-6d, "no stale tilt survives the release");
            Check.Near(12d, reused.MaxDistance, 1e-4d, "the range budget is re-planned per shot");
            Check.True(ReferenceEquals(reused.ShooterRoot, archer.gameObject), "the shooter is the current one");
        }

        private static void RestoresThirdPartyPrior()
        {
            Scanner.ObjectCondition prior = new Scanner.ObjectCondition(_ => true);
            Archer archer = ArmWithPrior(prior);
            Scanner.ObjectCondition composed = Filter(archer);
            Check.True(composed != null && composed.Pointer != prior.Pointer, "the composed filter is ours");
            Check.True(composed.Invoke(Fixture.NewDeer()), "the composed filter still hunts deer");
            Check.True(prior.Invoke(Fixture.NewCritter()), "the prior alone would have allowed small animals (AND proof)");

            MusketeerIdentity.Units.Clear();
            MusketeerRuntime.Tick();
            Scanner.ObjectCondition after = Filter(archer);
            Check.True(after != null && after.Pointer == prior.Pointer,
                "the third-party predicate is restored exactly (never dropped by the strip)");
        }
    }
}
