# AE: Beyond Dimensions · 更新日志 / Changelog

## AE 0.1.26

### 简体中文

搭配《更好的体验》（ImproveGame）使用 AE 磁卡时，开启自动收纳的旗帜盒和药水袋会优先接收对应物品。即使容器没有空格，只要已有物品还能堆叠，就可以继续收纳；剩余物品按磁卡的拾取设置处理。

这套优先顺序也适用于领取开袋奖励和用魔杖拆箱时取出的物品。多人游戏中的团队终端现在也会考虑玩家容器的剩余空间。

### English (machine translation)

When using the AE Magnet Card with ImproveGame (Quality of Terraria), the Banner Chest and Potion Bag take priority for matching items if auto-storage is enabled. Even with no empty slots, a container can still top up an existing stack. Any remainder follows your Magnet Card pickup settings.

The same priority applies when claiming bag rewards or breaking a chest with the wand. In multiplayer, the team terminal also takes the available space in each player's containers into account.

## AE 0.1.25

### 简体中文

终端仓库和待合成订单的数量改用原版字体，在格子右下角按宽度缩放。1000 以内显示完整数字，更大的数量使用 K、M 等缩写，例如 1500 显示为 1.5K。合成状态按钮只在有待处理订单时显示数量。

在 Windows 上点进终端搜索框时默认使用英文直输。需要中文时可以照常切换输入法，系统候选窗会跟随光标，离开搜索框时恢复原输入模式。

### English (machine translation)

Storage slots and pending crafting orders now show counts in the vanilla font, sized to fit the lower-right corner of each slot. Counts up to 1,000 are shown in full. Larger counts use K, M and other abbreviations, so 1,500 appears as 1.5K. The crafting status button only shows a count when orders are pending.

On Windows, the terminal search field starts in direct English input mode. You can switch back to Chinese while typing. The system IME candidate window follows the caret, and leaving the field restores the previous input mode.

## 0.1.21（含 0.1.20）

### 简体中文

- 改善终端文字在不同界面缩放下的清晰度。仓库格和订单格按可用宽度显示数量，有多个词缀变体的装备格会为展开标记留出空间。
- 中文输入法组字时，终端搜索框会显示尚未确认的文字，候选栏显示在搜索框旁。方向键用于选词，确认后才更新搜索。
- 大型合成树跳过可视区域外的分支绘制，并保留穿过视口的连线。终端指南调整了标题、行距和换行，让窄窗口里的说明更易阅读。

### English (machine translation)

- Improved terminal text clarity at different UI scales. Stack counts fit the available width in storage and order slots. Equipment with multiple prefixes leaves room for the expand indicator.
- The terminal search field displays Chinese IME composition text, with the candidate list beside the field. Arrow keys select candidates, and the search updates when you confirm the text.
- Large crafting trees skip off-screen branches while retaining connectors that cross the viewport. The terminal guide uses revised headings, line spacing, and wrapping for narrower windows.

## 0.1.19

### 简体中文

- ME 控制器新增信号塔（6×12 格）、中继桥（14×6 格）和通量环（10×10 格）。三款均按 AE2 控制器的连接规则排布，加上大型和紧凑型，共有五种造型。
- 在「模组配置 → 通用配置 → ME 控制器结构」中点击预览图并保存，即可选择下一台控制器的造型。每张图标有占地尺寸。已放置的控制器保留原样，旧版大小开关会自动迁移到对应造型。
- 补全工作台的英文缺料提示。悬停材料格可查看缺少的数量，悬停成品格可查看蓝底和红底的含义。
- 补全物品详情中的英文 Shift 提示。磁卡的中英文说明现在会介绍掉落物吸引和拾取去向，并修正了配置中磁力拾取范围的名称与说明。

### English

- Added three ME Controller designs: Signal Tower (6×12 tiles), Relay Bridge (14×6), and Flux Ring (10×10). All three follow AE2’s controller connection rules. Together with Large and Compact, there are now five designs.
- Open **Mod Configuration → General Settings → ME Controller design**, select a preview, and save. Each picture shows the footprint of the next controller you place. Existing controllers retain their shape, and your previous Large or Compact setting carries over automatically.
- Added English workbench tooltips for missing ingredients. Hover over an ingredient to see how many more you need, or over the output to find out what the blue and red slot backgrounds mean.
- Added the English “Hold Shift” hint for full item details. The Magnet Card tooltip now explains item attraction and pickup destinations in both languages. Fixed the magnet pickup range label and description in the mod settings.
