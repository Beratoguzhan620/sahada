using System.Globalization;
using Sahada.Core.WorkOrders;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Sahada.Infrastructure.Reports;

public sealed record ReportPart(string Sku, string Name, decimal Quantity, long UnitPriceMinor, long TotalMinor);

public sealed record ReportPhoto(byte[] Jpeg, string Caption);

/// <summary>Everything the customer's copy shows. Times are already in the company's time zone.</summary>
public sealed record ServiceReportDocument(
    string Company,
    string Code,
    string Title,
    string Status,
    string Priority,
    string CustomerName,
    string CustomerPhone,
    string Site,
    string Address,
    string? Equipment,
    string? Technician,
    DateTime OpenedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string Description,
    IReadOnlyList<(string Text, bool Done)> Checklist,
    string Resolution,
    IReadOnlyList<ReportPart> Parts,
    long PartsTotalMinor,
    string SignedBy,
    DateTime? SignedAt,
    IReadOnlyList<IReadOnlyList<SignaturePoint>> Signature,
    IReadOnlyList<ReportPhoto> Photos,
    DateTime PrintedAt);

/// <summary>
/// The service report a technician leaves with the customer: the job, what was done, the parts and their cost,
/// photos and the customer's signature. A4, set in IBM Plex, which has every Turkish letter.
/// </summary>
public static class ServiceReport
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double Left = 48;
    private const double Width = PageWidth - (2 * Left);
    private const double Bottom = 56;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly byte[] Regular = Font("IBMPlexSans-Regular.ttf");
    private static readonly byte[] SemiBold = Font("IBMPlexSans-SemiBold.ttf");

    public static byte[] Render(ServiceReportDocument doc)
    {
        var builder = new PdfDocumentBuilder();
        var writer = new Writer(builder, builder.AddTrueTypeFont(Regular), builder.AddTrueTypeFont(SemiBold), doc);
        writer.Header();
        writer.Details();
        writer.Section("Reported problem", doc.Description.Length > 0 ? doc.Description : "—");
        writer.Checklist();
        writer.Section("Work done", doc.Resolution.Length > 0 ? doc.Resolution : doc.Status == "Completed" ? "—" : "Not completed yet.");
        writer.Parts();
        writer.Photos();
        writer.Signature();
        writer.Footers();
        return builder.Build();
    }

    private static string Money(long minor) => (minor / 100m).ToString("N2", Turkish) + " TRY";

    private static string When(DateTime? value) => value?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—";

    private static byte[] Font(string name)
    {
        using var stream = typeof(ServiceReport).Assembly.GetManifestResourceStream($"Sahada.Infrastructure.Reports.Fonts.{name}")
            ?? throw new InvalidOperationException($"The font {name} is not embedded.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Width and height from a JPEG's start-of-frame marker, or null when it cannot be read.</summary>
    internal static (int Width, int Height)? JpegSize(byte[] jpeg)
    {
        var i = 2;
        while (i + 9 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                return null;
            }

            var marker = jpeg[i + 1];
            var length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return ((jpeg[i + 7] << 8) | jpeg[i + 8], (jpeg[i + 5] << 8) | jpeg[i + 6]);
            }

            i += 2 + length;
        }

        return null;
    }

    private sealed class Writer(PdfDocumentBuilder builder, PdfDocumentBuilder.AddedFont regular, PdfDocumentBuilder.AddedFont bold, ServiceReportDocument doc)
    {
        private readonly List<PdfPageBuilder> _pages = [];
        private PdfPageBuilder _page = null!;
        private double _y;

        public void Header()
        {
            NewPage();
            Fill(0x8A, 0x5A, 0x00);
            _page.DrawRectangle(new PdfPoint(Left, _y - 44), Width, 44, 0, fill: true);
            Fill(0xFF, 0xFF, 0xFF);
            _page.AddText("Sahada", 18, new PdfPoint(Left + 18, _y - 29), bold);
            var right = $"Service report · {doc.Code}";
            _page.AddText(right, 10, new PdfPoint(Left + Width - 18 - TextWidth(right, 10, regular), _y - 27), regular);
            _y -= 76;

            Fill(0x6B, 0x68, 0x62);
            _page.AddText(doc.Company.ToUpperInvariant(), 9, new PdfPoint(Left, _y), regular);
            _y -= 26;
            Fill(0x1A, 0x1A, 0x1A);
            foreach (var line in Wrap(doc.Title, 20, bold, Width))
            {
                _page.AddText(line, 20, new PdfPoint(Left, _y), bold);
                _y -= 26;
            }

            Fill(0x6B, 0x68, 0x62);
            _page.AddText($"{doc.Status} · {doc.Priority} priority", 11, new PdfPoint(Left, _y), regular);
            _y -= 24;
        }

        public void Details()
        {
            var rows = new (string Label, string Value)[]
            {
                ("CUSTOMER", doc.CustomerPhone.Length > 0 ? $"{doc.CustomerName} · {doc.CustomerPhone}" : doc.CustomerName),
                ("SITE", $"{doc.Site} · {doc.Address}"),
                ("EQUIPMENT", doc.Equipment ?? "—"),
                ("TECHNICIAN", doc.Technician ?? "—"),
                ("OPENED", When(doc.OpenedAt)),
                ("STARTED", When(doc.StartedAt)),
                ("COMPLETED", When(doc.CompletedAt)),
            };
            const double half = Width / 2;
            Stroke(0xE2, 0xDF, 0xD8);
            var height = (Math.Ceiling(rows.Length / 2.0) * 34) + 14;
            _page.DrawRectangle(new PdfPoint(Left, _y - height), Width, height, 1);
            var top = _y - 22;
            for (var i = 0; i < rows.Length; i++)
            {
                var x = Left + 16 + (i % 2 * half);
                var y = top - (i / 2 * 34);
                Fill(0x6B, 0x68, 0x62);
                _page.AddText(rows[i].Label, 7.5, new PdfPoint(x, y + 10), regular);
                Fill(0x1A, 0x1A, 0x1A);
                _page.AddText(Fit(rows[i].Value, 10.5, bold, half - 28), 10.5, new PdfPoint(x, y - 4), bold);
            }

            _y -= height + 22;
        }

        public void Section(string title, string body)
        {
            Title(title);
            Fill(0x1A, 0x1A, 0x1A);
            foreach (var paragraph in body.Split('\n'))
            {
                foreach (var line in Wrap(paragraph.Length == 0 ? " " : paragraph, 10.5, regular, Width))
                {
                    Room(16);
                    _page.AddText(line, 10.5, new PdfPoint(Left, _y), regular);
                    _y -= 15;
                }
            }

            _y -= 10;
        }

        public void Checklist()
        {
            if (doc.Checklist.Count == 0)
            {
                return;
            }

            Title("Checklist");
            foreach (var (text, done) in doc.Checklist)
            {
                Room(18);
                Stroke(0x6B, 0x68, 0x62);
                if (done)
                {
                    Fill(0x8A, 0x5A, 0x00);
                    _page.DrawRectangle(new PdfPoint(Left, _y - 1), 9, 9, 1, fill: true);
                }
                else
                {
                    _page.DrawRectangle(new PdfPoint(Left, _y - 1), 9, 9, 1);
                }

                Fill(0x1A, 0x1A, 0x1A);
                _page.AddText(Fit(text, 10.5, regular, Width - 20), 10.5, new PdfPoint(Left + 18, _y), regular);
                _y -= 17;
            }

            _y -= 8;
        }

        public void Parts()
        {
            if (doc.Parts.Count == 0)
            {
                return;
            }

            Title("Parts");
            double[] columns = [Left, Left + 90, Left + 300, Left + 360, Left + Width];
            Fill(0x6B, 0x68, 0x62);
            _page.AddText("SKU", 7.5, new PdfPoint(columns[0], _y), regular);
            _page.AddText("PART", 7.5, new PdfPoint(columns[1], _y), regular);
            Right("QTY", 7.5, regular, columns[3] - 10);
            Right("UNIT", 7.5, regular, columns[3] + 60);
            Right("TOTAL", 7.5, regular, columns[4]);
            _y -= 8;
            Rule();
            foreach (var part in doc.Parts)
            {
                Room(20);
                _y -= 12;
                Fill(0x1A, 0x1A, 0x1A);
                _page.AddText(Fit(part.Sku, 9.5, regular, 84), 9.5, new PdfPoint(columns[0], _y), regular);
                _page.AddText(Fit(part.Name, 9.5, regular, 200), 9.5, new PdfPoint(columns[1], _y), regular);
                Right(part.Quantity.ToString("0.##", Turkish), 9.5, regular, columns[3] - 10);
                Right(Money(part.UnitPriceMinor), 9.5, regular, columns[3] + 60);
                Right(Money(part.TotalMinor), 9.5, bold, columns[4]);
                _y -= 6;
            }

            Rule();
            _y -= 14;
            Right($"Parts total  {Money(doc.PartsTotalMinor)}", 11, bold, columns[4]);
            _y -= 24;
        }

        public void Photos()
        {
            var photos = doc.Photos.Select(p => (p, Size: JpegSize(p.Jpeg))).Where(p => p.Size is { Width: > 0, Height: > 0 }).Take(4).ToList();
            if (photos.Count == 0)
            {
                return;
            }

            const double gap = 16;
            Room(150 + 30 + 48);
            Title("Photos");
            const double box = (Width - gap) / 2;
            const double boxHeight = 150;
            for (var i = 0; i < photos.Count; i += 2)
            {
                Room(boxHeight + 24);
                for (var j = i; j < Math.Min(i + 2, photos.Count); j++)
                {
                    var (photo, size) = photos[j];
                    var scale = Math.Min(box / size!.Value.Width, boxHeight / size.Value.Height);
                    var (w, h) = (size.Value.Width * scale, size.Value.Height * scale);
                    var x = Left + ((j - i) * (box + gap));
                    try
                    {
                        _page.AddJpeg(photo.Jpeg, new PdfRectangle(x, _y - h, x + w, _y));
                    }
                    catch (Exception)
                    {
                        // A photo the PDF library cannot embed is left out rather than spoiling the report.
                        continue;
                    }

                    Fill(0x6B, 0x68, 0x62);
                    _page.AddText(Fit(photo.Caption.Length > 0 ? photo.Caption : $"Photo {j + 1}", 8.5, regular, box), 8.5, new PdfPoint(x, _y - boxHeight - 12), regular);
                }

                _y -= boxHeight + 30;
            }
        }

        public void Signature()
        {
            Room(118);
            Title("Customer sign-off");
            const double boxWidth = 220, boxHeight = 70;
            Stroke(0xE2, 0xDF, 0xD8);
            _page.DrawRectangle(new PdfPoint(Left, _y - boxHeight), boxWidth, boxHeight, 1);
            if (doc.Signature.Count > 0)
            {
                Stroke(0x1A, 0x1A, 0x1A);
                foreach (var stroke in doc.Signature)
                {
                    for (var i = 1; i < stroke.Count; i++)
                    {
                        _page.DrawLine(Pad(stroke[i - 1]), Pad(stroke[i]), 1.2);
                    }
                }
            }

            _y -= boxHeight + 14;
            Fill(0x1A, 0x1A, 0x1A);
            var signer = doc.SignedBy.Length > 0 ? $"{doc.SignedBy} · {When(doc.SignedAt)}" : "Not signed";
            _page.AddText(signer, 10, new PdfPoint(Left, _y), doc.SignedBy.Length > 0 ? bold : regular);
            _y -= 20;

            PdfPoint Pad(SignaturePoint p) => new(Left + 8 + (p.X * (boxWidth - 16)), _y - 8 - (p.Y * (boxHeight - 16)));
        }

        public void Footers()
        {
            for (var i = 0; i < _pages.Count; i++)
            {
                var page = _pages[i];
                page.SetTextAndFillColor(0x6B, 0x68, 0x62);
                page.AddText($"{doc.Company} · {doc.Code} · printed {When(doc.PrintedAt)}", 8, new PdfPoint(Left, 36), regular);
                var number = $"Page {i + 1} of {_pages.Count}";
                var letters = page.MeasureText(number, 8, new PdfPoint(0, 0), regular);
                var width = letters.Count == 0 ? 0 : letters[^1].EndBaseLine.X - letters[0].StartBaseLine.X;
                page.AddText(number, 8, new PdfPoint(Left + Width - width, 36), regular);
            }
        }

        private void NewPage()
        {
            _page = builder.AddPage(PageSize.A4);
            _pages.Add(_page);
            _y = PageHeight - 42;
        }

        private void Room(double needed)
        {
            if (_y - needed < Bottom)
            {
                NewPage();
                _y -= 20;
            }
        }

        private void Title(string text)
        {
            Room(48);
            Fill(0x8A, 0x5A, 0x00);
            _page.AddText(text.ToUpperInvariant(), 8.5, new PdfPoint(Left, _y), bold);
            _y -= 20;
        }

        private void Rule()
        {
            Stroke(0xE2, 0xDF, 0xD8);
            _page.DrawLine(new PdfPoint(Left, _y), new PdfPoint(Left + Width, _y), 0.8);
        }

        private void Right(string text, double size, PdfDocumentBuilder.AddedFont font, double right) =>
            _page.AddText(text, size, new PdfPoint(right - TextWidth(text, size, font), _y), font);

        private void Fill(byte r, byte g, byte b) => _page.SetTextAndFillColor(r, g, b);

        private void Stroke(byte r, byte g, byte b) => _page.SetStrokeColor(r, g, b);

        private double TextWidth(string text, double size, PdfDocumentBuilder.AddedFont font)
        {
            var letters = _page.MeasureText(text, size, new PdfPoint(0, 0), font);
            return letters.Count == 0 ? 0 : letters[^1].EndBaseLine.X - letters[0].StartBaseLine.X;
        }

        private IEnumerable<string> Wrap(string text, double size, PdfDocumentBuilder.AddedFont font, double width)
        {
            var line = string.Empty;
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";
                if (line.Length > 0 && TextWidth(candidate, size, font) > width)
                {
                    yield return line;
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }

            yield return line.Length > 0 ? line : " ";
        }

        private string Fit(string text, double size, PdfDocumentBuilder.AddedFont font, double width)
        {
            if (TextWidth(text, size, font) <= width)
            {
                return text;
            }

            var cut = text;
            while (cut.Length > 1 && TextWidth(cut + "…", size, font) > width)
            {
                cut = cut[..^1];
            }

            return cut.TrimEnd() + "…";
        }
    }
}
