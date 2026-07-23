using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

internal sealed class SessionStatusTracker
{
    private bool recoveryPending;

    public SessionStatusDisplay? Observe(SessionUpdate update)
    {
        if (update.IsError)
        {
            recoveryPending = true;
            return new SessionStatusDisplay(update.Status, true);
        }

        if (update.FilterRule is "CaptureDeferred" or "TranslationCooldown")
        {
            recoveryPending = true;
            return new SessionStatusDisplay(update.Status, false);
        }

        if (update.Status is TranslationSession.RunningStatus or TranslationSession.StoppedStatus)
        {
            recoveryPending = false;
            return new SessionStatusDisplay(update.Status, false);
        }

        if (update.DiagnosticKind == DiagnosticKind.OcrTranslated || recoveryPending)
        {
            recoveryPending = false;
            return new SessionStatusDisplay(TranslationSession.RunningStatus, false);
        }

        return null;
    }
}

internal sealed record SessionStatusDisplay(string Text, bool IsError);
