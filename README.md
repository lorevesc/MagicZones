# MagicZones

Zone magiche per setup multi-monitor su Windows 10/11. Trascini una finestra e **sopra la finestra
appare un popup con la mini-mappa** di tutti i monitor e delle loro zone. Porti il cursore su una
zona del popup e rilasci: la finestra viene **lanciata** lì, anche sul monitor più lontano, con
un'animazione di volo. Rilasci fuori dal popup = spostamento normale.

C'è anche una modalità alternativa "zone a tutto schermo" (menu *Modalità*) in cui le zone vengono
disegnate direttamente sui monitor e si può lanciare la finestra con uno scatto del mouse.

## Compilare e installare

```powershell
.\build.ps1              # Release -> bin\Release\MagicZones.exe
.\build.ps1 -Installer   # + dist\MagicZones-Setup-<versione>.exe (serve Inno Setup 6)
```

`MagicZones.csproj` è un progetto MSBuild classico su .NET Framework 4.8 (già incluso in Windows,
niente da installare per usarlo). Per compilare basta il MSBuild di Visual Studio: `build.ps1` lo
trova da solo. Nessun pacchetto NuGet, nessun offuscatore o packer: l'exe è un normale assembly IL
con nome prodotto, azienda e versione nelle proprietà del file (si cambiano nel `.csproj`).

Per usarlo davvero, installalo con `MagicZones-Setup-x.y.z.exe`: va in
`%LOCALAPPDATA%\Programs\MagicZones`, senza diritti di amministratore, con collegamento nel menu Start,
opzione "Avvia con Windows" e disinstallazione da Impostazioni > App. Lanciare l'exe una seconda volta
apre l'editor delle zone nell'istanza già in esecuzione.

## Uso

### Modalità popup (default)

| Azione | Cosa succede |
|---|---|
| Trascina una finestra | Sopra la finestra (o sopra il suo contenuto, se è in cima allo schermo) appare la mini-mappa |
| Cursore su una zona del popup | La zona si illumina, una freccia mostra il lancio e sul monitor reale compare l'anteprima della destinazione |
| Rilascia sulla zona | La finestra vola lì e si incastra (anche sull'altro monitor) |
| Rilascia fuori dal popup | Spostamento normale, il popup si dissolve |
| Allontanarsi dal popup | Diventa trasparente per non dare fastidio |
| `Ctrl` passando su più zone | Le unisce: la finestra le occupa tutte |
| `Shift` mentre trascini | Chiude il popup (in modalità "Solo Shift" è il contrario) |
| Monitor senza zone | Nel popup è una zona unica a schermo intero |

### Modalità zone a tutto schermo

| Azione | Cosa succede |
|---|---|
| Trascina una finestra | Compaiono le zone su tutti i monitor e quella sotto il cursore si illumina |
| Rilascia sopra una zona | La finestra si incastra nella zona (con la spaziatura configurata) |
| **Lancia** (rilascio in movimento) | La finestra vola nella zona in quella direzione, zona arancione = destinazione |
| `Ctrl` mentre trascini | Unisce più zone: la finestra le occupa tutte |
| `Shift` mentre trascini | Nessuna zona, spostamento normale (in modalità "Solo Shift" è il contrario) |

### Sempre attivi

| Azione | Cosa succede |
|---|---|
| Trascini fuori una finestra agganciata | Torna alla dimensione originale |
| `Ctrl+Alt+Win+←↑→↓` | Sposta la finestra attiva nella zona vicina (anche tra monitor) |
| `Ctrl+Alt+Win+Z` o doppio clic sull'icona | Apre l'editor delle zone |

### Lancio a scatto (solo modalità tutto schermo): come funziona

Mentre trascini viene misurata la velocità del cursore negli ultimi 80 ms. Se al rilascio supera
`throwMinSpeed` (px/s), la finestra prosegue lungo la traiettoria per `velocità × throwMomentum`
pixel e atterra nell'ultima zona attraversata. Un lancio forte sbatte contro il bordo del desktop,
gli spazi vuoti tra monitor vengono sorvolati. Se ti fermi prima di rilasciare, è un normale drop.

### Editor zone

Si apre a schermo intero su tutti i monitor.

- Trascina sul vuoto per disegnare una zona. Trascina bordi e angoli per ridimensionare, il centro per spostare.
- I bordi si agganciano (magnete) a bordi schermo, altre zone, metà/terzi/quarti. `Shift` disattiva il magnete.
- **Doppio clic** o `T` cambia il tipo di zona: `SNAP` (incastra), `MASSIMIZZA` (massimizza su quel monitor), `MINIMIZZA` (una zona "cestino").
- Tasto destro o `Canc` elimina, `Ctrl+Z` annulla, `H` nasconde la barra.
- `Invio` salva, `Esc` esce (chiede conferma se ci sono modifiche).
- Preset pronti: 2/3 colonne, 25|50|25, Main + 2, 2×2, 2/3 righe (comodi sul monitor verticale).

## Configurazione

`%APPDATA%\MagicZones\config.json` (dal menu dell'icona: *Apri cartella configurazione*, poi
*Ricarica configurazione*). Le zone sono salvate in frazioni dell'area di lavoro di ogni monitor,
quindi reggono i cambi di risoluzione. I monitor sono riconosciuti per ID hardware, quindi le zone
restano sul monitor giusto anche se cambia la numerazione.

| Chiave | Default | Significato |
|---|---|---|
| `mode` | `"popup"` | `"popup"` = mini-mappa sopra la finestra, `"overlay"` = zone a tutto schermo |
| `popupWidth` | `460` | Larghezza del popup (px a 100%, scalata col DPI) |
| `activation` | `"always"` | `"always"` = popup/zone a ogni trascinamento, `"shift"` = solo tenendo Shift |
| `throwEnabled` | `true` | Lancio a scatto (solo modalità overlay) |
| `throwMinSpeed` | `2600` | Velocità minima al rilascio per considerarlo un lancio (px fisici/s) |
| `throwMomentum` | `0.35` | Distanza di volo = velocità × questo valore (secondi) |
| `animate`, `animationMs` | `true`, `220` | Animazione di atterraggio |
| `gap` | `8` | Spaziatura tra finestre (px a 100%, scalata col DPI del monitor) |
| `restoreSizeOnUnsnap` | `true` | Ridà la dimensione originale quando porti la finestra fuori dalle zone |
| `hotkeys` | `true` | Scorciatoie `Ctrl+Alt+Win+…` |
| `accentColor`, `throwColor` | blu, arancio | Colori overlay |
| `excludedProcesses` | `[]` | Processi da ignorare, es. `["Photoshop", "obs64"]` |
| `debugLog` | `false` | Traccia ogni trascinamento nel log (per capire i problemi) |

Log errori: `%APPDATA%\MagicZones\log.txt`.

Il volo della finestra: prende subito la dimensione della zona e poi si sposta soltanto (niente
ridimensionamento a ogni fotogramma, che fa "ondeggiare" il contenuto), rallentando in atterraggio
senza rimbalzo. Se la riprendi mentre vola o appena atterrata, MagicZones la lascia subito.

## Note

- Le finestre di programmi avviati **come amministratore** (es. Supremo, Gestione attività, un
  terminale admin) non si possono spostare da un processo normale: è un blocco di Windows (UIPI).
  MagicZones le riconosce: il popup lo scrive in giallo e al rilascio spiega cosa fare. Se ti serve,
  clic destro sull'icona → **Riavvia come amministratore** (una conferma UAC, vale fino alla
  chiusura). Le app che girano come SYSTEM restano intoccabili anche così.
- **Avvio automatico**: solo tramite il valore `MagicZones` in
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, scritto dall'installer o dalla voce di menu
  "Avvia con Windows" (che lo toglie anche). Si attiva solo dalla copia installata in
  `%LOCALAPPDATA%\Programs` o `Program Files`, mai da Desktop, OneDrive, Download o Temp. Niente
  attività pianificate e niente avvio come amministratore: a MagicZones non servono.
- Se le Snap Layouts di Windows 11 (il menu che compare trascinando in alto) danno fastidio, si
  disattivano in *Impostazioni > Sistema > Multitasking*.

## Antivirus (Microsoft Defender)

Una versione precedente veniva cancellata da Defender (`Behavior:Win32/Execution.A!ml`, falso
positivo): un exe non firmato, lanciato da una cartella OneDrive, avviava `schtasks.exe` nascosto
per creare un'attività pianificata con privilegi massimi. Ora l'app:

- non avvia mai `schtasks`, `cmd` o `powershell` e non crea attività pianificate;
- non si copia da sola né si reinstalla: l'installazione la fa `MagicZones-Setup`;
- scrive l'avvio automatico solo su richiesta, nella chiave Run dell'utente, verso la copia installata;
- gira `asInvoker` (nessuna richiesta di amministratore all'avvio).

Per ridurre ancora i falsi positivi: firma exe e installer con un certificato di firma del codice
(la direttiva `SignTool` è già predisposta in `installer\MagicZones.iss`) e, se Defender segnala
ancora qualcosa, invia il file come falso positivo a Microsoft
(https://www.microsoft.com/wdsi/filesubmission). Non creare esclusioni in Defender per aggirarlo.
- Se le Snap Layouts di Windows 11 (il menu che compare trascinando in alto) danno fastidio, si
  disattivano in *Impostazioni > Sistema > Multitasking*.

## Sviluppo

```
MagicZones.csproj   progetto MSBuild (identità del file, Release in bin\Release)
installer/          MagicZones.iss: installer Inno Setup per utente
src/
  Program.cs        entry point, istanza singola, --selftest / --preview / --export-icon
  TrayApp.cs        icona nel tray, menu, hotkey, cambi monitor
  Startup.cs        avvio automatico (chiave Run HKCU) e controllo percorso stabile
  Integrity.cs      riconosce le finestre di app con privilegi più alti (UIPI)
  DragTracker.cs    hook WinEvent sul trascinamento, misura velocità, decide drop/lancio
  Popup.cs          mini-mappa sopra la finestra trascinata
  ZoneManager.cs    zone in pixel, hit test, fisica del lancio, zona vicina
  WindowMover.cs    SetWindowPos con compensazione bordi invisibili, animazione, riassestamento DPI
  Overlay.cs        overlay per monitor durante il drag
  Editor.cs         editor zone a schermo intero
  LayeredWindow.cs  finestre con alpha per pixel (UpdateLayeredWindow + DIB)
  Config.cs, Json.cs, Monitors.cs, Geometry.cs, Support.cs, Native.cs
```

- `bin\Release\MagicZones.exe --selftest | Out-String` esegue i test della logica (JSON, zone, lancio, percorsi di avvio) su un desktop sintetico.
- `bin\Release\MagicZones.exe --preview .\preview` salva in PNG overlay, popup ed editor di ogni monitor senza mostrarli.
- `tools\DragHarness.cs` è un test end-to-end: apre una finestra di prova e la trascina col mouse vero
  (SendInput) con MagicZones in esecuzione: rilascio da fermo, finestra in cima, massimizzata, popup
  con cambio idea, lancio, ripristino dimensione, ripresa subito dopo l'atterraggio. Muove il mouse per
  circa 30 secondi e clicca solo sulla propria finestra.
