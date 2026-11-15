using AiExamProctoring.Areas.Teacher.Controllers;

namespace AiExamProctoring.Services.Notifications;

// Pure operations: deterministic stable IDs, deduplication and filters.
// Kept free of HttpContext/database calls so regression tests can exercise them.
public static class TeacherNotificationRules
{
    public static List<TeacherActivityItem> Deduplicate(IEnumerable<TeacherActivityItem> events) =>
        events.Where(x => x.EntryId > 0 && !string.IsNullOrWhiteSpace(x.Kind))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.OccurredAtUtc ?? DateTime.MinValue).First())
            .OrderBy(x => x.Read)
            .ThenBy(x => x.Priority)
            .ThenByDescending(x => x.OccurredAtUtc ?? DateTime.MinValue)
            .ThenByDescending(x => x.EntryId).ToList();

    public static List<TeacherActivityItem> Filter(IEnumerable<TeacherActivityItem> events,
        string? category, bool unreadOnly)
    {
        string selected = category?.Trim() ?? "All";
        if (selected is not ("All" or "Monitoring" or "Review Requests" or "Messages" or
            "Examinations" or "Invitations" or "Connections")) selected = "All";
        return events.Where(x => (!unreadOnly || !x.Read) &&
            (selected == "All" || x.Category == selected))
            .OrderBy(x => x.Read).ThenBy(x => x.Priority)
            .ThenByDescending(x => x.OccurredAtUtc ?? DateTime.MinValue)
            .ThenByDescending(x => x.EntryId).ToList();
    }
}
