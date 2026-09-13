using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShopApp.Domain.Logic;

namespace ShopApp.Reports;

/// <summary>
/// One exporter for every report, because they all return the same
/// ReportResult shape. Adding a report needs no export code.
/// </summary>
public static class ReportExporter
{
    private const string HeaderTeal = "#1B7A9E";

    public static void ToExcel(ReportResult report, string path, string? businessName = null)
    {
        using var wb = new XLWorkbook();
        // Excel refuses sheet names over 31 chars or containing : \ / ? * [ ]
        var safe = new string(report.Title.Where(c => !"[]:\\/?*".Contains(c)).ToArray());
        var ws = wb.Worksheets.Add(safe.Length > 31 ? safe[..31] : safe);

        var row = 1;

        if (!string.IsNullOrWhiteSpace(businessName))
        {
            ws.Cell(row, 1).Value = businessName;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 13;
            row++;
        }

        ws.Cell(row, 1).Value = report.Title;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 12;
        row++;

        if (!string.IsNullOrWhiteSpace(report.Subtitle))
        {
            ws.Cell(row, 1).Value = report.Subtitle;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.Gray;
            row++;
        }

        row++;
        var headerRow = row;

        for (int c = 0; c < report.Columns.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = report.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderTeal);
        }
        row++;

        foreach (var r in report.Rows)
        {
            for (int c = 0; c < report.Columns.Count; c++)
            {
                var col = report.Columns[c];
                var cell = ws.Cell(row, c + 1);
                var value = r[col.Field];

                switch (col.Type)
                {
                    case ReportColumnType.Money:
                        cell.Value = r.Num(col.Field);
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        break;
                    case ReportColumnType.Quantity:
                        cell.Value = r.Num(col.Field);
                        cell.Style.NumberFormat.Format = "#,##0.###";
                        break;
                    case ReportColumnType.Number:
                        cell.Value = r.Num(col.Field);
                        cell.Style.NumberFormat.Format = "#,##0";
                        break;
                    case ReportColumnType.Percent:
                        cell.Value = r.Num(col.Field);
                        cell.Style.NumberFormat.Format = "#,##0.00\"%\"";
                        break;
                    case ReportColumnType.Date:
                        if (value is DateTime dt)
                        {
                            cell.Value = dt;
                            cell.Style.DateFormat.Format = "dd-MM-yyyy";
                        }
                        break;
                    default:
                        cell.Value = value?.ToString() ?? "";
                        break;
                }

                if (r.IsSummaryRow) cell.Style.Font.Bold = true;
            }
            row++;
        }

        if (report.Totals.Count > 0 && report.Rows.Count > 0)
        {
            ws.Cell(row, 1).Value = "Total";
            ws.Cell(row, 1).Style.Font.Bold = true;

            for (int c = 0; c < report.Columns.Count; c++)
            {
                var col = report.Columns[c];
                if (!report.Totals.TryGetValue(col.Field, out var total)) continue;

                var cell = ws.Cell(row, c + 1);
                cell.Value = total;
                cell.Style.Font.Bold = true;
                cell.Style.NumberFormat.Format =
                    col.Type == ReportColumnType.Quantity ? "#,##0.###" : "#,##0.00";
                cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            }
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);
        wb.SaveAs(path);
    }

    public static void ToPdf(ReportResult report, string path, string? businessName = null)
    {
        InvoicePrinter.EnsureLicence();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                // Wide reports need landscape or the columns become unreadable.
                page.Size(report.Columns.Count > 6
                    ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(Fonts.Calibri));

                page.Header().Column(col =>
                {
                    if (!string.IsNullOrWhiteSpace(businessName))
                        col.Item().Text(businessName).FontSize(13).Bold();
                    col.Item().Text(report.Title).FontSize(12).SemiBold();
                    if (!string.IsNullOrWhiteSpace(report.Subtitle))
                        col.Item().Text(report.Subtitle).FontSize(9)
                           .FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(8);
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        foreach (var col in report.Columns)
                            c.RelativeColumn((float)col.Width);
                    });

                    table.Header(header =>
                    {
                        foreach (var col in report.Columns)
                        {
                            var cell = header.Cell()
                                .Background(HeaderTeal).PaddingVertical(5).PaddingHorizontal(4);
                            var text = IsNumeric(col.Type)
                                ? cell.AlignRight().Text(col.Header)
                                : cell.Text(col.Header);
                            text.FontColor(Colors.White).Bold().FontSize(9);
                        }
                    });

                    foreach (var r in report.Rows)
                    {
                        foreach (var col in report.Columns)
                        {
                            var cell = table.Cell()
                                .BorderBottom(1).BorderColor("#E5E7EB")
                                .PaddingVertical(4).PaddingHorizontal(4);

                            var value = Format(r, col);
                            var text = IsNumeric(col.Type)
                                ? cell.AlignRight().Text(value)
                                : cell.Text(value);
                            text.FontSize(9);
                            if (r.IsSummaryRow) text.Bold();
                        }
                    }

                    if (report.Totals.Count > 0 && report.Rows.Count > 0)
                    {
                        foreach (var col in report.Columns)
                        {
                            var cell = table.Cell()
                                .BorderTop(1).BorderColor(Colors.Black)
                                .PaddingVertical(5).PaddingHorizontal(4);

                            if (col == report.Columns[0])
                            {
                                cell.Text("Total").Bold().FontSize(9);
                            }
                            else if (report.Totals.TryGetValue(col.Field, out var total))
                            {
                                cell.AlignRight()
                                    .Text(InvoiceFormatting.Money(total,
                                        col.Type != ReportColumnType.Quantity, true))
                                    .Bold().FontSize(9);
                            }
                            else cell.Text("");
                        }
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Generated {DateTime.Now:dd-MM-yyyy HH:mm}")
                       .FontSize(8).FontColor(Colors.Grey.Medium);
                    row.RelativeItem().AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });
        }).GeneratePdf(path);
    }

    private static bool IsNumeric(ReportColumnType t) =>
        t is ReportColumnType.Money or ReportColumnType.Quantity
          or ReportColumnType.Number or ReportColumnType.Percent;

    private static string Format(ReportRow r, ReportColumn col) => col.Type switch
    {
        ReportColumnType.Money => InvoiceFormatting.Money(r.Num(col.Field), true, true),
        ReportColumnType.Quantity => InvoiceFormatting.Qty(r.Num(col.Field)),
        ReportColumnType.Number => InvoiceFormatting.Money(r.Num(col.Field), false, true),
        ReportColumnType.Percent => $"{r.Num(col.Field):0.##}%",
        ReportColumnType.Date => r[col.Field] is DateTime d ? InvoiceFormatting.Date(d) : "",
        _ => r[col.Field]?.ToString() ?? ""
    };
}
