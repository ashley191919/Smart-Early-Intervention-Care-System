using System.Globalization;
using System.Reflection;
using EarlyInterventionCare.Api.Services.Questionnaires;

int passed = 0, failed = 0;
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Check(string name, Action work)
{
    try { work(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}"); }
}
void Throws<T>(Action work) where T : Exception
{
    try { work(); throw new Exception("Expected rejection"); } catch (T) { }
}
ParentEligibilityEvidence Unknown() => new(ParentEligibilityEvidenceChecks.CandidateContractVersion,
    Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
    null, null, null, null, null, null, null, null);

Check("structure can be valid while all qualification fields remain unknown", () => {
    var e = Unknown(); ParentEligibilityEvidenceChecks.ValidateStructure(e);
    Assert(ParentEligibilityEvidenceChecks.FindMissingCandidateFields(e).SequenceEqual(new[] {
        "BindingGeneration", "ParentEligibilityEpoch", "DocumentEligibilityEpoch", "DocumentContentHash", "ScopeCode", "ScopeKey", "Aba", "EligibilityStamp" }));
    Assert(e.EligibilityStamp is null);
});
foreach (var version in new string?[] { null, "", " ", "parent-eligibility/v2", "PARENT-ELIGIBILITY/V1", "parent-eligibility/v1 " })
    Check($"reject invalid/unknown version ({version ?? "NULL"})", () => {
        var e = Unknown() with { ContractVersion = version! };
        Throws<ArgumentException>(() => ParentEligibilityEvidenceChecks.ValidateStructure(e));
        Throws<ArgumentException>(() => ParentEligibilityEvidenceChecks.FindMissingCandidateFields(e));
    });
Check("null record is rejected", () => Throws<ArgumentNullException>(() => ParentEligibilityEvidenceChecks.ValidateStructure(null!)));
foreach (var field in new[] { "TaskId", "QuestionnaireVersionId" })
    Check($"reject empty {field}", () => {
        var e = field == "TaskId" ? Unknown() with { TaskId = Guid.Empty } : Unknown() with { QuestionnaireVersionId = Guid.Empty };
        Throws<ArgumentException>(() => ParentEligibilityEvidenceChecks.ValidateStructure(e));
    });
foreach (var value in new ulong[] { 0, 1, 9007199254740991, 9007199254740992, ulong.MaxValue })
    Check($"explicit test integer {value} preserved without replacing unknowns", () => {
        var unknown = Unknown(); var e = unknown with { BindingGeneration = value };
        Assert(e != unknown && e.BindingGeneration == value && e.ParentEligibilityEpoch is null && e.DocumentEligibilityEpoch is null);
        Assert(!ParentEligibilityEvidenceChecks.FindMissingCandidateFields(e).Contains("BindingGeneration"));
        Assert(unknown.BindingGeneration is null && e.EligibilityStamp is null);
    });
foreach (var field in new[] { "DocumentContentHash", "ScopeCode", "ScopeKey", "EligibilityStamp" })
    Check($"{field}: NULL and empty remain distinct but both are missing", () => {
        var e = Unknown(); var empty = field switch {
            "DocumentContentHash" => e with { DocumentContentHash = "" }, "ScopeCode" => e with { ScopeCode = "" },
            "ScopeKey" => e with { ScopeKey = "" }, _ => e with { EligibilityStamp = "" }
        };
        Assert(e != empty && typeof(ParentEligibilityEvidence).GetProperty(field)!.GetValue(e) is null);
        Assert((string?)typeof(ParentEligibilityEvidence).GetProperty(field)!.GetValue(empty) == "");
        Assert(ParentEligibilityEvidenceChecks.FindMissingCandidateFields(e).Contains(field) &&
            ParentEligibilityEvidenceChecks.FindMissingCandidateFields(empty).Contains(field));
    });
Check("copy preserves every Evidence and ABA field without a generated Stamp", () => {
    // Explicit synthetic boundary values only; not persisted generations or usable eligibility.
    var aba = new ParentEligibilityAbaEvidence(0, 1, 2, 3, 4, ulong.MaxValue);
    var e = Unknown() with { BindingGeneration = ulong.MaxValue, ParentEligibilityEpoch = 0,
        DocumentEligibilityEpoch = 1, DocumentContentHash = "", ScopeCode = "CASE", ScopeKey = "synthetic-scope", Aba = aba };
    var copy = e with { }; var abaCopy = aba with { };
    Assert(copy == e && !ReferenceEquals(copy, e) && abaCopy == aba && !ReferenceEquals(abaCopy, aba));
    foreach (var p in typeof(ParentEligibilityEvidence).GetProperties()) Assert(Equals(p.GetValue(copy), p.GetValue(e)));
    foreach (var p in typeof(ParentEligibilityAbaEvidence).GetProperties()) Assert(Equals(p.GetValue(abaCopy), p.GetValue(aba)));
    Assert(ReferenceEquals(copy.Aba, e.Aba) && copy.EligibilityStamp is null);
});
Check("absent ABA differs from present ABA with all unknown generations", () => {
    var e = Unknown(); var unknownAba = e with { Aba = new(null, null, null, null, null, null) };
    var missing = ParentEligibilityEvidenceChecks.FindMissingCandidateFields(unknownAba);
    Assert(e != unknownAba && missing.Count(x => x.StartsWith("Aba.", StringComparison.Ordinal)) == 6 && !missing.Contains("Aba"));
});
foreach (var field in typeof(ParentEligibilityAbaEvidence).GetProperties())
    Check($"unknown {field.Name} cannot be hidden by other ABA values", () => {
        var names = new[] { "UserGeneration", "AuthorizationGeneration", "CaseGeneration", "GuardianGeneration", "TaskGeneration", "ConsentGeneration" };
        var values = names.Select(n => n == field.Name ? (ulong?)null : 1).Cast<object?>().ToArray();
        var aba = (ParentEligibilityAbaEvidence)typeof(ParentEligibilityAbaEvidence).GetConstructors().Single(c => c.IsPublic).Invoke(values);
        var e = Unknown() with { Aba = aba }; var missing = ParentEligibilityEvidenceChecks.FindMissingCandidateFields(e);
        Assert(missing.Contains("Aba." + field.Name) && missing.Count(n => n.StartsWith("Aba.", StringComparison.Ordinal)) == 1);
        Assert(field.PropertyType == typeof(ulong?) && field.GetValue(aba) is null && e.EligibilityStamp is null);
    });
Check("checks never mutate data, synthesize Stamp or create default generations", () => {
    var e = Unknown(); var before = e with { };
    ParentEligibilityEvidenceChecks.ValidateStructure(e); ParentEligibilityEvidenceChecks.FindMissingCandidateFields(e);
    Assert(e == before && e.Aba is null && e.EligibilityStamp is null && e.BindingGeneration is null);
});
Check("consumer references the single shared type, no pinned Context required", () => {
    ParentEligibilityEvidence PassThrough(ParentEligibilityEvidence evidence) => evidence;
    var e = Unknown(); Assert(ReferenceEquals(PassThrough(e), e));
    Assert(typeof(ParentEligibilityEvidence).Namespace == "EarlyInterventionCare.Api.Services.Questionnaires");
    Assert(typeof(Program).Assembly.GetTypes().Count(t => t.Name == nameof(ParentEligibilityEvidence)) == 1);
    Assert(!typeof(Program).Assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("EntityFramework", StringComparison.Ordinal) || a.Name == "EarlyInterventionCare.Api"));
});
Check("only the specified Evidence fields exist; no transient state or answers", () => {
    Assert(typeof(ParentEligibilityEvidence).GetProperties().Select(p => p.Name).Order().SequenceEqual(new[] {
        "ContractVersion", "TaskId", "QuestionnaireVersionId", "BindingGeneration", "ParentEligibilityEpoch", "DocumentEligibilityEpoch",
        "DocumentContentHash", "ScopeCode", "ScopeKey", "Aba", "EligibilityStamp" }.Order()));
    Assert(new[] { "BindingGeneration", "ParentEligibilityEpoch", "DocumentEligibilityEpoch" }
        .All(n => typeof(ParentEligibilityEvidence).GetProperty(n)!.PropertyType == typeof(ulong?)));
});
Check("checks expose no authorization result or Stamp encoder", () => {
    var methods = typeof(ParentEligibilityEvidenceChecks).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
    Assert(methods.Length == 2 && methods.All(m => m.ReturnType == typeof(void) || m.ReturnType == typeof(IReadOnlyList<string>)));
    Assert(typeof(ParentEligibilityEvidence).IsSealed && typeof(ParentEligibilityAbaEvidence).IsSealed);
});
foreach (var culture in new[] { "tr-TR", "ar-SA", "zh-TW" })
    Check($"candidate structure checks independent of {culture}", () => {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); var e = Unknown();
            ParentEligibilityEvidenceChecks.ValidateStructure(e); Assert(ParentEligibilityEvidenceChecks.FindMissingCandidateFields(e).Count == 8); }
        finally { CultureInfo.CurrentCulture = previous; }
    });
Console.WriteLine("BLOCKED persistent ABA/generation sources, initialization and all eligibility writers are not fixed");
Console.WriteLine("BLOCKED Stamp encoding/golden bytes/Digest, provenance and same-transaction authorization are not implemented");
Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL, 2 BLOCKED; candidate structure only; no Stamp, API, socket or database");
return failed > 0 ? 1 : 2;
