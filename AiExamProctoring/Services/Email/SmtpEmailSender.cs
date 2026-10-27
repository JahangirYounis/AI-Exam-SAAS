using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace AiExamProctoring.Services.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IConfiguration configuration,
        ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var host = _configuration["Email:Smtp:Host"];
        var portText = _configuration["Email:Smtp:Port"];
        var username = _configuration["Email:Smtp:Username"];
        var password = _configuration["Email:Smtp:Password"];
        var from = _configuration["Email:Smtp:From"] ?? username;

        // Development-friendly behavior: if SMTP is not configured,
        // keep the application usable and log a safe diagnostic.
        // Configure SMTP for real password-reset emails.
        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(from))
        {
            _logger.LogWarning(
                "SMTP email is not configured. Email notification for {Email} was not sent.",
                email);
            return;
        }

        var port = int.TryParse(portText, out var parsedPort)
            ? parsedPort
            : 587;

        using var message = new MailMessage
        {
            From = new MailAddress(from),
            Subject = subject,
            Body = htmlMessage,
            IsBodyHtml = true
        };

        message.To.Add(new MailAddress(email));

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password)
        };

        await client.SendMailAsync(message);
    }
}
