namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>
/// Candidate server-only contract, not an HTTP DTO, authorization token or verified evidence.
/// Unknown persistent generations remain null: never infer them from timestamps or Revision.
/// Constructing/copying this record proves neither provenance nor transaction authorization.
/// </summary>
public sealed record ParentEligibilityEvidence(
    string ContractVersion,
    Guid TaskId,
    Guid QuestionnaireVersionId,
    ulong? BindingGeneration,
    ulong? ParentEligibilityEpoch,
    ulong? DocumentEligibilityEpoch,
    string? DocumentContentHash,
    string? ScopeCode,
    string? ScopeKey,
    ParentEligibilityAbaEvidence? Aba,
    string? EligibilityStamp);

/// <summary>Candidate ABA fields; persistent sources and writer protocols are not implemented.</summary>
public sealed record ParentEligibilityAbaEvidence(
    ulong? UserGeneration,
    ulong? AuthorizationGeneration,
    ulong? CaseGeneration,
    ulong? GuardianGeneration,
    ulong? TaskGeneration,
    ulong? ConsentGeneration);
