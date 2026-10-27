using System.Diagnostics;

namespace AiExamProctoring.Services.Web;

/// <summary>Starts the bundled local APIs when launching from Visual Studio on Windows.</summary>
public sealed class DevelopmentAiStartupService(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<DevelopmentAiStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!environment.IsDevelopment() || !OperatingSystem.IsWindows() ||
            !configuration.GetValue("LocalAiServices:AutoStart", true))
            return;

        // Allow the website to start while the larger models load.
        await Task.Yield();
        var projectRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
        var launcher = Path.Combine(projectRoot, "START_AI_SERVICES.ps1");
        if (!File.Exists(launcher))
        {
            logger.LogWarning("Local AI launcher was not found. Start the configured AI services separately.");
            return;
        }

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", launcher })
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return;
            var output = process.StandardOutput.ReadToEndAsync(stoppingToken);
            var error = process.StandardError.ReadToEndAsync(stoppingToken);
            await process.WaitForExitAsync(stoppingToken);
            var summary = await output;
            var failure = await error;
            if (process.ExitCode == 0)
                logger.LogInformation("Local AI services initialized. {Summary}", summary.Trim());
            else
                logger.LogWarning("Local AI startup failed. {Summary} {Error}", summary.Trim(), failure.Trim());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not start local AI services. Run START_AI_SERVICES.ps1 from the project folder.");
        }
    }
}
