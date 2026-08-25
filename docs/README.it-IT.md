<h1 align="center">Moonward</h1>

<p align="center">
  Un launcher di terze parti open source basato su <a href="https://github.com/Scighost/Starward">Starward</a> per i giochi PC di miHoYo<br/>
  <a href="https://github.com/TurmoilZoom/Moonward/releases/latest">Download</a>
</p>

<p align="center">
  <a href="../README.md">简体中文</a>
  · <a href="README.zh-TW.md">繁體中文</a>
  · <a href="README.en-US.md">English</a>
  · <a href="README.de-DE.md">Deutsch</a>
  · <a href="README.es-ES.md">Español</a>
  · Italiano
  · <a href="README.ja-JP.md">日本語</a>
  · <a href="README.ko-KR.md">한국어</a>
  · <a href="README.ru-RU.md">Русский</a>
  · <a href="README.th-TH.md">ไทย</a>
  · <a href="README.vi-VN.md">Tiếng Việt</a>
</p>


---

Sulla base dello Starward upstream, le azioni di tutti i giorni stanno in un collegamento sul desktop e in un unico URL, con miglioramenti a check-in, gacha e sfondi. Funzioni principali:

#### Gacha

- **Cronologia gacha** — Le statistiche dei banner si riordinano trascinando (scorrimento orizzontale automatico vicino al bordo); l’elenco si scorre trascinando; le statistiche restano in alto. Streak UP / miss e probabilità di vincita si vedono subito. Il pity di Miliastra Wonderland usa una barra di avanzamento
- **Filtro e condivisione** — Il menu a tendina della barra del titolo sceglie quali banner mostrare: seleziona tutto / inverti / reimposta. Un clic genera un’immagine opaca da condividere, con pity e avanzamento della garanzia
- **Sincronizzazione gacha** — Genshin Impact / Zenless Zone Zero e altri possono aggiornare i record tramite miHoYo BBS. I personaggi nuovi non ancora in catalogo ricevono icona e nome in automatico. I nomi degli oggetti seguono la lingua dell’app
- **Scambio dati** — Import / export della cronologia gacha in UIGF. La cronologia si può importare in sola lettura dallo Starward upstream

#### Account e toolbox

- **Check-in giornaliero** — Check-in miHoYo BBS / HoYoLAB, interruttore per ogni gioco, check-in automatico e recupero. All’avvio del gioco da collegamento / URL / riga di comando, quell’account fa anche un check-in a parte
- **Accesso** — Server cinese: codice SMS sul telefono; server internazionale: login web. Se la sessione scade, viene rinnovata automaticamente quando possibile, senza rifare il login ogni volta
- **Report mensili e note** — I report mensili del toolbox (Calendario mensile dell'esplorazione / Report mensile di Inter-Knot / Diario del viaggiatore) condividono lo stesso layout. Il report Inter-Knot corregge i dati giornalieri tra fusi orari e mostra di default il mese corrente. Se le note in tempo reale incontrano il controllo del rischio, c’è un ingresso di verifica

#### Avvio

- **Più profili di avvio** — Per lo stesso gioco si possono salvare set illimitati di parametri e programmi di avvio personalizzati. Cambiare o modificare non richiede di reinserire tutto; si può dare un nome e creare un collegamento sul desktop
- **Protocollo URL** — `moonward://` avvia / ferma / riavvia il gioco, il profilo e l’account indicati, oppure esegue solo il check-in. Si può incorporare in script o pagine web (vedi [docs/UrlProtocol](UrlProtocol.md))
- **Avvio rapido** — Il menu hamburger della home raggruppa impostazioni di gioco, avvio rapido e «crea collegamento del menu Start»

#### Aspetto e sfondo

- **Sfondi Trust** — In Zenless Zone Zero gli sfondi dinamici Trust e quelli statici Mindscape della wiki si possono scaricare e impostare come sfondo personalizzato. Aprendo la galleria si usa la cache locale; gli aggiornamenti si verificano in silenzio in background
- **Sfondo personalizzato** — Finestra dedicata per immagine / video (trascina sulla home per sostituire). Il ripristino dal vassoio non sfarfalla più. Dopo l’aggiornamento dell’elenco sfondi resta la preferenza del poster

#### Altro

- **Integrazione di sistema** — Avvio opzionale all’accesso Windows nel vassoio. Nella pagina Informazioni si precompilano i dati diagnostici, si apre il feedback GitHub e la cartella dei log in un clic
- **Aggiornamenti silenziosi** — La nuova versione si scarica in background, si installa dopo la chiusura e al successivo avvio mostra il contenuto dell’aggiornamento (Velopack + GitHub Releases)

I pacchetti di installazione sono in [Releases](https://github.com/TurmoilZoom/Moonward/releases).

Progetto upstream: [Scighost/Starward](https://github.com/Scighost/Starward)  
Ringraziamenti: [CREDITS.md](../CREDITS.md) (progetti open source di riferimento per funzioni e design)  
Licenza: [MIT](../LICENSE)

Privacy: [docs/Privacy.md](Privacy.md)
