# 盾卫外缘柔化（B）

离线生成两张盾卫角色图集。只给暴露在外部透明区域旁的不透明纯黑描边改 RGB，保留造型、盾缘形状、内部线条、透明度、脚线、帧格和全部动作。不会修改动画步频、移动速度、PPU、游戏或存档，也不自动安装素材。

## 输入与两层管线

1. 原有 `tools/heavy_shield_walk/build_atlases.py` 从其固定的旧基线打包已审过的 24 格走路修复。产物与提交 `100c181e91a6edf0dc673fb94645e40b4b20f2c0` 中的两张图完全相同。原有 `tests/heavy-shield-walk/verify_pixels.py` 应对这层 **pre-outline** 产物验证 24 格范围、上身、原 palette、盾缘、支撑行及鞋部连接。
2. 本工具始终用 `git show` 从上述完整提交提取 PNG，变换前核对 PNG 与 decoded RGBA 的固定 SHA-256；**不读取工作区图集**。因此工作区即使已经安装 B，再运行也不会重复柔化。新 verifier 对最终图独立验证允许改色范围、alpha、包围盒、支撑行、鞋部拓扑、509 个有效格及三个空尾格，并核对玩家已选 B 的 decoded RGBA 哈希。

最终图有意改变各动作的外缘色与 palette，不能再声称“最终只改 24 格”，也不能把旧 palette 测试直接套到最终图。两层证据分别保留。

固定输入 PNG SHA-256：

| 图集 | SHA-256 |
|---|---|
| `HeavyShieldSoldierAtlas.png` | `7fece0bbfb56bcb6f4c8f6443a3ee655a0ccd5654b795317ee2c58a0dc51b94c` |
| `HeavyShieldSoldierAtlasCoarse.png` | `923c2f72940168d075ac098c06bed19b14f49c62388ad41d91cae2584fc894a3` |

## 复现与验证

需要 Git、Python 3 与 Pillow。命令从仓库根目录运行。`--output` 必须显式指定一个**尚不存在**的目录，父目录须存在且路径中不能含符号链接。已有目录（包括空目录）一律拒绝；复跑请另选新目录。只输出两张 PNG 与无本机路径的 `manifest.json`，不下载字体或其他素材。

```sh
mkdir -p .local/shield-outline-check
python3 -B tools/heavy_shield_walk/build_atlases.py --output .local/shield-outline-check/pre-outline
python3 -B tests/heavy-shield-walk/verify_pixels.py --assets .local/shield-outline-check/pre-outline --evidence .local/shield-outline-check/legacy-walk
python3 -B tools/heavy_shield_outline/build_atlases.py --output .local/shield-outline-check/selected-B
python3 -B tests/heavy-shield-outline/verify_pixels.py --assets .local/shield-outline-check/selected-B --evidence .local/shield-outline-check/outline --self-test
```

最后一步的 `--self-test` 会分别篡改 alpha、内部黑像素、非黑外缘和空尾格，要求各自被对应的范围检查拒绝；还会实际调用生成器，将其结果与 `--assets` 的两张图按 mode、尺寸及 decoded RGBA 比较，并要求同环境两次 fresh 输出的 PNG 与 manifest 字节完全相同，同时验证已有目录不被覆盖、符号链接输出和父目录均被拒绝。`--assets` 不需要 manifest，PNG 重新压缩但像素相同仍可通过。新 verifier 不导入生成器，使用带 padding 的 Pillow floodfill，独立于生成器的队列实现。

审核通过后由维护者从独立输出目录安装两张 PNG；生成器本身不写 `il2cpp/Assets/`。

## B 的精确边界

逐格以四连通 floodfill 找出由格框连通的 alpha=0 区域。候选仅为该区域八邻接的不透明纯黑像素。材质供色仅取同格半径 3 内、亮度 20–180、最大通道不超过 215 的非黑不透明原像素，避免近白高光污染；限最近平方距离加 2 范围，以整数 `2520 // distance²` 加权，材质组权重不足 60% 时保留原黑。并列按固定排序选择原 palette 色。

选 B：供色 RGB 乘 `0.68`，亮度上限 `64`。只改色，不删除轮廓。Detail 有 50,025 个候选，改 45,683 个，保留 4,342 个不明确候选；Coarse 有 26,952 个候选，改 25,035 个，保留 1,917 个。所有非候选 RGBA、透明 RGB 与 alpha 均不变。

玩家已选 B 的 decoded RGBA SHA-256：

| 图集 | SHA-256 |
|---|---|
| Detail | `9c52b4eb99e0170e53bd535f600d0f7ada2834ff17a88f20f7781e3b2272fd12` |
| Coarse | `1def2baaae8a24ee123d734bf62ace505852f735700e7e530b99845d145a1b4f` |

PNG 编码字节可能随 Pillow/压缩库变化；decoded RGBA 哈希是像素等值依据，manifest 同时记录两种哈希。工具与新 verifier 使用稳定的 `tobytes()`、`load()` 等接口，不依赖 Pillow 12.1 新增的像素枚举 API。

这些检查证明离线像素范围与已选预览等值，不代表实机材质、昼夜光照或动画步态已验收，也不声称完全消除所有实机黑边。
