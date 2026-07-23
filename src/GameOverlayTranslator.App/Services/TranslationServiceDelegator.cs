using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class TranslationServiceDelegator : ITranslationService
{
    private readonly DeepLTranslationService deepLService;
    private readonly GoogleUnofficialTranslationService googleUnofficialService;
    private readonly GoogleWebAppTranslationService googleWebAppService;
    private readonly Func<AppSettings> settingsProvider;

    public TranslationServiceDelegator(
        HttpClient httpClient,
        Func<string?> deepLKeyProvider,
        Func<AppSettings> settingsProvider)
    {
        this.settingsProvider = settingsProvider;
        this.deepLService = new DeepLTranslationService(httpClient, deepLKeyProvider);
        this.googleUnofficialService = new GoogleUnofficialTranslationService(httpClient);
        this.googleWebAppService = new GoogleWebAppTranslationService(httpClient, () => this.settingsProvider().GoogleWebAppUrl);
    }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var settings = settingsProvider();
        ITranslationService service = ResolveEffectiveTranslator(
            settings.TranslatorType,
            request.SourceLanguage,
            request.TargetLanguage) switch
        {
            TranslationServiceType.GoogleUnofficial => googleUnofficialService,
            TranslationServiceType.GoogleWebApp => googleWebAppService,
            _ => deepLService
        };
        return service.TranslateAsync(request, ct);
    }

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        var settings = settingsProvider();
        ITranslationService service = ResolveEffectiveTranslator(
            settings.TranslatorType,
            request.SourceLanguage,
            request.TargetLanguage) switch
        {
            TranslationServiceType.GoogleUnofficial => googleUnofficialService,
            TranslationServiceType.GoogleWebApp => googleWebAppService,
            _ => deepLService
        };
        return service.TranslateBatchAsync(request, ct);
    }

    internal string GetCacheNamespace(string? sourceLanguage, string targetLanguage) =>
        ResolveEffectiveTranslator(
            settingsProvider().TranslatorType,
            sourceLanguage,
            targetLanguage).ToString();

    internal static TranslationServiceType ResolveEffectiveTranslator(
        TranslationServiceType selectedTranslator,
        string? sourceLanguage,
        string? targetLanguage) =>
        selectedTranslator == TranslationServiceType.DeepL
        && (!DeepLTranslationService.SupportsSourceLanguage(sourceLanguage)
            || !DeepLTranslationService.SupportsTargetLanguage(targetLanguage))
            ? TranslationServiceType.GoogleUnofficial
            : selectedTranslator;

    internal static string GetDisplayName(TranslationServiceType translatorType) =>
        translatorType switch
        {
            TranslationServiceType.DeepL => "DeepL API",
            TranslationServiceType.GoogleUnofficial => "Google 번역",
            TranslationServiceType.GoogleWebApp => "Google Apps Script",
            _ => translatorType.ToString()
        };

    internal static string GetEffectiveDisplayName(
        TranslationServiceType selectedTranslator,
        string? sourceLanguage,
        string? targetLanguage)
    {
        var effectiveTranslator = ResolveEffectiveTranslator(
            selectedTranslator,
            sourceLanguage,
            targetLanguage);
        var displayName = GetDisplayName(effectiveTranslator);
        return effectiveTranslator != selectedTranslator
            ? $"{displayName} (DeepL 미지원 언어 자동 전환)"
            : displayName;
    }

    internal static bool RequiresDeepLApiKey(TranslationServiceType effectiveTranslator) =>
        effectiveTranslator == TranslationServiceType.DeepL;

    internal static bool RequiresGoogleWebAppUrl(TranslationServiceType effectiveTranslator) =>
        effectiveTranslator == TranslationServiceType.GoogleWebApp;
}
