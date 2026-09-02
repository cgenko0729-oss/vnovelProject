# 大头贴素材 AI 生成指南（Nano Banana Pro）

给拍大头照事件模块（`event photo`）生成**贴纸 / 边框 / 背景布**用的 prompt 库。
立绘漫符（`mark` 命令）的素材 prompt 在 `MarkAssetPrompts.md`。
风格定为**日系大头贴亮片风**，透明底走**纯洋红抠图**，贴纸走**九宫格 sheet 后期切**。

---

## 一、技术规格（先看这个，出图前必须知道）

| 素材 | 代码里怎么用 | 出图规格 | 存放建议 |
|---|---|---|---|
| **贴纸** `VNPhotoStickerDef.sprite`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoStickerDef.cs:43`](Assets/Project/Scripts/VNEffects/Script/VNPhotoStickerDef.cs#L43)） | `preserveAspect = true`，装进正方框内 fit。游戏内初始边长 `defaultSize`（24~400，默认 96），玩家可滚轮缩放 0.2~4 倍 | **1:1 九宫格 sheet**，切出来每张 **1024×1024** 透明 PNG | `Assets/Art/Photo/Stickers/` |
| **边框底图** `VNPhotoFrameDef.frameSprite`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoFrameDef.cs:68`](Assets/Project/Scripts/VNEffects/Script/VNPhotoFrameDef.cs#L68)） | **拉伸铺满**取景框 880×660（4:3），比例不对会变形 | **1760×1320（4:3）** PNG | `Assets/Art/Photo/Frames/` |
| **边框前景** `frontSprite` | 同上，压在人物上方 | 1760×1320（4:3），**大面积透明** | 同上 |
| **背景布** `VNPhotoBackdropDef.sprite`（[`Assets/Project/Scripts/VNEffects/Script/VNPhotoBackdropDef.cs:44`](Assets/Project/Scripts/VNEffects/Script/VNPhotoBackdropDef.cs#L44)） | 真 **cover** 铺满开窗（宁可裁边也不变形、不留白） | **1200×900** 不透明 PNG（**不用抠图**） | `Assets/Art/Photo/Backdrops/` |

### 三个必须记住的坑

1. **边框中央是要被盖住的**。开窗默认占取景框 `maskSize = 0.72 × 0.82`（椭圆），
   中间那块会被背景布 + 立绘完全遮住。所以边框底图的装饰**只能活在外圈和四角**，
   上下边距只剩约 9%（≈118px），左右各 14%（≈246px）。
   → 想让边框装饰有发挥空间，把资产里的 `maskSize` 调到 `0.62 × 0.72` 会舒服很多。
2. **`tint` 会乘上去**。贴纸资产的 `tint` 默认白色 = 原样；用自定义 sprite 时
   **务必确认 tint 是纯白 (1,1,1,1)**，否则整张图会被染色。
   （反过来：如果你想让一张白色线稿贴纸出 5 种颜色，就做 1 张白图 + 5 个资产各配 tint。）
3. **背景布不需要透明**，别浪费 prompt 写抠图要求；它是照相馆背景布，满幅铺就对了。

---

## 二、工作流（照着做）

```
1. 在 Nano Banana Pro 里发【风格宪章】那条 prompt（只发一次，定住风格）
2. 逐批发【批次 prompt】，每批出一张 3×3 = 9 个物件的洋红底 sheet
3. 出图后上传回对话，说 "use this as style reference" —— 后续批次风格才咬得住
4. 抠洋红 → 切 9 张 → 命名 → 丢进 Assets/Art/Photo/Stickers/
5. Unity 里建 VNPhotoStickerDef 资产，配 sprite + defaultSize + tint 纯白
6. 登记进 VNGameConfig 的「照片贴纸」区
```

> **换新对话要重发风格宪章**，否则风格会漂。

### 底色怎么选（无白描边后这条变重要了）

物件没有白包边，边缘的抗锯齿像素会直接和底色混，抠完容易挂一圈残色。
**底色要选这批物件里绝对不出现的色相**：

| 这批物件主色 | 用什么底 |
|---|---|
| 粉 / 洋红 / 紫（A、C、D、F 批大多是） | **纯绿 #00FF00** |
| 绿 / 薄荷 / 青（E 批的薄荷相机、G 批的竹子） | **洋红 #FF00FF** |
| 什么色都有、拿不准 | 拆成两张 sheet 分批出，别硬凑一张 |

---

## 三、【风格宪章】开场 prompt（每个新对话发一次）

```
You are generating game asset sheets for a Japanese visual novel purikura
(photo booth) minigame. Lock this art style for every image in this conversation:

STYLE:
Japanese purikura decoration sticker art. Flat cel-shaded anime style.
NO white die-cut sticker border. Objects must NOT have any white outline,
white halo, or cut-out edge around them. Each object carries only its own thin
dark line art — a clean 2-3px anime interior outline in a deep tone of the
object's own color — sitting ON the object, never around it.
Glossy candy highlights, tiny 4-point sparkle accents, soft inner glow.
Pastel candy palette: sakura pink #FFB7CE, mint #A8E6CF, lavender #C9B6E4,
butter yellow #FFE9A8, sky #AEDFF7, with saturated accents.
Crisp vector-like edges, no photorealism, no heavy gradients, no texture noise.
Cute, glossy, girly, early-2000s Japanese sticker-machine energy.

FRAMING:
Each object is a standalone icon, front-facing or slight 3/4 view, centered in
its own cell with generous margin, fully inside the cell, never overlapping
another cell, never cropped by the canvas edge.

BACKGROUND (critical):
Solid flat pure green #00FF00, perfectly uniform. No gradient, no vignette,
no texture, no pattern. Objects cast NO shadow onto the background.
Object edges must be crisp and hard against the green so the sheet can be
color-keyed cleanly — no green bleeding into the artwork, no semi-transparent
glow or soft halo spilling onto the green.

FORBIDDEN in every image:
no grid lines, no cell borders, no text, no letters, no numbers, no captions,
no watermark, no signature, no drop shadows, no background scenery,
no human faces, no hands holding the objects.

LAYOUT: 3x3 grid, 9 objects, evenly spaced, equal spacing, square 1:1 canvas.

Reply "ready" and wait for my object list.
```

---

## 四、贴纸批次 prompt（宪章发完后，一批一条）

每条直接复制发送即可。**建议顺序**：先发 A 批，出图满意后上传回去说
`use this image as the style reference for all following sheets`，再往下发。

### 批次 A — 头戴道具（最优先，猫耳在这批）

```
Sheet A — head accessories. 3x3 grid, 9 objects, same locked style, green #00FF00 background:
1. cat ears headband, pink inner ears
2. bunny ears headband, white with pink inner
3. bear ears headband, brown round ears
4. small golden princess crown with pink gems
5. big pink ribbon bow hair clip
6. flower crown of daisies and pink blossoms
7. glowing golden angel halo ring
8. small red devil horns
9. white beret hat with a pink pompom
```

### 批次 B — 脸部道具（墨镜在这批）

```
Sheet B — face accessories. 3x3 grid, 9 objects, same locked style, green #00FF00 background:
1. star-shaped sunglasses, pink lenses
2. heart-shaped sunglasses, red lenses
3. round nerd glasses, thin black frame, white lens shine
4. black classic sunglasses, glossy
5. white surgical face mask
6. curly gentleman mustache, black
7. novelty disguise glasses with big nose
8. cat whiskers and cat nose set, black lines with pink nose
9. pair of round pink blush cheek patches
```

### 批次 C — 漫符 / 情绪符号

```
Sheet C — emotion marks. 3x3 grid, 9 objects, same locked style, green #00FF00 background:
1. glossy red heart
2. two small pink hearts, overlapping
3. glossy yellow five-point star
4. white 4-point sparkle burst
5. blue anime sweat drop
6. red anger cross-popping vein mark
7. bold yellow exclamation mark
8. bold pink question mark
9. pair of purple music notes
```

> C 批和现有程序化贴纸（爱心/星星/闪光/音符）重名，属于**升级替换**：
> 直接把新 sprite 填进 `Assets/VNEffects/Photo/Stickers/*.asset` 的 `sprite` 槽即可，
> 主题评分表里的 `stickerId` 不用改，剧本也不用动。

### 批次 D — 对话泡 / 文字条（**故意不带字**）

```
Sheet D — empty speech bubbles and banners, ALL COMPLETELY BLANK INSIDE,
absolutely no text or letters. 3x3 grid, 9 objects, same locked style, green #00FF00 background:
1. round speech bubble, white with pink outline, empty inside
2. spiky explosion shout bubble, yellow, empty inside
3. cloud-shaped thought bubble, white, empty inside
4. pink ribbon banner scroll, blank
5. rectangular label tag with pink border, blank
6. strip of washi tape, pastel striped, blank
7. yellow sticky note, slightly curled corner, blank
8. heart-shaped sign board, blank
9. star-shaped sign board, blank
```

### 批次 E — 手持小道具

```
Sheet E — cute handheld props. 3x3 grid, 9 objects, same locked style, green #00FF00 background:
1. pink handheld microphone
2. strawberry ice cream cone
3. swirl lollipop, pink and white
4. takeaway drink cup with straw and heart on it
5. retro instant camera, mint green
6. pink heart balloon on a string
7. Japanese folding paper fan, sakura pattern
8. badminton racket with a white shuttlecock
9. cute bento box with rice ball and sausage
```

### 批次 F — 边角装饰件（配边框用）

```
Sheet F — corner decoration pieces. 3x3 grid, 9 objects, same locked style, green #00FF00 background:
1. cluster of sakura petals
2. curled pink satin ribbon
3. white lace doily corner piece
4. string of small yellow stars
5. string of white pearls
6. confetti burst, pastel colors
7. sakura branch with blossoms
8. fluffy white cloud
9. pastel butterfly with glitter wings
```

### 批次 G — 季节 / 节日

```
Sheet G — seasonal items. 3x3 grid, 9 objects, same locked style, green #00FF00 background:
1. red santa hat
2. jack-o-lantern pumpkin
3. colorful firework burst
4. Japanese wagasa paper umbrella, red
5. round uchiwa summer fan with goldfish pattern
6. white snowflake crystal
7. red maple leaf
8. koinobori carp streamer, blue and red
9. kadomatsu new year bamboo decoration
```

---

## 五、边框底图 prompt（4:3，中央必须空）

**每张单独出，比例选 4:3**。中央那块会被立绘和背景布盖掉，写清楚要它留空：

```
Purikura photo frame border, 4:3 landscape canvas, decorative frame ONLY.

CRITICAL COMPOSITION RULE: the central area — an ellipse covering 72% of the
width and 82% of the height, centered — must be COMPLETELY EMPTY: flat solid
pale cream color, absolutely no decoration, no pattern, no object, nothing.
It will be covered by characters at runtime. ALL ornament must live in the
outer margin band and especially in the FOUR CORNERS, which are the widest
usable space because the empty area is an ellipse.

STYLE: <<在这里写主题，见下面几行>>
Flat cel-shaded kawaii Japanese sticker style, thick clean outlines, glossy
highlights, pastel palette, crisp vector edges.
No text, no letters, no watermark, no human figures, no photo inside the frame.
```

主题行可用（一次一个，替换 `<<...>>`）：

- `pink gingham check pattern border with small hearts and lace trim in the corners`
- `night sky border, deep navy with gold stars, crescent moon in the top-left corner, shooting stars`
- `retro film strip border, black with sprocket holes on top and bottom edges`
- `sakura border, soft cream base with cherry blossom branches sweeping in from the top-left and bottom-right corners`
- `summer festival border, deep indigo with fireworks in the corners and goldfish swimming along the bottom edge`
- `school notebook border, pale blue ruled paper with washi tape corners and doodles`

### 前景层 `frontSprite`（可选，压在人物上）

```
Purikura frame FOREGROUND overlay layer, 4:3 landscape canvas.
Background is solid flat pure green #00FF00, perfectly uniform — the ENTIRE image
must be green except for a few decorative elements at the very edges:
a pink ribbon banner along the bottom edge, small sparkles in the top corners,
and a soft glitter shimmer along the top edge only.
The whole center is pure untouched green. No shadow onto the green.
Flat cel-shaded kawaii sticker style, NO white die-cut outline — only thin dark
interior line art. No text, no letters.
```

---

## 六、背景布 prompt（1200×900，**不抠图**）

背景是照相馆的背景布，**满幅平铺、无主体、中心别太抢**（人站中间）。
比例做 4:3，代码按 cover 铺，会裁掉少量边缘 —— **别把关键图案压在边上**。

```
Japanese photo booth studio backdrop, 4:3 landscape, seamless full-bleed pattern,
flat vector illustration, kawaii pastel palette, no characters, no objects in the
center, no text, no watermark, no border, no frame. The composition must stay
visually calm in the center so people photographed in front of it stand out;
put the visual interest toward the edges. Even lighting, no vignette.
PATTERN: <<在这里写图案，见下面几行>>
```

图案行可用：

- `classic manga radial speed lines bursting from the center, hot pink on white`（大头贴机最经典的底）
- `large pastel polka dots on mint green`
- `diagonal candy stripes, pink and cream`
- `starry night sky, navy blue with golden stars and a soft milky way`
- `soft rainbow gradient with fluffy clouds`
- `warm sunset gradient, orange to purple, with silhouetted birds near the top edge`
- `golden bokeh light circles on a warm brown blur`
- `sakura petals falling over a pale pink gradient`
- `school classroom blackboard with faint chalk doodles`
- `summer festival night, paper lanterns strung across the top`

---

## 七、后期处理

### 抠洋红

> 物件**没有白包边**，所以抠图参数要保守：**容差往小调（12~18），去边必做 2px**。
> 容差开大会直接啃掉粉色/浅色物件的边缘。

| 工具 | 做法 |
|---|---|
| Photopea（免费网页版，最省事） | 选择 → 色彩范围 → 吸底色 → **容差 12~18** → Delete → 图层 → 修边 → **去边（Defringe）2px** |
| Photoshop | 同上 |
| GIMP | 颜色 → 颜色到 Alpha → 选底色（这个算法自带去溢色，无白边时表现最好） |
| 命令行批量 | 见下方 ImageMagick |

绿底：

```bash
magick in.png -fuzz 10% -transparent "#00FF00" \
  -channel A -blur 0x0.5 -level 40%,60% +channel out.png
```

洋红底把 `#00FF00` 换成 `#FF00FF` 即可。后面 `-channel A -blur -level` 那段是**收 alpha 边**，
把半透明的残色像素推成全透明或全不透明，无白描边时这步基本等价于 Defringe。

**去边那一步别省** —— 不做的话贴纸边缘会挂一圈绿/紫麻边，
在游戏里叠到浅色立绘上非常显眼。

### 切九宫格

ImageMagick 一行切（1024 是切完每格的边长，按实际 sheet 尺寸调）：

```bash
magick sheetA.png -crop 3x3@ +repage +adjoin stickerA_%d.png
```

### Unity 导入设置

| 项 | 值 |
|---|---|
| Texture Type | **Sprite (2D and UI)** |
| Sprite Mode | Single |
| Alpha Is Transparency | **✔ 勾上**（不勾边缘会发黑） |
| Mesh Type | Full Rect |
| Max Size | 贴纸 512 够用；边框 2048 |
| Compression | 贴纸建议 High Quality 或不压（图小，边缘干净更重要） |
| Generate Mip Maps | ✘ 关（UI 不需要，开了会糊） |

### 建资产时的 `defaultSize` 建议

`defaultSize` 是贴到照片上的初始边长（取景框是 880×660）。分档给：

| 类别 | 建议值 | 理由 |
|---|---|---|
| 头戴道具（猫耳/兔耳/皇冠/帽子） | **200 ~ 240** | 要能盖住一个人的头 |
| 脸部道具（墨镜/口罩/胡子） | **120 ~ 150** | 只盖脸的一部分 |
| 手持小道具 | **130 ~ 170** | — |
| 漫符 / 情绪符号 | **80 ~ 110** | 点缀用，大了抢戏 |
| 对话泡 / 横幅 | **180 ~ 260** | 要装得下想象中的字 |
| 边角装饰件 | **100 ~ 160** | — |

配好后记得登记进 **VNGameConfig → 照片贴纸** 区，
想让某贴纸能加分，再去 `Assets/VNEffects/Photo/Themes/*.asset` 的贴纸加分清单里挂上它。

---

## 八、常见翻车与对策

| 症状 | 原因 | 对策 |
|---|---|---|
| 出图带了网格线 / 编号 | 模型爱给 sheet 加标注 | prompt 里的 `no grid lines, no numbers` 保留；仍出现就回一句 `regenerate without any grid lines or numbering` |
| **物件带了厚白包边** | 模型默认按 die-cut 贴纸画 | 回 `remove the thick white die-cut outline entirely — no white border, no white halo around the objects; keep only thin dark interior line art` |
| 背景不是纯色（带渐变/阴影） | 模型自作主张打光 | 回 `the background must be 100% flat uniform #00FF00 with zero shading and no cast shadows` |
| 抠完边缘一圈绿边/紫边 | 没做去边，或容差开太大 | 去边 2px；容差**往小调**到 12~18，别往大调 |
| 抠完粉色物件缺了一圈 | 底色和物件色相太近 | 换底色（粉色物件用绿底），重出这一批 |
| 第 3 批开始风格漂了 | 上下文里没有视觉锚点 | 把前面满意的 sheet 上传回去，写 `match this sheet's outline weight, palette and gloss exactly` |
| 贴纸进游戏后被拉扁 | 图不是正方形 | 代码 `preserveAspect=true` 不会变形，但非正方形会浪费尺寸；切图时补成正方形画布 |
| 贴纸进游戏后整张变色 | 资产 `tint` 不是白色 | 把 `tint` 改成 (1,1,1,1) |
| 边框装饰被人物盖掉大半 | 装饰画进了中央开窗区 | 出图时守住「中央 72%×82% 留空」；或把资产 `maskSize` 调到 0.62×0.72 |
| 背景布关键图案被裁掉 | cover 铺法会裁边 | 关键元素往中心靠，边缘只放可牺牲的纹理 |
