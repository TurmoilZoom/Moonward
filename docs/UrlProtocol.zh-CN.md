# URL 协议

其他软件甚至网站都可以通过 `starward` URL 协议调用 Starward 的部分功能。只有在用户在设置页面中启用该功能后，URL 协议才会被注册。

![URL Protocol](https://user-images.githubusercontent.com/61003590/278273851-7c614cde-d8c4-403b-876e-cecc3570f684.png)


## 可用功能

下文中的 `game_biz` 参数为游戏区服标识符，完整列表可查看 [GameBiz.cs](https://github.com/Scighost/Starward/blob/main/src/Starward.Core/GameBiz.cs)。

| game_biz (string) | 说明 |
| ----------------- | ---- |
| hk4e_cn           | 原神（中国大陆） |
| hk4e_global       | 原神（国际服） |
| hk4e_bilibili     | 原神（Bilibili） |
| hkrpg_cn          | 崩坏：星穹铁道（中国大陆） |
| hkrpg_global      | 崩坏：星穹铁道（国际服） |
| hkrpg_bilibili    | 崩坏：星穹铁道（Bilibili） |
| bh3_cn            | 崩坏 3（中国大陆） |
| bh3_global        | 崩坏 3（国际服） |
| nap_cn            | 绝区零（中国大陆） |
| nap_global        | 绝区零（国际服） |
| nap_bilibili      | 绝区零（Bilibili） |


### 启动游戏

```
starward://startgame/{game_biz}?install_path={install_path}&profile={profile}&uid={uid}
```

**可用查询参数**

| 键 | 类型 | 说明 |
|---|---|---|
| install_path | `string`（可选） | 游戏可执行文件所在文件夹的完整路径。 |
| profile | `string`（可选） | 启动方式 / 配置内部名。`none` 表示「无」：不使用启动参数配置启动（仍受 DX12 等全局开关影响）。`configN`（N ≥ 1，无数量上限）与「配置文件 N」一一对应（`config1` 数据存于 legacy 键）。省略时跟随软件当前生效的启动方式（「跟随软件设置」）；默认生效方式为 `none`。 |
| uid | `long`（可选） | 米游社工具箱中的游戏角色 UID。指定且对应角色 Cookie 含有效 `stoken` 时，启动前换取 auth ticket 并附加 `login_auth_ticket`，使游戏自动以该账号登录（仅国服）。优先级高于配置文件内保存的登录账号。 |


### 记录游戏时长

```
starward://playtime/{game_biz}?pid={pid}
```

**可用查询参数**

| 键 | 类型 | 说明 |
|---|---|---|
| pid | `int`（可选） | 游戏进程 ID。 |