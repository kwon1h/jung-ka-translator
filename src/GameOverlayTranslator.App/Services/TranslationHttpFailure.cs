using System.Net;
using System.Net.Http;

namespace GameOverlayTranslator.App.Services;

internal static class TranslationHttpFailure
{
    private static readonly TimeSpan DefaultRateLimitDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromHours(1);

    public static Exception Create(
        string message,
        HttpResponseMessage response,
        DateTimeOffset? now = null)
    {
        var retryAfter = GetRetryAfter(response, now ?? DateTimeOffset.UtcNow);
        return retryAfter is { } delay
            ? new TranslationRetryAfterException($"{message} {FormatRetryDelay(delay)} 후 다시 시도할 수 있습니다.", delay)
            : new InvalidOperationException(message);
    }

    internal static TimeSpan? GetRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var retryHeader = response.Headers.RetryAfter;
        TimeSpan? delay = retryHeader?.Delta;
        if (delay is null && retryHeader?.Date is { } retryDate)
        {
            delay = retryDate - now;
        }

        if ((delay is null || delay <= TimeSpan.Zero)
            && response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            delay = DefaultRateLimitDelay;
        }

        if (delay is null || delay <= TimeSpan.Zero)
        {
            return null;
        }

        return delay > MaximumRetryDelay ? MaximumRetryDelay : delay;
    }

    internal static string FormatRetryDelay(TimeSpan delay)
    {
        if (delay >= TimeSpan.FromMinutes(1))
        {
            return $"{Math.Max(1, (int)Math.Ceiling(delay.TotalMinutes))}분";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds))}초";
    }
}

public sealed class TranslationRetryAfterException(string message, TimeSpan retryAfter)
    : Exception(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
