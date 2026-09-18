# Releasing and updating

How a change gets from this repository onto his machine.

---

## One-time setup

### 1. Install the Velopack tool

```powershell
dotnet tool install -g vpk --version 1.2.0
```

The tool and the `Velopack` NuGet package **must be the same version** - that is
the only configuration Velopack supports. If you raise one, raise the other.

### 2. Decide where releases are published

Velopack needs a folder reachable over HTTPS. Anything that serves static files
works — there is no server component:

- a folder on cheap web hosting
- a GitHub release (public or private with a token)
- S3, Azure blob storage, or similar

Whatever you choose, note the URL. For one client a plain hosting folder is
plenty.

### 3. Put that URL in the app

`src/ShopApp.UI/App.xaml.cs`, near the top of `OnStartup`:

```csharp
UpdateService.UpdateFeedUrl = "https://yoursite.com/shopapp/";
```

Leave it empty for development builds. The Settings screen then says updates
are not configured rather than failing.

---

## Every release

### 1. Raise the version

`src/ShopApp.UI/ShopApp.UI.csproj` — all three:

```xml
<Version>1.1.0</Version>
<AssemblyVersion>1.1.0.0</AssemblyVersion>
<FileVersion>1.1.0.0</FileVersion>
```

**Velopack compares this with the server.** Forget it and the client never sees
the release, with no error to tell you why.

### 2. Publish

```powershell
dotnet publish src\ShopApp.UI\ShopApp.UI.csproj -c Release -r win-x64 --self-contained true -o publish
```

### 3. Package

```powershell
vpk pack -u ShopApp -v 1.1.0 -p publish -e ShopApp.exe --packTitle "Batch-Costed Inventory ERP"
```

`-u ShopApp` is the application id and **must never change** — it is how
Velopack recognises an installed copy as the same app. A different id looks
like a different program and installs alongside rather than updating.

### 4. Upload

Everything from `Releases\` to the folder behind your feed URL, including
`RELEASES` — that index is what the client reads.

### 5. Confirm

On a machine with the previous version: Settings, Check for updates. It should
offer the new one.

---

## What he sees

Settings, Updates, Check for updates. If there is one, **Install and restart**
downloads it, applies it and reopens the app.

Deltas mean he downloads only what changed — a bug fix is a few hundred
kilobytes, not the whole application.

---

## What an update does not touch

His database is at `%LOCALAPPDATA%\ShopApp\shop.db`, separate from the program
files. An update replaces the application around it.

Schema changes apply themselves: `DbBootstrapper` calls `Migrate()` on every
start, EF compares `__EFMigrationsHistory` against the migrations shipped in
the new build, and runs only the missing ones. He can be several versions
behind and it still catches up in order.

**A backup is taken immediately before any update is applied**, and the update
is abandoned if that backup fails. That is deliberate: updates only go
forwards. Reinstalling an older build leaves a database whose schema the older
code does not understand, so the backup is the way back.

---

## Migration discipline

The one rule that matters: **migrations add, they never remove.**

Dropping or narrowing a column destroys what was in it, and by the time anyone
notices the backup may have rolled past it. The migrations written so far are
all additive, and they should stay that way. If a column genuinely has to go,
write a migration that copies the data somewhere first and release that on its
own, one version ahead.

---

## First install

The installer Velopack produces is what he runs once:

```
Releases\ShopApp-win-Setup.exe
```

No admin rights needed — it installs per user. After that it updates itself.

---

## Code signing

Unsigned builds get flagged by SmartScreen, and a machine with Smart App
Control enabled will refuse them outright — the same wall that blocked the
development build on this machine.

A code-signing certificate from a CA is roughly $200–400 a year. Velopack
signs during packing:

```powershell
vpk pack -u ShopApp -v 1.1.0 -p publish -e ShopApp.exe `
  --signParams "/a /f cert.pfx /p password /fd sha256 /tr http://timestamp.digicert.com /td sha256"
```

Until then, tell him the warning is coming and how to get past it. A security
dialog he was not warned about is a bad first impression of software he is
paying for.

---

## Version numbering

`major.minor.patch`:

- **patch** — a fix, nothing new
- **minor** — a new feature, existing data untouched
- **major** — something he has to be told about

Version numbers only go up. Publishing a lower one than is already on the
server leaves clients stranded on the higher one with no way to move.
