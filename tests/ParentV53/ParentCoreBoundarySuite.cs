using System.Globalization;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Questionnaires;

public static class ParentCoreBoundarySuite
{
    public static void Run(Action<bool, string> check)
    {
        void Reject(Action action, string code)
        {
            try { action(); } catch (CaseAccessException e) { check(e.Code == code, code); return; }
            throw new Exception("Expected " + code);
        }
        var task = Guid.NewGuid(); var version = Guid.NewGuid();
        foreach (var number in new[] { "0", "9007199254740993", "18446744073709551615" })
            check(ParentCorePayloadMapping.FormatRevision(ParentCorePayloadMapping.ParseRevision(number)) == number, "lossless unsigned revision " + number);
        foreach (var invalid in new string?[] { null, "", "00", "01", "0001", "-1", "+1", "1.0", "1e3", " 1", "1 ", "１", "18446744073709551616" })
            Reject(() => ParentCorePayloadMapping.ParseRevision(invalid), "INVALID_INPUT");
        var originalCulture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("ar-SA"); check(ParentCorePayloadMapping.FormatRevision(ulong.MaxValue) == "18446744073709551615", "culture independent revision"); }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        foreach (var relationship in new[] { "FATHER", "MOTHER", "GRANDPARENT", "RELATIVE", "OTHER" })
        {
            QuestionnaireAnswer[] answers = [new("Q1", "0")];
            var draft = ParentCorePayloadMapping.PrepareDraft(new(version, "18446744073709551615", "synthetic", "2026-10-30", relationship, answers, "note"));
            var submit = ParentCorePayloadMapping.PrepareSubmit(new(version, "18446744073709551615", "synthetic", "2026-10-30", relationship, answers, "note"));
            check(draft.Payload.ExpectedRevision == ulong.MaxValue && draft.RespondentRelationship == relationship, "draft metadata retained " + relationship);
            check(submit.ExpectedRevision == ulong.MaxValue && submit.RespondentRelationship == relationship && submit.Payload.Observation == "note", "submit metadata retained " + relationship);
            answers[0] = new("Q1", "3");
            check(draft.Payload.Answers![0]!.OptionValue == "0" && submit.Payload.Answers![0]!.OptionValue == "0", "mapped answers isolated from input mutation");
        }
        Reject(() => ParentCorePayloadMapping.PrepareDraft(new(version, "0", null, null, "ADMIN", [], null)), "INVALID_INPUT");
        var saved = new QuestionnaireDraftResponse(task, version, ulong.MaxValue, "synthetic", "2026-10-30", [new("Q1", "0")], null, DateTime.UtcNow, "IN_PROGRESS");
        var restored = ParentCorePayloadMapping.RestoreDraft(saved, task, version, "GRANDPARENT");
        check(restored.Revision == "18446744073709551615" && restored.RespondentRelationship == "GRANDPARENT", "restore trusted stored relationship and precise revision");
        check(ParentCorePayloadMapping.RestoreDraft(saved, task, version, null).RespondentRelationship is null, "legacy draft relationship remains null");
        Reject(() => ParentCorePayloadMapping.RestoreDraft(saved, task, version, "ADMIN"), "PARENT_CORE_RESPONSE_INVALID");
        check(ParentCorePayloadMapping.PrepareDraft(new(version, "0", null, null, null, [], null)).RespondentRelationship is null, "unfilled draft may save null relationship");
        Reject(() => ParentCorePayloadMapping.PrepareSubmit(new(version, "0", "synthetic", "2026-10-30", null!, [], null)), "INVALID_INPUT");
        Reject(() => ParentCorePayloadMapping.RestoreDraft(saved, Guid.NewGuid(), version, "MOTHER"), "PARENT_CORE_RESPONSE_INVALID");
        Reject(() => ParentCorePayloadMapping.RestoreDraft(saved, task, Guid.NewGuid(), "MOTHER"), "PARENT_CORE_RESPONSE_INVALID");
        var empty = new QuestionnaireDraftResponse(task, version, 0, null, null, [], null, null, "PENDING");
        check(ParentCorePayloadMapping.RestoreDraft(empty, task, version, null).RespondentRelationship is null, "new draft does not invent relationship");
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Path = "/api/dev/teacher-workspace/task";
        var action = new Microsoft.AspNetCore.Mvc.ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var exception = new Microsoft.AspNetCore.Mvc.Filters.ExceptionContext(action, [])
            { Exception = new QuestionnaireWorkflowException("DRAFT_CONFLICT", "teacher owned boundary", 409) };
        new ParentModuleErrors().OnException(exception);
        check(!exception.ExceptionHandled && exception.Result is null, "parent core error handling does not replace teacher error contract");
    }
}
