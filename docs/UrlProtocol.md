# URL Protocol

Other software even website could use url protocol `moonward` to call some features of Moonward. The url protocol is registered only when the user enables this feature in setting page.

![URL Protocol](https://user-images.githubusercontent.com/61003590/278273851-7c614cde-d8c4-403b-876e-cecc3570f684.png)


## Available features

The parameter `game_biz`  in the following is game region identifier and can be viewed in [GameBiz.cs](https://github.com/Scighost/Starward/blob/main/src/Starward.Core/GameBiz.cs).

| game_biz (string) | Description                             |
| ----------------- | --------------------------------------- |
| hk4e_cn           | Genshin Impact (Mainland China)         |
| hk4e_global       | Genshin Impact (Global)                 |
| hk4e_bilibili     | Genshin Impact (Bilibili)               |
| hkrpg_cn          | Star Rail (Mainland China)              |
| hkrpg_global      | Star Rail (Global)                      |
| hkrpg_bilibili    | Star Rail (Bilibili)                    |
| bh3_cn            | Honkai 3rd (Mainland China)             |
| bh3_global        | Honkai 3rd (Global)                     |


### Start game

```
moonward://startgame/{game_biz}?install_path={install_path}&profile={profile}&uid={uid}
```

**Acceptable query arguments**

|Key|Type|Description|
|---|---|---|
|install_path| `string` (Option) | Folder full path of game executable. |
|profile| `string` (Option) | Launch method / profile id. `none` = launch without launch-argument profiles (DX12 and other global toggles still apply). `configN` (N ≥ 1, no upper limit) maps 1:1 to display name “Profile N” (`config1` uses legacy storage). When omitted, the app's currently active launch method is used ("follow app setting"); the default active method is `none`. |
|uid| `long` (Option) | Game character UID from HoYoLAB / miyoushe toolbox roles. When set (and the matching role Cookie has a valid `stoken`), Moonward requests an auth ticket and appends `login_auth_ticket` so the game logs in as that account (CN servers only). Takes priority over the profile's saved login account. When a custom launcher is used, the token is not injected (the tool starts the game itself); the UID is only used for daily sign-in. |


### Record playtime

```
moonward://playtime/{game_biz}?pid={pid}
```

**Acceptable query arguments**

|Key|Type|Description|
|---|---|---|
|pid| `int` (Option) | Game process id. |
