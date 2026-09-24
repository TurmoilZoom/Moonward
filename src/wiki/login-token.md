## 1. 总览：登录方式 → 根凭证 → 其它 Token

<img  alt="diagram" src="https://github.com/user-attachments/assets/73055b32-97d3-4b10-9b7d-fa1d03a4a316" />

## 2. Token 角色一览

| Token             | Cookie / 字段           | 配对 ID       | 主要用途                                       | 寿命/特点    |
| ----------------- | ----------------------- | ------------- | ---------------------------------------------- | ------------ |
| **Login Ticket**  | `login_ticket`          | 米游社 UID    | 兑换 LToken（历史还能换 SToken，现已弱化）     | ~30 分钟     |
| **Game Token**    | `game_token`            | `account_id`  | 游戏侧登录凭证；可换 SToken / Cookie Token     | 游戏登录态   |
| **SToken V1**     | `stoken`（非 v2_）      | `stuid`       | 米游社内写操作、兑换其它 Token 的枢纽          | 改密码会变   |
| **SToken V2**     | `stoken`（`v2_` 开头）  | `mid`         | V1 升级版，同样是枢纽                          | 改密码会变   |
| **LToken V1**     | `ltoken`                | `ltuid`       | 读游戏记录、实时便笺等查询                     | 改密码会变   |
| **LToken V2**     | `ltoken_v2`             | `ltmid_v2`    | 新版查询 Cookie；值与 V1 不同                  | 改密码会变   |
| **Cookie Token**  | `cookie_token` / `_v2`  | `account_id`  | 账号相关 Web API；可换 Hk4e Token              | V1/V2 值不同 |
| **Hk4e Token**    | `e_hk4e_token`          | 游戏 UID/区服 | 原神网页活动标识                               | 活动向       |
| **Action Ticket** | 通常不在 Cookie，作参数 | —             | 米游社部分网页操作（如查绑定角色）             | 短期         |
| **Auth Key A/B**  | URL/请求体参数          | 账号或角色    | A：账号级；B：抽卡记录等（`webview_gacha` 等） | 短期         |

ID 对应关系（同一账号）：
数字 UID 一族：account_id ≈ ltuid ≈ stuid ≈ login_uid（同一米游社 UID）
字符串 mid 一族：mid ≈ ltmid_v2（同一 MiHoYo ID）

## 3. 登录方式分别落到哪里

<p align="center">
  <img width="500" height="auto" alt="diagram" src="https://github.com/user-attachments/assets/c2dcdb57-14e1-46ee-8163-f51e3f219a5b" />
</p>

## 4. Auth Key

获取抽卡记录等 API 用的是 Auth Key B。
### Auth Key A
| 项     | 内容                                                         |
| ------ | ------------------------------------------------------------ |
| 文档名 | 通过 SToken 获取账号 Auth Key A                              |
| 接口   | `POST https://api-takumi.miyoushe.com/account/auth/api/genAuthKey` |
| 鉴权   | Cookie：SToken                                               |
| 请求体 | 主要是 `game_biz`（如国服米游社 `bbs_cn`）                   |
| 粒度   | **账号级**（不绑具体游戏 UID）                               |
| 返回   | `authkey` + `sign_type` + `authkey_ver`                      |
### Auth Key B
| 项                | 内容                                                         |
| ----------------- | ------------------------------------------------------------ |
| 文档名            | 通过 SToken 获取账号 Auth Key B                              |
| 接口              | `POST https://api-takumi.miyoushe.com/binding/api/genAuthKey` |
| 鉴权              | Cookie：SToken；还要 `x-rpc-client_type: 5`、LK2 salt、DS1 等 |
| 请求体            | `game_biz`、`game_uid`、`region`、`auth_appid`               |
| 粒度              | **绑定到某个游戏角色**                                       |
| 典型 `auth_appid` | `webview_gacha`（抽卡记录）、`csc`（客服页）、`im_ccs`（米游社相关） |
| 返回              | 同样是 `authkey` + `sign_type` + `authkey_ver`               |

## 5.注意：
V1 / V2 不能混配：ltoken 必须配 ltuid，ltoken_v2 必须配 ltmid_v2；SToken 同理。
改密码会使 LToken / SToken 失效。
getMultiTokenByLoginTicket 现在基本只给 LToken，历史上能同时拿 SToken 的路径已弱化（见文档 issue #46）。
游戏扫码拿 Game Token → 换 SToken，是目前文档里通往「完整 Token 树」较清晰的一条路；Web 密码/扫码更常直接给 LToken V2 + Cookie Token。
