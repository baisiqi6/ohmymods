using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerRuntimeTests
{
    /// <summary>测试世界的最小夹具：可控时间/世界/原生 prefab 基线/单位与敌人。</summary>
    internal static class Fixture
    {
        internal const int EnemiesLayer = 10;
        internal const int CitizensLayer = 11;
        internal const int WildlifeLayer = 12;

        internal static void Reset()
        {
            NetworkBigBoss.HasWorldAuth = true;
            Time.time = 0f;
            Time.deltaTime = 0f;
            Time.unscaledTime = 0f;
            Time.timeScale = 1f;
            Time.frameCount = 1;
            UnityEngine.Object.Destroyed.Clear();
            UnityEngine.Object.ThrowOnDestroyCount = 0;
            Physics2D.QueuedHits.Clear();
            Physics2D.Saturate = false;
            Physics2D.ThrowOnCast = false;
            Physics2D.CastCount = 0;
            Physics2D.ThrowOnListCast = false;
            Physics2D.ListCastCount = 0;
            Physics2D.queriesHitTriggers = true;
            Physics2D.LastLayerMask = 0;
            Physics2D.LastListFilter = default;
            Physics2D.LastListLayerMask = 0;
            Il2CppSystem.Collections.Generic.List<RaycastHit2D>.CreatedForTests = 0;
            MusketeerAccess.Reset();
            MusketeerIdentity.Reset();
            MusketeerVisuals.Clear();
            MusketeerVisuals.SetAtlasForTests(null);   // 测试程序集没有嵌入图集：默认不可用
            MusketeerRuntime.ResetForTests();
            // 测试程序集没有嵌入弹丸贴图：注入一个替身精灵（生产走 embedded resource + 尺寸校验）。
            MusketeerCombat.SetBulletSpriteForTests(new Sprite());
            PatchRoles_Crossbowman.CrossbowmanResult = false;
            World.GroundCollider = null;
            Holder.Inst = null;
            Managers.Inst = null;
        }

        internal static Transform NewWorld()
        {
            var layer = new GameObject("GameLayer");
            MusketeerAccess.WorldValue = layer.transform;
            if (Managers.Inst == null) Managers.Inst = new Managers();
            return layer.transform;
        }

        /// <summary>原生 Archer prefab 基线（Holder.tagCharacterPairs["Archer"]）。</summary>
        internal static Archer InstallNativeArcherPrefab(float shootRange = 8f, float prep = 0.5f,
            float cooldown = 1f, float intervalMin = 0.5f, float intervalMax = 1.5f)
        {
            var holder = new Holder();
            Holder.Inst = holder;
            if (Managers.Inst == null) Managers.Inst = new Managers();
            Managers.Inst.holder = holder;

            var prefabGo = new GameObject("Archer");
            var prefab = prefabGo.AddComponent<Archer>();
            prefab.shootRange = shootRange;
            prefab.shootPrepTime = prep;
            prefab.shootCooldownTime = cooldown;
            prefab._shootIntervalRange = new Vector2(intervalMin, intervalMax);
            prefab.minAttempts = 1;
            prefab.maxAttempts = 3;
            prefab.perfectArrowProbability = 0.2f;
            prefab._arrowAttack = NewAttack();
            prefab.ActiveArrowAttack = prefab._arrowAttack;

            var character = prefabGo.AddComponent<Character>();
            holder.tagCharacterPairs["Archer"] = character;
            return prefab;
        }

        internal static ArrowAttack NewAttack()
        {
            var arrowGo = new GameObject("Arrow");
            arrowGo.layer = EnemiesLayer;
            arrowGo.AddComponent<SpriteRenderer>();
            var attack = new ArrowAttack { _arrowPrefab = arrowGo.AddComponent<Arrow>() };
            return attack;
        }

        /// <summary>地面单位（Archer 本体）：Character/Damageable/renderer/animator + 基础箭。</summary>
        internal static Archer NewArcher(string name = "archer")
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(0f, 0.5f, 0f);   // 合成测试脚点；平飞场景应显式安装对应地面，不代表真实世界 root 高度。
            var archer = go.AddComponent<Archer>();
            archer._character = go.AddComponent<Character>();
            archer._damageable = go.AddComponent<Damageable>();
            archer._spriteRenderer = go.AddComponent<SpriteRenderer>();
            archer._animator = go.AddComponent<Animator>();
            archer._arrowAttack = NewAttack();
            archer.ActiveArrowAttack = archer._arrowAttack;
            return archer;
        }

        /// <summary>地面敌人（Enemies 层 + Damageable + Enemy 组件）。</summary>
        internal static GameObject NewEnemy(EnemyType type = EnemyType.TrollWeak, bool acceptsArrow = true,
            bool addSquidComponent = false, bool addEnemyComponent = true)
        {
            var go = new GameObject("enemy");
            go.layer = EnemiesLayer;
            go.SetActive(true);
            var damageable = go.AddComponent<Damageable>();
            damageable.acceptsArrow = acceptsArrow;
            if (addEnemyComponent && !addSquidComponent) go.AddComponent<Enemy>().Type = type;
            if (addSquidComponent)
            {
                var squid = go.AddComponent<Squid>();
                squid.Type = EnemyType.Squid;
            }
            return go;
        }

        /// <summary>把一个 Archer 装成活动火铳手（identity 名单 + 一次 Tick 装包）。</summary>
        internal static Archer ArmMusketeer()
        {
            Archer archer = NewArcher();
            MusketeerIdentity.Units.Add(archer);
            MusketeerRuntime.Tick();
            return archer;
        }

        /// <summary>白天开关（Kingdom.isDaytime）：鹿猎判据/发射门读它；未设置时判据 fail-closed。</summary>
        internal static void SetDaytime(bool daytime)
        {
            if (Managers.Inst == null) Managers.Inst = new Managers();
            if (Managers.Inst.kingdom == null) Managers.Inst.kingdom = new Kingdom();
            Managers.Inst.kingdom.isDaytime = daytime;
        }

        /// <summary>普通鹿根：Wildlife 层 + "Wildlife" tag + Deer 组件 + 鹿根自己的 Damageable/Petrifiable。</summary>
        internal static GameObject NewDeer(string name = "deer")
        {
            var go = new GameObject(name);
            go.layer = WildlifeLayer;
            go.tag = "Wildlife";
            go.SetActive(true);
            go.AddComponent<Deer>();
            go.AddComponent<Damageable>();
            go.AddComponent<Petrifiable>();
            return go;
        }

        /// <summary>兔子等小动物：Wildlife 层 + tag + Damageable，但没有 Deer 根组件。</summary>
        internal static GameObject NewCritter(string name = "rabbit")
        {
            var go = new GameObject(name);
            go.layer = WildlifeLayer;
            go.tag = "Wildlife";
            go.SetActive(true);
            go.AddComponent<Critter>();
            go.AddComponent<Damageable>();
            return go;
        }

        /// <summary>坐骑（Hind）：同在 Wildlife 层/tag，但组件是 Hind，绝不是鹿。</summary>
        internal static GameObject NewHind(string name = "hind")
        {
            var go = new GameObject(name);
            go.layer = WildlifeLayer;
            go.tag = "Wildlife";
            go.SetActive(true);
            go.AddComponent<Hind>();
            go.AddComponent<Damageable>();
            return go;
        }

        /// <summary>安装一个顶面为 top 的世界地面（弹道的地面裁剪/终止都读 World.GroundCollider.bounds.max.y）。</summary>
        internal static void SetGroundTop(float top)
        {
            var ground = new GameObject("ground").AddComponent<BoxCollider2D>();
            ground.bounds = new Bounds { center = new Vector3(0f, top - 0.2f, 0f), extents = new Vector3(40f, 0.2f, 0f) };
            World.GroundCollider = ground;
        }

        /// <summary>鹿根自己的 body collider：枪口水平线（测试夹具 y≈0.894）穿过其 bounds → 平射可命中。</summary>
        internal static Collider2D AddFlatBody(GameObject deer, float centerX = 3f)
        {
            Collider2D body = deer.AddComponent<Collider2D>();
            body.bounds = new Bounds
            {
                center = new Vector3(centerX, 0.90f, 0f),
                extents = new Vector3(0.45f, 0.20f, 0f),
            };
            return body;
        }

        /// <summary>鹿根自己的 body collider：模拟资源几何推算的低于枪口情形（非游戏实测）→ 需要直线微调。</summary>
        internal static Collider2D AddLowBody(GameObject deer, float centerX = 3f)
        {
            Collider2D body = deer.AddComponent<Collider2D>();
            body.bounds = new Bounds
            {
                center = new Vector3(centerX, 0.60f, 0f),
                extents = new Vector3(0.45f, 0.18f, 0f),
            };
            return body;
        }

        /// <summary>向 physics 桩排队一条命中（距离 = 沿本段计算的距离）。</summary>
        internal static void QueueHit(GameObject target, float distance)
            => QueueHit(target.GetComponent<Collider2D>() ?? AddCollider(target), distance);

        /// <summary>向 physics 桩排队一条命中（指定 collider；多 collider/重复命中场景用）。</summary>
        internal static void QueueHit(Collider2D collider, float distance)
        {
            Physics2D.QueuedHits.Add(new RaycastHit2D
            {
                collider = collider,
                distance = distance,
            });
        }

        private static Collider2D AddCollider(GameObject target)
        {
            var collider = target.AddComponent<Collider2D>();
            return collider;
        }
    }
}
