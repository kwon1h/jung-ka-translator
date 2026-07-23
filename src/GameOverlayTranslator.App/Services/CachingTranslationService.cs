using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class CachingTranslationService : ITranslationService
{
    private const int DefaultMaxCacheEntries = 5000;
    private const int MaxFailedTexts = 1024;
    private readonly ITranslationService innerService;
    private readonly ScreenTranslationCacheStore cacheStore;
    private readonly Dictionary<string, string> cache;
    private readonly Queue<string> cacheInsertionOrder;
    private readonly int maxCacheEntries;
    private readonly object cacheLock = new();

    private readonly Dictionary<string, DateTime> failedTexts = new(StringComparer.Ordinal);
    private int continuousFailures;
    private DateTime blockUntil = DateTime.MinValue;
    private readonly TimeSpan negativeCacheDuration = TimeSpan.FromSeconds(10);
    private readonly TimeSpan circuitBreakerDuration = TimeSpan.FromSeconds(15);
    private const int FailureThreshold = 3;

    public CachingTranslationService(
        ITranslationService innerService,
        ScreenTranslationCacheStore cacheStore,
        int maxCacheEntries = DefaultMaxCacheEntries)
    {
        this.innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
        this.cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        if (maxCacheEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCacheEntries));
        }

        this.maxCacheEntries = maxCacheEntries;
        cache = cacheStore.Load() ?? new Dictionary<string, string>(StringComparer.Ordinal);
        cacheInsertionOrder = new Queue<string>(cache.Keys);
        TrimCache();
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var cacheKey = GetCacheKey(request.Text, request.TargetLanguage, request.SourceLanguage);
        var legacyKey = GetLegacyCacheKey(request.Text, request.TargetLanguage);

        lock (cacheLock)
        {
            PruneFailedTexts(DateTime.UtcNow);
            if (DateTime.UtcNow < blockUntil)
            {
                return new TranslationResult(request.Text, request.Text, request.SourceLanguage, new TranslationUsage(SkippedCount: 1));
            }

            if (TryGetCachedTranslation(cacheKey, legacyKey, out var cachedTranslation))
            {
                return new TranslationResult(request.Text, cachedTranslation, request.SourceLanguage, new TranslationUsage(CacheHitCount: 1));
            }

            if (failedTexts.TryGetValue(cacheKey, out var failedTime) && DateTime.UtcNow - failedTime < negativeCacheDuration)
            {
                return new TranslationResult(request.Text, request.Text, request.SourceLanguage, new TranslationUsage(SkippedCount: 1));
            }
        }

        try
        {
            var result = await innerService.TranslateAsync(request, ct);
            ValidateTranslation(request.Text, result.TranslatedText);
            var usage = result.Usage ?? TranslationUsage.Outbound(1, request.Text.Length);

            lock (cacheLock)
            {
                continuousFailures = 0;
                failedTexts.Remove(cacheKey);
                SetCachedTranslation(cacheKey, result.TranslatedText);
                cacheStore.Save(cache);
            }

            return result with { Usage = usage };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            lock (cacheLock)
            {
                failedTexts[cacheKey] = DateTime.UtcNow;
                PruneFailedTexts(DateTime.UtcNow);
                continuousFailures++;
                if (continuousFailures >= FailureThreshold)
                {
                    blockUntil = DateTime.UtcNow + circuitBreakerDuration;
                }
            }
            throw;
        }
    }

    public async Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        var targetLanguage = request.TargetLanguage;
        var texts = request.Texts;
        var results = new string[texts.Count];
        var missTexts = new List<string>();
        var missKeys = new List<string>();
        var batchMissMap = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var usage = TranslationUsage.None;

        lock (cacheLock)
        {
            PruneFailedTexts(DateTime.UtcNow);
            var isBlocked = DateTime.UtcNow < blockUntil;

            for (var index = 0; index < texts.Count; index++)
            {
                var text = texts[index];
                var key = GetCacheKey(text, targetLanguage, request.SourceLanguage);
                var legacyKey = GetLegacyCacheKey(text, targetLanguage);

                if (isBlocked)
                {
                    results[index] = text;
                    usage = usage.Add(new TranslationUsage(SkippedCount: 1));
                    continue;
                }

                if (TryGetCachedTranslation(key, legacyKey, out var cachedTranslation))
                {
                    results[index] = cachedTranslation;
                    usage = usage.Add(new TranslationUsage(CacheHitCount: 1));
                }
                else if (failedTexts.TryGetValue(key, out var failedTime) && DateTime.UtcNow - failedTime < negativeCacheDuration)
                {
                    results[index] = text;
                    usage = usage.Add(new TranslationUsage(SkippedCount: 1));
                }
                else if (batchMissMap.TryGetValue(key, out var indices))
                {
                    indices.Add(index);
                }
                else
                {
                    batchMissMap[key] = new List<int> { index };
                    missKeys.Add(key);
                    missTexts.Add(text);
                }
            }
        }

        if (missTexts.Count > 0)
        {
            try
            {
                var batchResult = await innerService.TranslateBatchAsync(
                    new BatchTranslationRequest(missTexts, targetLanguage, request.SourceLanguage),
                    ct);
                ValidateBatchTranslations(missTexts, batchResult.TranslatedTexts);
                var outboundUsage = batchResult.Usage ?? TranslationUsage.Outbound(1, missTexts.Sum(text => text.Length));
                usage = usage.Add(outboundUsage);

                lock (cacheLock)
                {
                    continuousFailures = 0;

                    for (var index = 0; index < missTexts.Count; index++)
                    {
                        var text = missTexts[index];
                        var key = missKeys[index];
                        var translatedText = batchResult.TranslatedTexts[index];

                        SetCachedTranslation(key, translatedText);
                        failedTexts.Remove(key);

                        foreach (var resultIndex in batchMissMap[key])
                        {
                            results[resultIndex] = translatedText;
                        }
                    }
                    cacheStore.Save(cache);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                lock (cacheLock)
                {
                    foreach (var key in missKeys)
                    {
                        failedTexts[key] = DateTime.UtcNow;
                    }
                    PruneFailedTexts(DateTime.UtcNow);

                    continuousFailures++;
                    if (continuousFailures >= FailureThreshold)
                    {
                        blockUntil = DateTime.UtcNow + circuitBreakerDuration;
                    }
                }
                throw;
            }
        }

        return new BatchTranslationResult(results, usage);
    }

    private static void ValidateTranslation(string sourceText, string translatedText)
    {
        if (!string.IsNullOrWhiteSpace(sourceText) && string.IsNullOrWhiteSpace(translatedText))
        {
            throw new InvalidOperationException("번역 서비스가 빈 결과를 반환했습니다.");
        }
    }

    private static void ValidateBatchTranslations(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<string> translatedTexts)
    {
        if (translatedTexts.Count != sourceTexts.Count)
        {
            throw new InvalidOperationException(
                $"번역 서비스가 {sourceTexts.Count}개 요청 중 {translatedTexts.Count}개 결과만 반환했습니다.");
        }

        for (var index = 0; index < sourceTexts.Count; index++)
        {
            ValidateTranslation(sourceTexts[index], translatedTexts[index]);
        }
    }

    private bool TryGetCachedTranslation(string cacheKey, string legacyKey, out string translatedText)
    {
        if (cache.TryGetValue(cacheKey, out translatedText!))
        {
            return true;
        }

        if (!cache.TryGetValue(legacyKey, out translatedText!))
        {
            return false;
        }

        SetCachedTranslation(cacheKey, translatedText);
        return true;
    }

    private void SetCachedTranslation(string cacheKey, string translatedText)
    {
        if (!cache.ContainsKey(cacheKey))
        {
            cacheInsertionOrder.Enqueue(cacheKey);
        }

        cache[cacheKey] = translatedText;
        TrimCache();
    }

    private void TrimCache()
    {
        while (cache.Count > maxCacheEntries && cacheInsertionOrder.TryDequeue(out var oldestKey))
        {
            cache.Remove(oldestKey);
        }
    }

    private void PruneFailedTexts(DateTime now)
    {
        foreach (var expiredKey in failedTexts
                     .Where(pair => now - pair.Value >= negativeCacheDuration)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            failedTexts.Remove(expiredKey);
        }

        while (failedTexts.Count > MaxFailedTexts)
        {
            var oldest = failedTexts.MinBy(pair => pair.Value);
            failedTexts.Remove(oldest.Key);
        }
    }

    private static string GetCacheKey(string text, string targetLanguage, string? sourceLanguage)
    {
        var source = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage.Trim().ToLowerInvariant();
        return $"v2\u001f{targetLanguage}\u001f{source}\u001f{TranslationTextNormalizer.CanonicalizeCacheText(text)}";
    }

    private static string GetLegacyCacheKey(string text, string targetLanguage)
    {
        return $"{targetLanguage}\u001f{NormalizeLegacyCacheText(text)}";
    }

    private static string NormalizeLegacyCacheText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
