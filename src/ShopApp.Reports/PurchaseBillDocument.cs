using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShopApp.Domain.Logic;

namespace ShopApp.Reports;

public record PurchaseLineData(int Serial, string ItemName, decimal Qty, string Unit,
                               decimal Rate, decimal Amount, string? BatchNo,
                               DateTime? Expiry);

public record PurchaseBillData(
    string BillNo,
    DateTime Date,
    string SupplierName,
    string? SupplierAddress,
    IReadOnlyList<PurchaseLineData> Lines,
    decimal SubTotal,
    decimal Charges,
    decimal Total,
    decimal Paid,
    decimal Balance,
    string? Description,
    string? BusinessName,
    string? BusinessAddress,
    string? BusinessPhone);

public record PaymentVoucherData(
    string VoucherNo,
    DateTime Date,
    bool Incoming,
    string PartyName,
    decimal Amount,
    string Mode,
    string? ReferenceNo,
    decimal PartyBalanceAfter,
    IReadOnlyList<(string Document, DateTime Date, decimal Amount)> Settles,
    string? Description,
    string? BusinessName,
    string? BusinessAddress,
    string? BusinessPhone);

/// <summary>
/// A purchase bill as he would file it.
///
/// This is his own record of what a supplier delivered, not a document sent to
/// anyone, so it carries the batch and expiry that the supplier's own invoice
/// usually omits. Those are the two things he cannot reconstruct later.
/// </summary>
public class PurchaseBillDocument : IDocument
{
    private readonly PurchaseBillData _d;
    private readonly bool _withDescription;

    private const string Teal = "#1B7A9E";
    private const string RowLine = "#D9D9D9";

    public PurchaseBillDocument(PurchaseBillData data, bool withDescription)
    {
        _d = data;
        _withDescription = withDescription;
    }

    public void Compose(IDocumentContainer container) =>
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => t.FontSize(10).FontFamily(Fonts.Calibri));

            page.Content().Column(col =>
            {
                col.Item().BorderBottom(2).PaddingBottom(6)
                   .Text("Purchase Bill").FontSize(16).Bold().AlignCenter();

                col.Item().PaddingTop(12).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Supplier").Bold();
                        c.Item().Text(_d.SupplierName).FontSize(12).Bold();
                        if (!string.IsNullOrWhiteSpace(_d.SupplierAddress))
                            c.Item().Text(_d.SupplierAddress).FontSize(9);
                    });

                    row.ConstantItem(200).Column(c =>
                    {
                        c.Item().AlignRight().Text("Bill Details").Bold();
                        c.Item().AlignRight().Text($"Bill No.: {_d.BillNo}");
                        c.Item().AlignRight().Text($"Date: {_d.Date:dd-MM-yyyy}");
                    });
                });

                col.Item().PaddingTop(14).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(26);   // #
                        c.RelativeColumn(3);    // item
                        c.RelativeColumn(1.4f); // batch
                        c.RelativeColumn(1.2f); // expiry
                        c.RelativeColumn(1);    // qty
                        c.RelativeColumn(1);    // unit
                        c.RelativeColumn(1.2f); // rate
                        c.RelativeColumn(1.4f); // amount
                    });

                    table.Header(h =>
                    {
                        void Head(string text, bool right = false)
                        {
                            var cell = h.Cell().Background(Teal).Padding(5);
                            var t = cell.Text(text).FontColor(Colors.White).Bold().FontSize(9);
                            if (right) t.AlignRight();
                        }

                        Head("#"); Head("Item"); Head("Batch"); Head("Expiry");
                        Head("Qty", true); Head("Unit"); Head("Rate", true); Head("Amount", true);
                    });

                    foreach (var l in _d.Lines)
                    {
                        IContainer Cell() => table.Cell()
                            .BorderBottom(1).BorderColor(RowLine).PaddingVertical(5).PaddingHorizontal(4);

                        Cell().Text(l.Serial.ToString());
                        Cell().Text(l.ItemName).Bold();
                        Cell().Text(l.BatchNo ?? "-").FontSize(9);
                        Cell().Text(l.Expiry is null ? "-" : $"{l.Expiry:dd-MM-yyyy}").FontSize(9);
                        Cell().AlignRight().Text(InvoiceFormatting.Qty(l.Qty));
                        Cell().Text(l.Unit);
                        Cell().AlignRight().Text(InvoiceFormatting.Money(l.Rate, false, true));
                        Cell().AlignRight().Text(InvoiceFormatting.Money(l.Amount, false, true));
                    }
                });

                col.Item().PaddingTop(14).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        // Only when he asked for it. A bill going into a file
                        // for a supplier dispute reads better without a private
                        // note about the delivery.
                        if (_withDescription && !string.IsNullOrWhiteSpace(_d.Description))
                        {
                            c.Item().Text("Description").Bold().FontSize(9);
                            c.Item().PaddingTop(2).Text(_d.Description).FontSize(9);
                        }
                    });

                    row.ConstantItem(230).Column(c =>
                    {
                        void Line(string label, decimal value, bool bold = false)
                        {
                            c.Item().Row(r =>
                            {
                                var l = r.RelativeItem().Text(label);
                                var v = r.ConstantItem(110).AlignRight()
                                         .Text(InvoiceFormatting.Money(value, true, true));
                                if (bold) { l.Bold(); v.Bold(); }
                            });
                        }

                        Line("Sub Total", _d.SubTotal);
                        if (_d.Charges != 0) Line("Freight / other", _d.Charges);
                        c.Item().PaddingVertical(3).LineHorizontal(1).LineColor(RowLine);
                        Line("Total", _d.Total, bold: true);
                        Line("Paid", _d.Paid);
                        Line("Balance", _d.Balance, bold: true);
                    });
                });

                col.Item().PaddingTop(26).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        if (!string.IsNullOrWhiteSpace(_d.BusinessName))
                            c.Item().Text(_d.BusinessName).Bold().FontSize(9);
                        if (!string.IsNullOrWhiteSpace(_d.BusinessPhone))
                            c.Item().Text(_d.BusinessPhone).FontSize(8);
                    });
                    row.ConstantItem(180).AlignRight().PaddingTop(24)
                       .BorderTop(1).BorderColor(RowLine)
                       .Text("Received by").FontSize(9).AlignCenter();
                });
            });
        });
}

/// <summary>
/// A payment voucher - the slip he hands over or files when money moves.
///
/// It lists which bills or invoices the money settled, because that is the
/// question asked weeks later, and neither party remembers.
/// </summary>
public class PaymentVoucherDocument : IDocument
{
    private readonly PaymentVoucherData _d;
    private readonly bool _withDescription;

    private const string Teal = "#1B7A9E";
    private const string RowLine = "#D9D9D9";

    public PaymentVoucherDocument(PaymentVoucherData data, bool withDescription)
    {
        _d = data;
        _withDescription = withDescription;
    }

    public void Compose(IDocumentContainer container) =>
        container.Page(page =>
        {
            // Half a sheet. A voucher on A4 wastes most of the page, and he
            // prints these several times a day.
            page.Size(PageSizes.A5.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(t => t.FontSize(10).FontFamily(Fonts.Calibri));

            page.Content().Column(col =>
            {
                col.Item().BorderBottom(2).PaddingBottom(6)
                   .Text(_d.Incoming ? "Receipt Voucher" : "Payment Voucher")
                   .FontSize(15).Bold().AlignCenter();

                col.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(_d.Incoming ? "Received from" : "Paid to").Bold().FontSize(9);
                        c.Item().Text(_d.PartyName).FontSize(13).Bold();
                    });

                    row.ConstantItem(170).Column(c =>
                    {
                        c.Item().AlignRight().Text($"Voucher No.: {_d.VoucherNo}");
                        c.Item().AlignRight().Text($"Date: {_d.Date:dd-MM-yyyy}");
                        c.Item().AlignRight().Text($"Mode: {_d.Mode}");
                        if (!string.IsNullOrWhiteSpace(_d.ReferenceNo))
                            c.Item().AlignRight().Text($"Ref: {_d.ReferenceNo}").FontSize(9);
                    });
                });

                col.Item().PaddingTop(14).Background("#F3F6FA").Padding(12).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("AMOUNT").FontSize(8).Bold().FontColor("#667186");
                        c.Item().Text(InvoiceFormatting.Money(_d.Amount, true, true))
                                .FontSize(20).Bold();
                        c.Item().PaddingTop(3)
                                .Text(NumberToWords.RupeesInWords(_d.Amount))
                                .FontSize(8).Italic();
                    });

                    row.ConstantItem(190).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("BALANCE AFTER").FontSize(8).Bold()
                                .FontColor("#667186");
                        c.Item().AlignRight()
                                .Text(InvoiceFormatting.Money(Math.Abs(_d.PartyBalanceAfter), true, true))
                                .FontSize(13).Bold();
                        c.Item().AlignRight()
                                .Text(_d.PartyBalanceAfter >= 0 ? "to receive" : "to pay")
                                .FontSize(8);
                    });
                });

                if (_d.Settles.Count > 0)
                {
                    col.Item().PaddingTop(14).Text("Settled against").Bold().FontSize(9);

                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1.2f);
                            c.RelativeColumn(1.2f);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background(Teal).Padding(4)
                             .Text("Document").FontColor(Colors.White).Bold().FontSize(8);
                            h.Cell().Background(Teal).Padding(4)
                             .Text("Date").FontColor(Colors.White).Bold().FontSize(8);
                            h.Cell().Background(Teal).Padding(4).AlignRight()
                             .Text("Amount").FontColor(Colors.White).Bold().FontSize(8);
                        });

                        foreach (var (doc, date, amount) in _d.Settles)
                        {
                            IContainer Cell() => table.Cell()
                                .BorderBottom(1).BorderColor(RowLine).PaddingVertical(4).PaddingHorizontal(4);

                            Cell().Text(doc).FontSize(9);
                            Cell().Text($"{date:dd-MM-yyyy}").FontSize(9);
                            Cell().AlignRight()
                                  .Text(InvoiceFormatting.Money(amount, false, true)).FontSize(9);
                        }
                    });
                }

                if (_withDescription && !string.IsNullOrWhiteSpace(_d.Description))
                {
                    col.Item().PaddingTop(12).Text("Description").Bold().FontSize(9);
                    col.Item().PaddingTop(2).Text(_d.Description).FontSize(9);
                }

                col.Item().PaddingTop(26).Row(row =>
                {
                    row.RelativeItem().PaddingTop(20).BorderTop(1).BorderColor(RowLine)
                       .Text(_d.Incoming ? "Received by" : "Paid by").FontSize(9).AlignCenter();
                    row.ConstantItem(30);
                    row.RelativeItem().PaddingTop(20).BorderTop(1).BorderColor(RowLine)
                       .Text("Signature").FontSize(9).AlignCenter();
                });
            });
        });
}
