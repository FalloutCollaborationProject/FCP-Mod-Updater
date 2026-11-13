namespace FCPModUpdater.Gui.Services;

public static class Humanize
{
    public static string Ago(DateTimeOffset when, DateTimeOffset? now = null)
    {
        TimeSpan span = (now ?? DateTimeOffset.UtcNow) - when;

        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return Plural((int)span.TotalMinutes, "minute");
        if (span < TimeSpan.FromDays(1)) return Plural((int)span.TotalHours, "hour");
        if (span < TimeSpan.FromDays(14)) return Plural((int)span.TotalDays, "day");
        if (span < TimeSpan.FromDays(60)) return Plural((int)(span.TotalDays / 7), "week");
        if (span < TimeSpan.FromDays(365)) return Plural((int)(span.TotalDays / 30), "month");
        return Plural((int)(span.TotalDays / 365), "year");

        static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
    }

    public static string Size(long kilobytes)
    {
        if (kilobytes < 1024) return $"{kilobytes} KB";
        double mb = kilobytes / 1024.0;
        if (mb < 1024) return $"{mb:0} MB";
        return $"{mb / 1024:0.0} GB";
    }

    public static string Count(int n, string singular) => $"{n} {singular}{(n == 1 ? "" : "s")}";
}
