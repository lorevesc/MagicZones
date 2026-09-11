# MagicZones

Zone magiche per setup multi-monitor su Windows 10/11. Trascini una finestra e **sopra la finestra
appare un popup con la mini-mappa** di tutti i monitor e delle loro zone. Porti il cursore su una
zona del popup e rilasci: la finestra viene **lanciata** lì, anche sul monitor più lontano, con
un'animazione di volo. Rilasci fuori dal popup = spostamento normale.

C'è anche una modalità alternativa "zone a tutto schermo" (menu *Modalità*) in cui le zone vengono
disegnate direttamente sui monitor e si può lanciare la finestra con uno scatto del mouse.

## Avvio

```powershell
.\build.ps1 -Run
```

Il risultato è `bin\MagicZones.exe`, un singolo exe su .NET Framework 4.8 (già incluso in Windows,
niente da installare). Per compilare serve il Roslyn di Visual Studio (`build.ps1` lo trova da solo).
Lanciare l'exe una seconda volta apre l'editor delle zone nell'istanza già in esecuzione.

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

- Le finestre di programmi avviati **come amministratore** non si possono spostare da un processo
  normale (limite di Windows/UIPI): per quelle, avvia anche MagicZones come amministratore.
- Se le Snap Layouts di Windows 11 (il menu che compare trascinando in alto) danno fastidio, si
  disattivano in *Impostazioni > Sistema > Multitasking*.

## Sviluppo

```
src/
  Program.cs        entry point, istanza singola, --selftest / --preview / --export-icon
  TrayApp.cs        icona nel tray, menu, hotkey, cambi monitor
  DragTracker.cs    hook WinEvent sul trascinamento, misura velocità, decide drop/lancio
  Popup.cs          mini-mappa sopra la finestra trascinata
  ZoneManager.cs    zone in pixel, hit test, fisica del lancio, zona vicina
  WindowMover.cs    SetWindowPos con compensazione bordi invisibili, animazione, riassestamento DPI
  Overlay.cs        overlay per monitor durante il drag
  Editor.cs         editor zone a schermo intero
  LayeredWindow.cs  finestre con alpha per pixel (UpdateLayeredWindow + DIB)
  Config.cs, Json.cs, Monitors.cs, Geometry.cs, Support.cs, Native.cs
```

- `bin\MagicZones.exe --selftest | Out-String` esegue i test della logica (JSON, zone, lancio) su un desktop sintetico.
- `bin\MagicZones.exe --preview .\preview` salva in PNG overlay, popup ed editor di ogni monitor senza mostrarli.
- `tools\DragHarness.cs` è un test end-to-end: apre una finestra di prova e la trascina col mouse vero
  (SendInput) con MagicZones in esecuzione: rilascio da fermo, finestra in cima, massimizzata, popup
  con cambio idea, lancio, ripristino dimensione, ripresa subito dopo l'atterraggio. Muove il mouse per
  circa 30 secondi e clicca solo sulla propria finestra.
