using System.Globalization;
using System.Text;

namespace Sahada.Core.WorkOrders;

/// <summary>A point of a signature, scaled to the signing pad: 0,0 top left, 1,1 bottom right.</summary>
public readonly record struct SignaturePoint(double X, double Y);

/// <summary>
/// Signatures are kept as the pen's strokes, not as a picture: a few kilobytes, sharp in the report at any size.
/// Stored as "x,y x,y;x,y …" with three decimals.
/// </summary>
public static class SignatureStrokes
{
    public const int MaxPoints = 4000;
    public const int MaxStrokes = 200;

    public static string Encode(IReadOnlyList<IReadOnlyList<SignaturePoint>> strokes)
    {
        var points = strokes.Sum(s => s.Count);
        if (strokes.Count == 0 || strokes.Count > MaxStrokes || points < 2 || points > MaxPoints)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A signature has 1 to {MaxStrokes} strokes and 2 to {MaxPoints} points.");
        }

        var builder = new StringBuilder(points * 12);
        foreach (var stroke in strokes)
        {
            if (builder.Length > 0)
            {
                builder.Append(';');
            }

            for (var i = 0; i < stroke.Count; i++)
            {
                var (x, y) = (stroke[i].X, stroke[i].Y);
                if (!double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1)
                {
                    throw new DomainException(ErrorCodes.ValidationFailed, "Signature points are between 0 and 1.");
                }

                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(x.ToString("0.###", CultureInfo.InvariantCulture)).Append(',').Append(y.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<IReadOnlyList<SignaturePoint>> Decode(string encoded) =>
        string.IsNullOrEmpty(encoded)
            ? []
            : encoded.Split(';').Select(stroke => (IReadOnlyList<SignaturePoint>)stroke.Split(' ')
                .Select(p => p.Split(','))
                .Select(p => new SignaturePoint(double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture)))
                .ToList()).ToList();
}
