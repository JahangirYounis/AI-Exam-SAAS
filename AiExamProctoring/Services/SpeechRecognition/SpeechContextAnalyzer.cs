using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AiExamProctoring.Services.SpeechRecognition;

public sealed record SpeechContextMatch(bool Related, string Reason);

// An explainable text matcher layered on Model 4, not a trained speaker or cheating classifier.
public static class SpeechContextAnalyzer
{
    public static bool IsHighSeverity(SpeechContextAnalysis? analysis) =>
        analysis?.Ready == true && analysis.Related && !analysis.Uncertain &&
        analysis.Score is double score && double.IsFinite(score) && score > .60 && score <= 1;

    private static readonly Regex Words = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static string[] Tokens(string text) => Words.Matches(WebUtility.HtmlDecode(
        Regex.Replace(text, "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        .Normalize(NormalizationForm.FormKC).ToLowerInvariant()).Select(x => x.Value).ToArray();

    public static SpeechContextMatch Analyze(string transcript, string question, IEnumerable<string?> options)
    {
        var spoken = Tokens(transcript);
        var labels = new HashSet<string> { "a", "b", "c", "d", "e", "1", "2", "3", "4", "5", "one", "two", "three", "four", "five", "اے", "بی", "سی", "ڈی", "ایک", "دو", "تین", "چار" };
        var cues = new HashSet<string> { "option", "answer", "choose", "select", "آپشن", "جواب" };
        for (var i = 0; i < spoken.Length - 1; i++)
            if (cues.Contains(spoken[i]) && labels.Contains(spoken[i + 1]))
            {
                // "Answer a question" uses the article a, not option A.
                if (spoken[i + 1] == "a" && i + 2 < spoken.Length &&
                    spoken[i + 2] is not ("please" or "now" or "is" or "correct")) continue;
                return new(true, "An answer or option label was spoken.");
            }

        if (ContainsPhrase(spoken, Tokens(question), 4))
            return new(true, "At least four consecutive words match the displayed question.");
        foreach (var option in options.Where(x => !string.IsNullOrWhiteSpace(x)))
            if (ContainsPhrase(spoken, Tokens(option!), 3))
                return new(true, "At least three consecutive words match a displayed option.");
        return new(false, "No sufficiently specific question or option phrase matched.");
    }

    private static bool ContainsPhrase(string[] spoken, string[] source, int minimum)
    {
        if (spoken.Length < minimum || source.Length < minimum) return false;
        for (var i = 0; i <= spoken.Length - minimum; i++)
            for (var j = 0; j <= source.Length - minimum; j++)
                if (spoken.AsSpan(i, minimum).SequenceEqual(source.AsSpan(j, minimum))) return true;
        return false;
    }
}
