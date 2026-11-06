namespace EarlyInterventionCare.Api.Services.Cases;
public sealed class CaseAccessException(string code, int status, string message) : Exception(message)
{ public string Code { get; } = code; public int Status { get; } = status; }
