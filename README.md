# TelemetryDash

**Real-time sensor telemetry monitoring for Windows.**

🌐 **[English](#english) · [Italiano](#italiano)**

---

<a name="english"></a>

## English

### What it is

**TelemetryDash** is a Windows desktop application (WPF, .NET 8) for **monitoring sensor
telemetry in real time**. It ingests a continuous stream of readings from one of several
interchangeable data sources, shows each channel on a live dashboard, raises alarms when
values cross configured thresholds, flags statistical anomalies with a machine-learning
model, records every session to a local database, and can replay past sessions or export a
signed PDF report.

Out of the box it monitors four channels typical of an industrial / aerospace test bench:

| Channel    | Quantity     | Unit    | Nominal range |
|------------|--------------|---------|---------------|
| `TEMP_A1`  | Temperature  | °C      | 30 – 100      |
| `PRESS_B2` | Pressure     | hPa     | 900 – 1100    |
| `VIB_C3`   | Vibration    | g       | 0 – 1.5       |
| `FLOW_D4`  | Flow rate    | L/min   | 80 – 170      |

### What it's for

It targets scenarios where you need to **watch live sensor data and not miss anything**:
test benches, ground stations, industrial rigs, lab data acquisition. Concretely it lets you:

- See current values, quality flags and a rolling sparkline for every channel.
- Get **threshold alarms** (per-channel min/max, with `Warning` / `Critical` severity).
- Get **anomaly detection** that learns each channel's normal behaviour and flags spikes
  that simple thresholds would miss.
- **Record** every session to a database so nothing is lost.
- **Replay** any recorded session at variable speed for after-the-fact analysis.
- **Export a PDF report** with per-channel statistics and the full alarm history.

### Features

- **Pluggable data sources** — data sources are plugins discovered at runtime. Two ship
  in the box (`Simulator`, `TCP Receiver`) and external ones can be dropped into a
  `Plugins/` folder without recompiling.
- **Live dashboard** — one card per channel with current value, unit, quality flag and a
  sparkline of recent history.
- **At-a-glance channel health** — a card turns **amber/red** the moment its value breaches
  the alarm threshold (with a quality-flag badge), and greys out with **NO DATA** if the
  sensor stops sending for a few seconds (staleness detection).
- **Threshold alarms** — configurable min/max per channel; breaches appear in the *Active
  Alarms* panel with a live count, raise an **audible alert** on `Critical`, and can be
  cleared with **Acknowledge**. They are also persisted.
- **In-app settings** — edit per-channel alarm thresholds (min / max / severity) from a
  Settings panel; saved to `settings.json` and reloaded on the next launch.
- **ML anomaly detection** — per-channel IID Spike Detection (ML.NET). Each detector goes
  through a learning phase before it starts flagging anomalies.
- **Session recording** — readings and alarms are batched and written to SQLite.
- **Playback** — load any past session and replay it, honouring the original time gaps,
  with a speed multiplier from 0.25× to 8× and a **scrubber/timeline** (drag to seek).
- **PDF reports** — min / max / mean / std-dev / count per channel plus alarm history,
  rendered with QuestPDF and accompanied by an RSA signature file. A toast offers a
  one-click **Open** when the report is ready.
- **Action feedback** — toast notifications for errors and successes, plus a connection
  indicator that distinguishes *Disconnected / Connecting / Connected*.
- **Rolling CSV logs** — structured daily log files via Serilog.
- **Runtime localization** — switch the UI between **English and German** on the fly.

### Getting started

**Prerequisites**

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3 *(optional — only for the TCP test simulator)*

**Build & run**

```bash
# Build the whole solution
dotnet build TelemetryDash.sln

# Run the app
dotnet run --project TelemetryDash
```

**Using the app**

1. Pick a data source from the dropdown (`Simulator` is selected by default).
2. Click **Connect** — channel cards appear and update roughly twice a second.
3. Watch **Active Alarms** and the **Event Log** as values move; threshold breaches and
   anomalies show up there. Cards turn amber/red when a channel is out of range; click
   **Ack** to clear the alarm list.
4. Click **Settings** to adjust per-channel thresholds (min / max / severity) and **Save**.
5. Click **Generate Report** to export a PDF (saved under `Documents/TelemetryDash/`); use
   the **Open** button on the toast to view it.
6. Click **Playback** to leave live mode, choose a recorded session, **Play / Pause / Stop**
   it, set the speed, and drag the **scrubber** to seek.
7. Use the **Language** selector to switch EN / DE at any time.

**Testing with the TCP source**

The `TCP Receiver` plugin reads binary frames over TCP. A Python simulator is included to
feed it:

```bash
python tools/tcp_simulator.py --port 5000 --interval 500
```

Then select **TCP Receiver** in the app and click **Connect**.

**Running the tests**

```bash
dotnet test TelemetryDash.Tests
```

### Architecture & how the technologies are used

The solution is split into clean layers so that the UI, the domain, the business logic and
the data/integration concerns stay independent and testable.

```
TelemetryDash             WPF UI — views, view-models, DI bootstrap (MVVM)
TelemetryDash.Core        Domain models, enums, service interfaces, MVVM base classes
TelemetryDash.Services    Business logic — alarms, anomaly detection, playback, reports, logging
TelemetryDash.Infrastructure   Data access (EF Core/SQLite) + plugin system (MEF)
TelemetryDash.Tests       Unit tests (xUnit + Moq)
```

**How each technology earns its place:**

- **WPF + MVVM** — the UI is fully data-bound. View-models expose `ObservableCollection`s
  and bindable properties; views stay declarative. A small home-grown MVVM kit
  (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`) keeps the dependency surface
  small.
- **Dependency Injection** (`Microsoft.Extensions.DependencyInjection`) — every service is
  registered in `App.xaml.cs` and resolved against its interface, which is what makes the
  whole thing unit-testable.
- **Reactive Extensions** (`System.Reactive`) — each data source exposes its readings as an
  `IObservable<TelemetryReading>`. The UI subscribes on the dispatcher thread, so the
  source doesn't need to know anything about threading or the UI.
- **MEF** (`System.ComponentModel.Composition`) — data sources are plugins. The
  `IDataSourcePlugin` interface is marked `[InheritedExport]`, so any class that implements
  it is discovered automatically — built-in ones from the main assembly, external ones from
  a directory catalog.
- **EF Core + SQLite** — sessions, readings and alarms are persisted to a local
  `telemetry.db`. Readings are written in batches (and indexed on timestamp / channel /
  session) to keep the write path off the hot loop.
- **ML.NET + ML.TimeSeries** — anomaly detection uses IID Spike Detection, one detector per
  channel, each fitted over a sliding window of recent samples.
- **QuestPDF** — reports are composed fluently (header, statistics table, alarm table,
  footer with page numbers) and rendered to PDF.
- **RSA** (`System.Security.Cryptography`) — each report is hashed and signed, with the
  signature written next to the PDF as a `.sig` file.
- **Serilog** — structured logging to rolling daily CSV files under `logs/`.

### TCP binary frame format

The `TCP Receiver` plugin (and the Python simulator) speak this little-endian frame:

```
[4 bytes] payload length (uint32 LE)
[8 bytes] timestamp (Unix ms, int64 LE)
[1 byte]  channel index (0=TEMP_A1, 1=PRESS_B2, 2=VIB_C3, 3=FLOW_D4)
[8 bytes] value (double LE)
[1 byte]  quality flag (0=OK, 1=WARN, 2=ERR)
```

### Tech stack

| Area               | Technology |
|--------------------|------------|
| Runtime / UI       | .NET 8 (`net8.0-windows`), WPF, MVVM |
| Dependency injection | `Microsoft.Extensions.DependencyInjection` |
| Async streams      | `System.Reactive` (Rx.NET) |
| Plugin system      | MEF (`System.ComponentModel.Composition`) |
| Persistence        | Entity Framework Core + SQLite |
| Machine learning   | `Microsoft.ML` + `Microsoft.ML.TimeSeries` |
| Reporting          | QuestPDF |
| Digital signature  | RSA (`System.Security.Cryptography`) |
| Logging            | Serilog (rolling file sink) |
| Testing            | xUnit, Moq, coverlet |
| TCP test simulator | Python 3 (`socket`, `struct`) |

---

<a name="italiano"></a>

## Italiano

### Cos'è

**TelemetryDash** è un'applicazione desktop per Windows (WPF, .NET 8) per il **monitoraggio
in tempo reale di telemetria da sensori**. Riceve un flusso continuo di letture da una tra
più sorgenti dati intercambiabili, mostra ogni canale su una dashboard live, genera allarmi
quando i valori superano le soglie configurate, segnala anomalie statistiche tramite un
modello di machine learning, registra ogni sessione su un database locale e può riprodurre
sessioni passate o esportare un report PDF firmato.

Di base monitora quattro canali tipici di un banco prova industriale / aerospaziale:

| Canale     | Grandezza    | Unità   | Range nominale |
|------------|--------------|---------|----------------|
| `TEMP_A1`  | Temperatura  | °C      | 30 – 100       |
| `PRESS_B2` | Pressione    | hPa     | 900 – 1100     |
| `VIB_C3`   | Vibrazione   | g       | 0 – 1.5        |
| `FLOW_D4`  | Portata      | L/min   | 80 – 170       |

### A cosa serve

È pensato per gli scenari in cui serve **tenere d'occhio i dati dei sensori dal vivo senza
perdere nulla**: banchi prova, stazioni di terra, impianti industriali, acquisizione dati in
laboratorio. In concreto permette di:

- Vedere valori correnti, flag di qualità e una sparkline aggiornata per ogni canale.
- Avere **allarmi a soglia** (min/max per canale, con severità `Warning` / `Critical`).
- Avere il **rilevamento di anomalie** che impara il comportamento normale di ogni canale e
  segnala picchi che le semplici soglie non coglierebbero.
- **Registrare** ogni sessione su database così non si perde niente.
- **Riprodurre** qualsiasi sessione registrata a velocità variabile per l'analisi a posteriori.
- **Esportare un report PDF** con le statistiche per canale e lo storico completo degli allarmi.

### Funzionalità

- **Sorgenti dati a plugin** — le sorgenti dati sono plugin individuati a runtime. Due sono
  incluse (`Simulator`, `TCP Receiver`) e altre esterne si possono aggiungere in una cartella
  `Plugins/` senza ricompilare.
- **Dashboard live** — una card per canale con valore corrente, unità, flag di qualità e una
  sparkline della cronologia recente.
- **Salute del canale a colpo d'occhio** — la card diventa **ambra/rossa** appena il valore
  sfora la soglia d'allarme (con badge del flag di qualità) e si oscura con **NO DATA** se il
  sensore smette di trasmettere per qualche secondo (rilevamento staleness).
- **Allarmi a soglia** — min/max configurabili per canale; gli sforamenti compaiono nel
  pannello *Active Alarms* con un contatore, emettono un **avviso sonoro** sui `Critical` e si
  possono azzerare con **Acknowledge**. Vengono inoltre salvati.
- **Impostazioni in-app** — modifica le soglie d'allarme per canale (min / max / severità) da
  un pannello Settings; salvate in `settings.json` e ricaricate all'avvio successivo.
- **Rilevamento anomalie ML** — IID Spike Detection per canale (ML.NET). Ogni rilevatore
  attraversa una fase di apprendimento prima di iniziare a segnalare anomalie.
- **Registrazione sessioni** — letture e allarmi vengono accumulati a batch e scritti su SQLite.
- **Playback** — carica una sessione passata e la riproduce rispettando gli intervalli di tempo
  originali, con un moltiplicatore di velocità da 0.25× a 8× e una **timeline/scrubber**
  (trascina per saltare a un punto).
- **Report PDF** — min / max / media / dev. standard / conteggio per canale più lo storico
  allarmi, generati con QuestPDF e accompagnati da un file di firma RSA. Un toast offre
  l'apertura con un click (**Open**) quando il report è pronto.
- **Feedback delle azioni** — notifiche toast per errori e successi e un indicatore di
  connessione che distingue *Disconnected / Connecting / Connected*.
- **Log CSV rotanti** — file di log giornalieri strutturati tramite Serilog.
- **Localizzazione a runtime** — cambio lingua dell'interfaccia tra **inglese e tedesco** al volo.

### Come si usa

**Prerequisiti**

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3 *(opzionale — solo per il simulatore TCP di test)*

**Build ed esecuzione**

```bash
# Compila l'intera solution
dotnet build TelemetryDash.sln

# Avvia l'app
dotnet run --project TelemetryDash
```

**Uso dell'app**

1. Scegli una sorgente dati dal menu a tendina (`Simulator` è selezionato di default).
2. Premi **Connect** — le card dei canali compaiono e si aggiornano circa due volte al secondo.
3. Osserva **Active Alarms** e l'**Event Log** mentre i valori cambiano: sforamenti di soglia
   e anomalie compaiono lì. Le card diventano ambra/rosse quando un canale è fuori range;
   premi **Ack** per azzerare la lista allarmi.
4. Premi **Settings** per regolare le soglie per canale (min / max / severità) e **Save**.
5. Premi **Generate Report** per esportare un PDF (salvato in `Documenti/TelemetryDash/`);
   usa il pulsante **Open** sul toast per aprirlo.
6. Premi **Playback** per uscire dalla modalità live, scegliere una sessione registrata,
   farla partire con **Play / Pause / Stop**, regolare la velocità e trascinare lo **scrubber**.
7. Usa il selettore **Language** per passare tra EN / DE in qualsiasi momento.

**Test con la sorgente TCP**

Il plugin `TCP Receiver` legge frame binari su TCP. È incluso un simulatore Python per
alimentarlo:

```bash
python tools/tcp_simulator.py --port 5000 --interval 500
```

Poi seleziona **TCP Receiver** nell'app e premi **Connect**.

**Esecuzione dei test**

```bash
dotnet test TelemetryDash.Tests
```

### Architettura e come ho usato le tecnologie

La solution è divisa in layer puliti così che UI, dominio, logica di business e
accesso/integrazione dati restino indipendenti e testabili.

```
TelemetryDash             UI WPF — view, view-model, bootstrap della DI (MVVM)
TelemetryDash.Core        Modelli di dominio, enum, interfacce dei servizi, classi base MVVM
TelemetryDash.Services    Logica di business — allarmi, anomalie, playback, report, logging
TelemetryDash.Infrastructure   Accesso ai dati (EF Core/SQLite) + sistema di plugin (MEF)
TelemetryDash.Tests       Test unitari (xUnit + Moq)
```

**Perché ogni tecnologia è dove è:**

- **WPF + MVVM** — l'interfaccia è interamente in data-binding. I view-model espongono
  `ObservableCollection` e proprietà bindabili; le view restano dichiarative. Un piccolo kit
  MVVM scritto a mano (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`) mantiene
  ridotte le dipendenze.
- **Dependency Injection** (`Microsoft.Extensions.DependencyInjection`) — ogni servizio è
  registrato in `App.xaml.cs` e risolto sulla sua interfaccia: è ciò che rende il tutto
  testabile a unità.
- **Reactive Extensions** (`System.Reactive`) — ogni sorgente dati espone le letture come
  `IObservable<TelemetryReading>`. La UI si sottoscrive sul thread del dispatcher, così la
  sorgente non deve sapere nulla di threading o interfaccia.
- **MEF** (`System.ComponentModel.Composition`) — le sorgenti dati sono plugin. L'interfaccia
  `IDataSourcePlugin` è marcata `[InheritedExport]`, quindi qualsiasi classe che la implementa
  viene individuata in automatico — quelle integrate dall'assembly principale, quelle esterne
  da un catalogo su cartella.
- **EF Core + SQLite** — sessioni, letture e allarmi sono persistiti in un `telemetry.db`
  locale. Le letture vengono scritte a batch (e indicizzate su timestamp / canale / sessione)
  per tenere la scrittura fuori dal percorso caldo.
- **ML.NET + ML.TimeSeries** — il rilevamento anomalie usa IID Spike Detection, un rilevatore
  per canale, ciascuno addestrato su una finestra scorrevole di campioni recenti.
- **QuestPDF** — i report sono composti in modo fluente (intestazione, tabella statistiche,
  tabella allarmi, piè di pagina con numerazione) e renderizzati in PDF.
- **RSA** (`System.Security.Cryptography`) — ogni report viene sottoposto a hash e firmato,
  con la firma scritta accanto al PDF come file `.sig`.
- **Serilog** — logging strutturato su file CSV giornalieri rotanti sotto `logs/`.

### Formato del frame binario TCP

Il plugin `TCP Receiver` (e il simulatore Python) parlano questo frame little-endian:

```
[4 byte] lunghezza payload (uint32 LE)
[8 byte] timestamp (Unix ms, int64 LE)
[1 byte] indice canale (0=TEMP_A1, 1=PRESS_B2, 2=VIB_C3, 3=FLOW_D4)
[8 byte] valore (double LE)
[1 byte] flag di qualità (0=OK, 1=WARN, 2=ERR)
```

### Stack tecnologico

| Ambito              | Tecnologia |
|---------------------|------------|
| Runtime / UI        | .NET 8 (`net8.0-windows`), WPF, MVVM |
| Dependency injection | `Microsoft.Extensions.DependencyInjection` |
| Stream asincroni    | `System.Reactive` (Rx.NET) |
| Sistema di plugin   | MEF (`System.ComponentModel.Composition`) |
| Persistenza         | Entity Framework Core + SQLite |
| Machine learning    | `Microsoft.ML` + `Microsoft.ML.TimeSeries` |
| Reportistica        | QuestPDF |
| Firma digitale      | RSA (`System.Security.Cryptography`) |
| Logging             | Serilog (sink su file rotanti) |
| Test                | xUnit, Moq, coverlet |
| Simulatore TCP      | Python 3 (`socket`, `struct`) |
