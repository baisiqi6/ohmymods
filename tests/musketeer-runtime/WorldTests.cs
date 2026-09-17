using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// 世界生命周期（评审 world-fix）：换岛/换世界时自有弹丸池与在场子弹的收尾、
    /// 旧层销毁或仅失活两条路径、失活世界绝不出膛、同世界暂停/开关不重置、
    /// 复用前校验 + 重挂当前层 + 同一次调用不丢射击、Destroy 失败保留退休回执、
    /// 物理缓冲容量只增不减的饱和回归。
    /// </summary>
    internal static class WorldTests
    {
        internal static void Run()
        {
            Case.Run("world change (previous layer destroyed) discards pool and the first new shot works", DestroyedPreviousLayer);
            Case.Run("world change (previous layer merely inactive) resets and the first new shot works", InactivePreviousLayer);
            Case.Run("inactive current world never fires and never consumes the gate", InactiveCurrentWorld);
            Case.Run("no old live bullet damages the new world", OldBulletNeverHitsNewWorld);
            Case.Run("a damage callback that switches world aborts the rest of the frame and drops old bullets", CallbackBoundaryAbortsOldWorldBullets);
            Case.Run("a damage callback that pauses (or loses authority) stops the rest of the frame", CallbackBoundaryHonoursPause);
            Case.Run("a damage callback that disables the feature stops the rest of the frame", CallbackBoundaryHonoursDisable);
            Case.Run("pause inside the same world keeps pool and live bullets", PauseKeepsPool);
            Case.Run("feature toggle inside the same world keeps the pool reusable", ToggleKeepsPool);
            Case.Run("stale pool entries are skipped and the same call still rents a working visual", StalePoolEntriesDoNotEatShots);
            Case.Run("pooled visual with a foreign parent is reparented to the current layer", ReuseReparentsToCurrentLayer);
            Case.Run("destroy failure keeps a retirement receipt and retries later", RetireReceiptRetried);
            Case.Run("physics capacity is retained (saturation falls back to the complete list, then the array is reused)", CapacityRetainedAfterSaturation);
        }

        private static void DestroyedPreviousLayer()
        {
            Archer archer = Armed(out GameObject target);
            Fire(archer);
            LandBullet(target);
            Check.Equal(1, MusketeerCombat.PooledVisualCount, "a visual is pooled after the bullet lands");
            GameObject pooled = MusketeerCombat.PooledVisualForTests(0);

            Transform oldLayer = MusketeerAccess.WorldValue;
            UnityEngine.Object.Destroy(oldLayer.gameObject);          // 旧岛卸载：层被销毁
            Fixture.NewWorld();                                       // 新岛
            Time.time = 5f;
            MusketeerRuntime.Tick();

            Check.Equal(0, MusketeerCombat.LiveCount, "no live bullet survives the world change");
            Check.Equal(0, MusketeerCombat.PooledVisualCount, "the stale pool is discarded");
            Check.True(UnityEngine.Object.Destroyed.Contains(pooled), "the stale pooled visual was destroyed");
            Check.Equal(0, MusketeerCombat.RetiredVisualCount, "and needed no retirement receipt");

            GameObject newTarget = Fixture.NewEnemy();
            archer._shootingTarget = newTarget;
            Fire(archer);
            Check.Equal(1, MusketeerCombat.LiveCount, "the first shot in the new world is not lost");
            Check.True(MusketeerCombat.VisualForTests(0) != null, "the new bullet has a visual");
            Check.True(MusketeerCombat.VisualForTests(0).transform.parent == MusketeerAccess.WorldValue,
                "the new visual is parented to the current layer");
        }

        private static void InactivePreviousLayer()
        {
            Archer archer = Armed(out GameObject target);
            Fire(archer);
            LandBullet(target);
            Check.Equal(1, MusketeerCombat.PooledVisualCount, "pooled before the island switch");

            Transform oldLayer = MusketeerAccess.WorldValue;
            oldLayer.gameObject.SetActive(false);                     // 旧岛只是失活（未销毁）
            Fixture.NewWorld();                                       // 新岛
            Time.time = 5f;
            MusketeerRuntime.Tick();
            Check.Equal(0, MusketeerCombat.PooledVisualCount, "the pool of the inactive layer is discarded");

            GameObject newTarget = Fixture.NewEnemy();
            archer._shootingTarget = newTarget;
            Fire(archer);
            Check.Equal(1, MusketeerCombat.LiveCount, "first shot in the new world works");
            Check.True(MusketeerCombat.VisualForTests(0).transform.parent == MusketeerAccess.WorldValue,
                "the visual lives under the active current layer");
            Check.True(MusketeerCombat.VisualForTests(0).transform.parent.gameObject.activeInHierarchy,
                "its parent is active (no invisible damaging bullet)");
        }

        private static void InactiveCurrentWorld()
        {
            Archer archer = Armed(out GameObject target);
            MusketeerAccess.WorldValue.gameObject.SetActive(false);   // 当前世界失活（过渡中）
            Time.time = 3f;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject),
                "the native arrow is still suppressed");
            Check.Equal(0, MusketeerCombat.LiveCount, "no bullet is constructed for an unusable world");
            Check.True(MusketeerRuntime.GateOpen(archer), "the shot gate is not consumed by a failed shot");
            Check.Equal(0, MusketeerCombat.BoundWorldGoId, "the unusable world is not bound");
        }

        private static void OldBulletNeverHitsNewWorld()
        {
            Archer archer = Armed(out GameObject target);
            Fire(archer);
            Check.Equal(1, MusketeerCombat.LiveCount, "bullet in flight in the old world");
            Fixture.QueueHit(target, 0.5f);                           // 本段就能命中

            Fixture.NewWorld();                                       // 换世界
            Time.time = 5f;
            MusketeerRuntime.Tick();

            Check.Equal(0, MusketeerCombat.LiveCount, "the old bullet is discarded on the world change");
            Check.Equal(0, target.GetComponent<Damageable>().DamageLog.Count,
                "the old bullet never damages anything after the world change");
        }

        private static void CallbackBoundaryAbortsOldWorldBullets()
        {
            // 评审门：伤害回调里换 world → 本帧剩余弹（旧 world）绝不继续结算，并立即收尾旧弹/旧池。
            Archer archer = Armed(out GameObject target);
            Fire(archer);                                            // B（index0，本帧稍后才轮到）
            Time.time = 5f;
            Fire(archer);                                            // A（index1，先命中并触发回调）
            Check.Equal(2, MusketeerCombat.LiveCount, "B and A in flight");

            Damageable damageable = target.GetComponent<Damageable>();
            damageable.OnReceiveDamage = (multiplier, damager, source) => Fixture.NewWorld();
            Fixture.QueueHit(target, 0.5f);                          // B 若继续推进也会命中 → 必须被中止
            MusketeerCombat.Tick(0.05f, true);

            Check.Equal(1, damageable.DamageLog.Count, "only A landed before the world change");
            Check.Equal(0, MusketeerCombat.LiveCount,
                "the old world's remaining bullets are dropped at the callback boundary");
            Check.Equal(0, MusketeerCombat.PooledVisualCount, "and the old world's pooled visuals were released with it");
        }

        private static void CallbackBoundaryHonoursPause()
        {
            // 评审门：伤害回调里暂停/失权 → 本帧剩余弹不推进（暂停原语义：弹保留）。
            Archer archer = Armed(out GameObject target);
            Fire(archer);
            Time.time = 5f;
            Fire(archer);

            Damageable damageable = target.GetComponent<Damageable>();
            damageable.OnReceiveDamage = (multiplier, damager, source) => MusketeerAccess.PlayingValue = false;
            Fixture.QueueHit(target, 0.5f);
            MusketeerCombat.Tick(0.05f, true);

            Check.Equal(1, damageable.DamageLog.Count, "only A landed before the pause");
            Check.Equal(1, MusketeerCombat.LiveCount, "paused frames keep live bullets");
            MusketeerBullet b = MusketeerCombat.LiveRecordForTests(0);
            Check.True(b != null && ReferenceEquals(b.ShooterRoot, archer.gameObject), "B stays (pause semantics)");
            Check.Near(0d, b.Travelled, 1e-4d, "B is not advanced after the callback paused / lost authority");
        }

        private static void CallbackBoundaryHonoursDisable()
        {
            // 评审门：伤害回调里关闭功能 → 本帧剩余弹不推进、不结算（清理由 runtime reconcile 负责）。
            Archer archer = Armed(out GameObject target);
            Fire(archer);
            Time.time = 5f;
            Fire(archer);

            Damageable damageable = target.GetComponent<Damageable>();
            damageable.OnReceiveDamage = (multiplier, damager, source) => MusketeerAccess.EnabledValue = false;
            Fixture.QueueHit(target, 0.5f);
            MusketeerCombat.Tick(0.05f, true);

            Check.Equal(1, damageable.DamageLog.Count, "only A landed before the toggle");
            Check.Equal(1, MusketeerCombat.LiveCount, "the frame aborts; the next runtime reconcile clears bullets");
            MusketeerBullet b = MusketeerCombat.LiveRecordForTests(0);
            Check.Near(0d, b.Travelled, 1e-4d, "B is not advanced after the callback disabled the feature");
        }

        private static void PauseKeepsPool()
        {
            Archer archer = Armed(out GameObject target);
            Fire(archer);
            LandBullet(target);
            Time.time = 5f;
            Fire(archer);
            Check.Equal(1, MusketeerCombat.LiveCount, "second bullet in flight");
            int pooled = MusketeerCombat.PooledVisualCount; // snapshot after rental, immediately before pause

            Time.timeScale = 0f;                                     // 暂停（同世界）
            MusketeerCombat.Tick(0f, false);
            Check.Equal(1, MusketeerCombat.LiveCount, "paused bullets stay");
            Check.Equal(pooled, MusketeerCombat.PooledVisualCount, "paused pool is untouched");
            Check.Equal(0, MusketeerCombat.RetiredVisualCount, "no retirement while paused");

            // 暂停时 dt 再大也不推进（playing=false 门在完整 dt 之前）。
            Vector3 before = MusketeerCombat.VisualForTests(0).transform.position;
            MusketeerCombat.Tick(0.25f, false);
            Vector3 after = MusketeerCombat.VisualForTests(0).transform.position;
            Check.Near(before.x, after.x, 1e-6d, "a paused tick never advances a live bullet");
        }

        private static void ToggleKeepsPool()
        {
            Archer archer = Armed(out GameObject target);
            Fire(archer);
            LandBullet(target);
            Check.Equal(1, MusketeerCombat.PooledVisualCount, "pooled after landing");
            GameObject entry = MusketeerCombat.PooledVisualForTests(0);

            MusketeerAccess.EnabledValue = false;                     // 关闭（同世界）
            MusketeerRuntime.Tick();
            Check.Equal(1, MusketeerCombat.PooledVisualCount, "toggle off keeps the pool");
            Check.Equal(0, MusketeerCombat.LiveCount, "toggle off clears live bullets");
            Check.False(UnityEngine.Object.Destroyed.Contains(entry), "toggle off does not destroy pooled visuals");

            MusketeerAccess.EnabledValue = true;
            Time.time = 5f;
            MusketeerRuntime.Tick();
            Fire(archer);
            Check.Equal(1, MusketeerCombat.LiveCount, "toggle back on shoots again");
            Check.Equal(0, MusketeerCombat.PooledVisualCount, "the retained pool entry is reused");
            Check.True(MusketeerCombat.VisualForTests(0) == entry, "reused the very same pooled visual");
        }

        private static void StalePoolEntriesDoNotEatShots()
        {
            Archer archer = Armed(out GameObject target);

            var destroyed = new GameObject("KEM_MusketeerBullet");
            destroyed.transform.SetParent(MusketeerAccess.WorldValue, false);

            var good = new GameObject("KEM_MusketeerBullet");
            good.transform.SetParent(MusketeerAccess.WorldValue, false);
            good.AddComponent<SpriteRenderer>();

            // 池顶是坏条目（销毁的），下面才是可用条目：一次调用必须继续租下去。
            MusketeerCombat.InjectPooledVisualForTests(good);
            MusketeerCombat.InjectPooledVisualForTests(destroyed);
            UnityEngine.Object.Destroy(destroyed); // destruction happens after the object entered the pool
            Check.Equal(2, MusketeerCombat.PooledVisualCount, "two entries queued");

            Fire(archer);
            Check.Equal(1, MusketeerCombat.LiveCount, "the paid shot is not lost to a stale pool entry");
            Check.True(MusketeerCombat.VisualForTests(0) == good, "the working entry is rented");
            Check.Equal(0, MusketeerCombat.PooledVisualCount, "the broken entry was discarded, not kept");
        }

        private static void ReuseReparentsToCurrentLayer()
        {
            Archer archer = Armed(out GameObject target);
            var foreignParent = new GameObject("OldIslandLayer");

            var entry = new GameObject("KEM_MusketeerBullet");
            entry.transform.SetParent(foreignParent.transform, false);
            entry.AddComponent<SpriteRenderer>();
            MusketeerCombat.InjectPooledVisualForTests(entry);

            Fire(archer);
            Check.Equal(1, MusketeerCombat.LiveCount, "shot fired with a pooled entry available");
            Check.True(MusketeerCombat.VisualForTests(0) == entry, "the pooled entry is reused");
            Check.True(MusketeerCombat.VisualForTests(0).transform.parent == MusketeerAccess.WorldValue,
                "it was reparented to the current layer before activation");
        }

        private static void RetireReceiptRetried()
        {
            Archer archer = Armed(out GameObject target);
            Fire(archer);
            LandBullet(target);
            GameObject entry = MusketeerCombat.PooledVisualForTests(0);

            UnityEngine.Object.ThrowOnDestroyCount = 2;               // 本次收尾 + 立即重试都失败
            MusketeerAccess.WorldValue = new GameObject("GameLayer2").transform;   // 换世界 → 触发收尾
            Time.time = 5f;
            MusketeerRuntime.Tick();
            Check.Equal(0, MusketeerCombat.PooledVisualCount, "the pool was handed to retirement");
            Check.Equal(1, MusketeerCombat.RetiredVisualCount, "the owned reference is kept in a receipt");
            Check.False(UnityEngine.Object.Destroyed.Contains(entry), "destroy failed, so no ownership was lost");

            Time.unscaledTime = 2f;                                   // 越过限频窗口
            MusketeerRuntime.Tick();
            Check.Equal(0, MusketeerCombat.RetiredVisualCount, "the retry completed the retirement");
            Check.True(UnityEngine.Object.Destroyed.Contains(entry), "the owned visual was destroyed");
        }

        private static void CapacityRetainedAfterSaturation()
        {
            Archer archer = Armed(out GameObject target);
            Physics2D.Saturate = true;                    // 数组饱和 → 完整 List 兜底（不再吞弹）
            Fixture.QueueHit(target, 0.5f);
            Fire(archer);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(256, MusketeerCombat.HitCapacity, "buffer grew to the soft cap");
            Check.Equal(1, target.GetComponent<Damageable>().DamageLog.Count,
                "the complete-list fallback still lands the hit");

            Physics2D.Saturate = false;
            Physics2D.QueuedHits.Clear();
            Physics2D.CastCount = 0;
            Time.time = 5f;
            Fixture.QueueHit(target, 0.5f);
            Fire(archer);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, Physics2D.CastCount, "one array cast per segment again");
            Check.Equal(256, MusketeerCombat.HitCapacity, "the enlarged buffer is retained (never shrunk back to 32)");
            Check.Equal(2, target.GetComponent<Damageable>().DamageLog.Count, "normal casts still damage");
        }

        // ---------- helpers ----------

        private static Archer Armed(out GameObject target)
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            Archer archer = Fixture.ArmMusketeer();
            target = Fixture.NewEnemy();
            archer._shootingTarget = target;
            return archer;
        }

        private static void Fire(Archer archer)
        {
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "shot handled");
        }

        /// <summary>推进到子弹命中排队的敌人（视觉回池）。</summary>
        private static void LandBullet(GameObject target)
        {
            Physics2D.QueuedHits.Clear();
            Fixture.QueueHit(target, 0.5f);
            MusketeerCombat.Tick(0.05f, true);
        }
    }
}
