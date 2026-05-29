using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed record DiagnosticLogEntry(
    string Status,
    string Source,
    string Rule,
    string Reason,
    string ApiUsage);

public static class DiagnosticLogFormatter
{
    public static DiagnosticLogEntry? Create(SessionUpdate update)
    {
        if (update.DiagnosticKind != DiagnosticKind.OcrTranslated || update.TranslationRequestCount <= 0)
        {
            return null;
        }

        return new DiagnosticLogEntry(
            update.Status,
            update.DiagnosticSourceText ?? update.OcrRawText ?? update.SourceText ?? string.Empty,
            update.FilterRule ?? string.Empty,
            update.FilterReason ?? string.Empty,
            $"{update.TranslationRequestCount}건 {update.TranslationCharacterCount}자");
    }
}
