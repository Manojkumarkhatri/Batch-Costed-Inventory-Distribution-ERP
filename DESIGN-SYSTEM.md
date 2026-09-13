# ShopApp — Design System

Everything visual is defined once, in `src/ShopApp.UI/App.xaml`. This document
says what each definition is for and how a screen uses it.

**The rule that matters: a screen declares no styles of its own.** If a screen
needs something this document doesn't cover, the system is missing a component —
add it to `App.xaml` and document it here. Don't add it to the screen.

That rule exists because of measured drift: before this system, `Panel` was
declared separately in eight screens, `Caption` in six, and the same field label
appeared under three different names. Each copy had slightly different numbers.

---

## 0. The reference screen

**`src/ShopApp.UI/Views/PartiesView.xaml` is the reference implementation.**
Open it first. Every list and master-detail screen should copy its patterns
exactly — not approximately.

Rules get interpreted loosely; a finished screen does not. When this document
and `PartiesView.xaml` appear to disagree, the file wins.

What to copy from it:

`src/ShopApp.UI/Views/SaleListView.xaml` is the reference for **list**
screens; `PartiesView.xaml` is the reference for **master-detail** screens.
Copy whichever matches the screen you are working on.

| Pattern | Where in the file |
|---|---|
| Watermarked search box | left card, top |
| Filter row: combo, checkbox, Clear chip | left card, under search |
| Stat tiles with a `StatCaption` over a `FigureSmall` | right card, `UniformGrid` |
| Badge column via `DataGridTemplateColumn` | ledger grid, TYPE |
| `Converter={StaticResource Dash}` on anything that can be empty | throughout |
| `RowStyle="{StaticResource VoidedRow}"` | ledger grid |
| `CanUserSortColumns="False"` on a running-balance table | ledger grid |
| Empty-state card when nothing is selected | bottom of the right column |

From `SaleListView.xaml` (list screens):

| Pattern | Where in the file |
|---|---|
| One filter strip, primary action pulled right | `FilterBar` card at the top |
| Compact KPI row, `KpiLabel` over `KpiValue` | under the filter strip |
| Serial `#` column bound to `AlternationIndex` | first column, with `AlternationCount` set on the grid |
| A cell style named on **every** column | `DataGrid.Columns` |
| Badge column for the type | TYPE column |
| Actions and status docked at the bottom | below the table |

It declares **zero styles**. That is the standard for every screen.

---

## 1. Working method

Apply this **one screen at a time**, building after each. A single sweep across
every screen that breaks the build is worse than the inconsistency it replaces.

For each screen:

1. Delete its entire `<UserControl.Resources>` block, except converters that are
   genuinely local.
2. Replace every reference to a deleted local style with the system key from the
   tables below.
3. Replace every hard-coded hex colour with a brush key. There should be **no
   `#RRGGBB` left in any screen file** when you're done.
4. `dotnet build`, then look at the screen.

Order: Purchase Bills, Payment-In, Payment-Out, Expenses, Items, Dashboard,
Settings, then the dialogs. Parties, Sale Invoices and Reports are done and
are the references.

A screen is not finished when it compiles. It is finished when it has the
badges, the tiles, the watermarks and the em dashes that `PartiesView.xaml`
has. A refactor that changes the file but not the screen has missed the point.

Close the running app before each build or the file lock fails it.

---

## 2. Colour

Never write a hex value in a screen. Use the key.

| Key | Use |
|---|---|
| `Ink` | Anything read closely — values, names, headings |
| `Muted` | Field and filter labels |
| `Faint` | Asides, placeholders, voided rows |
| `Line` | Every border and rule |
| `Surface` | Card background |
| `Wash` | Column headers, tiles, dialog header and footer |
| `Canvas` | Page background behind cards |
| `Accent` `AccentDeep` `AccentWash` `AccentTint` | Selection, focus, links, primary action |
| `Positive` / `PositiveWash` | Money coming in, stock arriving, healthy state |
| `Negative` / `NegativeWash` | Money going out, danger, over limit |
| `Warn` / `WarnWash` | Needs attention but isn't wrong — expenses, near expiry |
| `RailBase` `RailHover` | The dark left rail only |
| `RowHover` `RowPicked` `RowPickedHover` | Table row states |

**Positive and Negative carry meaning and are never decorative.** Green means
money in. If something is green for any other reason, it's wrong.

---

## 3. Spacing

One ladder: `Space1`=4, `Space2`=8, `Space3`=12, `Space4`=16, `Space5`=20,
`Space6`=28. A margin not on this ladder is a mistake.

Cards sit 10 apart vertically. `CardPadding` is 20; `FilterBar` uses 16,12 and
`GridCard` uses 14 because a grid brings its own row padding.

Radii: `RadiusCard`=8, `RadiusControl`=6, `RadiusPill`=12.

---

## 4. Type

Six sizes. Don't invent a seventh.

| Key | Size | Use |
|---|---|---|
| `PageTitle` | 22 bold | Screen name, once per screen |
| `SectionHead` | 16 bold | A section inside a card |
| `Body` | 14 | Default |
| `Label` | 12 muted | Above a field |
| `InlineLabel` | 12 muted | Beside a filter |
| `Hint` | 11 faint | A short aside |
| `Micro` | 10 semibold | The caption above a figure |
| `Figure` | 22 bold tabular | A number worth looking at |
| `FigureSmall` | 16 semibold tabular | A number in a detail pane |

**Figures are tabular** so columns of numbers line up on the decimal point.

### On explanatory text

`Hint` is for a phrase, not a paragraph. Page-level prose explaining how a
feature works was removed from every list screen for good reason: if a screen
needs a paragraph to be understood, the screen is wrong.

Hints inside **dialogs** are fine — they sit beside a field at the moment you
fill it in, which is different from a paragraph parked on a page.

---

## 5. Layout

Every screen is **cards on the canvas**. Nothing floats loose.

**List screens** (Parties, Items, Sale, Purchase, Payment, Expenses) are:

```
FilterBar card      filters left, primary action right
Card                summary figures, StatTile each
GridCard            the table
StatusLine          one line, bottom left
```

**Master–detail screens** (Parties, Items) are a fixed-width list card on the
left and a stack of cards on the right. Left column 330, min 250.

**Dialogs** are `DialogHeader` / scrollable content / `DialogFooter`, with
Cancel then the primary action, right-aligned. Always put the content in a
`ScrollViewer` — fixed-height dialogs silently swallow the last field when the
type scale changes.

---

## 6. Components

| Key | What it is |
|---|---|
| `Card` | The only container |
| `FilterBar` | A card holding a filter strip |
| `GridCard` | A card holding a table |
| `StatTile` | One figure with a `Micro` caption |
| `Badge` + `BadgeText` | A coloured pill in a table cell |
| `BadgeSale` `BadgePurchase` `BadgeMoneyIn` `BadgeMoneyOut` | Tinted variants |
| `NavListItem` `NavGroupHead` | A column of choices, e.g. the report list |
| `DialogHeader` `DialogFooter` `StatusLine` | Chrome |

### Badges

Type columns read faster as a shape than as a word. Use a
`DataGridTemplateColumn`:

```xml
<DataGridTemplateColumn Header="TYPE" Width="110">
  <DataGridTemplateColumn.CellTemplate>
    <DataTemplate>
      <Border Style="{StaticResource BadgeSale}">
        <TextBlock Text="{Binding Transaction}" Style="{StaticResource BadgeText}"/>
      </Border>
    </DataTemplate>
  </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

Sale → `BadgeSale`. Purchase → `BadgePurchase`. Payment in → `BadgeMoneyIn`.
Payment out and expenses → `BadgeMoneyOut`. Cancelled → plain `Badge`.

---

## 7. Buttons

`Button` with no style is the **secondary** button and is the default.

| Key | Use |
|---|---|
| `PrimaryButton` | Blue. **At most one per card.** |
| `SaleButton` | Red. Add Sale, Add Party, Payment-In. |
| `SuccessButton` | Green. Back up now. |
| `DangerButton` | Red text on transparent. Delete, Cancel invoice. |
| `ChipButton` | A date preset in a filter bar |
| `LinkButton` | View All, and similar |

**Destructive actions are never a filled red block.** Red text on transparent,
so the weight on screen matches how often it should be pressed.

---

## 8. Tables

Set by the implicit `DataGrid` style — don't override per screen.

- **Rows ruled across, columns divided in the header only.** Verticals down the
  body turn a list into a spreadsheet.
- Row height 42, header 38, no row-header gutter, white to the bottom.
- Hover light blue, selected deeper blue, both hovered deeper still.
- Cells transparent so the row colour shows through, `VerticalAlignment`
  **Stretch**. Centre shrinks the cell to its text and draws a second line
  inside the row — that was a real bug.

Column cell styles, via `ElementStyle`:

| Key | Use |
|---|---|
| `NumberCell` | Any number: right, tabular |
| `MoneyCell` | Money: right, tabular, semibold |
| `MoneyInCell` | Money in: green |
| `MoneyOutCell` | Money out: red |

`VoidedRow` greys and italicises anything with `IsCancelled` true.

`EntryGrid` is the exception: the line-item grids on the sale and purchase forms
keep column dividers all the way down, because you're typing into those cells
and they must line up with the header.

### Conventions

- **Empty cells render as an em dash**, via `Converter={StaticResource Dash}`.
  Blank reads as "not loaded"; `0.00` reads as a real figure of zero. Neither is
  what a missing email address means.
- **Type columns are badges**, not plain text — see the TYPE column in
  `PartiesView.xaml`.
- **Turn sorting off on any table with a running balance.** One click on a
  header reverses the order and every balance then looks wrong.
- Headers are uppercase, set by the style — write `Header="PARTY NAME"`.
- Cancelled documents stay in the list, greyed, excluded from totals.
- Section headings and subtotals use `IsSummaryRow`, which the grid, Excel
  export and PDF export all already bold.

---

## 9. What to leave alone

**ComboBox and DatePicker keep their stock templates.** Retemplating an editable
ComboBox means wiring `PART_EditableTextBox` and `PART_Popup` exactly right, and
the unit, party and category pickers all depend on editable mode. Getting it
subtly wrong fails at runtime, not at build. Sizing and colour only.

**The left rail and the invoice PDF** are not part of this sweep. The rail has
its own self-contained styles in `MainWindow.xaml`; the invoice is QuestPDF and
follows the customer's printed form, not the screen.

---

## 10. Checklist per screen

- [ ] `<UserControl.Resources>` holds only genuinely local converters
- [ ] No `#RRGGBB` anywhere in the file
- [ ] No `FontSize` outside the type styles
- [ ] No margin off the spacing ladder
- [ ] One primary button per card at most
- [ ] Every number column uses a `*Cell` style
- [ ] Empty numeric cells show an em dash
- [ ] No paragraph of explanatory prose on the page
- [ ] Dialog content is inside a `ScrollViewer`
- [ ] Search boxes have a watermark
- [ ] Type columns are badges
- [ ] Figures sit in `StatTile`, not as bare text
- [ ] Sorting off where a running balance is shown
- [ ] A serial `#` column, with `AlternationCount` set on the grid
- [ ] Every column names a cell style - no column left on the default
- [ ] Money shown with `StringFormat=N0`; decimals only where they exist
- [ ] `dotnet build` clean, and the screen still works
- [ ] **Put it side by side with Parties. If it looks like a different app, it is not done.**
