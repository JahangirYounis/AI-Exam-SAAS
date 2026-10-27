namespace AiExamProctoring.Services.Proctoring;

public static class ProctoringObjectPolicy
{
    public static readonly HashSet<string> BannedLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "phone", "mobile_phone", "cell phone", "watch", "smartwatch", "wristwatch", "smart_watch",
        "book", "earphones", "earphone", "earbuds", "headphones", "headphone", "bottle", "water_bottle",
        "extra_screen", "monitor", "tv", "laptop", "tablet", "screen", "extra_person",
        "microphone", "calculator", "notes_paper", "lecture-notes", "pen_pencil", "pen", "pencil", "bag"
    };
}
