# Batch-Costed Inventory & Distribution ERP

Desktop system for a food distribution business. Purchasing, sales, receivables
and payables, banking, expenses and financial reporting — built around batch
level costing rather than average cost.

**WPF · .NET 8 · EF Core · SQLite · QuestPDF · ClosedXML**

---

## The problem it solves

Most small-business software treats stock as a number: *200 Kg of yeast*. That
works for screws. It does not work for goods that arrive in dated consignments
at different prices.

Here, stock is held in **batches**. Each delivery keeps its own landed cost and
its own expiry date. A sale allocates **first-expiry-first-out** and freezes the
batch cost onto the sale line, so profit per invoice is exact and history cannot
shift afterwards.

A worked example from the test data:

| | Qty | Cost/Kg |
|---|---|---|
| Batch 1, bought 1 Sep | 100 Kg | 500 |
| Batch 2, bought 7 Sep | 100 Kg | 560 |

Selling 150 Kg at 650 draws 100 from the first batch and 50 from the second:
revenue 97,500, cost 78,000, profit **19,500** — not an estimate against an
averaged 530.

---

## Design decisions worth reading

**Stock is never stored, only derived.** On-hand is always the sum of an
append-only `StockMove` ledger. Corrections are new rows, never edits. A stored
quantity is a number that can drift away from its own history.

**Cost is frozen at the moment of sale.** Every `SaleLine` records the batch it
drew from and the cost that batch held that day. Re-costing a batch next month
cannot retrospectively change last month's profit.

**Money is `decimal` in code and integer paisa in SQLite**, through EF value
converters. No floating point anywhere near a balance. The consequence is that
EF cannot `SUM` a converted column, so reports filter in SQL and aggregate in
memory — deliberate, and commented where it matters.

**Nothing is hard-deleted.** Cancelling a sale sets a flag and writes reversing
stock moves. The invoice number stays in the list, marked cancelled: a gap in a
sequence is harder to explain later than a voided row.

**Every multi-table write is one transaction.** A sale touches sales, sale
lines, stock moves and payments — all of it commits or none of it does.

**Expenses are apportioned over the period they cover.** Rent paid in advance on
the 1st is charged a day at a time, so a daily P&L shows a day of rent rather
than a month of it. Cash flow still shows the whole amount on the day it left.

---

## What is in it

| Area | |
|---|---|
| Items | dual units, batch tracking, expiry, reorder levels, CSV import |
| Parties | one balance per party, no customer/supplier split — several are both |
| Purchases | landed cost apportionment, batch creation, cancellation with guards |
| Sales | FEFO allocation, stock checks, PDF invoices with per-invoice print options |
| Payments | in and out, oldest-invoice-first allocation, on-account balances |
| Banks | accounts, transfers, adjustments, derived balances |
| Expenses | categories, and coverage periods that apportion across reports |
| Reports | 27, on a shared result contract, with Excel and PDF export |
| Backup | AES-256-GCM, key derived from a passphrase, restore on any machine |

---

## Layout

```
ShopApp.Domain     entities and pure logic — no dependencies
ShopApp.Data       EF Core, SQLite, migrations, value converters
ShopApp.Services   business operations, transactional
ShopApp.Reports    QuestPDF invoices, Excel and PDF export
ShopApp.UI         WPF, MVVM via CommunityToolkit.Mvvm
ShopApp.Tests      xUnit, FluentAssertions
```

Domain has no project references. FEFO allocation, landed cost apportionment,
money rounding, invoice numbering and the validators are pure functions and are
unit tested without a database.

---

## Running it

```bash
dotnet build
dotnet run --project src/ShopApp.UI
```

The database is created and migrated on first launch at
`%LOCALAPPDATA%\ShopApp\shop.db`. Schema changes apply themselves on startup, so
an existing installation upgrades in place.

---

## Honest state

Built as a working system for a real business, not a demo. What is deliberately
not there:

- **No double-entry bookkeeping.** It records trading, not full books. The
  balance sheet reports measured assets and liabilities and presents equity as
  the residual — stated plainly rather than implying an audit.
- **Single user, single machine.** No concurrency tokens, by design.
- **Service-layer tests are not written yet.** The domain logic is covered; the
  services are not. That is the next piece of work, and the most valuable one.
- **No logging or global exception handling yet.** Also queued.

Editing a saved sale or purchase is not possible once its stock has moved — the
cost is already frozen onto downstream invoices. Cancel and re-enter is the
supported path, and the guard refuses rather than corrupting history.
