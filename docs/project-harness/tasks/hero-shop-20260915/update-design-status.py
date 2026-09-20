from pathlib import Path
import json

root = Path(__file__).resolve().parents[4]
harness = root / 'docs/project-harness'
summary = ('2026-09-15 英雄商店进入设计：用户要求原创商店刷新领地中段。'
           '已检查英雄自动选举、原生PayableComponent和独立存档方案，生成首张木石驿站/红旗/金弓草图。'
           '每岛一座/8金币升级现有弓手/面板改控商店仍为待用户回复的建议，不是已确认规则。'
           'Payable owner接口注入及CRPCHeader生命周期须实测；付费身份必须与临时战斗资格分离。'
           '本轮未改玩法代码、DLL、配置或存档，正式8.0保持；任务doing，见tasks/hero-shop-20260915/plan.md。')
checklist_path = harness / 'harness-checklist.json'
data = json.loads(checklist_path.read_text(encoding='utf-8-sig'))
if not any(x['id'] == 'hero-shop-20260915' for x in data['items']):
    data['items'].insert(0, {
        'id': 'hero-shop-20260915', 'title': '原创领地中段英雄商店与付费获取',
        'status': 'doing', 'priority': 'p1', 'owner': 'codex',
        'selected_in_session': 'codex-hero-shop-20260915', 'updated_at': '2026-09-15',
        'dependencies': [], 'blocked_by': [], 'blocked_reason': None,
        'acceptance': '规则收敛、原创像素素材、原生投币安全事务、稳定选址、购买附加档、回归审核与实机验收',
        'verification': summary,
        'handoff': '当前仅调查/草图；先接收用户规则回复，再锁定付款与持久化契约并实现。',
        'artifacts': {'plan': 'docs/project-harness/tasks/hero-shop-20260915/plan.md'}
    })
checklist_path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
for relative in ('progress.md', 'domain-model.md', 'game-logic-map/patch-patterns.md'):
    path = harness / relative
    existing = path.read_text(encoding='utf-8-sig')
    path.write_text(existing + '\n\n### 2026-09-15 英雄商店设计调查\n\n' + summary + '\n', encoding='utf-8')
plan = harness / 'current/task_plan.md'
plan.write_text(summary + '\n\n' + plan.read_text(encoding='utf-8-sig'), encoding='utf-8')
print('Design status recorded; gameplay and player files untouched.')
