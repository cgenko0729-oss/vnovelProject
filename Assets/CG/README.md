# CG 素材目录

把 CG（一枚绘）图片放进这个目录（.png / .jpg），然后重新生成剧本场景
（Tools → VN Effects → Create Script Demo Scene），生成器会把它们
自动灌入 `VNStage.cgLibrary`，**文件名（不含扩展名）= 剧本里的 cg id**。

## 剧本用法

```
cg 告白_夕阳                       # 显示 CG：默认隐藏立绘、暂停环境特效
cg 告白_夕阳 transition:CircleWipe  # 带全屏转场
cg 雨中告白 fx:keep                 # 保留雨/云影等环境特效层
cg 教室日常 chars:keep fx:keep      # 立绘和特效都保留（CG 当特殊背景用）
cg off                             # 关闭 CG，恢复背景/立绘/环境特效
```

- CG 之间可直接连续切换（差分/阶段演出），不必先 cg off。
- 显示过的 CG 自动记入全局鉴赏解锁（`vn_cg_unlocks.json`，与存档无关，
  读旧档/新周目不会丢），供之后的鉴赏画廊使用。
- 存档/读档/编辑器"从选中行播放"均会正确恢复 CG 显示状态。

建议尺寸：≥1920×1080（与画布一致），横构图。
