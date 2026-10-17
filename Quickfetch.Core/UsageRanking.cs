namespace Quickfetch.Core;

public sealed record AppUsage(long Count, DateTimeOffset LastUsed, double Weight);

public static class UsageRanking
{
    // A seven-day half-life balances frequency with recency, without overriding match tiers.
    public static double Score(AppUsage usage, DateTimeOffset now) => usage.Weight * Math.Pow(0.5, Math.Max(0, (now - usage.LastUsed).TotalDays) / 7);
    public static AppUsage Record(AppUsage? previous, DateTimeOffset now) => new((previous?.Count ?? 0) + 1, now, 1 + (previous is null ? 0 : Score(previous, now)));
    public static IReadOnlyList<SearchResult> Rank(IReadOnlyList<SearchResult> matches, IReadOnlyDictionary<string, AppUsage> history, DateTimeOffset now)
        => matches.Select((match, index) => (match, index, usage: history.GetValueOrDefault(match.Path)))
            .OrderBy(x => x.match.Tier).ThenByDescending(x => x.match.ExactName)
            .ThenByDescending(x => x.usage is null ? 0 : Score(x.usage, now))
            .ThenByDescending(x => x.usage?.LastUsed ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.index).Select(x => x.match).ToArray();
}
