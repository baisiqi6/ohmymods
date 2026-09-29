using System;
using System.Reflection;
using KingdomEnhancedMod;

namespace MapWidthTests
{
    /// <summary>
    /// PatchWorld_Level.Finalizer 的实际契约测试：反射调用真实补丁方法，断言 Harmony 语义下
    /// 异常对象不被替换/吞掉（Harmony：finalizer 返回 null 会清除异常，返回同一实例才继续抛出），
    /// 且 scope 无论成功/异常路径都会归还。这里不执行游戏程序集，只验证补丁方法的可观察返回契约。
    /// </summary>
    internal static class FinalizerTests
    {
        private static readonly MethodInfo FinalizerMethod = typeof(PatchWorld_Level).GetMethod(
            "Finalizer", BindingFlags.NonPublic | BindingFlags.Static);

        internal static void Run()
        {
            Console.WriteLine("patch finalizer:");

            Case.Run("finalizer can carry and return the original exception", () =>
            {
                Check.True(FinalizerMethod != null, "PatchWorld_Level.Finalizer exists");
                Check.Equal(typeof(Exception), FinalizerMethod.ReturnType,
                    "must return Exception: void/null cannot keep the native exception");
                ParameterInfo[] parameters = FinalizerMethod.GetParameters();
                Check.Equal(2, parameters.Length, "parameter count");
                Check.Equal(typeof(Exception), parameters[0].ParameterType, "receives the incoming exception");
                Check.Equal(typeof(MapWidthScope.Frame), parameters[1].ParameterType, "receives the scope state");
            });

            Case.Run("no native exception: null stays null and the scope is released", () =>
            {
                TestLog.Clear();
                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                object returned = Invoke(frame, null);
                Check.True(returned == null, "no exception must stay absent");

                // scope 已归还：没有可消费的 frame，GetBlocks 不得改动结果。
                BridgeTests.Fixture fixture = BridgeTests.MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;
                MapWidthScope.TryApply(fixture.Layout, ref list);
                Check.Same(fixture.Result, list, "scope released by the finalizer");
            });

            Case.Run("native exception object is returned unchanged", () =>
            {
                TestLog.Clear();
                var native = new InvalidOperationException("native generation failure");
                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                object kept = Invoke(frame, native);
                Check.Same(native, kept, "the same exception instance must be returned (continues to throw)");

                // 异常之后栈已恢复，新 scope 可正常工作。
                BridgeTests.Fixture fixture = BridgeTests.MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;
                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame next);
                try
                {
                    MapWidthScope.TryApply(fixture.Layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, next);
                }
                Check.True(list.Count > 5, "fresh scope works after an exception");
            });

            Case.Run("nested and disabled scopes each keep their own exception", () =>
            {
                TestLog.Clear();
                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame outer);
                MapWidthScope.Open(false, 5f, out MapWidthScope.Frame innerDisabled);

                var innerException = new InvalidOperationException("inner");
                Check.Same(innerException, Invoke(innerDisabled, innerException),
                    "disabled inner scope keeps its exception");

                var outerException = new InvalidOperationException("outer");
                Check.Same(outerException, Invoke(outer, outerException),
                    "outer scope keeps its exception");

                // 栈恢复：新 scope 正常扩图。
                BridgeTests.Fixture fixture = BridgeTests.MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;
                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame next);
                try
                {
                    MapWidthScope.TryApply(fixture.Layout, ref list);
                }
                finally
                {
                    MapWidthScope.Close(null, next);
                }
                Check.True(list.Count > 5, "scope stack restored after nested finalizers");
            });

            Case.Run("published scope still rethrows the original exception", () =>
            {
                TestLog.Clear();
                BridgeTests.Fixture fixture = BridgeTests.MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> list = fixture.Result;

                MapWidthScope.Open(true, 2f, out MapWidthScope.Frame frame);
                MapWidthScope.TryApply(fixture.Layout, ref list);
                Check.NotSame(fixture.Result, list, "padding was published before the native failure");

                var native = new InvalidOperationException("after publish");
                Check.Same(native, Invoke(frame, native), "published scope still returns the original exception");
                Check.True(TestLog.WarningContains("generation threw after GetBlocks"), "partial padding warned");

                // 归还后无残留 frame。
                BridgeTests.Fixture second = BridgeTests.MakeFixture();
                Il2CppSystem.Collections.Generic.List<LevelBlock> secondList = second.Result;
                MapWidthScope.TryApply(second.Layout, ref secondList);
                Check.Same(second.Result, secondList, "no stale scope after the exception path");
            });
        }

        private static object Invoke(MapWidthScope.Frame state, Exception exception)
        {
            return FinalizerMethod.Invoke(null, new object[] { exception, state });
        }
    }
}
