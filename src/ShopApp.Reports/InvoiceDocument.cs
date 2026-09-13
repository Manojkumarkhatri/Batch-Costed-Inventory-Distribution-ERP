using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Logic;

namespace ShopApp.Reports;

public record InvoiceLineData(int Serial, string ItemName, decimal Qty, string Unit,
                              decimal Rate, decimal Amount);

public record InvoiceData(
    string InvoiceNo,
    DateTime Date,
    string PartyName,
    string? PartyAddress,
    IReadOnlyList<InvoiceLineData> Lines,
    decimal SubTotal,
    decimal Total,
    decimal Received,
    decimal Balance,
    decimal PartyCurrentBalance,
    string? BusinessName,
    string? BusinessAddress,
    string? BusinessPhone);

/// <summary>
/// Reproduces the invoice he approved, field for field.
///
/// Layout, from his sample:
///   thick black rule -> "Invoice" centred
///   Bill To (left)   |  Invoice Details (right)
///   teal item table: # / Item Name / Quantity / Unit / Price per Unit / Amount
///   Amount In Words + Payment Type (left)  |  Amounts block (right)
///
/// The Received, Balance and Current Balance rows appear only when the
/// corresponding toggle is on, which is why the same bill can print two ways.
/// </summary>
public class InvoiceDocument : IDocument
{
    private readonly InvoiceData _data;
    private readonly InvoicePrintOptions _options;

    // Sampled from his screenshots.
    private const string HeaderTeal = "#1B7A9E";
    private const string RowLine = "#D9D9D9";

    /// <summary>
    /// How many item rows fit on one A4 page underneath the header block and
    /// above the summary block. Used to fill a short bill with blank ruled rows
    /// without pushing the totals onto a second sheet.
    /// </summary>
    private const int RowsPerPage = 14;

    public InvoiceDocument(InvoiceData data, InvoicePrintOptions options)
    {
        _data = data;
        _options = options;
    }

    private string Rs(decimal v) =>
        InvoiceFormatting.Rs(v, _options.ShowDecimals, _options.GroupDigits);

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Invoice {_data.InvoiceNo}",
        Author = _data.BusinessName ?? "Shop Manager"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => t.FontSize(10).FontFamily(Fonts.Calibri));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            // The thick rule across the very top of his sample.
            col.Item().PaddingBottom(6).LineHorizontal(2).LineColor(Colors.Black);

            if (!string.IsNullOrWhiteSpace(_data.BusinessName))
            {
                col.Item().AlignCenter().Text(_data.BusinessName)
                   .FontSize(14).Bold();
                if (!string.IsNullOrWhiteSpace(_data.BusinessAddress))
                    col.Item().AlignCenter().Text(_data.BusinessAddress).FontSize(9);
                if (!string.IsNullOrWhiteSpace(_data.BusinessPhone))
                    col.Item().AlignCenter().Text(_data.BusinessPhone).FontSize(9);
                col.Item().PaddingTop(4);
            }

            col.Item().AlignCenter().Text("Invoice").FontSize(15).Bold();
            col.Item().PaddingTop(10);

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Bill To").Bold().FontSize(10);
                    left.Item().PaddingTop(3).Text(_data.PartyName).Bold().FontSize(11);
                    if (!string.IsNullOrWhiteSpace(_data.PartyAddress))
                        left.Item().Text(_data.PartyAddress).FontSize(9);
                });

                row.RelativeItem().Column(right =>
                {
                    right.Item().AlignRight().Text("Invoice Details").Bold().FontSize(10);
                    right.Item().AlignRight().PaddingTop(3)
                         .Text($"Invoice No.: {_data.InvoiceNo}").FontSize(10);
                    right.Item().AlignRight()
                         .Text($"Date: {InvoiceFormatting.Date(_data.Date)}").FontSize(10);
                });
            });

            col.Item().PaddingTop(10);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Element(ComposeItemTable);

            if (_options.ShowTotalItemQuantity)
            {
                var totalQty = _data.Lines.Sum(l => l.Qty);
                col.Item().PaddingTop(4).AlignRight()
                   .Text($"Total Quantity: {InvoiceFormatting.Qty(totalQty)}")
                   .FontSize(9).SemiBold();
            }

            // "Expand table to whole page" is done by filling the table with
            // blank ruled rows, not by a spacer. A spacer here asks for the
            // whole of the remaining page, which leaves the summary with
            // nowhere to sit and throws it onto a second sheet.
            col.Item().PaddingTop(14);

            col.Item().Element(ComposeSummary);
        });
    }

    private void ComposeItemTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(24);    // #
                c.RelativeColumn(3.4f);  // Item Name
                c.RelativeColumn(1.3f);  // Quantity
                c.RelativeColumn(0.9f);  // Unit
                c.RelativeColumn(1.3f);  // Price/Unit
                c.RelativeColumn(1.5f);  // Amount
            });

            table.Header(header =>
            {
                static IContainer Cell(IContainer c) =>
                    c.Background(HeaderTeal).PaddingVertical(7).PaddingHorizontal(5);

                header.Cell().Element(Cell).Text("#").FontColor(Colors.White).Bold();
                header.Cell().Element(Cell).Text("Item Name").FontColor(Colors.White).Bold();
                header.Cell().Element(Cell).AlignRight().Text("Quantity").FontColor(Colors.White).Bold();
                header.Cell().Element(Cell).AlignCenter().Text("Unit").FontColor(Colors.White).Bold();
                header.Cell().Element(Cell).AlignRight().Text("Price/Unit").FontColor(Colors.White).Bold();
                header.Cell().Element(Cell).AlignRight().Text("Amount").FontColor(Colors.White).Bold();
            });

            static IContainer Body(IContainer c) =>
                c.BorderBottom(1).BorderColor(RowLine).PaddingVertical(6).PaddingHorizontal(5);

            foreach (var line in _data.Lines)
            {
                table.Cell().Element(Body).Text(line.Serial.ToString());
                table.Cell().Element(Body).Text(line.ItemName).Bold();
                table.Cell().Element(Body).AlignRight().Text(InvoiceFormatting.Qty(line.Qty));
                table.Cell().Element(Body).AlignCenter().Text(line.Unit);
                table.Cell().Element(Body).AlignRight().Text(Rs(line.Rate));
                table.Cell().Element(Body).AlignRight().Text(Rs(line.Amount));
            }

            // No blank ruled rows. A bill with one line was drawing twelve
            // empty ruled rows under it, which reads as though something is
            // missing. Lines exist where there is an item; below that the page
            // is simply blank.
        });
    }

    private void ComposeSummary(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem(1.15f).Column(left =>
            {
                left.Item().Background(HeaderTeal).Padding(5)
                    .Text("Invoice Amount In Words:").FontColor(Colors.White).Bold().FontSize(10);

                left.Item().PaddingVertical(5).PaddingRight(10)
                    .Text(NumberToWords.RupeesInWords(
                        _data.Total, _options.AmountInWordsIndianFormat))
                    .FontSize(10);

                left.Item().PaddingTop(6).Background(HeaderTeal).Padding(5)
                    .Text("Payment Type:").FontColor(Colors.White).Bold().FontSize(10);

                left.Item().PaddingTop(5).Text(_options.PaymentType).FontSize(10);
            });

            row.ConstantItem(18);

            row.RelativeItem(1f).Column(right =>
            {
                right.Item().Background(HeaderTeal).Padding(5)
                     .Text("Amounts").FontColor(Colors.White).Bold().FontSize(10);

                void Line(string label, string value, bool bold = false, bool rule = true)
                {
                    right.Item()
                         .BorderBottom(rule ? 1 : 0).BorderColor(RowLine)
                         .PaddingVertical(5)
                         .Row(r =>
                         {
                             var l = r.RelativeItem().Text(label).FontSize(10);
                             if (bold) l.Bold();
                             var v = r.RelativeItem().AlignRight().Text(value).FontSize(10);
                             if (bold) v.Bold();
                         });
                }

                Line("Sub Total", Rs(_data.SubTotal));
                Line("Total", Rs(_data.Total), bold: true);

                if (_options.ShowReceivedAmount)
                    Line("Received", Rs(_data.Received));

                if (_options.ShowBalanceAmount)
                    Line("Balance", Rs(_data.Balance));

                if (_options.ShowPartyCurrentBalance)
                    right.Item().PaddingTop(6).Row(r =>
                    {
                        r.RelativeItem().Text("Current Balance").FontSize(10);
                        r.RelativeItem().AlignRight()
                         .Text(Rs(_data.PartyCurrentBalance)).FontSize(10);
                    });
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignRight().Text(t =>
        {
            t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
            t.Span("Page ");
            t.CurrentPageNumber();
            t.Span(" of ");
            t.TotalPages();
        });
    }
}
