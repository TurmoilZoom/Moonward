<h1 align="center">Moonward</h1>

<p align="center">
  基於 <a href="https://github.com/Scighost/Starward">Starward</a> 的開源第三方啟動器，面向米哈遊 PC 遊戲<br/>
  <a href="https://github.com/TurmoilZoom/Moonward/releases/latest">下載</a>
</p>

<p align="center">
  <a href="../README.md">简体中文</a>
  · 繁體中文
  · <a href="README.en-US.md">English</a>
  · <a href="README.de-DE.md">Deutsch</a>
  · <a href="README.es-ES.md">Español</a>
  · <a href="README.it-IT.md">Italiano</a>
  · <a href="README.ja-JP.md">日本語</a>
  · <a href="README.ko-KR.md">한국어</a>
  · <a href="README.ru-RU.md">Русский</a>
  · <a href="README.th-TH.md">ไทย</a>
  · <a href="README.vi-VN.md">Tiếng Việt</a>
</p>


---

在上游 Starward 的基礎上，把常用操作收進桌面捷徑與一條 URL，並在簽到、抽卡、背景等方面做了增強。主要功能：

#### 抽卡

- **抽卡紀錄** — 卡池統計可拖曳排序（靠近邊緣自動橫向捲動）、清單支援拖曳捲動、統計吸頂；連 UP / 連歪、不歪機率等一目了然；千星奇域「已墊」改用進度條
- **篩選與分享** — 標題列下拉篩選顯示哪些卡池，可全選 / 反選 / 重設；一鍵產生磨砂風格分享圖，含墊數與保底進度
- **抽卡同步** — 原神 / 絕區零等可透過米遊社相關方式更新紀錄；抽到未收錄的新角色時自動補全圖示與名稱；物品名跟隨應用語言
- **資料互通** — 支援 UIGF 抽卡紀錄匯入 / 匯出；可從上游 Starward 唯讀匯入歷史資料

#### 帳號與工具箱

- **每日簽到** — 米遊社 / HoYoLAB 簽到，每個遊戲獨立開關，支援自動簽到與補簽；用捷徑 / URL / 命令列啟動遊戲時，也會給該帳號單獨簽一次
- **登入改進** — 國服用手機號碼收驗證碼登入，國際服走網頁登入；登入過期時盡量自動續上，不必反覆重新登入
- **月報與便箋** — 工具箱月報（開拓月曆 / 繩網月報 / 旅行者札記）版面統一；繩網月報修正跨時區每日資料、預設顯示當月；即時便箋遇風控時提供驗證入口

#### 啟動

- **多啟動設定** — 同一遊戲可儲存多套啟動參數與自訂啟動程式，數量不限；切換設定、改參數不必每次重填，可命名儲存並產生桌面捷徑
- **URL 通訊協定** — `moonward://` 指定遊戲、設定與帳號直接啟動 / 停止 / 重新啟動，也可單獨觸發簽到；能嵌入指令碼或網頁（詳見 [docs/UrlProtocol](UrlProtocol.zh-CN.md)）
- **快速啟動** — 首頁漢堡選單整合遊戲設定、快速啟動與「產生開始功能表捷徑」

#### 外觀與背景

- **好感壁紙** — 絕區零可將百科「好感動態壁紙」與「滿影畫靜態壁紙」下載並設為自訂背景；開啟圖庫即用本機快取，背景靜默校驗更新
- **自訂背景** — 獨立的自訂背景對話方塊，支援圖片 / 影片（可拖入首頁直接取代）；從系統匣還原不再閃爍；背景清單更新後保留海報偏好

#### 其他

- **系統整合** — 可設定開機自啟到系統匣；關於頁一鍵預填診斷資訊並跳轉 GitHub 回饋，同時開啟記錄資料夾
- **靜默更新** — 背景下載新版本，結束軟體後自動安裝，下次啟動展示更新內容（Velopack + GitHub Releases）

安裝套件見 [Releases](https://github.com/TurmoilZoom/Moonward/releases)。

上游專案：[Scighost/Starward](https://github.com/Scighost/Starward)  
致謝：[CREDITS.md](../CREDITS.md)（功能與設計參考的開源專案）  
授權條款：[MIT](../LICENSE)

隱私權政策：[docs/Privacy.md](Privacy.md) · [中文](Privacy.zh-CN.md)
