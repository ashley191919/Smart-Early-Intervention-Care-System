using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Questionnaires;

namespace EarlyInterventionCare.Integration.ParentBridge;

/// <summary>
/// Test-assembly-only four-method skeleton. There is deliberately no enable switch,
/// injected store/adapter/delegate, or forwarding constructor. Missing qualification
/// infrastructure must never fall back to the legacy public parent Gateway methods.
/// Compile with real 11 types only in a separately reviewed future integration.
/// </summary>
internal sealed class ParentGatewayBridgeSkeleton : IParentQuestionnaireGateway
{
    private static Task<T> Blocked<T>() => Task.FromException<T>(new QuestionnaireWorkflowException(
        "PARENT_ACCESS_NOT_INTEGRATED", "Parent bridge qualification integration is unavailable.", 503));

    public Task<ParentDraftResponse> GetDraftAsync(ParentRequestContext context, Guid taskId,
        CancellationToken ct = default) => Blocked<ParentDraftResponse>();

    public Task<ParentDraftResponse> SaveDraftAsync(ParentRequestContext context, Guid taskId,
        ParentDraftRequest input, CancellationToken ct = default) => Blocked<ParentDraftResponse>();

    public Task<ParentSubmissionReceipt> SubmitAsync(ParentRequestContext context, Guid taskId,
        Guid idempotencyKey, ParentSubmitRequest input, CancellationToken ct = default) => Blocked<ParentSubmissionReceipt>();

    public Task<ParentSubmissionReceipt> GetReceiptAsync(ParentRequestContext context, Guid taskId,
        CancellationToken ct = default) => Blocked<ParentSubmissionReceipt>();
}
