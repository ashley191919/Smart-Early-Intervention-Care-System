namespace EarlyInterventionCare.Api.Services.Authorization;

/// <summary>Authorization lookup is unavailable; this is not a permission denial.</summary>
public sealed class PermissionLookupException : Exception
{
    public PermissionLookupException() : base("Permission lookup is unavailable.") { }
}
