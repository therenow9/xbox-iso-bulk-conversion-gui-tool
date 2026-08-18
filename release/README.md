# XISO Converter

Bulk-converts Original Xbox XISO images into extracted game folders, named so they
actually work once they reach the console.

Point it at a folder of `.iso` files, press **Convert**, and it extracts each one into
its own folder. Re-run it whenever new downloads finish — anything already converted
is skipped, so only the new images get processed.

---

## What you need

**Nothing.** Unzip this folder anywhere and run `XisoConverter.exe`.

- **No .NET install** — the `.exe` is self-contained.
- **No separate download** — `extract-xiso` is bundled, and the app finds it on its
  own, so the tool path is already filled in the first time you start it. The bundled
  copy is the newest release published by the
  [XboxDev/extract-xiso](https://github.com/XboxDev/extract-xiso) project at the time
  this version was built — `extract-xiso\VERSION.txt` names the exact build, and
  [the releases page](https://github.com/XboxDev/extract-xiso/releases) is where to
  check for anything newer.
- **Windows PowerShell** is already on your machine. Windows ships version 5.1 as
  standard; the app uses it to run `convert-xiso.ps1`.

Windows 10 or 11, 64-bit.

Keep the folder intact — the app looks for the other pieces beside itself:

```
XisoConverter.exe          the app
convert-xiso.ps1           the engine - the app will not run without it
README.md                  this file
LICENSE                    this app's licence (MIT)
THIRD-PARTY-NOTICES.md     licences and attribution
extract-xiso\
    extract-xiso.exe       does the actual extraction
    LICENSE.TXT            its licence
    VERSION.txt            which upstream build this is
```

Prefer your own copy of extract-xiso? Point the first **Browse…** button at it. Your
choice is remembered and always wins over the bundled one.

---

## First run: Windows will warn you

This download is **not code-signed**, so the first time you run it Windows shows:

> **Windows protected your PC**
> Microsoft Defender SmartScreen prevented an unrecognised app from starting.

That is a *reputation* warning, not a virus detection. SmartScreen shows it for any
executable it hasn't seen enough copies of before, and a small project's release never
reaches that threshold. To get past it:

**Click "More info" → "Run anyway".** Once only; Windows remembers.

If Windows is more stubborn about it, the cause is usually the zip rather than the app.
Windows tags downloaded files, and everything extracted from a tagged zip inherits the
tag. Fix it at the source:

1. Right-click **the zip** (before extracting) → **Properties**
2. Tick **Unblock** at the bottom → **OK**
3. Extract again

**Do not turn off your antivirus to run this.** That advice is bad for you generally,
and it isn't necessary here. If you'd rather not take the binary on trust, don't — the
source is public, MIT licensed, and the GitHub Actions workflow that produced this exact
zip is in the repository. Building it yourself takes one command.

Code signing needs a paid certificate, which this project doesn't have. If that ever
changes, the warning goes away.

---

## Using it

1. **extract-xiso.exe** — Browse to wherever you unzipped it.
2. **Source folder** — where your `.iso` / `.xiso` files live.
3. **Output folder** — where extracted game folders should go.

The list fills in as soon as those three are set. Each row shows the ISO, its size, and
the folder name the converter will create. **A folder name in bold amber means it was
changed** to survive the Xbox filesystem — that is the point of this tool, and seeing
`Thing, The (USA).iso → Thing The (USA)` is the reassurance you want.

Tick the rows you want and press **Convert**. New images are ticked for you by default.

### The list keeps itself up to date

Leave the app open while downloads finish. New images appear in the list on their own —
no button to press.

It deliberately waits until a download is **completely finished** before adding it: a
file still being written is a partial image, and reading one would fail the Xbox
signature check and wrongly label your new game `NotXbox`. So expect a few seconds
between the download completing and the row appearing. Nothing is added mid-download.

**Refresh list** is still there for the two cases this cannot cover:

- You added or deleted extracted game folders **in the output folder** outside the app,
  so `Skipped` and `ToDo` are out of date. Only the source folder is watched.
- Your source folder **cannot be watched** — common on network shares and NAS drives,
  where Windows may never report changes. The app says so in the log when this happens,
  and the Refresh tooltip changes to match, so you are never left waiting for a refresh
  that will not come.

### The statuses

| Status | Meaning |
|---|---|
| `ToDo` | new, not converted yet — ticked by default |
| `Skipped` | already has a `default.xbe`, so it is done |
| `OK` | extracted successfully this run |
| `Failed` | extract-xiso could not read it — usually an incomplete download |
| `NotXbox` | no Xbox signature in the file, so it was left alone (see below) |
| `Cancelled` | you stopped the run while this one was in progress |

### The options

- **Skip $SystemUpdate** — leaves out the system-updater folder many discs carry. It
  isn't part of the game, it wastes space on the console, and running an old system
  update can break a softmod. On by default. Untick it for a byte-for-byte faithful
  extraction.
- **Force re-extract** — redo games that are already done.
- **Dry run** — report what *would* happen and write nothing.

---

## Why the folder names change

FATX, the Xbox filesystem, is stricter than Windows in two ways that bite:

- Names are capped at **42 characters**.
- These characters are illegal: `" * + , / : ; < = > ? \ |`

Comma, plus, semicolon and equals are all perfectly legal on Windows. That is the trap:
a folder called `Thing, The (USA)` copies fine on the PC, then fails to FTP to the
console with `550 Filename invalid`. Fixing the name at extraction time is much easier
than repairing it afterwards.

Over-length names are shortened in a deliberate order — bracketed tags like `(USA)` go
first, then trailing region/dump junk, then a leading `The`/`A`/`An`, and only as a last
resort is the title itself cut.

## Why some images say NotXbox

Every Original Xbox disc image carries a `MICROSOFT*XBOX*MEDIA` signature in a known
place. Files without it — a PS2 rip, an Xbox 360 image, a PC ISO that wandered into the
folder — are marked `NotXbox`, left unticked, and skipped.

They stay visible in the list on purpose. If you believe one really is an Xbox game,
tick it and press Convert: the app takes that as an override and attempts it anyway.

## Cancelling is safe

Each game is extracted into a `<name>.partial` folder and only renamed once
extract-xiso finishes cleanly **and** `default.xbe` is confirmed present. A cancelled or
crashed run can never leave behind a folder that looks finished, and the app deletes the
leftover `.partial` for you.

## Where things are kept

Your paths, options and window position live in:

```
%APPDATA%\XisoConverter\settings.json
```

Deleting that file resets the app. A corrupt one is ignored rather than fatal.

Each real run also writes a plain-text log into your output folder as
`_convert-log_<date>.txt`, and the summary dialog links straight to it.

---

## Thanks to

This app is a front-end. The hard parts were solved by other people first.

- **[extract-xiso](https://github.com/XboxDev/extract-xiso)** — does all the actual
  extraction. This tool would have nothing to do without it. Maintained by the
  **[XboxDev](https://github.com/XboxDev)** project, building on the work of its
  original author, *in* &lt;in@fishtank.com&gt;.
- **[Redump](http://redump.org)** — disc preservation work, and the source of the
  full-dump layout this app recognises.
- The wider **Original Xbox homebrew community**, whose documentation of the disc
  format and of FATX's limits is what makes the naming rules here possible.
- **[.NET](https://github.com/dotnet/runtime)** and
  **[Windows Forms](https://github.com/dotnet/winforms)** — the app is built on both.

> This product includes software developed by in &lt;in@fishtank.com&gt;.

`extract-xiso` is bundled here **unmodified**, straight from an official release of
the upstream project, under its 4-clause BSD licence, which travels with it in
`extract-xiso\LICENSE.TXT`. `extract-xiso\VERSION.txt` names the exact upstream build,
and it was the latest one available when this version was built.

- Source and releases: **https://github.com/XboxDev/extract-xiso**
- Latest release: **https://github.com/XboxDev/extract-xiso/releases/latest**

Neither *in* nor the XboxDev project endorses this app. Full attribution is in
`THIRD-PARTY-NOTICES.md`.

---

## Troubleshooting

**"convert-xiso.ps1 was not found"** — the `.ps1` must sit in the same folder as the
`.exe`. Don't move one without the other.

**Everything says Failed** — check the extract-xiso path points at a real
`extract-xiso.exe`, and that your output drive has room. The tally in the log pane and
the summary dialog carry the exact error text.

**A real game says NotXbox** — tick it and convert anyway; that overrides the check.

**Nothing appears in the list** — the source folder needs `.iso` or `.xiso` files
directly inside it. Subfolders aren't searched.

**A new download hasn't shown up** — give it a few seconds; the list waits until the
file has finished being written. If it never appears, check the log pane: on a network
share Windows may not report changes at all, in which case press **Refresh list**.
