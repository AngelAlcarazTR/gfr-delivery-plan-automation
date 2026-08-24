namespace Core.Application;

public static class HolidayHtmlParser
{
    private static readonly Regex YearRx =
        new(@"<h2[^>]*>\s*<strong>\s*(?<year>\d{4})\s*</strong>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CellRx =
        new(@"<t[hd][^>]*>(?<text>.*?)</t[hd]>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, int> Months =
        Enumerable.Range(1, 12).ToDictionary(
            m => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m),
            m => m,
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<LocalDate> Parse(string innerHtml, int? fallbackYear = null)
    {
        if (string.IsNullOrWhiteSpace(innerHtml))
            return Array.Empty<LocalDate>();

        var yearMatch = YearRx.Match(innerHtml);
        int year = yearMatch.Success
            ? int.Parse(yearMatch.Groups["year"].Value)
            : fallbackYear ?? throw new FormatException(
                "Holiday HTML has no <h2><strong>year</strong></h2> and no fallbackYear was provided.");

        var cells = CellRx.Matches(innerHtml)
            .Select(m => StripTags(m.Groups["text"].Value).Trim())
            .ToList();

        var holidays = new List<LocalDate>();
        for (int i = 0; i + 1 < cells.Count; i += 2)
        {
            var date = TryParseMonthDay(cells[i + 1], year);
            if (date is not null)
                holidays.Add(date.Value);
        }

        return holidays.Distinct().OrderBy(d => d).ToList();
    }

    private static LocalDate? TryParseMonthDay(string text, int year)
    {
        var m = Regex.Match(text.Trim(), @"^(?<month>[A-Za-z]+)\s+(?<day>\d{1,2})");
        if (!m.Success) return null;
        if (!Months.TryGetValue(m.Groups["month"].Value, out var month)) return null;

        var day = int.Parse(m.Groups["day"].Value);
        try { return new LocalDate(year, month, day); }
        catch { return null; }
    }

    private static string StripTags(string s) =>
        Regex.Replace(s, "<.*?>", string.Empty)
             .Replace("&nbsp;", " ")
             .Replace("&amp;", "&");
}