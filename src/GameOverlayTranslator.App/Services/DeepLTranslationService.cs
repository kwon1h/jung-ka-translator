using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Collections.Generic;
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
        return new TranslationResult(request.Text, translatedText, detected, TranslationUsage.Outbound(1, request.Text.Length));
    }

    public async Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        var authKey = authKeyProvider();
        if (string.IsNullOrWhiteSpace(authKey))
        {
            throw new InvalidOperationException("DeepL API 인증 키를 입력하세요.");
        }

        if (request.Texts.Count == 0)
        {
            return new BatchTranslationResult(Array.Empty<string>(), TranslationUsage.None);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api-free.deepl.com/v2/translate");
        message.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", authKey.Trim());
        
        var parameters = new List<KeyValuePair<string, string>>();
        foreach (var text in request.Texts)
        {
            parameters.Add(new("text", text));
        }
        parameters.Add(new("target_lang", request.TargetLanguage.ToUpperInvariant()));
        if (!string.IsNullOrWhiteSpace(request.SourceLanguage))
        {
            parameters.Add(new("source_lang", request.SourceLanguage.ToUpperInvariant()));
        }
        
        message.Content = new FormUrlEncodedContent(parameters);

        using var response = await httpClient.SendAsync(message, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"DeepL 번역 요청 실패: {(int)response.StatusCode} {ReadError(json)}");
        }

        using var document = JsonDocument.Parse(json);
        var translations = document.RootElement.GetProperty("translations");
        var translatedTexts = new List<string>();
        for (int i = 0; i < translations.GetArrayLength(); i++)
        {
            var text = translations[i].GetProperty("text").GetString() ?? string.Empty;
            translatedTexts.Add(text);
        }
        
        return new BatchTranslationResult(translatedTexts, TranslationUsage.Outbound(1, request.Texts.Sum(text => text.Length)));
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
