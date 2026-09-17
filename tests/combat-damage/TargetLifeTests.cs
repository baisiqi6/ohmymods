using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomCombatDamage.Tests
{
    /// <summary>
    /// 目标生命标识契约：
    /// - marker 只在无 life 时取一次全局单调序号：AddComponent 同步 OnEnable 与读取初始化幂等互让，绝不双增；
    /// - 只用自有 marker 组件、不写任何 flags（DontSave 位会带额外 On/Off 语义）：GO 停用/启用（池回收）才是新 life；组件单独 disable 不换号；
    /// - 失败显式降级（返回 false + 计数），绝不编造/复用 token；
    /// - 销毁重加不碰撞（全局单调 life）。
    /// 实机运行结果以 Operator 的构建/测试为准（本文件只提供可执行回归）。
    /// </summary>
    public sealed class TargetLifeTests : CombatTestBase
    {
        [Fact]
        public void Initialize_RegistersMarkerIdempotently()
        {
            Assert.True(CombatTargetLife.Initialize());
            Assert.True(CombatTargetLife.Initialize());
            Assert.True(Il2CppInterop.Runtime.Injection.ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(CombatTargetLifeMarker)));
        }

        [Fact]
        public void TryResolve_SameLifeTwice_KeepsToken_AndNeverTouchesFlags()
        {
            Damageable target = NewTarget(out GameObject go);

            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken first));
            Assert.True(first.Life > 0);
            Assert.Equal(1, CombatTargetLife.StatMarkerCreated);

            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken again));
            Assert.True(CombatTargetToken.Matches(first, again));            // 同一 life 不换号、不加第二个 marker

            CombatTargetLifeMarker marker = go.GetComponent<CombatTargetLifeMarker>();
            Assert.NotNull(marker);
            Assert.Equal(HideFlags.None, marker.hideFlags);                  // 保持默认 flags（DontSave 会带额外 On/Off 语义）
            Assert.Equal(0, UnityEngine.Object.HideFlagsWrites);             // 生产全程未写任何 hideFlags
            Assert.Equal(1, CombatTargetLife.StatMarkerCreated);
        }

        [Fact]
        public void MarkerOnEnableThenResolve_DoesNotTakeASecondLife()
        {
            Damageable target = NewTarget(out GameObject go);
            CombatTargetLifeMarker marker = go.AddComponent<CombatTargetLifeMarker>();   // 活动 GO：同步 OnEnable 取号
            long afterOnEnable = marker.Life;
            Assert.True(afterOnEnable > 0);

            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken token));

            Assert.Equal(afterOnEnable, token.Life);                         // 读取初始化没有再次取号（幂等）
            Assert.Equal(afterOnEnable, marker.EnsureLife());
        }

        [Fact]
        public void ReadInitBeforeActivation_ThenOnEnable_KeepsTheSameLife()
        {
            Damageable target = NewTarget(out GameObject go);
            go.SetActive(false);                                             // 未激活：AddComponent 不会派发 OnEnable

            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken before));
            Assert.True(before.Life > 0);                                    // 读取初始化先取号

            go.SetActive(true);                                              // 之后的 OnEnable 必须幂等让位
            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken after));
            Assert.True(CombatTargetToken.Matches(before, after));
        }

        [Fact]
        public void ComponentOnlyDisable_KeepsLifeButFailsClosedUntilObservedGoCycle()
        {
            Damageable target = NewTarget(out GameObject go);
            CombatTargetLifeMarker marker = go.AddComponent<CombatTargetLifeMarker>();
            long life = marker.EnsureLife();

            for (int i = 0; i < 3; i++)
            {
                marker.enabled = false;
                UnityLifecycle.Invoke(marker, "OnDisable");                  // GO 仍激活：Unity 只派发组件消息
                Assert.Equal(life, marker.Life);                             // 不结束、也不换号
                marker.enabled = true;
                UnityLifecycle.Invoke(marker, "OnEnable");
                Assert.Equal(life, marker.Life);                             // 反复开关不伪造新 life
            }

            // 观察缺口（组件禁用期间 GO 的停用/启用不可见）→ fail-closed：不得沿用旧 life
            Assert.False(CombatTargetLife.TryResolve(target, out CombatTargetToken blind));
            Assert.Equal(0, blind.Life);

            // 确证一次完整 GO 停用/启用后才恢复，并取新 life
            go.SetActive(false);
            go.SetActive(true);
            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken recovered));
            Assert.NotEqual(life, recovered.Life);
        }

        [Fact]
        public void MarkerDisabledDuringGoRecycle_FailsClosedThenRecovers()
        {
            Damageable target = NewTarget(out GameObject go);
            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken first));
            CombatTargetLifeMarker marker = go.GetComponent<CombatTargetLifeMarker>();

            marker.enabled = false;
            UnityLifecycle.Invoke(marker, "OnDisable");                      // 单独禁用（GO 仍活跃）→ 观察缺口开始
            Assert.Equal(first.Life, marker.Life);                           // 旧 Life 保留
            Assert.False(CombatTargetLife.TryResolve(target, out _));        // 仍 disabled → 拒绝

            go.SetActive(false);                                             // stub 不向 disabled 组件派发消息
            go.SetActive(true);                                              // 缺口期间发生真实 GO 复用
            marker.enabled = true;
            UnityLifecycle.Invoke(marker, "OnEnable");                       // 重新启用：不得伪造新 life
            Assert.Equal(first.Life, marker.Life);
            Assert.False(CombatTargetLife.TryResolve(target, out CombatTargetToken blind));   // 观察缺口 → 拒绝
            Assert.Equal(0, blind.Life);

            go.SetActive(false);                                             // marker 已启用：确证完整 GO 停用/启用
            go.SetActive(true);
            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken recovered));
            Assert.NotEqual(first.Life, recovered.Life);
        }

        [Fact]
        public void ActivityReadFailure_IsNotConfirmedDeactivation()
        {
            Damageable target = NewTarget(out GameObject go);
            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken first));
            CombatTargetLifeMarker marker = go.GetComponent<CombatTargetLifeMarker>();

            go.ThrowActiveRead = true;
            UnityLifecycle.Invoke(marker, "OnDisable");                      // 活性读取失败 = 未知，不是「已确证停用」
            Assert.Equal(first.Life, marker.Life);                           // 不得据此结束/换号
            Assert.False(CombatTargetLife.TryResolve(target, out _));        // 观察不可信 → fail-closed

            go.ThrowActiveRead = false;
            go.SetActive(false);                                             // 确证的完整停用/启用
            go.SetActive(true);
            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken recovered));
            Assert.NotEqual(first.Life, recovered.Life);
        }

        [Fact]
        public void RegistrationIsAttemptedOnce_PerProcess_FailsClosedWithoutRetry()
        {
            ResetRegistrationState();
            Il2CppInterop.Runtime.Injection.ClassInjector.FailRegistration = true;

            Damageable first = NewTarget(out _);
            Damageable second = NewTarget(out _);
            Assert.False(CombatTargetLife.TryResolve(first, out CombatTargetToken a));
            Assert.False(CombatTargetLife.TryResolve(second, out CombatTargetToken b));
            Assert.Equal(0, a.Life);
            Assert.Equal(0, b.Life);

            Assert.Equal(1, Il2CppInterop.Runtime.Injection.ClassInjector.RegisterCalls);   // 每进程只尝试一次注册
            Assert.Equal(1, RegisterErrorLines());                                           // 注册错误只报告一次
            Assert.True(CombatTargetLife.StatDegraded >= 2);                                 // 命中点仍计数（限频日志）

            Il2CppInterop.Runtime.Injection.ClassInjector.FailRegistration = false;
            Damageable third = NewTarget(out _);
            Assert.False(CombatTargetLife.TryResolve(third, out _));                         // 本进程 fail-closed
            Assert.Equal(1, Il2CppInterop.Runtime.Injection.ClassInjector.RegisterCalls);    // 不再重复尝试

            ResetRegistrationState();                                                        // 收尾：不影响其它用例
        }

        private static int RegisterErrorLines()
        {
            int count = 0;
            foreach (string line in KingdomEnhancedPlugin.Instance.LogSource.Lines)
            {
                if (line.Contains("register:")) count++;
            }
            return count;
        }

        /// <summary>测试用：清注入器桩状态 + 显式重置生产私有注册状态（生产不暴露 reset）。</summary>
        private static void ResetRegistrationState()
        {
            Il2CppInterop.Runtime.Injection.ClassInjector.ResetForTests();
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            foreach (string field in new[] { "MarkerRegistered", "RegistrationAttempted" })
            {
                typeof(CombatTargetLife).GetField(field, flags).SetValue(null, false);
            }
        }

        [Fact]
        public void GoDeactivation_Reactivation_MintsNewLifeForSameGo()
        {
            Damageable target = NewTarget(out GameObject go);
            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken first));

            go.SetActive(false);                                             // 真实池回收：GO 停用 → life 结束
            Assert.Equal(0, go.GetComponent<CombatTargetLifeMarker>().Life);
            go.SetActive(true);                                              // 重新启用 → 新 life

            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken second));
            Assert.NotEqual(first.Life, second.Life);
            Assert.True(second.Life > first.Life);                           // 全局单调
            Assert.Equal(first.GoPointer, second.GoPointer);                 // 同一 GO / 同一指针
            Assert.False(CombatTargetToken.Matches(first, second));          // 新 life 可在后续 burst 再吃一次
        }

        [Fact]
        public void DestroyedTarget_ThenNewTarget_NeverCollides()
        {
            Damageable target = NewTarget(out GameObject go);
            Assert.True(CombatTargetLife.TryResolve(target, out CombatTargetToken first));
            UnityLifecycle.Invoke(go.GetComponent<CombatTargetLifeMarker>(), "OnDestroy");

            go.DestroySelf();
            Assert.False(CombatTargetLife.TryResolve(target, out CombatTargetToken dead));
            Assert.Equal(0, dead.Life);

            Damageable other = NewTarget(out _);
            Assert.True(CombatTargetLife.TryResolve(other, out CombatTargetToken second));
            Assert.NotEqual(first.Life, second.Life);                        // 销毁重加不碰撞
            Assert.False(CombatTargetToken.Matches(first, second));
        }

        [Fact]
        public void MarkerUnavailable_DegradesWithoutThrowOrFakeToken()
        {
            Damageable target = NewTarget(out GameObject go);
            go.RejectAdd = true;                                             // marker 创建失败

            Assert.False(CombatTargetLife.TryResolve(target, out CombatTargetToken token));
            Assert.Equal(0, token.Life);                                     // 绝不编造 token
            Assert.True(CombatTargetLife.StatDegraded >= 1);
            Assert.Null(go.GetComponent<CombatTargetLifeMarker>());
        }

        [Fact]
        public void IdReadFailure_DegradesWithoutThrow()
        {
            Damageable target = NewTarget(out GameObject go);
            go.ThrowIdRead = true;                                           // GO 身份读失败 → 不完整身份

            Assert.False(CombatTargetLife.TryResolve(target, out CombatTargetToken token));
            Assert.Equal(0, token.Life);
            Assert.True(CombatTargetLife.StatDegraded >= 1);
        }

        [Fact]
        public void TokenMatching_RequiresFullLifeIdentity()
        {
            CombatTargetToken a = new CombatTargetToken
            { GoPointer = new IntPtr(16), GoId = 1, Damageable = new IntPtr(32), Life = 7 };
            CombatTargetToken same = new CombatTargetToken
            { GoPointer = new IntPtr(16), GoId = 1, Damageable = new IntPtr(32), Life = 7 };
            CombatTargetToken otherLife = new CombatTargetToken
            { GoPointer = new IntPtr(16), GoId = 1, Damageable = new IntPtr(32), Life = 8 };
            CombatTargetToken otherGo = new CombatTargetToken
            { GoPointer = new IntPtr(48), GoId = 3, Damageable = new IntPtr(64), Life = 7 };

            Assert.True(CombatTargetToken.Matches(a, same));
            Assert.False(CombatTargetToken.Matches(a, otherLife));           // 新 life ≠ 旧 life
            Assert.False(CombatTargetToken.Matches(a, otherGo));
            Assert.False(CombatTargetToken.Matches(a, default));             // 降级/零 token 永不匹配
            Assert.False(CombatTargetToken.Matches(default, default));
        }

        [Fact]
        public void Marker_ExposesSimpleUnityCallbacks_AndNoPerFrameWork()
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string message in new[] { "OnEnable", "OnDisable", "OnDestroy" })
            {
                MethodInfo method = typeof(CombatTargetLifeMarker).GetMethod(message, flags, null, Type.EmptyTypes, null);
                Assert.NotNull(method);
                Assert.Equal(typeof(void), method.ReturnType);
                Assert.Empty(method.GetParameters());
            }
            Assert.NotNull(typeof(CombatTargetLifeMarker).GetConstructor(new[] { typeof(IntPtr) }));   // ClassInjector 注入约定
            foreach (string perFrame in new[] { "Update", "FixedUpdate", "LateUpdate", "OnGUI" })
            {
                Assert.Null(typeof(CombatTargetLifeMarker).GetMethod(perFrame, flags, null, Type.EmptyTypes, null));
            }
        }

        [Fact]
        public void Marker_HidesNonUnityHelpers_ButExposesLifecycleMessagesToInjection()
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type hide = typeof(Il2CppInterop.Runtime.Attributes.HideFromIl2CppAttribute);

            foreach (string message in new[] { "OnEnable", "OnDisable", "OnDestroy" })
            {
                MethodInfo method = typeof(CombatTargetLifeMarker).GetMethod(message, flags, null, Type.EmptyTypes, null);
                Assert.NotNull(method);
                Assert.Null(method.GetCustomAttribute(hide));       // 三个 Unity 消息必须留给注入器
            }
            foreach (string helper in new[] { "EnsureLife", "GameObjectActive" })
            {
                MethodInfo method = typeof(CombatTargetLifeMarker).GetMethod(helper, flags);
                Assert.NotNull(method);
                Assert.NotNull(method.GetCustomAttribute(hide));    // long / bool? 辅助方法不生成 invoker
            }
        }
    }
}
