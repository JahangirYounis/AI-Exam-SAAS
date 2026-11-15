namespace AiExamProctoring.Services.SpeechRecognition;
public static class SpeechEvidencePolicy {
 // Heuristics screen unusable ASR; thresholds are not calibrated probabilities of misconduct.
 public static bool Intelligible(SpeechRecognitionResponse result, decimal minimum) =>
  result.SpeechDetected && !string.IsNullOrWhiteSpace(result.Transcript) &&
  result.Confidence >= Math.Max(.5m, minimum) && result.Confidence <= 1 &&
  double.IsFinite(result.VadSpeechSeconds) && result.VadSpeechSeconds >= .3 &&
  result.Segments.Any(s=>s.End>s.Start && s.Start>=0 && s.Confidence>=Math.Max(.5m,minimum) && s.NoSpeechProbability<=.4m);
}
