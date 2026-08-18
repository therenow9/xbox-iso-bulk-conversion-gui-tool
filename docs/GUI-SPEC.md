# XISO Converter GUI — build spec

A Windows desktop front-end for bulk-converting Original Xbox XISO images to
extracted game folders. Hand this file to Claude Code as the starting brief.

## What this app does — and nothing else

1. **Bulk convert** every new XISO in a source folder to an extracted game folder
2. **Name folders so they work on the Xbox** (FATX rules, below)
3. **Skip games already converted**, so it can be re-run as downloads finish

That is the whole scope. Resist adding features.

## Files you need

| File | Role |
|---|---|
| `Convert-Xiso.ps1` | The engine. The GUI shells out to this. Do not modify its logic. |
| `GUI-SPEC.md` | This file. |
| `extract-xiso.exe` | Runtime dependency, path chosen by the user in the UI. |

Nothing else. Any other `.ps1` files in the toolkit are unrelated FTP
troubleshooting tools and play no part in this app.

## The core rule

**The GUI does not reimplement conversion logic.** It shells out to
`Convert-Xiso.ps1 -Json` and renders the event stream.

That script already handles a set of rules that took real-world failures to
discover, and getting any of them wrong produces games that copy fine on the PC
and then fail on the console in confusing ways:

- FATX (the Xbox filesystem) caps names at **42 characters**
- FATX rejects `" * + , / : ; < = > ? \ |` — **comma, plus, semicolon and equals
  are legal on Windows**, so `Thing, The (USA)` copies fine and then fails over
  FTP with `550 Filename invalid`
- Over-length names are shortened by dropping bracketed tags, then trailing
  region/dump junk, then a leading article, and only cut mid-title as a last resort
- Extraction goes to a `<name>.partial` folder and is renamed only after
  extract-xiso exits clean **and** `default.xbe` is confirmed present, so a
  cancelled run never leaves a folder that looks finished
- Folders from earlier naming rules are renamed rather than re-extracted

If the GUI ever needs behaviour the script doesn't have, **add a parameter to the
script**, don't fork the logic into C#.

---

## Event protocol

Invoke:

```
powershell -NoProfile -ExecutionPolicy Bypass -File "Convert-Xiso.ps1" -Json ^
    -ExtractXiso "<exe>" -Source "<dir>" -Output "<dir>" [-SkipSystemUpdate] [-Force] [-DryRun]
```

In `-Json` mode the script writes **nothing but newline-delimited JSON on
stdout** (verified: no stray text, stderr empty). One object per line. Read it a
line at a time and update the UI as each arrives — do not buffer to the end.

### Events

| `event` | When | Fields |
|---|---|---|
| `start` | Once, after preflight | `total`, `tool`, `source`, `output`, `dryRun` |
| `game-start` | Before each image | `index`, `total`, `iso`, `folder`, `bytes` |
| `log` | Each line extract-xiso prints | `index`, `text` |
| `game-done` | After each image | `index`, `iso`, `folder`, `status`, `seconds`, `bytes`, `detail` |
| `summary` | Once at the end | `extracted`, `skipped`, `failed`, `todo`, `seconds`, `log` |
| `warning` | Non-fatal | `code` (`low-space`), plus code-specific fields |
| `error` | Fatal, script exits | `code` (`tool-missing`, `source-missing`), `text` |

`status` is one of `OK`, `Skipped`, `Failed`, `ToDo`.
`folder` is the FATX-safe name the script chose — show this, since seeing
`Thing, The (USA).iso -> Thing The (USA)` is exactly the reassurance the user wants.
`detail` carries the failure reason when `status` is `Failed`.

### Exit codes

`0` all good · `1` preflight failure (an `error` event explains) · `2` at least one image failed

### Real output

Dry run:

```json
{"event":"start","total":3,"tool":"...","source":"...","output":"...","dryRun":true}
{"event":"game-start","index":3,"total":3,"iso":"Thing, The (USA).iso","folder":"Thing The (USA)","bytes":150000}
{"event":"game-done","index":3,"iso":"Thing, The (USA).iso","folder":"Thing The (USA)","status":"ToDo","seconds":0,"bytes":0}
{"event":"summary","extracted":0,"skipped":0,"failed":0,"todo":3,"seconds":0.1,"log":"..."}
```

Real run with a failure:

```json
{"event":"log","index":1,"text":"extract-xiso v2.7.1"}
{"event":"game-done","index":1,"iso":"fail game.iso","folder":"fail game","status":"Failed","seconds":0.0,"bytes":0,"detail":"extract-xiso exited with code 1"}
{"event":"game-done","index":2,"iso":"Halo 2.iso","folder":"Halo 2","status":"OK","seconds":0.0,"bytes":42048,"detail":""}
```

Note JSON key order is not stable — parse by name, never by position.

---

## UI

Single window, resizable, minimum ~900x600.

**Top — paths.** Three rows, each a read-only textbox plus a Browse button:
extract-xiso.exe (file picker), Source folder, Output folder. A red inline
warning when a path doesn't exist. Persist all three (see Settings).

**Middle — the game list.** A DataGridView, one row per ISO:

| Column | Notes |
|---|---|
| ☑ | checkbox, include in run |
| ISO | source filename |
| Size | humanised, right-aligned |
| → Folder | the FATX-safe name; **highlight when it differs from the ISO name** |
| Status | Pending / Working / OK / Skipped / Failed |
| Time | seconds, filled in on `game-done` |

Populate it by running the script with `-DryRun -Json`, which reports `ToDo` vs
`Skipped` without extracting anything. A **Refresh** button re-runs that scan.
Default the checkboxes to `ToDo` rows only, so re-running after new downloads
selects exactly what's new. Colour rows by status; sortable columns.

**Options.** Checkboxes mapping to script switches: Skip $SystemUpdate (`-s`),
Force re-extract, and a Dry run toggle.

**Bottom — progress and log.** Overall progress bar (`index`/`total` from
`game-start`), current-game label, a collapsible monospace log pane fed by `log`
events, Convert / Cancel buttons, and a status strip showing the running
extracted / skipped / failed tally.

---

## Threading

The thing that makes or breaks this. Conversion runs for minutes to hours.

- Launch with `ProcessStartInfo` — `RedirectStandardOutput = true`,
  `UseShellExecute = false`, `CreateNoWindow = true`
- Read stdout with `OutputDataReceived` + `BeginOutputReadLine`, or
  `await reader.ReadLineAsync()` in an async loop
- **Every UI update must go through `Invoke`/`BeginInvoke`** — the handler runs
  on a background thread
- Disable the path controls and Convert while running; keep Cancel live
- Cancel = kill the process tree. The script's `.partial` design means a killed
  run leaves no folder that looks finished, but the orphaned `.partial` folder is
  yours to clean up — delete `<Output>\*.partial` after a cancel
- Don't append every `log` line straight to a TextBox; batch on a timer or it
  will bog down on a long run

## Settings

`%APPDATA%\XisoConverter\settings.json` — the three paths, option checkbox
states, window size and position. Write on close, load on open, fall back to
sensible defaults when the file is absent or malformed. Never crash on a bad
settings file; just ignore it.

## Milestones

Build in this order so there's something runnable early:

1. Paths + Browse + settings persistence. Hardcode a fake game list.
2. Run `-DryRun -Json`, parse the stream, populate the grid. No conversion yet.
3. Real conversion with live progress, log pane, and the status tally.
4. Cancel, `.partial` cleanup, and disabling controls mid-run.
5. Polish: sorting, row colours, remembering checkbox state, a summary dialog.

## Testing without an Xbox or real ISOs

Don't wait on 4 GB images to test the UI. Write a stub that mimics
extract-xiso — takes `-x -s -d <dir> <iso>`, sleeps a second or two, creates
`<dir>\default.xbe` plus a dummy `Media\movie.wmv`, and exits `1` when the
filename starts with `fail`. Point `-ExtractXiso` at it. That exercises every
status path in seconds. This is how the script itself was tested.

Cases worth covering: names with commas, names over 42 characters, an ISO that
fails, one already extracted (skip path), and cancelling mid-run.

## Out of scope

No FTP. No uploading to the console. No artwork. No library management. If the
converter names folders correctly and skips what's done, the job is finished —
the naming rules are what made uploads fail in the first place, and fixing them
at extraction time removes the need for any repair tooling downstream.
