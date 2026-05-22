using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class DeepLTranslationService(HttpClient httpClient, Func<string?> authKeyProvider) : ITranslationService
{
    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var authKey = authKeyProvider();
        if (string.IsNullOrWhiteSpace(authKey))
        {
            throw new InvalidOperationException("DeepL API 인증 키를 입력하세요.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api-free.deepl.com/v2/translate");
        message.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", authKey.Trim());
        message.Content = new FormUrlEncodedContent(BuildParameters(request));

        using var response = await httpClient.SendAsync(message, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"DeepL 번역 요청 실패: {(int)response.StatusCode} {ReadError(json)}");
        }

        using var document = JsonDocument.Parse(json);
        var translation = document.RootElement.GetProperty("translations")[0];
        var translatedText = translation.GetProperty("text").GetString() ?? string.Empty;
        var detected = translation.TryGetProperty("detected_source_language", out var detectedNode)
            ? detectedNode.GetString()
            : request.SourceLanguage;
        return new TranslationResult(request.Text, translatedText, detected);
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildParameters(TranslationRequest request)
    {
        yield return new("text", request.Text);
        yield return new("target_lang", request.TargetLanguage.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(request.SourceLanguage))
        {
            yield return new("source_lang", request.SourceLanguage.ToUpperInvariant());
        }
    }

    private static string ReadError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("message", out var message)
                ? message.GetString() ?? "오류 본문 없음"
                : "오류 본문 없음";
        }
        catch
        {
            return "오류 본문을 읽지 못했습니다.";
        }
    }
}
