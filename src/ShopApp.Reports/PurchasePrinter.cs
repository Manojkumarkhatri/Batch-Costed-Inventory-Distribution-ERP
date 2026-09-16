using QuestPDF.Fluent;

namespace ShopApp.Reports;

/// <summary>
/// Renders the purchase bill and the payment voucher. Sits alongside
/// InvoicePrinter and shares its licence call, so the QuestPDF licence type is
/// declared once however the first document happens to be reached.
/// </summary>
public static class DocumentPrinter
{
    public static void SavePurchaseBill(PurchaseBillData data, bool withDescription, string path)
    {
        InvoicePrinter.EnsureLicence();
        new PurchaseBillDocument(data, withDescription).GeneratePdf(path);
    }

    public static void SavePaymentVoucher(PaymentVoucherData data, bool withDescription, string path)
    {
        InvoicePrinter.EnsureLicence();
        new PaymentVoucherDocument(data, withDescription).GeneratePdf(path);
    }
}
