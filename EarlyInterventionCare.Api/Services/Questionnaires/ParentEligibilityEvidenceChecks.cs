namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>
/// Pure, unregistered candidate-shape checks only. No source validation, hashing, locks,
/// eligibility decision or authorization. Even zero missing fields cannot authorize a call.
/// No values are supplied or normalized by these checks.
/// </summary>
public static class ParentEligibilityEvidenceChecks
{
    public const string CandidateContractVersion = "parent-eligibility/v1";

    /// <summary>Checks the candidate version and resource IDs; nullable fields may remain unknown.</summary>
    public static void ValidateStructure(ParentEligibilityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!string.Equals(evidence.ContractVersion, CandidateContractVersion, StringComparison.Ordinal))
            throw new ArgumentException("Unsupported candidate evidence contract version.", nameof(evidence));
        if (evidence.TaskId == Guid.Empty || evidence.QuestionnaireVersionId == Guid.Empty)
            throw new ArgumentException("Nonempty Task and questionnaire version IDs are required.", nameof(evidence));
    }

    /// <summary>
    /// Reports absent candidate fields, not trustworthy/complete qualification evidence.
    /// Nonempty strings and numbers can still be invalid, unverified or stale.
    /// Stamp format and persistent ABA sources must be reviewed separately before integration.
    /// </summary>
    public static IReadOnlyList<string> FindMissingCandidateFields(ParentEligibilityEvidence evidence)
    {
        ValidateStructure(evidence);
        var missing = new List<string>();
        if (evidence.BindingGeneration is null) missing.Add(nameof(evidence.BindingGeneration));
        if (evidence.ParentEligibilityEpoch is null) missing.Add(nameof(evidence.ParentEligibilityEpoch));
        if (evidence.DocumentEligibilityEpoch is null) missing.Add(nameof(evidence.DocumentEligibilityEpoch));
        if (string.IsNullOrWhiteSpace(evidence.DocumentContentHash)) missing.Add(nameof(evidence.DocumentContentHash));
        if (string.IsNullOrWhiteSpace(evidence.ScopeCode)) missing.Add(nameof(evidence.ScopeCode));
        if (string.IsNullOrWhiteSpace(evidence.ScopeKey)) missing.Add(nameof(evidence.ScopeKey));
        if (evidence.Aba is null) missing.Add(nameof(evidence.Aba));
        else
        {
            if (evidence.Aba.UserGeneration is null) missing.Add("Aba.UserGeneration");
            if (evidence.Aba.AuthorizationGeneration is null) missing.Add("Aba.AuthorizationGeneration");
            if (evidence.Aba.CaseGeneration is null) missing.Add("Aba.CaseGeneration");
            if (evidence.Aba.GuardianGeneration is null) missing.Add("Aba.GuardianGeneration");
            if (evidence.Aba.TaskGeneration is null) missing.Add("Aba.TaskGeneration");
            if (evidence.Aba.ConsentGeneration is null) missing.Add("Aba.ConsentGeneration");
        }
        if (string.IsNullOrWhiteSpace(evidence.EligibilityStamp)) missing.Add(nameof(evidence.EligibilityStamp));
        return missing.AsReadOnly();
    }
}
