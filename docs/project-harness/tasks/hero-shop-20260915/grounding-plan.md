# 英雄驿站脚点对齐地面

用户截图证明f8c25095已可创建商店，但下半部分被地面遮挡。当前实际日志owner preflight passed、ready x74.93、purchase completed，故原interface修复已获一次真实正例，后续保留该桥。

实际2.4资源提取94个商店对象，rootY为0.875或0.88，body SpriteRenderer在root、bottom pivot0、PPU32。现有英雄商店rootY取GameLayer.y（通常0），未继承原版shop的地面基准，约低28源像素。自身四帧alpha bbox均(2,14,124,79)，最低可见像素在texture底上1px，pivot底部2px，约1px落在root基准以下；本轮不改PNG/pivot。禁止按视觉猜测改角色缩放。

修复契约：只在创建时选同当前世界、活动原生PayableShop根的地面高度；不取merchant/旗帜/工具子renderer的Y或bounds，缺可靠参考延后。英雄商店的主体、旗帜与Payable同root整体抬升，保持x选址、付款、存档、IntPtr owner桥和已完成英雄功能。创建日志记录参考和实际位置。原世界商店与存档不改。

worker：夜间本机OMP18.1.19，deepseek/deepseek-v4-flash、thinking=max，session01a0a4ee-46e1-7693-bdef-2c588b0e3a2c。仅HeroShop.cs/tests允许写。Operator提取实际资源与负责集成；独立内置reviewer按用户授权只读审核。

构建禁自动部署；代码与证据通过后若游戏确实关闭，可按既有本机安装授权备份安装。不擅自启动游戏。新截图验证前不能宣称实机高度已修复。
