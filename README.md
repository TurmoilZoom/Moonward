## 在Starward中加入WinUI3动画
> WinUI3参考文档
> - 开发文档：https://learn.microsoft.com/en-us/windows/apps/get-started/start-here?tabs=wingetconfig
> - Claude中可用的插件：winui@win-dev-skills
> 
> 开源组件：https://github.com/CommunityToolkit/Windows

### 亚克力风格

🎈目前Starward的主要风格是亚克力，排除一些下拉按钮（DropDownButton，因为弹出的列表可能溢出整个软件，导致采样不到底层的图片，做不了亚克力效果，所以微软把他们做成了单独的视觉树，也就是说，就算我强制对它进行修改，它也会回退成纯色），可以结合WinUI3的动画，加上少量的编码，就能渲染出不错的效果~



### 两种动画路线：Xaml动画（Storyboard）还是Composition(Microsoft.UI.Composition)

🙌最直观的区别就是，Composition已经开始借助显卡的力量了：Composition方式不在UI线程上运作，而是单独的合成线程。后端写多了的话UI线程简直反直觉，因为UI组件就是一个自由的设计，它不线程安全，一碰就碎，因为这个问题，产生了很多代价，包括DispatcherQueue这个用来强制使用UI线程操作的机制，还有ConfigureAwait(false)来控制异步操作之后不要再使用UI线程了，这种欲拒还迎的戏码占据了开发者很多的精力。所以投入Composition的怀抱吧，未来是你的~

<p align="center">
🌟 ───────────────────────────── 🌟
</p>


<h3 align="center">修了什么</h2>

> 日常使用过程中遇到的问题，当然有些并不算修，可能就是设计如此

:memo:「设置」-「游戏截图」中，使用滚轮拖动滚动条间歇性失灵。

🙌「自定义背景」开启之后，下方的「选择」按钮和其它组件的颜色风格还是旧的。

🎈「祈愿记录」，跳转到其他页面再返回，此时若系统分辨率发生变化，崩溃~ 顺带看了一下，这个页面有内存泄露

😴「祈愿记录」，导入UIGF记录之后，页面为空，你来回跳转刷新的样子真狼狈。

😍「图库」删除图片，首页下侧指示器不更新。

😎  开启自定义背景功能的情况下，切换到官方壁纸，在系统分辨率变化、软件重启、切换游戏之后，自动切回自定义图片。我换个壁纸真的要去设置里点开关嘛，555

😊「祈愿记录」，tab标签其实是两个控件叠在了一起，这里隐藏了没用的那个。

😋「隐藏卡池」迁移到「筛选卡池」，给了一点额外的能力，全选、反选~

<p align="center">
🌟 ───────────────────────────── 🌟
</p>

<h3 align="center">新增了什么</h2>

🎶切换语言时，将切换卡池导入记录中条目的语言

🤩记住用户使用壁纸类型：官方视频、官方海报、自定义

:star:<img width="173" height="18" alt="image-20260607162736674" src="https://github.com/user-attachments/assets/c9bc7672-1bfd-4d1f-b372-b1dc5c47fd17" />
首页下侧新增图钉，妈妈再也不用担心我不知道这里有这么多功能啦

🤞winUI3怎么能少了动画：

https://github.com/user-attachments/assets/00da1f4b-d713-4728-8719-492c46c609f9

[output.webm](https://github.com/user-attachments/assets/4796104c-ac6a-4b44-9824-d958587f4df2)










🤦‍♂️「首页」「开始游戏」按钮添加了呼吸灯、聚光灯、流光效果，单纯想看看winUI行不行。

😒「拖拽卡池」，灵活调整位置

[output.webm](https://github.com/user-attachments/assets/5cdbba7d-4b89-4ebf-ad51-7bd9423abae4)






