<h1 align="center">Moonward</h1>

<p align="center">
  Ein Open-Source-Drittanbieter-Launcher auf Basis von <a href="https://github.com/Scighost/Starward">Starward</a> für miHoYo-PC-Spiele<br/>
  <a href="https://github.com/TurmoilZoom/Moonward/releases/latest">Download</a>
</p>

<p align="center">
  <a href="../README.md">简体中文</a>
  · <a href="README.zh-TW.md">繁體中文</a>
  · <a href="README.en-US.md">English</a>
  · Deutsch
  · <a href="README.es-ES.md">Español</a>
  · <a href="README.it-IT.md">Italiano</a>
  · <a href="README.ja-JP.md">日本語</a>
  · <a href="README.ko-KR.md">한국어</a>
  · <a href="README.ru-RU.md">Русский</a>
  · <a href="README.th-TH.md">ไทย</a>
  · <a href="README.vi-VN.md">Tiếng Việt</a>
</p>


---

Auf Basis des Upstream-Projekts Starward sind häufige Aktionen in einer Desktopverknüpfung und einer URL gebündelt; Check-in, Gacha und Hintergründe wurden erweitert. Die wichtigsten Funktionen:

#### Gacha

- **Gacha-Verlauf** — Bannerstatistiken per Ziehen sortierbar (am Rand automatischer Horizontalscroll), Liste per Ziehen scrollbar, Statistiken oben angepinnt. UP-/Miss-Serien und Trefferquote auf einen Blick. Pity in Miliastra Wonderland als Fortschrittsbalken
- **Filter & Teilen** — Dropdown in der Titelleiste wählt sichtbare Banner: alle / invertieren / zurücksetzen. Ein Klick erzeugt ein mattes Teilen-Bild mit Pity-Zähler und Garantiefortschritt
- **Gacha-Sync** — Genshin Impact / Zenless Zone Zero u. a. können Einträge über miHoYo BBS aktualisieren. Neue Figuren ohne Katalogeintrag erhalten Icon und Namen automatisch. Gegenstandsnamen folgen der App-Sprache
- **Datenaustausch** — Import / Export von Gacha-Daten im UIGF-Format. Verlauf aus Upstream-Starward nur lesend importierbar

#### Konto & Toolbox

- **Täglicher Check-in** — miHoYo-BBS- / HoYoLAB-Check-in, eigener Schalter pro Spiel, Auto-Check-in und Nachholen. Beim Start per Verknüpfung / URL / Befehlszeile wird für dieses Konto zusätzlich einmal eingecheckt
- **Anmeldung** — China-Server: Login per SMS-Code aufs Handy; internationale Server: Web-Login. Abgelaufene Sitzungen werden nach Möglichkeit automatisch verlängert, ohne wiederholtes Einloggen
- **Monatsberichte & Notizen** — Einheitliches Layout der Toolbox-Monatsberichte (Monatskalender des Trailblazes / Monatlicher Inter-Knoten-Bericht / Tagebuch des Reisenden). Der Inter-Knot-Bericht korrigiert Tagesdaten über Zeitzonen und zeigt standardmäßig den aktuellen Monat. Bei Risikokontrolle in den Echtzeitnotizen gibt es einen Verifizierungseinstieg

#### Start

- **Mehrere Startprofile** — Beliebig viele Startparameter und benutzerdefinierte Starter pro Spiel. Wechsel und Änderungen ohne jedes Mal neu ausfüllen; Profile benennen und Desktopverknüpfung erzeugen
- **URL-Protokoll** — `moonward://` startet / stoppt / startet neu mit Spiel, Profil und Konto oder löst nur den Check-in aus. Einbettbar in Skripte oder Webseiten (siehe [docs/UrlProtocol](UrlProtocol.md))
- **Schnellstart** — Hamburger-Menü auf der Startseite vereint Spieleinstellungen, Schnellstart und „Startmenü-Verknüpfung erstellen“

#### Erscheinungsbild & Hintergrund

- **Trust-Hintergründe** — In Zenless Zone Zero können Wiki-Trust-Bewegthintergründe und Mindscape-Standhintergründe heruntergeladen und als eigener Hintergrund gesetzt werden. Die Galerie nutzt den lokalen Cache; Updates werden im Hintergrund still geprüft
- **Eigener Hintergrund** — Eigener Dialog für Bild / Video (auf die Startseite ziehen zum Ersetzen). Wiederherstellen aus dem Infobereich ohne Flackern. Posterwahl bleibt nach Listenupdate erhalten

#### Sonstiges

- **Systemintegration** — Optionaler Start mit der Windows-Anmeldung in den Infobereich. Auf der Infoseite Diagnosedaten vorausfüllen, zu GitHub-Feedback springen und den Logordner öffnen – in einem Schritt
- **Stille Updates** — Neue Version im Hintergrund laden, nach dem Beenden automatisch installieren, Changelog beim nächsten Start anzeigen (Velopack + GitHub Releases)

Installationspakete: [Releases](https://github.com/TurmoilZoom/Moonward/releases).

Upstream-Projekt: [Scighost/Starward](https://github.com/Scighost/Starward)  
Danksagung: [CREDITS.md](../CREDITS.md) (Open-Source-Projekte als Referenz für Funktionen und Design)  
Lizenz: [MIT](../LICENSE)

Datenschutz: [docs/Privacy.md](Privacy.md)
