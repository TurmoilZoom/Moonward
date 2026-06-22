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
starward://startgame/{game_biz}?install_path={install_path}&profile={profile}
```

**可用查询参数**

| 键 | 类型 | 说明 |
|---|---|---|
| install_path | `string`（可选） | 游戏可执行文件所在文件夹的完整路径。 |
| profile | `string`（可选） | 启动配置文件的内部名，可选值为 `Alice`、`Bob`、`Charlie`、`Dave`、`Eve`、`Mallory`、`Trent`、`Carol`。`Alice` 为默认配置文件；省略时跟随软件当前生效（已应用）的配置（即「跟随软件设置」）；未找到或为 `Alice` 时使用默认配置文件。 |


### 记录游戏时长

```
starward://playtime/{game_biz}?pid={pid}
```

**可用查询参数**

| 键 | 类型 | 说明 |
|---|---|---|
| pid | `int`（可选） | 游戏进程 ID。 |