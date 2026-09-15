using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KingdomEnhancedMod;

namespace HermesHeadwearCycleTests
{
    internal static class Check
    {
        internal static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        internal static void False(bool condition, string message)
        {
            if (condition) throw new Exception(message);
        }

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
            }
        }

        internal static void Sequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
        {
            if (expected.Count != actual.Count)
            {
                throw new Exception(message + " [count expected=" + expected.Count + " actual=" + actual.Count + "]");
            }
            for (int i = 0; i < expected.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                {
                    throw new Exception(message + " [index " + i + " expected=" + expected[i] + " actual=" + actual[i] + "]");
                }
            }
        }
    }

    /// <summary>一条用例；逐例隔离临时目录，串行执行（并发用例内部自带线程）。</summary>
    internal static class Case
    {
        internal static int Passed;
        internal static int Failed;
        internal static readonly List<string> Failures = new List<string>();

        internal static void Run(string name, Action body)
        {
            try
            {
                body();
                Passed++;
                Console.WriteLine("  PASS  " + name);
            }
            catch (Exception e)
            {
                Failed++;
                Failures.Add(name + " -> " + e.Message);
                Console.WriteLine("  FAIL  " + name);
                Console.WriteLine("        " + e.Message);
            }
        }
    }

    /// <summary>测试目录内的临时目录：只删自己这一棵，绝不扫别的目录。</summary>
    internal sealed class TempDir : IDisposable
    {
        internal readonly string Path;

        internal TempDir()
        {
            Path = System.IO.Path.Combine(AppContext.BaseDirectory, "hermes-cycle-tmp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
            try
            {
                Directory.Delete(System.IO.Path.GetDirectoryName(Path), false);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// 一个隔离的作用域：tempdir 中的 v2 路径（唯一注入的生产 seam）+ 同目录 v1 兄弟（旧文件，测试显式构造）
    /// + 主机权限 + 每例清空的日志类目。
    /// </summary>
    internal sealed class Scope : IDisposable
    {
        internal readonly TempDir Dir;

        /// <summary>注入 seam 的 v2 路径（当前 schema 的主文件）。</summary>
        internal readonly string Path;

        /// <summary>旧 v1 兄弟路径（只读迁移来源；生产按 v2 同目录推导，测试单独持有以便构造/断言）。</summary>
        internal readonly string LegacyPath;

        private Scope(bool authority)
        {
            Dir = new TempDir();
            Path = Dir.File("hermes-headwear-cycle.v2.json");
            LegacyPath = Dir.File("hermes-headwear-cycle.v1.json");
            HermesHeadwearCycle.ResetForTests();
            HermesHeadwearCycle.AuthorityOverride = () => authority;
            HermesHeadwearCycle.PathOverride = Path;
        }

        internal static Scope Create(bool authority = true)
        {
            return new Scope(authority);
        }

        internal void SetAuthority(bool value)
        {
            HermesHeadwearCycle.AuthorityOverride = () => value;
        }

        internal void SetPath(string path)
        {
            HermesHeadwearCycle.PathOverride = path;
        }

        public void Dispose()
        {
            HermesHeadwearCycle.AuthorityOverride = null;
            HermesHeadwearCycle.PathOverride = null;
            HermesHeadwearCycle.BeforeWriteHook = null;
            HermesHeadwearCycle.ResetForTests();
            Dir.Dispose();
        }
    }

    internal static class Fixture
    {
        internal const string Kind = "hermes-headwear-cycle";

        /// <summary>schema1 文档（旧 v1 迁移来源；测试独立构造，钉住盘上契约）。</summary>
        internal static byte[] DocV1(int nextChoice)
        {
            return Encoding.UTF8.GetBytes("{\"v\":1,\"kind\":\"" + Kind + "\",\"nextChoice\":" + nextChoice + "}");
        }

        /// <summary>schema2 文档（与生产 writer 逐字节一致，测试独立构造）。</summary>
        internal static byte[] DocV2(int nextChoice, int credit)
        {
            return Encoding.UTF8.GetBytes(
                "{\"v\":2,\"kind\":\"" + Kind + "\",\"nextChoice\":" + nextChoice + ",\"credit\":" + credit + "}");
        }

        internal static void Write(string path, byte[] bytes)
        {
            System.IO.File.WriteAllBytes(path, bytes);
        }

        internal static byte[] Read(string path)
        {
            return System.IO.File.ReadAllBytes(path);
        }

        internal static bool BytesEqual(byte[] a, byte[] b)
        {
            return a.AsSpan().SequenceEqual(b);
        }

        internal static string[] FilesIn(string directory)
        {
            string[] files = Directory.GetFiles(directory);
            for (int i = 0; i < files.Length; i++) files[i] = System.IO.Path.GetFileName(files[i]);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        internal static string Joined(string[] files)
        {
            return string.Join("|", files);
        }

        internal static int Next(string path)
        {
            Check.True(HermesHeadwearCycle.TryParseV2(Read(path), out int nextChoice, out int _, out string error),
                "v2 must parse: " + error);
            return nextChoice;
        }

        internal static int Credit(string path)
        {
            Check.True(HermesHeadwearCycle.TryParseV2(Read(path), out int _, out int credit, out string error),
                "v2 must parse: " + error);
            return credit;
        }

        internal static int LegacyNext(string path)
        {
            Check.True(HermesHeadwearCycle.TryParseV1(Read(path), out int nextChoice, out string error),
                "v1 must parse: " + error);
            return nextChoice;
        }

        /// <summary>断言一次成功调用并返回 choice（-1 = 本次未发放，credit 已记录）。</summary>
        internal static int Assign(int chance)
        {
            Check.True(HermesHeadwearCycle.TryAssign(chance, out int choice), "TryAssign(" + chance + ") must succeed");
            return choice;
        }

        /// <summary>断言一次拒绝调用：false 且 choice=-1。</summary>
        internal static int Refuse(int chance)
        {
            Check.False(HermesHeadwearCycle.TryAssign(chance, out int choice), "TryAssign(" + chance + ") must refuse");
            Check.Equal(HermesHeadwearCycle.ChoiceNone, choice, "refused call yields -1");
            return choice;
        }
    }
}
