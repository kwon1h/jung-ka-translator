using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class CachingTranslationService : ITranslationService
{
    private const int DefaultMaxCacheEntries = 5000;
    private const int MaxFailedTexts = 1024;
    private static readonly TimeSpan DefaultCacheSaveInterval = TimeSpan.FromSeconds(2);
    private readonly ITranslationService innerService;
    private readonly ITranslationCacheStore cacheStore;
    private readonly Dictionary<string, string> cache;
    private readonly Queue<string> cacheInsertionOrder;
    private readonly int maxCacheEntries;
    private readonly TimeSpan cacheSaveInterval;
    private readonly Func<string?, string, string>? cacheNamespaceProvider;
    private readonly object cacheLock = new();
    private DateTime nextCacheSaveUtc = DateTime.MinValue;
    private Task<bool>? pendingCacheSave;
    private long pendingCacheSaveVersion;
    private long cacheVersion;
    private long persistedCacheVersion;

    private readonly Dictionary<string, DateTime> failedTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderFailureState> providerFailures = new(StringComparer.Ordinal);
    private readonly TimeSpan negativeCacheDuration = TimeSpan.FromSeconds(10);
    private readonly TimeSpan circuitBreakerDuration = TimeSpan.FromSeconds(15);
    private const int FailureThreshold = 3;

    public CachingTranslationService(
        ITranslationService innerService,
        ITranslationCacheStore cacheStore,
        int maxCacheEntries = DefaultMaxCacheEntries,
        TimeSpan? cacheSaveInterval = null,
        Func<string?, string, string>? cacheNamespaceProvider = null)
    {
        this.innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
        this.cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        this.cacheNamespaceProvider = cacheNamespaceProvider;
        if (maxCacheEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCacheEntries));
        }

        this.maxCacheEntries = maxCacheEntries;
        this.cacheSaveInterval = cacheSaveInterval ?? DefaultCacheSaveInterval;
        if (this.cacheSaveInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheSaveInterval));
        }

        cache = cacheStore.Load() ?? new Dictionary<string, string>(StringComparer.Ordinal);
        cacheInsertionOrder = new Queue<string>(cache.Keys);
        TrimCache();
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var cacheNamespace = GetCacheNamespace(request.SourceLanguage, request.TargetLanguage);
        var failureScope = cacheNamespace ?? string.Empty;
        var cacheKey = GetCacheKey(request.Text, request.TargetLanguage, request.SourceLanguage, cacheNamespace);
        var legacyKey = cacheNamespace is null ? GetLegacyCacheKey(request.Text, request.TargetLanguage) : null;

        lock (cacheLock)
        {
            PruneFailedTexts(DateTime.UtcNow);
            if (IsProviderBlocked(failureScope, DateTime.UtcNow))
            {
                throw new TranslationTemporarilyUnavailableException();
            }

            if (TryGetCachedTranslation(cacheKey, legacyKey, out var cachedTranslation))
            {
                return new TranslationResult(request.Text, cachedTranslation, request.SourceLanguage, new TranslationUsage(CacheHitCount: 1));
            }

            if (failedTexts.TryGetValue(cacheKey, out var failedTime) && DateTime.UtcNow - failedTime < negativeCacheDuration)
            {
                throw new TranslationTemporarilyUnavailableException();
            }
        }

        try
        {
            var result = await innerService.TranslateAsync(request, ct);
            ValidateTranslation(request.Text, result.TranslatedText);
            var usage = result.Usage ?? TranslationUsage.Outbound(1, request.Text.Length);

            lock (cacheLock)
            {
                providerFailures.Remove(failureScope);
                failedTexts.Remove(cacheKey);
                SetCachedTranslation(cacheKey, result.TranslatedText);
                SaveCacheIfDue();
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
                RecordProviderFailure(failureScope, DateTime.UtcNow);
            }
            throw;
        }
    }

    public async Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        var targetLanguage = request.TargetLanguage;
        var texts = request.Texts;
        var cacheNamespace = GetCacheNamespace(request.SourceLanguage, targetLanguage);
        var failureScope = cacheNamespace ?? string.Empty;
        var results = new string[texts.Count];
        var missTexts = new List<string>();
        var missKeys = new List<string>();
        var batchMissMap = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var usage = TranslationUsage.None;
        var hasDeferredTranslation = false;

        lock (cacheLock)
        {
            PruneFailedTexts(DateTime.UtcNow);
            var isBlocked = IsProviderBlocked(failureScope, DateTime.UtcNow);

            for (var index = 0; index < texts.Count; index++)
            {
                var text = texts[index];
                var key = GetCacheKey(text, targetLanguage, request.SourceLanguage, cacheNamespace);
                var legacyKey = cacheNamespace is null ? GetLegacyCacheKey(text, targetLanguage) : null;

                if (isBlocked)
                {
                    hasDeferredTranslation = true;
                    continue;
                }

                if (TryGetCachedTranslation(key, legacyKey, out var cachedTranslation))
                {
                    results[index] = cachedTranslation;
                    usage = usage.Add(new TranslationUsage(CacheHitCount: 1));
                }
                else if (failedTexts.TryGetValue(key, out var failedTime) && DateTime.UtcNow - failedTime < negativeCacheDuration)
                {
                    hasDeferredTranslation = true;
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

        if (hasDeferredTranslation)
        {
            throw new TranslationTemporarilyUnavailableException();
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
                    providerFailures.Remove(failureScope);

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
                    SaveCacheIfDue();
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

                    RecordProviderFailure(failureScope, DateTime.UtcNow);
                }
                throw;
            }
        }

        return new BatchTranslationResult(results, usage);
    }

    internal void FlushCache()
    {
        while (true)
        {
            Task<bool>? pendingSave;
            long pendingVersion;

            lock (cacheLock)
            {
                if (pendingCacheSave is null)
                {
                    if (cacheVersion <= persistedCacheVersion)
                    {
                        return;
                    }

                    StartCacheSave(DateTime.UtcNow);
                }

                pendingSave = pendingCacheSave;
                pendingVersion = pendingCacheSaveVersion;
            }

            var saved = GetSaveResult(pendingSave!);
            CompletePendingSave(pendingSave!, pendingVersion, saved);
            if (!saved)
            {
                return;
            }
        }
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

    private bool TryGetCachedTranslation(string cacheKey, string? legacyKey, out string translatedText)
    {
        if (cache.TryGetValue(cacheKey, out translatedText!))
        {
            return true;
        }

        if (legacyKey is null || !cache.TryGetValue(legacyKey, out translatedText!))
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

    private void SaveCacheIfDue()
    {
        cacheVersion++;
        var now = DateTime.UtcNow;
        if (pendingCacheSave is not null || now < nextCacheSaveUtc)
        {
            return;
        }

        StartCacheSave(now);
    }

    private void StartCacheSave(DateTime now)
    {
        var snapshot = new Dictionary<string, string>(cache, StringComparer.Ordinal);
        var snapshotVersion = cacheVersion;
        var saveTask = Task.Run(() => TrySaveSnapshot(snapshot));
        pendingCacheSave = saveTask;
        pendingCacheSaveVersion = snapshotVersion;
        nextCacheSaveUtc = now + cacheSaveInterval;
        _ = saveTask.ContinueWith(
            completed => CompletePendingSave(completed, snapshotVersion, GetSaveResult(completed)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool TrySaveSnapshot(IReadOnlyDictionary<string, string> snapshot)
    {
        try
        {
            return cacheStore.Save(snapshot);
        }
        catch (Exception ex)
        {
            AppLog.Write("Screen cache save failed", ex);
            return false;
        }
    }

    private static bool GetSaveResult(Task<bool> saveTask)
    {
        try
        {
            return saveTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Write("Screen cache background save failed", ex);
            return false;
        }
    }

    private void CompletePendingSave(Task<bool> saveTask, long savedVersion, bool saved)
    {
        lock (cacheLock)
        {
            if (!ReferenceEquals(pendingCacheSave, saveTask))
            {
                return;
            }

            if (saved)
            {
                persistedCacheVersion = Math.Max(persistedCacheVersion, savedVersion);
            }

            pendingCacheSave = null;
            pendingCacheSaveVersion = 0;
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

    private bool IsProviderBlocked(string failureScope, DateTime now) =>
        providerFailures.TryGetValue(failureScope, out var failure)
        && now < failure.BlockUntil;

    private void RecordProviderFailure(string failureScope, DateTime now)
    {
        if (!providerFailures.TryGetValue(failureScope, out var failure))
        {
            failure = new ProviderFailureState();
            providerFailures[failureScope] = failure;
        }

        failure.ContinuousFailures++;
        if (failure.ContinuousFailures >= FailureThreshold)
        {
            failure.BlockUntil = now + circuitBreakerDuration;
        }
    }

    private string? GetCacheNamespace(string? sourceLanguage, string targetLanguage)
    {
        if (cacheNamespaceProvider is null)
        {
            return null;
        }

        var cacheNamespace = cacheNamespaceProvider(sourceLanguage, targetLanguage);
        return string.IsNullOrWhiteSpace(cacheNamespace)
            ? throw new InvalidOperationException("번역 캐시 공급자 식별자가 비어 있습니다.")
            : cacheNamespace.Trim().ToLowerInvariant();
    }

    private static string GetCacheKey(
        string text,
        string targetLanguage,
        string? sourceLanguage,
        string? cacheNamespace)
    {
        var source = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage.Trim().ToLowerInvariant();
        var normalizedText = TranslationTextNormalizer.CanonicalizeCacheText(text);
        return cacheNamespace is null
            ? $"v2\u001f{targetLanguage}\u001f{source}\u001f{normalizedText}"
            : $"v3\u001f{cacheNamespace}\u001f{targetLanguage.Trim().ToLowerInvariant()}\u001f{source}\u001f{normalizedText}";
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

    private sealed class ProviderFailureState
    {
        public int ContinuousFailures { get; set; }
        public DateTime BlockUntil { get; set; }
    }
}

public sealed class TranslationTemporarilyUnavailableException()
    : Exception("최근 번역 실패로 잠시 대기 중입니다. 자동으로 다시 시도합니다.");
