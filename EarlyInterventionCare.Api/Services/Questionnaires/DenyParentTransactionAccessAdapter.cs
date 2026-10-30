using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>Unregistered, unconditional deny; never accesses the supplied transaction.</summary>
public sealed class DenyParentTransactionAccessAdapter : IParentTransactionAccessAdapter
{
    public Task<ParentTransactionAccess> LockAndAuthorizeAsync(
        IParentQuestionnaireTransaction transaction, ClaimsPrincipal principal, Guid taskId,
        ParentAccessExpectation expectedAccess, bool writing, CancellationToken ct = default) =>
        Task.FromException<ParentTransactionAccess>(Denied());

    public Task RevalidateAsync(
        IParentQuestionnaireTransaction transaction, ClaimsPrincipal principal,
        ParentTransactionAccess access, CancellationToken ct = default) =>
        Task.FromException(Denied());

    private static QuestionnaireWorkflowException Denied() =>
        new("PARENT_ACCESS_NOT_INTEGRATED", "Parent transaction access is not integrated.", 503);
}
