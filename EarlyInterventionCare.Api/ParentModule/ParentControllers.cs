using System.ComponentModel.DataAnnotations;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EarlyInterventionCare.Api.ParentModule;

[ApiController, Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/parent/consent"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ParentConsentController(ParentConsentService service) : ControllerBase
{
    [HttpGet("context")]
    public Task<ConsentContextResponse> Context(CancellationToken ct) => service.ContextAsync(User, ct);
    [HttpPost, RequestSizeLimit(8192)]
    public Task<ParentConsentReceipt> Record(RecordParentConsentRequest request, CancellationToken ct) => service.RecordAsync(User, request, ct);
}

[ApiController, Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/parent/tasks/{taskId:guid}"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ParentQuestionnaireController(ApplicationDbContext db, IParentQuestionnaireAccess access,
    IParentQuestionnaireGateway gateway) : ControllerBase
{
    [HttpGet("draft")]
    public async Task<ParentDraftResponse> Draft(Guid taskId, CancellationToken ct) =>
        await gateway.GetDraftAsync(await access.RequireAsync(User, taskId, false, db, ct), taskId, ct);
    [HttpPut("draft"), RequestSizeLimit(65536)]
    public async Task<ParentDraftResponse> Save(Guid taskId, ParentDraftRequest request, CancellationToken ct)
    {
        Validate(request.ExpectedRevision, request.RespondentRelationship, request.Answers?.Length, false);
        return await gateway.SaveDraftAsync(await access.RequireAsync(User, taskId, true, db, ct), taskId, request, ct);
    }
    [HttpPost("submit"), RequestSizeLimit(65536)]
    public async Task<ParentSubmissionReceipt> Submit(Guid taskId, ParentSubmitRequest request, CancellationToken ct)
    {
        Validate(request.ExpectedRevision, request.RespondentRelationship, request.Answers?.Length, true);
        if (!Guid.TryParse(Request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty)
            throw new CaseAccessException("INVALID_IDEMPOTENCY_KEY", 400, "提交識別碼無效，請重新確認。");
        return await gateway.SubmitAsync(await access.RequireAsync(User, taskId, true, db, ct), taskId, key, request, ct);
    }
    [HttpGet("receipt")]
    public async Task<ParentSubmissionReceipt> Receipt(Guid taskId, CancellationToken ct) =>
        await gateway.GetReceiptAsync(await access.RequireAsync(User, taskId, false, db, ct), taskId, ct);
    static void Validate(string revision, string? relation, int? answers, bool submitting)
    {
        ParentCorePayloadMapping.ParseRevision(revision);
        if ((relation is null ? submitting : !ParentCorePayloadMapping.ValidRelationship(relation)) || answers is null or > 500)
            throw new CaseAccessException("INVALID_INPUT", 400, "請確認填表人關係與草稿版本。");
    }
}
