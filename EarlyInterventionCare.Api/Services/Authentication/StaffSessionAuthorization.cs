namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>
/// The current persisted role names identify the medical workspace roles. ADMIN is deliberately
/// excluded because its business access is still unconfirmed in docs/roles-permissions.md.
/// </summary>
public static class StaffSessionAuthorization
{
    public const string CaseManagerRole = "CASE_MANAGER";
    public const string MedicalStaffRole = "MEDICAL_STAFF";

    public static bool CanCreateStaffSession(string role) =>
        string.Equals(role, CaseManagerRole, StringComparison.Ordinal) ||
        string.Equals(role, MedicalStaffRole, StringComparison.Ordinal);
}
