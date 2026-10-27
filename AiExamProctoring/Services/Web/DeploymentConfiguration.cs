namespace AiExamProctoring.Services.Web;

public static class DeploymentConfiguration
{
    public static void ValidateAiSecrets(IConfiguration configuration, bool production)
    {
        foreach (var section in new[] { "FaceIdentity", "BehaviorAnalysis", "ObjectDetection", "SpeechRecognition" })
        {
            if (!configuration.GetValue($"{section}:Enabled", true)) continue;
            if (!Uri.TryCreate(configuration[$"{section}:BaseUrl"], UriKind.Absolute, out var url) ||
                (url.Scheme != "http" && url.Scheme != "https"))
                throw new InvalidOperationException($"{section} requires an absolute HTTP(S) BaseUrl.");
            if (production && (configuration[$"{section}:ApiKey"]?.Length ?? 0) < 32)
                throw new InvalidOperationException($"Set a strong {section}__ApiKey environment variable before production startup.");
        }
        if (production && (string.IsNullOrWhiteSpace(configuration["AllowedHosts"]) || configuration["AllowedHosts"] == "*"))
            throw new InvalidOperationException("Set AllowedHosts to your deployment hostnames before production startup.");
    }
}
