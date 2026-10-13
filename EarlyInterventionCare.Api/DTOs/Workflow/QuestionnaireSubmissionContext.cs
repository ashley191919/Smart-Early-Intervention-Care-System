namespace EarlyInterventionCare.Api.DTOs.Workflow;

/// <summary>
/// Internal handoff from an authenticated and authorized server-side Submit coordinator.
/// Never bind this record directly from an HTTP request or treat possession of it as authorization.
/// It identifies a submission; it is not evidence that a round is complete, current, or sealed.
/// </summary>
/// <param name="CaseId">Non-empty Case ID obtained from the authorized persisted Task.</param>
/// <param name="AssignmentRound">
/// Positive round obtained from that Task. The Workflow must separately verify that it is effective.
/// </param>
/// <param name="TaskId">Non-empty ID of the Task verified by Submit.</param>
/// <param name="ResponseId">
/// Non-empty ID of the validated response saved in the caller's transaction, or the existing response
/// for a verified idempotent replay. Never generate a replacement ID for a replay.
/// </param>
/// <param name="CorrelationId">
/// Non-empty server-provided diagnostic trace, such as HttpContext.TraceIdentifier.
/// Contains no answers, credentials, or personal data; not an idempotency key or proof of identity.
/// </param>
public sealed record QuestionnaireSubmissionContext(Guid CaseId, uint AssignmentRound,
    Guid TaskId, Guid ResponseId, string CorrelationId);
