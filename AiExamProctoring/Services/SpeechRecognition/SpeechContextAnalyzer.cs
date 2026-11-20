using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace AiExamProctoring.Services.SpeechRecognition;

// Local explainable cues supplement the Model 5 semantic score; neither proves misconduct.
public sealed record SpeechContextMatch(
    bool Related, string Reason, IReadOnlyList<string>? MatchedPhrases = null,
    string MatchKind = "none", bool Ambiguous = false);

public static class SpeechContextAnalyzer
{
    public static (string Status, string? Question, string? Phrases, double? Score) ReadReview(string? metadata)
    {
        try
        {
            using var document = JsonDocument.Parse(metadata ?? "{}");
            var data = document.RootElement;
            if (data.ValueKind != JsonValueKind.Object) return ("Analysis details unavailable", null, null, null);
            var status = StringValue(data, "contextStatus") switch
            {
                "missing_capture_context" => "Capture-time question unavailable",
                "unintelligible" => "Transcript confidence insufficient",
                "possible_match" => "Possible exam-related speech",
                "phrase_match_semantics_unavailable" => "Matching phrase found · semantic analysis unavailable",
                "generic_cue" => "Generic exam reference",
                "no_supported_match" => "No supported question match",
                "semantic_unavailable" => "Semantic analysis unavailable",
                _ => "Recorded speech analysis"
            };
            var question = data.TryGetProperty("capturedQuestions", out var questions) && questions.ValueKind == JsonValueKind.Array
                ? questions.EnumerateArray().Where(q => q.ValueKind == JsonValueKind.Object)
                    .Select(q => StringValue(q, "QuestionText")).FirstOrDefault() : null;
            var phrases = data.TryGetProperty("matchedPhrases", out var matches) && matches.ValueKind == JsonValueKind.Array
                ? string.Join(", ", matches.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString())) : null;
            double? score = data.TryGetProperty("semanticScore", out var value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
            return (status, question, phrases, score);
        }
        catch (JsonException) { return ("Analysis details unavailable", null, null, null); }

        static string? StringValue(JsonElement value, string field) =>
            value.TryGetProperty(field, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    }
    public static bool IsHighSeverity(SpeechContextAnalysis? analysis) =>
        analysis?.Ready == true && analysis.Related && !analysis.Uncertain &&
        analysis.Score is double score && double.IsFinite(score) && score > .60 && score <= 1;

    private static readonly Regex Words = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "option", "options", "answer", "answers", "question", "questions", "choose", "select",
        "ka", "kya", "hai", "he", "ye", "iska", "is", "the", "a", "an", "b", "c", "d",
        "one", "two", "three", "four", "five", "jawab", "sawal", "alpha", "alfa",
        "beta", "gamma", "delta", "سوال", "جواب", "آپشن", "کا", "کیا", "یہ"
    };
    private static string[] Tokens(string text) => Words.Matches(WebUtility.HtmlDecode(
        Regex.Replace(text ?? "", "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        .Normalize(NormalizationForm.FormKC).ToLowerInvariant()).Select(x => x.Value).ToArray();

    // A short, question-specific concept can corroborate a semantic paraphrase
    // even when no four-word verbatim phrase is repeated (e.g. Roman Urdu).
    // Never turn a generic "option B" or a single isolated noun into proof.
    private static readonly HashSet<string> NonSpecific = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "we", "you", "my", "your", "what", "which", "who", "when", "where", "why", "how",
        "do", "does", "did", "was", "were", "are", "am", "be", "been", "to", "of", "for", "in", "on",
        "with", "and", "or", "that", "this", "it", "please", "can", "could", "would", "should",
        "have", "has", "had", "need", "think", "tell", "give", "me", "us", "up", "out", "about",
        "karta", "karte", "karti", "kar", "raha", "rahi", "hain", "han", "hon", "ho", "mein", "mai",
        "سے", "میں", "ہے", "ہیں", "پر", "اور", "جو", "کو", "کے", "کی", "یہ", "وہ"
    };

    private static string? SpecificQuestionTerm(string[] spoken, string question, IEnumerable<string?> options)
    {
        static bool Useful(string word) => word.Length >= 3 && !Generic.Contains(word) && !NonSpecific.Contains(word);
        var content = spoken.Where(Useful).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (content.Length < 2) return null; // an isolated topic word is not enough
        var reference = Tokens(question).Concat(options.Where(o => o is not null).SelectMany(o => Tokens(o!)));
        var supported = reference.Where(Useful).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return content.FirstOrDefault(supported.Contains);
    }

    public static bool HasSpecificQuestionAnchor(string transcript, string question, IEnumerable<string?> options) =>
        SpecificQuestionTerm(Tokens(transcript), question, options) is not null;

    private static string? SharedPhrase(string[] spoken, string[] reference, int minimum)
    {
        if (spoken.Length < minimum || reference.Length < minimum) return null;
        for (var width = Math.Min(6, Math.Min(spoken.Length, reference.Length)); width >= minimum; width--)
            for (var i = 0; i <= spoken.Length - width; i++)
                for (var j = 0; j <= reference.Length - width; j++)
                {
                    var span = spoken.AsSpan(i, width);
                    if (span.SequenceEqual(reference.AsSpan(j, width)) &&
                        span.ToArray().Count(w => w.Length >= 4 && !Generic.Contains(w)) >= 2)
                        return string.Join(" ", span.ToArray());
                }
        return null;
    }

    public static SpeechContextMatch Analyze(string transcript, string question, IEnumerable<string?> options)
    {
        var spoken = Tokens(transcript);
        if (spoken.Length == 0) return new(false, "No intelligible speech.");
        var phrases = new List<string>();
        var questionMatch = SharedPhrase(spoken, Tokens(question), 4);
        if (questionMatch is not null) phrases.Add(questionMatch);
        foreach (var option in options.Where(o => !string.IsNullOrWhiteSpace(o)))
        {
            var optionMatch = SharedPhrase(spoken, Tokens(option!), 2);
            if (optionMatch is not null && !phrases.Contains(optionMatch)) phrases.Add(optionMatch);
        }
        if (phrases.Count > 0)
            return new(true, "Specific words from the captured question or option were spoken; teacher review required.",
                phrases.Take(4).ToArray(), questionMatch is not null ? "question_phrase" : "option_phrase");

        var utterance = string.Join(" ", spoken);
        var generic = Regex.IsMatch(utterance,
            @"\b(?:option|answer|question|choose|select|jawab|sawal|alfa|alpha|beta|gamma|delta)\b|(?:سوال|جواب|آپشن)",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        // Exact-phrase matching alone misses multilingual paraphrases. A specific
        // question/option term is only supporting evidence: Model 5 must independently
        // confirm the same captured question before a policy warning can qualify.
        var specificTerm = SpecificQuestionTerm(spoken, question, options);
        if (specificTerm is not null)
            return new(false, "A specific question concept was heard; semantic confirmation is required.",
                new[] { specificTerm }, "question_term", false);
        // 'Option B' alone is never evidence of the specific answer.
        if (generic)
            return new(false, "Generic exam/option phrase heard; question relevance uncertain, review only.",
                Array.Empty<string>(), "generic_exam_cue", true);
        return new(false, "No question-specific words matched the captured question.");
    }
}
