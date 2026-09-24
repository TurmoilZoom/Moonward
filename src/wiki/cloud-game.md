# 云游戏签到：每日免费时长

云·原神、云·绝区零（国服）的「签到」并没有签到接口，实际是**每天登录一次云游戏，服务端发放当日免费时长**。本页讲清这件事的接口与凭证、为什么不能用米游社账号自动换票、Moonward 里是怎么实现的，以及想脱离客户端自建自动化时有哪些选择。

> 本文对齐当前代码（`rebase/develop`，云游戏功能见 `src/Starward.Core/CloudGame/` 与 `src/Starward/Features/CloudGame/`）。接口结论来自 2026-09-22（云·绝区零）、2026-09-23（云·原神）两次 PC 客户端抓包实测，抓包方法见 [怎么抓Windows客户端的包](MITM-WINDOWS)。

---

## 一、先说结论

| 问题 | 结论 |
|------|------|
| 有没有「签到」接口 | **没有**。领时长的动作就是调一次云游戏登录接口 `gamer/api/login` |
| 发放规则 | 每日登录领 **15 分钟**，累积上限 **600 分钟（10 小时）**，**UTC+8 4:00** 刷新（不是零点） |
| 查钱包能不能顺带领 | **不能当成领取手段**。两次抓包里 `send_freetime` 始终为 0，云·原神的到账时间晚于首次钱包查询，说明发放由登录触发 |
| 鉴权靠什么 | 只有一个请求头 `x-rpc-combo_token`，没有 DS 签名、不需要设备 ID 与客户端版本号 |
| 凭证能不能用米游社账号换 | **不能**。stoken / auth ticket 三条路线实测全被挡（见第三节），凭证只能来自本机云游戏客户端 |
| 那 Moonward 怎么拿到凭证 | 读云游戏客户端自己写的 SDK 日志（见第四节），用户不必抓包 |

---

## 二、接口与凭证

### 2.1 两个接口

| 用途 | 方法与路径 | 说明 |
|------|------------|------|
| 登录云游戏（=领时长） | `POST {ApiBaseUrl}/gamer/api/login` | 请求体固定 `{}`，账号信息全在凭证里；返回 `new_user`，内容本身没用 |
| 查钱包 | `GET {ApiBaseUrl}/wallet/wallet/get?cost_method=COST_METHOD_UNSPECIFIED&get_type=GET_TYPE_DEFAULT` | 免费时长、付费货币、畅玩卡 |

官方客户端每次启动都是**先登录再查钱包**，自动领取照抄这个顺序即可。

### 2.2 各游戏的主机与业务标识

| 区服 | `ApiBaseUrl` | `x-rpc-cg_game_biz` | `x-rpc-op_biz` | `ai`（App ID） | AppKey |
|------|--------------|---------------------|----------------|----------------|--------|
| 云·原神国服 `hk4e_cn` | `https://api-cloudgame.mihoyo.com/hk4e_cg_cn` | `hk4e_cn` | `clgm_cn` | `4` | `d0d3a7342df2026a70f650b907800111` |
| 云·绝区零国服 `nap_cn` | `https://cg-nap-api.mihoyo.com/nap_cn/cg` | `nap_cn` | `clgm_nap-cn` | `12` | `8844b676f3268c082a56021d9f47a206` |

两者的路径前缀形状不同（`hk4e_cg_cn` vs `nap_cn/cg`），**不能按 biz 推导**，只能一条条记。代码里集中在 `CloudGameApiConfig.FromGameBiz`，给新云游戏开功能就是在这里加一条。

### 2.3 请求头只需四个

```http
x-rpc-combo_token: bi=nap_cn;ai=12;ci=1;ct=<combo_token>;oi=<open_id>;si=<sign>
x-rpc-cg_game_biz: nap_cn
x-rpc-op_biz:      clgm_nap-cn
x-rpc-language:    zh-cn
```

客户端实际会带十几个 `x-rpc-*`（设备 ID、客户端版本、渠道等），**实测不带也能通**。刻意不照抄，免得版本号过期后反而变成失败原因。`x-rpc-language` 只影响服务端下发的文案。

### 2.4 凭证串的结构

| 字段 | 含义 | 来源 |
|------|------|------|
| `bi` | 云游戏业务标识 | 同 `x-rpc-cg_game_biz` |
| `ai` | 游戏 SDK App ID | 原神 4 / 绝区零 12 |
| `ci` | 渠道 ID | 米哈游官服固定 `1` |
| `ct` | **combo_token 本体** | 客户端 SDK 登录后由服务端签发 |
| `oi` | **open_id**，即米哈游通行证账号 ID | 同上 |
| `si` | 防篡改签名 | 本地用 AppKey 算 |

签名算法（`CloudGameClient.ComputeSign`）：

```text
si = HMAC-SHA256(AppKey, "app_id={ai}&channel_id={ci}&combo_token={ct}&open_id={oi}")   // 小写十六进制
```

键值对按字母序拼接，**不参与 URL 编码**。

### 2.5 AppKey 只在拼票阶段用得上

| 步骤 | 请求 | 需要 AppKey | 说明 |
|------|------|:-----------:|------|
| 1. SDK 登录 | `POST /combo/granter/login/v2/login` | 需要 | 算请求体里的 `sign`；这一步**我们走不通**，见第三节 |
| 2. 拼装凭证 | 本地字符串拼接 | 需要 | 算 `si` |
| 3. 登录 / 查钱包 | `gamer/api/login`、`wallet/wallet/get` | **不需要** | 带上第 2 步拼好的静态凭证即可，无签名计算 |

换句话说，**有 AppKey 也做不到免抓包全自动**：AppKey 只能把「已经拿到的」`ct` + `oi` 拼成可用凭证，拿不到 `ct` 本身。

---

## 三、为什么不能用米游社账号自动换凭证

一个很自然的想法是：Moonward 里已经有米游社登录态（stoken），能不能像战绩、签到那样直接换云游戏的票？**2026-09-23 实测三条路线全被挡**：

| 路线 | 结果 | 原因 |
|------|------|------|
| 直接拿米游社 stoken 去云游戏 app_id 下校验 | `-100` | stoken 按签发它的 `x-rpc-app_id` 绑定（米游社域为 `ddxf5dufpuyo`），跨 app_id 无效 |
| SDK 换票 `POST /combo/granter/login/v2/login` | 两个游戏一律 `-114` | 该接口校验的是请求体里的**游戏** app_id（原神 4 / 绝区零 12），补任何请求头都无效 |
| 官方桥接 `POST /mdk/shield/api/loginByAuthTicket` | `-464` | auth ticket 本身能正常签出，但这条桥被服务端挡死 |

**结论：凭证只有「本机云游戏客户端」一个来源。** 用户必须先装上对应的云游戏客户端并登录过一次，查时长与自动领取才有数据可用。这条结论写在 `CloudGameClient.BuildComboToken` 与 `CloudGameClientCredentialProvider` 的注释里，改动前请先看那里。

---

## 四、怎么拿到凭证

### 4.1 读客户端 SDK 日志（Moonward 用的就是这条）

云游戏客户端的 SDK 把 granter 登录的**原始响应直接打进了自己的日志**，一次登录追加一条：

```text
%LocalAppData%\miHoYo\GenshinImpactCloudGame\config\logs\NativeSDK.log      # 云·原神国服
%LocalAppData%\miHoYo\ZenlessZoneZeroCloud\config\logs\NativeSDK.log        # 云·绝区零国服
```

```json
{"combo_id":"0","open_id":"<通行证ID>","combo_token":"<票>", ... }
```

要点：

- 字段顺序 `combo_id` → `open_id` → `combo_token`，两版客户端实测一致，正则按这个顺序匹配。
- 用户在客户端里换过账号，日志里就会有多个 `open_id`：**按 `open_id` 分组、各取最后一条**（最近一次登录），天然支持多通行证。
- 客户端运行时持有写句柄，读取必须允许共享读写（`FileShare.ReadWrite | FileShare.Delete`），否则直接 `IOException`。
- 只读日志、不改客户端任何文件；日志异常膨胀（> 8 MB）时直接放弃，不值得卡住 UI。

### 4.2 抓包

需要核对请求头、确认新游戏的路径形状时才需要抓包。PC 云游戏客户端是 Qt5 原生程序（libcurl + QtWebEngine，不是 Electron），业务层**不认代理环境变量**，要用 mitmproxy 的 `regular` + `local` 组合模式，详见 [怎么抓Windows客户端的包](MITM-WINDOWS)。

---

## 五、Moonward 里的实现

### 5.1 分层

| 层 | 文件 | 职责 |
|----|------|------|
| DTO | `Core/CloudGame/CloudGameWallet.cs`、`CloudGameGamerLoginResult.cs` | 钱包 / 登录响应；服务端把数值写成字符串，统一经 `LenientInt32JsonConverter` 读 |
| JsonContext | `Core/CloudGame/CloudGameJsonContext.cs` | 源生成注册，新 DTO 必须登记 |
| 接口常量 | `Core/CloudGame/CloudGameApiConfig.cs` | 主机、业务头、AppId / AppKey；**给新云游戏开功能 = 加一条** |
| Client | `Core/CloudGame/CloudGameClient.cs` | 登录、查钱包、`BuildComboToken` |
| 凭证读取 | `Features/CloudGame/CloudGameClientCredentialProvider.cs` | 扫客户端 `NativeSDK.log` |
| 业务 | `Features/CloudGame/CloudGameWalletService.cs` | 按账号查钱包并换算成分钟，结果缓存 60 秒 |
| 自动领取 | `Features/CloudGame/AutoCloudGameFreeTimeService.cs`、`CloudGameFreeTimeSchedule.cs` | 常驻循环与日界计算 |
| UI / 开关 | `Features/CloudGame/CloudGameButton.xaml(.cs)`、`GameFeatureConfig.SupportCloudGameWallet` | 弹层交互与按游戏启用 |
| 设置 | `AppConfig.Setting.cs` | 自动领取开关、凭证、已领日期、选中账号 |
| 错误文案 | `Features/MiHoYoApiErrorFeedback.cs`（`MiHoYoApiContext.CloudGame`） | `-100` 提示「去云游戏客户端重新登录」，不是重登本应用 |

### 5.2 界面

首页右侧工具栏的**云游戏**按钮（仅对 `SupportCloudGameWallet` 为 true 的游戏显示），弹层里有：

- 免费时长、付费货币折算时长（「10 个货币 = 1 分钟」，接口没给兑换比例时按此兜底）、畅玩卡剩余
- 多通行证切换菜单（用户在客户端登录过几个就列几个），选中项按游戏记在设置里
- **自动获取免费时长**开关，按游戏区分，打开后立即检查一轮
- 时长区有四种状态：没有可用凭证（引导「请先安装云游戏客户端并登录一次，凭证从客户端读取」）、查询中、显示时长、查询失败

### 5.3 自动领取的调度

结构刻意与自动签到（`AutoSignInService`）保持一致，差别只有两处：日界不是零点、服务端不下发「今天是否已领」。

| 场景 | 行为 |
|------|------|
| 软件启动 | 等自动签到首轮结束（最多等 15 分钟）后再缓冲 10 秒，避开启动高峰 |
| 到日界 | UTC+8 **4:00** 之后再加 **3–8 分钟**随机抖动 |
| 打开开关 | 立即叫醒循环检查一轮 |
| 同一区服多个通行证 | 开了就**全部**领一遍，相邻请求间隔随机 **3–8 秒** |
| 领取失败 | 该游戏 **10 分钟**冷却 |
| 当天没领全（断网等） | **30 分钟**后重试；免费时长整日都能领，不需要「当天 23 点截止」的兜底 |
| 凭证失效 / 风控 | 拉长到 **2 小时** |
| 休眠恢复 | 已到点则先随机错峰 **10–90 秒**；长等待按 15 分钟分段醒来用墙上时钟重算 |

跨日自动领取的前提同样是**进程还在**（主窗口或托盘），完全退出后要等下次启动。

### 5.4 记账方式

签到接口会告诉你「今日是否已签」，云游戏钱包**不会**，服务端日期无从对账。所以：

- 由本机时钟推算日界，成功后记下该账号已领的云游戏日历日（`AppConfig.GetCloudGameFreeTimeClaimedDate`）。免费时长整日都能领、重复登录也不出错，本机时钟略有偏差只会让领取时刻前后挪动，不会漏掉某一天。
- `free_time_limit`（600）用来区分**「领了但没涨」（满仓）**与**「没领到」**，避免满仓时被当成失败反复重试。
- `send_freetime` **不能**当作「今天领没领过」的依据，两次抓包里它始终是 0。

### 5.5 凭证的存储与日志

凭证等同云游戏账号的登录态，因此：

- 只经 `AppConfig` 存在本机数据库的 `Setting` 表（键前缀 `cloud_game_combo_token_`），不上传、不同步。
- 日志只记区服、账号 ID 与时长，**不输出凭证本体**。
- 删除账号时 `DeleteCloudGameComboTokens` 会把该账号的凭证与已领日期一并清掉。

---

## 六、想脱离客户端跑：自建自动化

Moonward 的自动领取依赖软件常驻。如果想「云端每天自己跑」，社区常见三种载体：

| 载体 | 适合谁 | 花钱 | 难点 |
|------|--------|------|------|
| **GitHub Actions**：GitHub 免费提供的临时云端虚拟机，按 Cron 定时开机跑脚本再关机 | 没有服务器、不想花钱 | 免费 | 对仓库与 Secrets 规范要求高；仓库 3 个月无更新会自动停用定时任务（要加保活）；有被判定滥用自动化而封禁的风险 |
| **华为云函数（Serverless）**：按次计费，平时休眠，到点开机几秒跑完就关 | 追求国内直连稳定 | 免费额度内 | 打包依赖与首次配置略繁琐 |
| **青龙面板**：装在自己服务器 / NAS / 软路由上的定时脚本网页管家 | 手上已有长期开机的机器 | 需自备运行环境 | 要有常开宿主机 |

参考实现与手册：

- [Marchen-orz/MiyoQian](https://github.com/Marchen-orz/MiyoQian)：由于云绝区零没有官方 Web 端，它**没有**内置用 stoken 换票的流程（原因即第三节），要求用户自行抓包，把客户端发的 `x-rpc-combo_token` 填进 `data/credentials.yaml`。
- [云·星铁自动签到手册](https://bili33.top/posts/SRCloud-AutoCheckin-Manual/) · [MHYY 自动签到手册（第二代）](https://bili33.top/posts/MHYY-AutoCheckin-Manual-Gen2/)

无论哪种载体，**凭证过期后都要回到客户端重新登录再取一次**，这是这类方案共同的维护成本，也是 Moonward 选择「直接读本机客户端日志」的原因。

---

## 七、返回码与常见现象

| 现象 | 含义 | 处理 |
|------|------|------|
| `-100` | 凭证失效 | 去**云游戏客户端**重新登录一次，Moonward 会重新读日志；不是重登本应用账号 |
| `-114` | SDK 拒绝换票 | 见第三节，不要再尝试用通行证换票这条路 |
| `-464` | 官方桥接被挡 | 同上 |
| `-1` | 响应结构对不上（SDK 改版） | 走通用文案，不要把内部英文描述甩给用户 |
| 领取成功但 `free_time` 没涨 | 多半已满 600 分钟 | 正常，按满仓处理不重试 |
| `send_freetime` 一直是 0 | 正常 | 发放是异步落账，此字段不可用于判断是否已领 |

---

## 八、注意事项

1. `combo_token` 等同云游戏账号登录态，**不要外传、不要贴进 issue**。抓包文件里同样有它，用完及时删除。
2. 相关接口均为非公开 API，规则与返回码会随客户端版本变化；本页结论标注了实测日期，过期请重新核对。
3. 仅用于个人账号的自动化便利，请勿用于违反服务条款的行为。
