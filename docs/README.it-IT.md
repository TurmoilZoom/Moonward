<p align="center">
  <img src="images/banner.png" alt="Moonward" width="720" />
</p>

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

Sulla base dello Starward upstream, le azioni di tutti i giorni stanno in un collegamento sul desktop e in un unico URL, con miglioramenti a gacha, registri di battaglia, installazione dei giochi, sfondi e altro. Funzioni principali:

#### Gacha

- **Cronologia gacha** — Le statistiche dei banner si riordinano trascinandole e restano fissate in alto. Si può passare dalla vista a elenco a una griglia compatta di icone. Serie di UP / mancati e percentuale di vittoria del 50/50 a colpo d'occhio
- **Filtro e condivisione** — Un menu a tendina nella barra del titolo sceglie quali banner mostrare, con seleziona tutto / inverti / reimposta. Un clic crea un'immagine da condividere in stile vetro smerigliato, con conteggio pity e avanzamento della garanzia
- **Sincronizzazione gacha** — Genshin Impact (server Cina) e Zenless Zone Zero possono sincronizzare i registri da miHoYo BBS; Zenless Zone Zero supporta anche HoYoLAB. Gli oggetti nuovi non ancora in catalogo ricevono automaticamente icona e nome. I nomi degli oggetti seguono la lingua dell'app
- **Miliastra Wonderland** — La pagina della cronologia dei desideri funziona come quella della cronologia gacha (filtro, vista compatta, riordino trascinando, immagine da condividere) e completa le icone degli oggetti mancanti upstream
- **UIGF** — Importazione / esportazione dei registri gacha in formato UIGF

#### Registri di battaglia e toolbox

- **Check-in giornaliero** — Check-in su miHoYo BBS / HoYoLAB, interruttori per gioco, check-in automatico e di recupero. Mentre è residente nell'area di notifica, rifà il check-in dopo ogni reset giornaliero
- **Accesso** — Server Cina con codice SMS; server internazionali con accesso via web. Le sessioni scadute vengono rinnovate automaticamente quando possibile, così non serve riaccedere di continuo
- **Aggiornamento automatico dei registri** — Imposta una frequenza per account e sezione di dati (inclusi mese corrente e precedente dei report mensili); i registri si aggiornano automaticamente all'avvio dell'app
- **Report mensili e note** — I report mensili della toolbox (Calendario mensile dell'Esploratore / Report mensile Inter-Knot / Diario del viaggiatore) condividono lo stesso layout. Il report Inter-Knot conta i dati giornalieri nel fuso orario del server e mostra il mese corrente per impostazione predefinita. Le note in tempo reale di Genshin Impact mostrano più dettagli. Quando scatta il controllo del rischio è disponibile un accesso alla verifica
- **Visualizzazione dei registri** — Registri di battaglia e report mensili possono essere esportati come immagine da condividere. Il Teatro Imaginarium segnala i personaggi in prova e di supporto; Anomaly Arbitration mostra i completamenti rapidi e i nomi delle difficoltà
- **Cloud gaming** — Visualizza il tempo rimanente di Cloud Genshin e Cloud Zenless Zone Zero e riscuoti automaticamente il tempo gratuito giornaliero

#### Avvio

- **Più profili di avvio** — Set illimitati di argomenti di avvio e launcher personalizzati per gioco. Ogni profilo ha le proprie impostazioni per l'argomento DX12 e per «avvia tramite CMD»; dai un nome al profilo e crea un collegamento sul desktop
- **Strumenti di terze parti** — Un launcher personalizzato può essere anche un link a un protocollo URL. Gli argomenti della riga di comando più comuni includono gruppi per BetterGI, ZZZ OneDragon e March7th Assistant
- **Protocollo URL** — `moonward://` avvia / arresta / riavvia un gioco, un profilo e un account specifici, oppure esegue solo il check-in. Incorporabile in script o pagine web (vedi [docs/UrlProtocol](UrlProtocol.md))
- **Avvio rapido** — Il menu hamburger della home riunisce impostazioni del gioco, avvio rapido e collegamenti al gioco. I collegamenti possono avviarsi senza la richiesta UAC e le relative attività pianificate si gestiscono per gioco nelle impostazioni
- **Tempo di gioco** — Mentre il gioco è in esecuzione, il pulsante Avvia gioco mostra un timer. Le statistiche del tempo di gioco permettono di cambiare gioco, scegliere anno e mese e creare un'immagine da condividere. Il tempo in cui il PC è in sospensione non viene conteggiato

#### Installazione e aggiornamenti

- **Server collegati tramite hard link** — Quando si aggiorna o ripara un gioco vengono gestiti anche gli altri server che ne condividono i file tramite hard link (l'installazione di base viene sempre aggiornata per prima). La finestra di disinstallazione elenca tutti i server installati dello stesso gioco e può rimuoverli insieme
- **Opzioni di installazione** — Zenless Zone Zero può installare le risorse complete o quelle di base. Accanto al percorso di installazione compare il tipo di disco (SSD / HDD / rimovibile / rete). I download a chunk procedono in parallelo per blocco
- **Riparazione e informazioni sulla versione** — La riparazione ha ora una finestra dedicata e può riparare anche solo il Miliastra Sandbox; il numero di thread di verifica si adatta al tipo di disco. La pagina «Informazioni sulla versione» nelle impostazioni del gioco mostra versione, patch incrementali e MD5 dell'eseguibile principale. La ricerca automatica ritrova i giochi installati dall'app (inclusi gli hard link)

#### Aspetto e sfondo

- **Sfondi Trust** — In Zenless Zone Zero, gli sfondi animati Trust e gli sfondi statici Mindscape della wiki possono essere scaricati e impostati come sfondo personalizzato. La modalità casuale cambia immagine all'avvio, al cambio di gioco o all'apertura dall'area di notifica. La galleria usa la cache locale; gli aggiornamenti sono controllati in silenzio in background
- **Sfondo personalizzato** — Finestra dedicata per immagini / video (trascina sulla home per sostituire). La scelta del poster resta dopo l'aggiornamento dell'elenco sfondi
- **Sfondi video** — I video che non si possono decodificare via hardware vengono convertiti automaticamente in H.264. Se manca l'estensione HEVC / VP9, l'app guida all'installazione o mostra un'immagine statica. A schermo spento il decoder viene rilasciato e non consuma più CPU
- **Barra degli strumenti della home** — Cronologia (conti alla rovescia di banner / eventi), codici di riscatto delle dirette (server Cina) e mappe interattive (Genshin Impact / Honkai: Star Rail)

#### Altro

- **Residente nell'area di notifica** — Avvio facoltativo nell'area di notifica all'accesso. Le scorciatoie globali e il controllo con gamepad continuano a funzionare; il menu dell'area di notifica può attivarli o disattivarli e cercare aggiornamenti
- **Sincronizzazione LAN** — Importa registri gacha, dati della toolbox e tempo di gioco da un altro dispositivo della rete locale. Vengono aggiunti solo i registri che mancano su questo PC; le credenziali di accesso non vengono mai sincronizzate
- **Gestione dei dati** — Database, cache, log e sfondi stanno in un'unica cartella a tua scelta. Dalle impostazioni si può eseguire il backup del database e svuotare la cache. Al primo avvio puoi importare i dati dallo Starward upstream (sola lettura; i dati originali non vengono toccati)
- **Feedback** — La pagina Informazioni precompila con un clic i dati sull'ambiente, apre la pagina delle issue su GitHub e apre la cartella dei log
- **Aggiornamenti silenziosi** — Le nuove versioni si scaricano in background (con controlli periodici anche dall'area di notifica) e si installano all'uscita o all'avvio successivo, senza interrompere un gioco di cui si sta registrando il tempo. Dopo l'installazione viene mostrato il changelog. I pacchetti arrivano prima da CNB e, in caso di errore, da GitHub (Velopack)

I pacchetti di installazione sono in [GitHub Releases](https://github.com/TurmoilZoom/Moonward/releases); dalla Cina continentale si possono scaricare anche da [CNB](https://cnb.cool/TurmoilZoom/Moonward/-/releases).

Progetto upstream: [Scighost/Starward](https://github.com/Scighost/Starward)  
Ringraziamenti: [CREDITS.md](../CREDITS.md) (progetti open source di riferimento per funzioni e design)  
Licenza: [MIT](../LICENSE)

Privacy: [docs/Privacy.md](Privacy.md)
