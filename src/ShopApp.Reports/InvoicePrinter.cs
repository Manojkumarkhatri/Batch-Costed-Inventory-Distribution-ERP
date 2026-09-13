using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ShopApp.Domain.Entities;

namespace ShopApp.Reports;

public static class InvoicePrinter
{
    private static bool _licenceSet;

    /// <summary>
    /// QuestPDF requires the licence type to be declared once before any render.
    /// Community is free below the revenue threshold - verify the current terms
    /// at questpdf.com before shipping commercially.
    /// </summary>
    public static void EnsureLicence()
    {
        if (_licenceSet) return;
        QuestPDF.Settings.License = LicenseType.Community;
        _licenceSet = true;
    }

    public static byte[] RenderPdf(InvoiceData data, InvoicePrintOptions options)
    {
        EnsureLicence();
        return new InvoiceDocument(data, options).GeneratePdf();
    }

    public static void SavePdf(InvoiceData data, InvoicePrintOptions options, string path)
    {
        EnsureLicence();
        new InvoiceDocument(data, options).GeneratePdf(path);
    }
}
