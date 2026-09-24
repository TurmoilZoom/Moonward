# 抓 Windows 客户端的包（以云游戏客户端为例）

[怎么逆向api](API-REVERSE) 讲的是**安卓模拟器**抓米游社，那套 Magisk + 证书信任 + 绕 Pinning 的流程对 PC 客户端不适用。本页记录 **Windows 桌面客户端**的抓法：以云·原神、云·绝区零为例，2026-09 实测跑通，`local` 模式两轮 **0 失败**，两个客户端的所有 HTTPS 域名都被解密。

> 关键结论：这类客户端内部有**好几条网络路径**，对代理的反应不一样，只设代理环境变量抓不全，必须 `regular` + `local` 两种模式一起开。

---

## 一、先认清三条网络路径

云·原神（国服 / 国际服）与云·绝区零都是 **Qt5 原生程序**（内嵌 QtWebEngine 与 libcurl，不是 Electron），接口全部走 libcurl 的 HTTPS，可以完整解密。唯一不走 HTTP 的是进游戏后的画面串流（UDP），这部分不在本文范围。

| 部分 | 典型域名 | 认 `HTTPS_PROXY` 吗 | 抓法 |
|------|----------|:-------------------:|------|
| Combo SDK（登录、签发 `combo_token`） | `passport-api`、`hk4e-sdk`、`nap-sdk-s` | **认** | `regular` 模式 + 进程级代理环境变量 |
| 云游戏业务层（登录、钱包、排队、配置） | `api-cloudgame`、`cg-nap-api` | **不认** | 只能用 `local` 模式（WinDivert 透明捕获） |
| 日志上报 | `*.mhystatic.com` | — | 用自带 CA 列表，拒绝 mitmproxy 证书；`--ignore-hosts` 放行即可 |

QtWebEngine 的命令行 flags 会被程序自己改写，指望用 flags 指定代理不可靠；libcurl 那部分老老实实认环境变量。

---

## 二、前置条件

1. 安装 mitmproxy，并把它的 CA 证书装进 Windows **「受信任的根证书颁发机构」（本地计算机）**。这一步与 [API-REVERSE 第八节](API-REVERSE) 相同。
2. `local` 模式要提权：首次启动时 mitmproxy 会拉起 `windows-redirector.exe`（WinDivert 捕获组件）并弹 UAC，点「是」。它只捕获你指定的进程。
3. 确认没有别的 mitmproxy 实例在跑，端口冲突会让你白折腾。

---

## 三、操作步骤

### 3.1 启动 mitmweb（两种模式一起开）

```powershell
& "C:\Program Files\mitmproxy\bin\mitmweb.exe" `
  --mode regular@8086 `
  --mode "local:Zenless Zone Zero Cloud,Genshin Impact Cloud" `
  --listen-host 127.0.0.1 `
  --ignore-hosts "mhystatic\.com"
```

- `regular@8086` 的端口要和下一步环境变量里的端口**一致**；端口本身随便挑，也可以写成 `local:<进程名>@18086` 给 local 模式单独指定端口。
- `--ignore-hosts "mhystatic\.com"` 放行日志上报域名，否则会刷一屏证书被拒的错误。
- 网页界面端口是另一个（默认 8081，可在 `config.yaml` 里用 `web_port` 改），启动后浏览器打开看流量。

### 3.2 从设好代理的窗口启动客户端

**另开一个 PowerShell 窗口**，变量只对这个窗口生效，不改系统代理：

```powershell
# 云·绝区零
$d = "$env:LOCALAPPDATA\Programs\miHoYo\ZenlessZoneZeroCloud"
$env:HTTP_PROXY  = 'http://127.0.0.1:8086'
$env:HTTPS_PROXY = 'http://127.0.0.1:8086'
Start-Process "$d\Zenless Zone Zero Cloud.exe" -WorkingDirectory $d
```

```powershell
# 云·原神国服
$d = "$env:LOCALAPPDATA\Programs\miHoYo\GenshinImpactCloudGame"
$env:HTTP_PROXY  = 'http://127.0.0.1:8086'
$env:HTTPS_PROXY = 'http://127.0.0.1:8086'
Start-Process "$d\Genshin Impact Cloud Game.exe" -WorkingDirectory $d
```

### 3.3 在客户端里登录

业务接口（钱包、登录）要**登录之后**才会发出。客户端停在登录框时只能抓到配置类请求。

---

## 四、`local` 模式的进程匹配规则

官方文档没写清楚，以下是看源码 + 实测确认的：

| 规则 | 说明 |
|------|------|
| 匹配方式 | 对进程名做**区分大小写的子串匹配** |
| 多进程 | 逗号分隔，如 `local:Zenless Zone Zero Cloud,Genshin Impact Cloud` |
| 子串技巧 | `Genshin Impact Cloud` 能同时命中国服 `Genshin Impact Cloud Game` 与国际服 `Genshin Impact Cloud` |
| 回环流量 | Windows 上**不捕获**回环，所以 `regular`（走 `127.0.0.1:8086`）与 `local`（透明捕获直连请求）可以并用，不会重复拦截 |
| 改规则 | mitmproxy 会热加载脚本，用一次性钩子在运行时改 redirector 的规则即可换进程，**不必重启、不会再弹 UAC** |

---

## 五、几个坑

- **客户端一定要从 3.2 那个窗口启动。** 很多人的用户环境变量里常年有 `HTTP(S)_PROXY=http://127.0.0.1:7890`（FlClash 之类）。从开始菜单启动的话，登录请求会发到那个本机端口，而 `local` 模式不捕获回环流量，`combo_token` 就抓不到。
- **别用不带进程名的 `--mode local`。** FlClash 开着 TUN 时，它会把 FlClash 自己的出站流量也截走，形成回环：客户端日志里会留下 curl `error 35`（TLS 握手失败）与 `net 502`（代理 Bad Gateway）——请求其实已经到了 mitmproxy、证书也被接受，是 mitmproxy 连不上上游。
- **抓包时别点开始游戏。** 画面串流是 UDP，同样会被 `local` 模式接管，mitmproxy 解不了，只会拖慢画面。
- **`mitmdump` 的标准输出有缓冲**，看着像没日志。要实时看就写个插件把主机与路径**自己**写进文本文件并刷新（顺便别输出请求头与查询串，免得凭证落盘）。
- **证书固定的域名直接放行**，不要试图硬解，`--ignore-hosts` 就够了。

---

## 六、实测到的链路

- **云·原神国服**（本机已有登录态）：`app/verify` → `combo/granter/login/v2/login` → `gamer/api/login` → **`wallet/wallet/get`**。钱包请求带 `x-rpc-combo_token` 等 16 个 `x-rpc-*` 头，实测其中只有 4 个是必需的，见 [怎么做云游戏签到](CLOUD-GAME)。
- **云·绝区零**：`cg-nap-api` 的业务接口全部解密成功。

---

## 七、收尾

1. 关掉客户端与 mitmproxy 进程；本文流程不改系统代理、不装额外软件，也就没有别的残留要清。
2. **抓包文件里有 `combo_token` 与通行证凭证，等同账号登录态**：不要外传、不要贴进 issue，用完删掉（`mitmweb -r <文件>` 可以回看）。
3. 相关接口与反抓包策略会持续变化，实际使用时结合最新社区经验调整。
