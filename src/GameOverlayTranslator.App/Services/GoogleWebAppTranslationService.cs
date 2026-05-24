using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class GoogleWebAppTranslationService(HttpClient httpClient, Func<string?> webAppUrlProvider) : ITranslationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var url = webAppUrlProvider();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Google Apps Script 웹 앱 URL이 설정되지 않았습니다. 설정 탭에서 URL을 입력하세요.");
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return new TranslationResult(request.Text, string.Empty, request.SourceLanguage, TranslationUsage.None);
        }

        var payload = new
        {
            q = request.Text,
            target = request.TargetLanguage,
            source = request.SourceLanguage ?? string.Empty
        };

        var jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var response = await httpClient.PostAsync(url.Trim(), content, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Web App 번역 요청 실패: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.TryGetProperty("translatedText", out var textProp))
        {
            var translatedText = textProp.GetString() ?? string.Empty;
            return new TranslationResult(request.Text, translatedText, request.SourceLanguage, TranslationUsage.Outbound(1, request.Text.Length));
        }

        throw new InvalidOperationException("Google Web App 번역 응답이 올바르지 않습니다. 'translatedText' 속성을 찾을 수 없습니다.");
    }

    public async Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        var url = webAppUrlProvider();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Google Apps Script 웹 앱 URL이 설정되지 않았습니다. 설정 탭에서 URL을 입력하세요.");
        }

        if (request.Texts.Count == 0)
        {
            return new BatchTranslationResult(Array.Empty<string>(), TranslationUsage.None);
        }

        var payload = new
        {
            q = request.Texts,
            target = request.TargetLanguage,
            source = request.SourceLanguage ?? string.Empty
        };

        var jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var response = await httpClient.PostAsync(url.Trim(), content, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Web App 번역 요청 실패: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.TryGetProperty("translatedTexts", out var textsProp) && textsProp.ValueKind == JsonValueKind.Array)
        {
            var translatedTexts = new List<string>();
            foreach (var element in textsProp.EnumerateArray())
            {
                translatedTexts.Add(element.GetString() ?? string.Empty);
            }
            return new BatchTranslationResult(translatedTexts, TranslationUsage.Outbound(1, request.Texts.Sum(text => text.Length)));
        }

        // If the web app didn't return translatedTexts, fallback to translating sequentially.
        var fallbackResults = new List<string>();
        var usage = TranslationUsage.None;
        foreach (var text in request.Texts)
        {
            var res = await TranslateAsync(new TranslationRequest(text, request.TargetLanguage, request.SourceLanguage), ct);
            fallbackResults.Add(res.TranslatedText);
            usage = usage.Add(res.Usage ?? TranslationUsage.Outbound(1, text.Length));
        }
        return new BatchTranslationResult(fallbackResults, usage);
    }
}
