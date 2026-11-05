using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Reports;
using UglyToad.PdfPig;

namespace Sahada.UnitTests;

public sealed class ReportTests
{
    private static ServiceReportDocument Document(int parts = 2, string description = "Kombi alttan su kaçırıyor.") => new(
        "Kuzey Klima",
        "SO-01001",
        "Kombi su kaçırıyor",
        "Completed",
        "High",
        "Atakum Apartmanı",
        "0362 000 00 00",
        "Kazan dairesi",
        "Cumhuriyet Cd. 12, Samsun",
        "Kombi Demirdöküm Nitromix · S/N DD-2291",
        "Ali Usta",
        new DateTime(2026, 11, 2, 9, 0, 0),
        new DateTime(2026, 11, 2, 10, 20, 0),
        new DateTime(2026, 11, 2, 11, 5, 0),
        description,
        [("Basıncı ölç", true), ("Gaz kaçağı testi", true)],
        "Emniyet ventili değişti.\nBasınç 1,5 bar.",
        Enumerable.Range(1, parts).Select(i => new ReportPart($"P-{i}", $"Parça {i}", 1.5m, 10_000, 15_000)).ToList(),
        parts * 15_000L,
        "Hasan Yılmaz",
        new DateTime(2026, 11, 2, 11, 6, 0),
        [[new SignaturePoint(0.1, 0.5), new SignaturePoint(0.5, 0.2), new SignaturePoint(0.9, 0.7)]],
        [],
        new DateTime(2026, 11, 2, 11, 10, 0));

    private static string TextOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Concat(document.GetPages().Select(p => p.Text)).Replace(" ", string.Empty).Replace(" ", string.Empty);
    }

    [Fact]
    public void The_report_shows_the_job_the_work_the_parts_and_the_signer()
    {
        var text = TextOf(ServiceReport.Render(Document()));

        Assert.Contains("SO-01001", text);
        Assert.Contains("Kombisukaçırıyor", text);
        Assert.Contains("Emniyetventilideğişti.", text);
        Assert.Contains("300,00TRY", text);
        Assert.Contains("HasanYılmaz·02.11.202611:06", text);
        Assert.Contains("Page1of1", text);
    }

    [Fact]
    public void Long_reports_flow_onto_more_pages()
    {
        using var document = PdfDocument.Open(ServiceReport.Render(Document(parts: 60, description: string.Join('\n', Enumerable.Repeat("Uzun bir açıklama satırı.", 20)))));

        Assert.True(document.NumberOfPages >= 2);
        Assert.Contains($"Page{document.NumberOfPages}of{document.NumberOfPages}", document.GetPage(document.NumberOfPages).Text.Replace(" ", string.Empty));
    }

    [Fact]
    public void Jpeg_sizes_are_read_from_the_frame_header()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0, 0, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x02, 0x58, 0x03, 0x20, 0x03];

        Assert.Equal((800, 600), ServiceReport.JpegSize(jpeg));
        Assert.Null(ServiceReport.JpegSize([0xFF, 0xD8, 0x00, 0x00]));
    }
}
