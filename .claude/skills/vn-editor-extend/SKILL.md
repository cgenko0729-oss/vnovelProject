---
name: vn-editor-extend
description: 修改/扩展剧本可视化编辑器（Scenario Editor）时的规则：Schema 登记、say 专用字段、SourceLineForRow 行号换算、Sprite textureRect、EditorPrefs 不标脏、窗口状态跨域重载存活、播放 Bridge 与热重载时序。Extend the visual scenario editor (VNScenarioEditorWindow, schema, thumbnails, hot reload).
---

# 剧本可视化编辑器扩展

## 何时用我
改 `Editor/VNScenarioEditorWindow.cs`、`VNScenarioDoc.cs`、`VNScenarioSchema.cs`，
或给编辑器加新行类型/新 UI 功能时。

## 铁律
- **文本是唯一真相**：`.vn.txt ↔ VNScenarioDoc.rows`，保存时重新生成文本，注释/空行必须保留。
- **say 的角色/表情是专用字段** `VNRow.speaker / expression`，**不是** `VNRow.values`；
  图片选择回调必须经专用访问器读写。`show` 才用普通 `character / expr` 参数。两条路径禁止混用。
- **`SpritePopup` / `CharacterPopup` / `ExpressionPopup` 是异步写值的**：选中走
  `PopupWindow` 回调 → `SetPopupValue` → 写进 `VNRow.values[key]`，**返回值当帧不变**。
  所以它们只能用在「值本来就存在 `values`（或 say 专用字段）」的参数格上。
  值存在别处的（camseq 路径点存在 `camLines` 文本里）必须用同步控件——
  `PopupString` / `EditorGUI.Popup`，否则选了不生效还顺手往文档里塞个野参数。
- **`PopupString` 的同步契约是硬的，别顺手改成回调写值**：它现在内部弹的是搜索窗
  （`VNSearchPopup`），但选中值只放进 `_popupResults[key]`，**由下一帧的 `PopupString`
  同步 return 给调用方**，调用方仍旧自己写回。因为它的三个调用点里有两个值不在
  `values`：camseq 路径点（写 `wp.point`）、choice 选项行（写 `VNChoiceOptionRow`）。
  给参数格加新控件时照这个模式走。
- **右键交互：按钮照画不误**。`GUI.Button` 只吃左键，右键要自己 `Use()`；但**不能因此
  跳过按钮的绘制调用**——IMGUI 控件 id 按调用顺序分配，少画一个控件会让同一帧后面的
  控件全部错位。先收右键、照常调按钮、再处理右键动作。
- **弹窗回调里不能直接改 `_doc.rows` 长度**（回调跑在另一个窗口的 GUI 里）：
  攒进 `_pendingNewRow` / `_pendingInsertAt`，留到下一个 Layout 事件由
  `ApplyPendingNewRow` / `ApplyPendingInsert` 落地。
- **UI 行号 ≠ 物理行号**：换算一律走 `SourceLineForRow`——choice 选项行和 camseq waypoint
  都额外占物理行；空行/注释从下一条有效命令启动。反向换算（物理行 → UI 行）走
  `RowForSourceLine`，两个函数的跨行规则必须保持一致，改一个就得改另一个。
- **`VNRowKind.Raw` 不等于「注释或空行」**：它还兜着前面没有 choice 的孤儿 `*`
  选项行、前面没有 camseq 的孤儿 `>` 路径点行。任何「批量处理 Raw 行」的功能
  （隐藏、折叠、清理）都必须先用 `IsHiddenRow` 那套判定筛出真正的空行与 `#` 注释，
  否则那两种语法残留会人间蒸发。
- **隐藏行走「高度归零」不走「过滤列表」**：`_list` 绑的是 `_doc.rows` 本体，
  让 `RowHeight` 返回 0 就够了，索引保持不变，行号换算/多选/拖动/删除全部零改动。
  换成过滤视图就要处理"拖到隐藏行之间算什么"，不值得。
- **窗口状态默认活不过域重载**：进/出 Play Mode、脚本重编译都会重建窗口，
  普通字段一律清空。任何"关掉/重编译也不该丢"的状态都要
  ①加 `[SerializeField]` ②在 `OnBeforeSerialize` 里写入 ③在 `OnEnable`
  （`RestoreAfterDomainReload`）里还原——**只加字段没用**。
  `_doc` 本体走 `_docText` 文本中转；`DateTime` 之类不可序列化的存 ticks。
  `OnBeforeSerialize` 里只能做纯 C# 运算，`OnAfterDeserialize` 里不能碰 Unity API。
- **Sprite 缩略图必须按 `textureRect` 画 UV**——图集里的 sprite 不能整张 texture 当缩略图。
- 纯外观偏好（如分类颜色）存 **EditorPrefs**，**不能因此把剧本文档标脏**。
- 枚举（transition/emote）显示中英对照，但写进剧本的值保持英文。

## 播放 / 热重载时序
- **播放路径只有一个入口** `PlayFromSourceLine(int sourceLine)`：
  Issues 校验 → `AutoSaveBeforePlay()` → 按是否在 Play Mode 分流。
  新加的播放按钮/菜单项都接它，别另起炉灶。
- **Play Mode 中走热重播**（`HotReplay`）：直接
  `runner.PlayFromSourceLine(内存文本, 行, rebuildState)`，
  不退出 Play Mode、不触发域重载。前面先 `runner.SetDebugScript(资产)`，
  否则翻译查表和 `chapter` / 跨文件 `jump` 会按旧的当前文件算。
- **编辑期走冷启动 Bridge**：`SessionState` 传 `_doc.GenerateText()`、目标行、
  rebuild 标志、资产路径后进 Play；未保存文本也能调试；**请求消费后必须清除**。
  Bridge 必须等 `VNScriptRunner.IsInitialized`，否则 Runner 的 Start/playOnStart
  会在调试启动后再覆盖一次播放位置。
- **自动保存有两个例外**：未命名文档（无路径）不弹保存框，直接播内存文本；
  `_externalChanged` 时不写盘，避免静默覆盖别处的改动。
- **跟随高亮用 `OnInspectorUpdate` 轮询**（10Hz）读 `runner.CurrentLine`，
  不给运行时加事件——省掉域重载前后订阅/退订的生命周期坑。
  只在 `runner.CurrentScriptName` 与打开的文件同名时才高亮，跨文件跳转后行号不通用。
- **暂停是命令级的**（闸门在 Runner `Run()` 主循环顶部），
  已经跑起来的 DOTween 补间和打字机不会冻结；别想着用 `Time.timeScale = 0` 代替。
- **`ShortcutManager` 不接受 `Return`/`Enter` 当绑定键**（会被忽略并刷警告）；
  Enter 系快捷键只能在 IMGUI 里自己收，且要排在 `HandleInsertKeys` 之前。
- 运行时入口：`VNScriptRunner.PlayFromSourceLine(source, line, rebuildState)`；
  重建逻辑本身见 [vn-save-compat] 与 [vn-debug]。

## 打字搜索（VNCommandSearch.cs）
- 四件套：`VNSearchItem`（候选）/ `VNSearchListView`（搜索框+列表+键盘）/
  `VNSearchPopup`（通用弹窗）/ `VNCommandPalette`（Ctrl+E 向导面板）。
- **键盘处理必须排在 `EditorGUI.TextField` 之前**：文本框会把 ↑↓ 拿去移光标、
  把 Enter 当「结束编辑」吃掉，先 `Event.current.Use()` 才轮得到候选列表。
- 命令候选表从 `VNScenarioSchema.Commands` 现场生成，**加新命令不用回来登记**。
- 匹配是刻意的子串包含（`VNSearchItem.Matches`），要改成模糊/拼音只动那一个方法。

## 新行类型 / 新参数
- 走 [vn-new-command] 清单第 5~6 步（Schema + CommandTranslations + VNParamSource 三处）。
- 背景预览来源 = 当前场景 `VNStage.backgrounds`；角色/表情来源 = VNCharacterDef 资产；
  改了来源记得 `Refresh Sources` 重建缓存逻辑仍然成立。

## 权威参考
- CLAUDE.md「剧本可视化编辑器」节；WhatAiDo.md 三十一/三十二（主体）、
  六十~六十二（试听/舞台一览/多选）、九十六（热重载调试与域重载存活）
- ProjectCodeGuide.md 九（编辑器工具）
