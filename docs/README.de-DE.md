<p align="center">
  <img src="images/banner.png" alt="Moonward" width="720" />
</p>

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

Auf Basis des Upstream-Projekts Starward sind häufige Aktionen in einer Desktopverknüpfung und einer URL gebündelt; Gacha, Kampfberichte, Spielinstallation, Hintergründe und mehr wurden erweitert. Die wichtigsten Funktionen:

#### Gacha

- **Gacha-Verlauf** — Banner-Statistiken lassen sich per Drag & Drop sortieren und bleiben oben fixiert. Umschaltbar zwischen Listenansicht und kompaktem Symbolraster. UP- / Fehlschlag-Serien und die 50/50-Gewinnquote sind auf einen Blick sichtbar
- **Filter & Teilen** — Über ein Dropdown in der Titelleiste wählen, welche Banner angezeigt werden; alles auswählen / umkehren / zurücksetzen. Ein Klick erzeugt ein Teilen-Bild im Milchglas-Stil mit Pity-Zähler und Garantie-Fortschritt
- **Gacha-Sync** — Genshin Impact (China-Server) und Zenless Zone Zero können Einträge aus miHoYo BBS synchronisieren; Zenless Zone Zero unterstützt auch HoYoLAB. Neue, noch nicht erfasste Gegenstände erhalten automatisch Symbol und Namen. Gegenstandsnamen folgen der App-Sprache
- **Miliastra Wonderland** — Die Gebetsverlauf-Seite funktioniert wie der Gacha-Verlauf (Filter, Kompaktansicht, Sortieren per Drag & Drop, Teilen-Bild); im Upstream fehlende Gegenstandssymbole werden ergänzt
- **UIGF** — Import / Export von Gacha-Einträgen im UIGF-Format

#### Kampfberichte & Toolbox

- **Täglicher Check-in** — miHoYo BBS / HoYoLAB, Schalter pro Spiel, automatischer und nachträglicher Check-in. Im Infobereich läuft nach jedem täglichen Reset automatisch ein weiterer Check-in
- **Anmeldung** — China-Server per SMS-Code, internationale Server per Web-Login. Abgelaufene Sitzungen werden nach Möglichkeit automatisch erneuert, damit man sich nicht ständig neu anmelden muss
- **Automatische Aktualisierung** — Aktualisierungsintervall pro Konto und Datenbereich (inklusive aktuellem und vorherigem Monat der Monatsberichte) festlegen; beim App-Start wird automatisch aktualisiert
- **Monatsberichte & Notizen** — Monatsberichte der Toolbox (Trailblaze-Monatskalender / Inter-Knot-Monatsbericht / Reisetagebuch) mit einheitlichem Layout. Der Inter-Knot-Bericht zählt Tagesdaten in der Zeitzone des Servers und zeigt standardmäßig den aktuellen Monat. Echtzeit-Notizen in Genshin Impact zeigen mehr Details. Bei Risikokontrolle gibt es einen Verifizierungseinstieg
- **Berichtsanzeige** — Kampfberichte und Monatsberichte lassen sich als Teilen-Bild ausgeben. Das Imaginarium-Theater kennzeichnet Probe- und Unterstützungscharaktere; Anomaly Arbitration zeigt Schnellabschlüsse und Schwierigkeitsnamen
- **Cloud-Gaming** — Verbleibende Spielzeit für Cloud Genshin und Cloud Zenless Zone Zero ansehen und die tägliche Gratiszeit automatisch abholen

#### Start

- **Mehrere Startprofile** — Beliebig viele Sätze aus Startargumenten und eigenen Launchern pro Spiel. Jedes Profil hat eigene Einstellungen für das DX12-Argument und „über CMD starten“; Profile benennen und als Desktopverknüpfung anlegen
- **Drittanbieter-Tools** — Ein eigener Launcher kann auch ein URL-Protokoll-Link sein. Gängige Befehlszeilenargumente enthalten Gruppen für BetterGI, ZZZ OneDragon und March7th Assistant
- **URL-Protokoll** — `moonward://` startet / stoppt / startet neu für ein bestimmtes Spiel, Profil und Konto oder löst nur den Check-in aus. In Skripte oder Webseiten einbettbar (siehe [docs/UrlProtocol](UrlProtocol.md))
- **Schnellstart** — Das Hamburger-Menü auf der Startseite vereint Spieleinstellungen, Schnellstart und Spielverknüpfungen. Verknüpfungen können ohne UAC-Abfrage starten; die zugehörigen geplanten Aufgaben lassen sich in den Einstellungen pro Spiel verwalten
- **Spielzeit** — Während das Spiel läuft, zeigt die Schaltfläche „Spiel starten“ einen Timer. Die Spielzeit-Statistik kann zwischen Spielen wechseln, Jahr und Monat wählen und ein Teilen-Bild erzeugen. Zeit im Energiesparmodus wird nicht mitgezählt

#### Installation & Updates

- **Per Hardlink verbundene Server** — Beim Aktualisieren oder Reparieren werden auch andere Server mitbehandelt, die per Hardlink Dateien teilen (die Basisinstallation wird immer zuerst aktualisiert). Der Deinstallationsdialog listet alle installierten Server desselben Spiels und kann sie gemeinsam entfernen
- **Installationsoptionen** — Zenless Zone Zero kann vollständige oder Basis-Ressourcen installieren. Neben dem Installationspfad steht der Laufwerkstyp (SSD / HDD / Wechseldatenträger / Netzwerk). Chunk-Downloads laufen blockweise parallel
- **Reparatur & Versionsinfo** — Die Reparatur ist jetzt ein eigener Dialog und kann auch nur die Miliastra-Sandbox reparieren; die Zahl der Prüf-Threads richtet sich nach dem Laufwerkstyp. Die Seite „Versionsinfo“ in den Spieleinstellungen zeigt Version, inkrementelle Patches und den MD5 der Hauptprogrammdatei. Die automatische Suche findet auch in der App installierte Spiele (inklusive Hardlinks) wieder

#### Erscheinungsbild & Hintergrund

- **Trust-Hintergründe** — In Zenless Zone Zero lassen sich die animierten Trust-Hintergründe und die statischen Mindscape-Hintergründe aus dem Wiki herunterladen und als eigener Hintergrund setzen. Der Zufallsmodus wechselt beim Start, beim Spielwechsel oder beim Öffnen aus dem Infobereich das Bild. Die Galerie nutzt den lokalen Cache; Updates werden still im Hintergrund geprüft
- **Eigener Hintergrund** — Eigener Dialog für Bild / Video (auf die Startseite ziehen zum Ersetzen). Die Poster-Auswahl bleibt nach Updates der Hintergrundliste erhalten
- **Videohintergründe** — Videos, die sich nicht per Hardware dekodieren lassen, werden automatisch nach H.264 umgewandelt. Fehlt die HEVC- / VP9-Erweiterung, hilft die App bei der Installation oder zeigt stattdessen ein Standbild. Bei ausgeschaltetem Bildschirm wird der Decoder freigegeben und verbraucht keine CPU mehr
- **Startseiten-Werkzeugleiste** — Zeitleiste (Banner- / Event-Countdowns), Einlösecodes aus Livestreams (China-Server) und interaktive Karten (Genshin Impact / Honkai: Star Rail)

#### Sonstiges

- **Im Infobereich** — Optional beim Anmelden in den Infobereich starten. Globale Tastenkürzel und Gamepad-Steuerung funktionieren weiter; über das Menü im Infobereich lassen sie sich umschalten und Updates suchen
- **LAN-Sync** — Gacha-Einträge, Toolbox-Daten und Spielzeit von einem anderen Gerät im lokalen Netzwerk übernehmen. Es werden nur auf diesem PC fehlende Einträge hinzugefügt; Anmeldedaten werden nie synchronisiert
- **Datenverwaltung** — Datenbank, Cache, Protokolle und Hintergründe liegen in einem Ordner Ihrer Wahl. In den Einstellungen lassen sich die Datenbank sichern und der Cache leeren. Beim ersten Start können Daten aus dem Upstream-Starward importiert werden (nur lesend; die Originaldaten bleiben unverändert)
- **Feedback** — Die Info-Seite füllt Umgebungsinformationen per Klick vor, öffnet die GitHub-Issue-Seite und den Protokollordner
- **Stille Updates** — Neue Versionen werden im Hintergrund geladen (auch im Infobereich regelmäßig geprüft) und beim Beenden oder beim nächsten Start installiert, ohne ein Spiel zu unterbrechen, dessen Spielzeit gerade aufgezeichnet wird. Nach der Installation wird das Änderungsprotokoll angezeigt. Update-Pakete kommen zuerst von CNB, bei Fehlern von GitHub (Velopack)

Installationspakete: [GitHub Releases](https://github.com/TurmoilZoom/Moonward/releases); in Festlandchina auch über [CNB](https://cnb.cool/TurmoilZoom/Moonward/-/releases).

Upstream-Projekt: [Scighost/Starward](https://github.com/Scighost/Starward)  
Danksagung: [CREDITS.md](../CREDITS.md) (Open-Source-Projekte als Referenz für Funktionen und Design)  
Lizenz: [MIT](../LICENSE)

Datenschutz: [docs/Privacy.md](Privacy.md)
