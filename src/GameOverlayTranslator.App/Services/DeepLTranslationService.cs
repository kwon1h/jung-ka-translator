using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Collections.Generic;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class DeepLTranslationService(HttpClient httpClient, Func<string?> authKeyProvider) : ITranslationService
{
    private const string FreeTranslateEndpoint = "https://api-free.deepl.com/v2/translate";
    private const string ProTranslateEndpoint = "https://api.deepl.com/v2/translate";
    private static readonly HashSet<string> SupportedSourceLanguages = new(StringComparer.Ordinal)
    {
        "AR", "BG", "CS", "DA", "DE", "EL", "EN", "ES", "ET", "FI", "FR", "HE",
        "HU", "ID", "IT", "JA", "KO", "LT", "LV", "NB", "NL", "PL", "PT", "RO",
        "RU", "SK", "SL", "SV", "TH", "TR", "UK", "VI", "ZH"
    };
    private static readonly HashSet<string> SupportedTargetLanguages = new(StringComparer.Ordinal)
    {
        "AR", "BG", "CS", "DA", "DE", "EL", "EN-GB", "EN-US", "ES", "ES-419", "ET",
        "FI", "FR", "HE", "HU", "ID", "IT", "JA", "KO", "LT", "LV", "NB", "NL", "PL",
        "PT-BR", "PT-PT", "RO", "RU", "SK", "SL", "SV", "TH", "TR", "UK", "VI",
        "ZH", "ZH-HANS", "ZH-HANT"
    };

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var authKey = authKeyProvider()?.Trim();
        if (string.IsNullOrWhiteSpace(authKey))
        {
            throw new InvalidOperationException("DeepL API 인증 키를 입력하세요.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, GetTranslateEndpoint(authKey));
        message.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", authKey);
        message.Content = new FormUrlEncodedContent(BuildParameters(request));

        using var response = await httpClient.SendAsync(message, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw TranslationHttpFailure.Create(
                $"DeepL 번역 요청 실패: {(int)response.StatusCode} {ReadError(json)}",
                response);
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
        var authKey = authKeyProvider()?.Trim();
        if (string.IsNullOrWhiteSpace(authKey))
        {
            throw new InvalidOperationException("DeepL API 인증 키를 입력하세요.");
        }

        if (request.Texts.Count == 0)
        {
            return new BatchTranslationResult(Array.Empty<string>(), TranslationUsage.None);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, GetTranslateEndpoint(authKey));
        message.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", authKey);
        
        var parameters = new List<KeyValuePair<string, string>>();
        foreach (var text in request.Texts)
        {
            parameters.Add(new("text", text));
        }
        parameters.Add(new("target_lang", NormalizeTargetLanguageCode(request.TargetLanguage)));
        var sourceLanguage = NormalizeSourceLanguageCode(request.SourceLanguage);
        if (sourceLanguage is not null)
        {
            parameters.Add(new("source_lang", sourceLanguage));
        }
        
        message.Content = new FormUrlEncodedContent(parameters);

        using var response = await httpClient.SendAsync(message, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw TranslationHttpFailure.Create(
                $"DeepL 번역 요청 실패: {(int)response.StatusCode} {ReadError(json)}",
                response);
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

    internal static string GetTranslateEndpoint(string authKey) =>
        authKey.Trim().EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
            ? FreeTranslateEndpoint
            : ProTranslateEndpoint;

    internal static bool SupportsSourceLanguage(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode) || NormalizeSourceLanguageCode(languageCode) is not null;

    internal static bool SupportsTargetLanguage(string? languageCode) =>
        !string.IsNullOrWhiteSpace(languageCode)
        && SupportedTargetLanguages.Contains(NormalizeTargetLanguageCode(languageCode));

    private static IEnumerable<KeyValuePair<string, string>> BuildParameters(TranslationRequest request)
    {
        yield return new("text", request.Text);
        yield return new("target_lang", NormalizeTargetLanguageCode(request.TargetLanguage));
        var sourceLanguage = NormalizeSourceLanguageCode(request.SourceLanguage);
        if (sourceLanguage is not null)
        {
            yield return new("source_lang", sourceLanguage);
        }
    }

    private static string NormalizeTargetLanguageCode(string languageCode) =>
        languageCode.Trim().ToLowerInvariant() switch
        {
            "ko" => "KO",
            "en" => "EN-US",
            "pt" => "PT-PT",
            "zh-cn" or "zh-hans" => "ZH-HANS",
            _ => languageCode.Trim().ToUpperInvariant()
        };

    internal static string? NormalizeSourceLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        var normalized = languageCode.Trim().ToLowerInvariant() switch
        {
            "zh-cn" or "zh-hans" or "zh-hant" => "ZH",
            "en-us" or "en-gb" => "EN",
            "pt-br" or "pt-pt" => "PT",
            var code => code.ToUpperInvariant()
        };
        return SupportedSourceLanguages.Contains(normalized) ? normalized : null;
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
