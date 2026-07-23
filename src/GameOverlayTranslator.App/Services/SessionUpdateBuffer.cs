using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

internal sealed class SessionUpdateBuffer
{
    private readonly object gate = new();
    private readonly List<SessionUpdate> pending = [];
    private bool dispatchScheduled;

    public bool Enqueue(SessionUpdate update)
    {
        lock (gate)
        {
            pending.Add(update);
            if (dispatchScheduled)
            {
                return false;
            }

            dispatchScheduled = true;
            return true;
        }
    }

    public IReadOnlyList<SessionUpdate> Drain()
    {
        lock (gate)
        {
            if (pending.Count == 0)
            {
                dispatchScheduled = false;
                return Array.Empty<SessionUpdate>();
            }

            var updates = pending.ToArray();
            pending.Clear();
            dispatchScheduled = false;
            return updates;
        }
    }

    public static SessionUpdate? SelectLatestOverlayUpdate(
        IReadOnlyList<SessionUpdate> updates,
        TranslationMode mode) =>
        mode == TranslationMode.Chat
            ? updates.LastOrDefault(update => update.ChatItems is not null)
                ?? updates.LastOrDefault(update => update.IsChatLine)
            : updates.LastOrDefault(update => update.ScreenItems is not null);
}
