# UI 约定

- 页面：`PageBase`；导航参数 `GameId` / `GameBiz`
- 亚克力背景；动画优先 Composition（`Helpers/FluentAnimations.cs`），少用 Storyboard
- `DropDownButton` 弹出层无法亚克力，不要强行改造
- 悬停说明优先 `InstantTooltip` / `InstantTooltipHost`
- **不要升级** `CommunityToolkit.WinUI.Controls.Segmented`（csproj 有回归说明）

## 页面动画与外观：先分清作用域

修改**某个页面**的动画效果或外观时，**先判断本次改动是否为全局改动**，以及是否会波及其他页面的视觉效果：

- **共享层**（改了即全局）：`FluentAnimations` / 通用动画 helper、App 级或资源字典中的 Style/Template、`PageBase` 与导航壳层、全局主题/画刷/字体等——改一处会影响所有调用方或继承页
- **页面局部**：仅该页 `.xaml` / code-behind、页面私有资源、未共享的控件实例属性——作用域通常限于当前页
- 为单页调参时，**优先局部实现**；确需改共享层时，明确列出受影响页面/控件，并手测相关页（含不同游戏、暗亮主题若适用），避免「修好一页、拖垮别处」

## 复杂控件：命中测试与输入路由

视觉树层级较深（叠层、装饰、遮罩、Popup、自定义模板、Composition 视觉）时，**不能只看效果对不对**，还要验证输入是否落到正确元素：

- **命中测试（hit test）**：透明/半透明装饰层、全屏叠层、未设 `IsHitTestVisible="False"` 的背景是否挡住下方按钮/列表；可点区域是否与视觉反馈一致
- **输入路由**：指针/触摸/键盘事件的捕获、冒泡与隧道是否被中间层截断；焦点与键盘导航是否仍可达；滚动容器与内嵌可点控件是否争抢手势
- 改模板或加装饰层后，至少手测：点击、悬停、拖拽、滚轮、Tab 焦点、以及叠层关闭后下层是否仍可交互

## 可视化控件：先查文档与参考实现

设计或重写**外观向**控件（自定义 ControlTemplate、视觉状态、Composition 动画、非标准布局）时，**先对齐正确做法再写代码**，避免凭直觉堆节点导致命中/无障碍/主题/性能问题：

1. **官方文档**：WinUI 3 / Windows App SDK（控件模板、视觉状态、Composition、输入与焦点）— [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
2. **社区文档与组件**：Community Toolkit for Windows 等已验证模式
3. **开源参考**：上游/同类 WinUI 应用与控件库中的成熟实现（模板结构、hit-test 边界、动画接入点），对齐后再适配本仓风格

本仓已有模式优先复用（如 `FluentAnimations`、`InstantTooltip`）；新模式应能说清「参考了何处、为何这样分层」。
