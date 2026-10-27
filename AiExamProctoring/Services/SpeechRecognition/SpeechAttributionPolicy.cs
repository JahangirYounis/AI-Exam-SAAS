using AiExamProctoring.Services.Proctoring;
namespace AiExamProctoring.Services.SpeechRecognition;

public static class SpeechAttributionPolicy
{
    // Supporting temporal evidence, not biometric speaker identification.
    public static bool MouthSupports(IncidentEpisode? mouth, DateTime capturedUtc) =>
        mouth?.StartedUtc is not null && mouth.ClearSinceUtc is null && mouth.Samples >= 2 &&
        (capturedUtc - mouth.LastSampleUtc).TotalSeconds is >= -1.5 and <= 2;
    public static bool Eligible(bool mouthSupported, bool questionRelated) => mouthSupported || questionRelated;
}
