using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace AiExamProctoring.Services.Proctoring;

public sealed record ProctoringVerificationTicket(
    string StudentId,
    int ExamId,
    decimal IdentityScore,
    string ModelVersion,
    DateTime VerifiedAtUtc,
    DateTime ExpiresAtUtc,
    string Nonce);

public interface IProctoringVerificationTicketService
{
    string Create(ProctoringVerificationTicket ticket);
    bool TryValidate(string? protectedTicket, out ProctoringVerificationTicket? ticket);
}

/// <summary>
/// Short-lived, server-protected ticket issued only after Model 1 verifies fresh
/// live frames. Browser-supplied scores are never trusted.
/// </summary>
public sealed class ProctoringVerificationTicketService : IProctoringVerificationTicketService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector;

    public ProctoringVerificationTicketService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(
            "AiExamProctoring.PreExamIdentityAndEnvironmentVerification.v2");
    }

    public string Create(ProctoringVerificationTicket ticket) =>
        _protector.Protect(JsonSerializer.Serialize(ticket, JsonOptions));

    public bool TryValidate(
        string? protectedTicket,
        out ProctoringVerificationTicket? ticket)
    {
        ticket = null;
        if (string.IsNullOrWhiteSpace(protectedTicket) || protectedTicket.Length > 8_000)
            return false;

        try
        {
            var json = _protector.Unprotect(protectedTicket);
            var value = JsonSerializer.Deserialize<ProctoringVerificationTicket>(json, JsonOptions);
            if (value is null ||
                string.IsNullOrWhiteSpace(value.StudentId) ||
                string.IsNullOrWhiteSpace(value.ModelVersion) ||
                string.IsNullOrWhiteSpace(value.Nonce) ||
                value.ExamId <= 0 ||
                value.ExpiresAtUtc <= DateTime.UtcNow ||
                value.VerifiedAtUtc > DateTime.UtcNow.AddMinutes(1))
            {
                return false;
            }

            ticket = value;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
