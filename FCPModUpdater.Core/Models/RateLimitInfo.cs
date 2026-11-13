namespace FCPModUpdater.Models;

public record RateLimitInfo(int Remaining, int Limit, DateTimeOffset Reset);

/// <summary>Result of a token check: <see cref="Info"/> on success, otherwise <see cref="Error"/>.</summary>
public record RateLimitCheck(RateLimitInfo? Info, string? Error)
{
    public bool Success => Info is not null;
}
