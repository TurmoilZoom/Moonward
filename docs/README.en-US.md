<h1 align="center">Moonward</h1>

<p align="center">
  An open-source third-party launcher based on <a href="https://github.com/Scighost/Starward">Starward</a>, for miHoYo PC games<br/>
  <a href="https://github.com/TurmoilZoom/Moonward/releases/latest">Download</a>
</p>

<p align="center">
  <a href="../README.md">简体中文</a>
  · <a href="README.zh-TW.md">繁體中文</a>
  · English
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

On top of upstream Starward, everyday actions are packed into a desktop shortcut and a single URL, with extras for check-in, gacha, and backgrounds. Highlights:

#### Gacha

- **Wish / gacha history** — Banner stats can be drag-reordered (auto-scrolls horizontally near the edge); the list supports drag-to-scroll; stats stick to the top. UP / miss streaks and win rate are easy to see. Miliastra Wonderland pity uses a progress bar
- **Filter & share** — Title-bar dropdown chooses which banners to show, with select all / invert / reset. One click makes a frosted share image with pity count and guarantee progress
- **Gacha sync** — Genshin Impact / Zenless Zone Zero and others can refresh records via miHoYo BBS. New characters not yet in the catalog get icons and names filled in automatically. Item names follow the app language
- **Data interchange** — Import / export gacha records as UIGF. History can be imported read-only from upstream Starward

#### Accounts & toolbox

- **Daily check-in** — miHoYo BBS / HoYoLAB check-in, per-game toggles, auto check-in and makeup check-in. Launching a game via shortcut / URL / command line also checks in that account once
- **Login** — China servers use a phone SMS code; overseas servers use web login. Expired sessions are renewed automatically when possible, so you don't have to log in again and again
- **Monthly reports & notes** — Toolbox monthly reports (Trailblaze Monthly Calendar / Inter-Knot Monthly Report / Traveler's Diary) share one layout. The Inter-Knot report fixes cross-timezone daily data and defaults to the current month. Real-time notes that hit risk control get a verification entry

#### Launch

- **Multiple launch profiles** — Unlimited sets of launch arguments and custom launchers per game. Switching or editing does not require retyping; name a profile and create a desktop shortcut
- **URL protocol** — `moonward://` starts / stops / restarts a given game, profile, and account, or runs check-in alone. Embeddable in scripts or web pages (see [docs/UrlProtocol](UrlProtocol.md))
- **Quick launch** — Home hamburger menu combines game settings, quick launch, and “create Start menu shortcut”

#### Look & background

- **Trust wallpapers** — In Zenless Zone Zero, wiki Trust motion wallpapers and Mindscape still wallpapers can be downloaded and set as a custom background. Opening the gallery uses the local cache; updates are checked silently in the background
- **Custom background** — Dedicated dialog for image / video (drag onto the home page to replace). Restoring from the tray no longer flashes. Poster choice is kept after the background list updates

#### Other

- **System integration** — Optional start at login into the tray. About page one-click pre-fills diagnostics, opens GitHub feedback, and opens the log folder
- **Silent updates** — New versions download in the background, install after you quit, and show the changelog on the next launch (Velopack + GitHub Releases)

Installers are on [Releases](https://github.com/TurmoilZoom/Moonward/releases).

Upstream: [Scighost/Starward](https://github.com/Scighost/Starward)  
Credits: [CREDITS.md](../CREDITS.md) (open-source projects referenced for features and design)  
License: [MIT](../LICENSE)

Privacy: [docs/Privacy.md](Privacy.md)
