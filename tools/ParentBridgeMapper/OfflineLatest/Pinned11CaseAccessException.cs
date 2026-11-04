// TEST-ONLY SOURCE PIN. Never include in the formal API.
// Commit: 26e8b7fb09fdaa24292c17a4f58d7ea4ce536491
// Source: EarlyInterventionCare.Api/Services/Cases/CaseAccessService.cs; Git blob: 6e3122f03682f9588e423418e8f29f2655f6a449
// END PIN METADATA
namespace EarlyInterventionCare.Api.Services.Cases;
public sealed class CaseAccessException(string code, int status, string message) : Exception(message)
{ public string Code { get; } = code; public int Status { get; } = status; }
