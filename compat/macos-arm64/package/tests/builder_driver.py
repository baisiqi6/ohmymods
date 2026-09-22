#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""测试专用构建器驱动（不属于生产 CLI）。

以 import 方式加载 build_package.py 副本，把契约常量 BUILTIN_ASSEMBLY_SHA256
monkeypatch 为夹具游戏的 GameAssembly 哈希后运行其 main()。生产构建器本身
没有任何测试放宽通道；测试只在自有副本上做替换。

用法: builder_driver.py <builder路径> <assembly_sha256|'-'> [构建器参数...]
  assembly_sha256 为 '-' 时不替换（用于验证契约常量强制生效）。
"""

import importlib.util
import sys


def main():
    if len(sys.argv) < 3:
        print("用法: builder_driver.py <builder路径> <assembly_sha256|'-'> [参数...]",
              file=sys.stderr)
        return 2
    builder_path, assembly_sha = sys.argv[1], sys.argv[2]
    sys.argv = ["build_package.py"] + sys.argv[3:]
    spec = importlib.util.spec_from_file_location("build_package_under_test", builder_path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    if assembly_sha != "-":
        mod.BUILTIN_ASSEMBLY_SHA256 = assembly_sha
    return mod.main()


if __name__ == "__main__":
    sys.exit(main())
