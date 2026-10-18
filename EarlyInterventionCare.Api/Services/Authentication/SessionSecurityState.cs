namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>Internal store snapshot only; never bind this to a request or include passwords/hashes.</summary>
public sealed record SessionSecurityState(Guid UserId, string Status, long SessionVersion);
