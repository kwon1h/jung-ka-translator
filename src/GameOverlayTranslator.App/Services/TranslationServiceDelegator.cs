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
        ITranslationService service = settings.TranslatorType switch
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
        ITranslationService service = settings.TranslatorType switch
        {
            TranslationServiceType.GoogleUnofficial => googleUnofficialService,
            TranslationServiceType.GoogleWebApp => googleWebAppService,
            _ => deepLService
        };
        return service.TranslateBatchAsync(request, ct);
    }
}
