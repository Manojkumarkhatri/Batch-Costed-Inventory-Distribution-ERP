# START HERE

This is the **complete project**, everything up to date. You do not need any of the earlier patch zips — ignore them.

## Setting it up

**1. Back up your old folder first.** Rename `E:\ShopApp` to `E:\ShopApp-old` rather than deleting it, so you can go back if anything surprises you.

**2. Unzip this** to `E:\ShopApp` (or better, `C:\Projects\ShopApp` — builds are much faster off a USB drive).

**3. Delete the old database.** The Party table changed, so the old file will not load:

```
%LOCALAPPDATA%\ShopApp\shop.db
```

Paste that into the File Explorer address bar and delete `shop.db` along with any `shop.db-wal` and `shop.db-shm` beside it.

**4. Open the `ShopApp` folder in VS Code** — the one containing `ShopApp.sln`.

**5. Run these in order:**

```
dotnet restore
dotnet test
dotnet ef migrations add Initial --project src/ShopApp.Data --startup-project src/ShopApp.UI
dotnet run --project src/ShopApp.UI
```

`dotnet test` should pass before you go further. If it does, the toolchain and all the business logic are sound, so anything that breaks afterwards is in the UI layer.

The `Migrations` folder ships empty on purpose — generating it on your machine keeps the model snapshot matched to your EF tooling.

## What you should see

Tabs: **Dashboard, Sale, Items, Parties, Purchase, Stock, Near expiry, Opening stock, Settings**, with a red "NO BACKUP SET UP" banner. That banner is correct — the Settings screen that configures backup is not built yet.

## First run, in this order

1. **Items → Import CSV** → pick `products.csv` in the project root. Loads all 35 products.
2. **Parties** → add one party.
3. **Opening stock** → "Add every item", fill quantity and cost for a few, save.
4. **Sale** → pick the party, add items, **Save & Print**. The PDF lands in `Documents\ShopApp Invoices\`.

That covers the whole cycle. Compare the printed PDF against his screenshot side by side.

## What is built

| Area | State |
|---|---|
| Items (dual units, categories, CSV import) | done |
| Parties (his approved field set) | done |
| Purchase entry (batches, landed cost) | done |
| Opening stock | done |
| Sale entry + FEFO batch picking | done |
| Invoice PDF with per-invoice print toggles | done |
| Stock summary, near-expiry | done |
| Backup engine | done, no settings screen yet |
| Payments screen | **not built** |
| Reports section | **not built** |
| Settings screen | **not built** |

## Decisions baked in

- **Invoice numbers are plain sequential** — `7`, matching his sample. Prefix and padding still exist in settings if he changes his mind.
- **`Sale.InternalRefNo`** is the hidden number he asked for. Assigned to every sale, never printed, gapless. Still needs his clarification on what it is for.
- **No Customer/Supplier split.** Any party can appear on a sale or a purchase, as in Vyapar. Group is for his own categorisation.
- **Tax is off** everywhere, but the columns exist so FBR registration later is a settings change, not a migration.

## Open questions for him

1. Why the hidden invoice number? My guess is that a visible sequence reveals his trading volume to customers, but confirm it.
2. Is Liquid Yeast cold-stored like Fresh Yeast? If so it moves to Frozen — two clicks, no code.
3. Which report does he look at most today? That should be the first one built.
