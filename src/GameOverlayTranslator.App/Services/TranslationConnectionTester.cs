using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

internal static class TranslationConnectionTester
{
    public static async Task<TranslationResult> TestAsync(
        ITranslationService service,
        TranslationLanguage targetLanguage,
        CancellationToken ct)
    {
        var request = CreateRequest(targetLanguage);
        var result = await service.TranslateAsync(request, ct);
        if (string.IsNullOrWhiteSpace(result.TranslatedText))
        {
            throw new InvalidOperationException("번역 서비스가 빈 결과를 반환했습니다.");
        }

        return result;
    }

    internal static TranslationRequest CreateRequest(TranslationLanguage targetLanguage) =>
        targetLanguage.Code.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? new TranslationRequest("연결 테스트", targetLanguage.Code, "ko")
            : new TranslationRequest("Connection test", targetLanguage.Code, "en");
}
