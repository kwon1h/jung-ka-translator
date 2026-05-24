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

public sealed class GoogleUnofficialTranslationService(HttpClient httpClient) : ITranslationService
{
    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return new TranslationResult(request.Text, string.Empty, request.SourceLanguage, TranslationUsage.None);
        }

        var source = string.IsNullOrWhiteSpace(request.SourceLanguage) ? "auto" : request.SourceLanguage;
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&sl={Uri.EscapeDataString(source)}&tl={Uri.EscapeDataString(request.TargetLanguage)}&q={Uri.EscapeDataString(request.Text)}";

        using var response = await httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google 번역 요청 실패: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var translatedTextBuilder = new StringBuilder();
        string? detectedLanguage = null;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var firstArray = root[0];
            if (firstArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in firstArray.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
                    {
                        var segment = element[0].GetString();
                        if (segment != null)
                        {
                            translatedTextBuilder.Append(segment);
                        }
                    }
                }
            }

            if (root.GetArrayLength() > 2)
            {
                detectedLanguage = root[2].GetString();
            }
        }

        return new TranslationResult(
            request.Text,
            translatedTextBuilder.ToString(),
            detectedLanguage ?? request.SourceLanguage,
            TranslationUsage.Outbound(1, request.Text.Length));
    }

    public async Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        if (request.Texts.Count == 0)
        {
            return new BatchTranslationResult(Array.Empty<string>(), TranslationUsage.None);
        }

        if (request.Texts.Count == 1)
        {
            var result = await TranslateAsync(new TranslationRequest(request.Texts[0], request.TargetLanguage, request.SourceLanguage), ct);
            return new BatchTranslationResult(new[] { result.TranslatedText }, result.Usage);
        }

        var delimiter = $"\n\uE000GOT_SPLIT_{Guid.NewGuid():N}\uE000\n";
        var packedText = string.Join(delimiter, request.Texts);
        var packedResult = await TranslateAsync(new TranslationRequest(packedText, request.TargetLanguage, request.SourceLanguage), ct);
        var packedParts = packedResult.TranslatedText.Split(delimiter, StringSplitOptions.None);
        if (packedParts.Length == request.Texts.Count)
        {
            return new BatchTranslationResult(packedParts, TranslationUsage.Outbound(1, packedText.Length));
        }

        // Google Unofficial API has no batch endpoint. Use parallel requests with a Semaphore to avoid Rate Limits.
        var semaphore = new SemaphoreSlim(4);
        var tasks = new List<Task<TranslationResult>>();

        foreach (var text in request.Texts)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    return await TranslateAsync(new TranslationRequest(text, request.TargetLanguage, request.SourceLanguage), ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        var results = await Task.WhenAll(tasks);
        var usage = packedResult.Usage ?? TranslationUsage.Outbound(1, packedText.Length);
        foreach (var result in results)
        {
            usage = usage.Add(result.Usage ?? TranslationUsage.Outbound(1, result.SourceText.Length));
        }

        return new BatchTranslationResult(results.Select(result => result.TranslatedText).ToArray(), usage);
    }
}
