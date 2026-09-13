namespace ShopApp.Domain.Logic;

public record OutstandingDoc(int DocId, string DocNo, DateTime Date, decimal Outstanding);

public record AgingBucket(string Label, decimal Amount);

/// <summary>Receivables aging: 0-30, 31-60, 61-90, 90+. The report he will live in.</summary>
public static class Aging
{
    public static IReadOnlyList<AgingBucket> Build(
        IEnumerable<OutstandingDoc> docs, DateTime asOf)
    {
        decimal b0 = 0, b31 = 0, b61 = 0, b90 = 0;

        foreach (var d in docs)
        {
            if (d.Outstanding <= 0) continue;
            var days = (asOf.Date - d.Date.Date).Days;

            if (days <= 30) b0 += d.Outstanding;
            else if (days <= 60) b31 += d.Outstanding;
            else if (days <= 90) b61 += d.Outstanding;
            else b90 += d.Outstanding;
        }

        return new List<AgingBucket>
        {
            new("0-30 days",  Money.Round(b0)),
            new("31-60 days", Money.Round(b31)),
            new("61-90 days", Money.Round(b61)),
            new("90+ days",   Money.Round(b90))
        };
    }
}
