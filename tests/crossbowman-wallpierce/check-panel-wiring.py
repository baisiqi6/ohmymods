"""Static wiring audit of the actual MOD角色 slider source (no UI fixture mirror)."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
panel = (ROOT / "il2cpp/ModPanel.cs").read_text(encoding="utf-8")
config = (ROOT / "il2cpp/ModConfig.cs").read_text(encoding="utf-8")

section = panel.split("case 8:", 1)[1].split("break;", 1)[0]
assert "CrossbowRatioSlider(ref y, width);" in section
assert "_category == 8 ? 8" in panel

method = panel.split("private static void CrossbowRatioSlider", 1)[1].split("private static void FloatSlider", 1)[0]
assert "CrossbowRatioPolicy.Normalize(config.Value)" in method
assert "CrossbowRatioPolicy.SnapSlider(raw)" in method
assert "out bool interacted" in method
assert "if (interacted)" in method
before, after = method.split("if (interacted)", 1)
assert "config.Value =" not in before and after.count("config.Value = next") == 1
assert "if (next != config.Value) config.Value = next;" in after

shared_slider = panel.split("private static float Slider(", 1)[1]
assert "eventType == EventType.MouseDown || eventType == EventType.MouseDrag" in shared_slider
assert "|| eventType == EventType.KeyDown" in shared_slider
assert "interacted = input && GUI.changed;" in shared_slider

setting = config.split('"RecruitmentRatio", CrossbowRatioPolicy.Default', 1)[1]
assert "new AcceptableValueList<float>(.25f, .5f, .75f, 1f)" in setting
assert "CrossbowRecruitmentRatio.SettingChanged" not in config + panel
print("PASS panel ratio: four config values, MOD角色 card, input-only write, shared slider gate")
