namespace AiExamProctoring.Services.Web;

public static class PrivateStorage
{
    public static string Root(IWebHostEnvironment environment)
    {
        var configured = Environment.GetEnvironmentVariable("EXAM_PRIVATE_STORAGE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured))
                throw new InvalidOperationException("EXAM_PRIVATE_STORAGE_ROOT must be an absolute persistent directory.");
            return Path.GetFullPath(configured);
        }
        // App Service deployments can mount wwwroot read-only. Runtime files
        // belong in its persistent data directory rather than the publish package.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")))
        {
            var azureHome = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(azureHome))
                throw new InvalidOperationException("Configure EXAM_PRIVATE_STORAGE_ROOT for this Azure host.");
            return Path.Combine(azureHome, "data", "exam-private");
        }
        return Path.Combine(environment.ContentRootPath, "App_Data");
    }
}
