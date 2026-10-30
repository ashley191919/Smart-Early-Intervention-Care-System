using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Questionnaires;

var passed = 0;
var failed = 0;
void Check(string name, Func<bool> test)
{
    try
    {
        if (!test()) throw new InvalidOperationException("Assertion failed");
        passed++; Console.WriteLine($"PASS {name}");
    }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Reject<T>(string name, Action work) where T : Exception => Check(name, () =>
{
    try { work(); return false; }
    catch (T) { return true; }
});
var task = Guid.Parse("11111111-1111-4111-8111-111111111111");
var version = Guid.Parse("22222222-2222-4222-8222-222222222222");
var validator = new QuestionnaireAnswerValidator();
var encoder = new ParentCanonicalSubmissionEncoder();
ValidatedQuestionnaireContent Validated(string name = "Alice", string? observation = null,
    QuestionnaireAnswer[]? answers = null)
{
    answers ??= [new("Q1", "0")];
    var snapshot = JsonSerializer.Serialize(new { questions = answers.Select(a => new {
        questionId = a.QuestionId, type = "singleChoice", options = new[] { new { value = a.OptionValue } }
    }).ToArray() });
    return validator.Validate(snapshot, new(version, name, "2026-10-30", answers, observation),
        new DateOnly(2026, 10, 30), complete: true);
}
ParentCanonicalSubmission Canonical(ValidatedQuestionnaireContent content, string relation = "FATHER") =>
    new(task, version, content.RespondentName!, content.FilledOn!.Value, content.Answers, content.Observation, relation);
var valid = Validated();
var baseline = Canonical(valid);
string Hash(ParentCanonicalSubmission content) => Convert.ToHexString(encoder.ComputeHash(content));

// Independently generated with Node Buffer.writeUInt32BE + crypto SHA-256, not the encoder.
const string goldenHex = "000000246561726C79636172652E706172656E742D7375626D69742E63616E6F6E6963616C2F76310000002431313131313131312D313131312D343131312D383131312D3131313131313131313131310000002432323232323232322D323232322D343232322D383232322D32323232323232323232323200000005416C6963650000000A323032362D31302D33300000000100000002513100000001300000000006464154484552";
const string goldenHash = "91E05E5302578620234CD4994D3728FCAAA861D65E088233E6CA0C11F1D4C452";
Check("golden canonical bytes", () => Convert.ToHexString(encoder.Encode(baseline)) == goldenHex);
Check("golden SHA256", () => Hash(baseline) == goldenHash);
Check("hash is SHA256 of canonical bytes", () => encoder.ComputeHash(baseline).SequenceEqual(SHA256.HashData(encoder.Encode(baseline))));
Check("same normalized content is deterministic", () => Enumerable.Range(0, 5).All(_ => Hash(Canonical(Validated())) == goldenHash));
Check("validated-output overload matches canonical overload", () => encoder.Encode(task, version, valid, "FATHER").SequenceEqual(encoder.Encode(baseline)) &&
    encoder.ComputeHash(task, version, valid, "FATHER").SequenceEqual(encoder.ComputeHash(baseline)));
Check("parent bytes are version/domain separated", () => ParentCanonicalSubmissionEncoder.FormatVersion == "earlycare.parent-submit.canonical/v1" &&
    Encoding.UTF8.GetString(encoder.Encode(baseline).AsSpan(4, 36)) == ParentCanonicalSubmissionEncoder.FormatVersion);
foreach (var relation in new[] { "MOTHER", "GRANDPARENT", "RELATIVE", "GUARDIAN", "CAREGIVER", "OTHER" })
    Check($"relationship {relation} changes hash", () => Hash(Canonical(valid, relation)) != goldenHash);
foreach (var (label, changed) in new (string, ParentCanonicalSubmission)[] {
    ("Task", baseline with { TaskId = version }),
    ("Version", baseline with { QuestionnaireVersionId = task }),
    ("Name", Canonical(Validated("Bob"))),
    ("Date", baseline with { FilledOn = new DateOnly(2026, 10, 29) }),
    ("QuestionId", Canonical(Validated(answers: [new("Q2", "0")]))),
    ("Option", Canonical(Validated(answers: [new("Q1", "1")]))),
    ("Observation", Canonical(Validated(observation: "note"))) })
    Check($"{label} is included in hash", () => Hash(changed) != goldenHash);
var many = Canonical(Validated(answers: [new("Q2", "1"), new("Q10", "0"), new("Q1", "1")]));
Check("answer permutation has same ordinal canonical order", () => Hash(many) == Hash(many with { Answers = many.Answers.Reverse().ToArray() }));
var inputOrder = many.Answers.Reverse().ToArray();
var before = inputOrder.ToArray();
encoder.Encode(many with { Answers = inputOrder });
Check("encoding does not sort caller array in place", () => inputOrder.SequenceEqual(before));
Check("answer pair boundaries cannot collapse ab+c vs a+bc", () =>
    !encoder.Encode(Canonical(Validated(answers: [new("ab", "c")]))).SequenceEqual(
        encoder.Encode(Canonical(Validated(answers: [new("a", "bc")])))));
Check("text field combinations cannot collapse", () => Hash(Canonical(Validated("ab", "c"))) != Hash(Canonical(Validated("a", "bc"))));
Check("answer count and pair boundaries differ", () => Hash(Canonical(Validated(answers: [new("a", "bc"), new("d", "e")]))) !=
    Hash(Canonical(Validated(answers: [new("a", "b"), new("cd", "e")]))));
Check("validator normalized name/observation preserved", () => Hash(Canonical(Validated(" Alice ", " note "))) == Hash(Canonical(Validated("Alice", "note"))));
Check("null and empty raw observation normalize equally", () => Hash(Canonical(Validated(observation: ""))) == goldenHash &&
    Hash(Canonical(Validated(observation: " \t "))) == goldenHash);
Check("literal null text differs from absent observation", () => Hash(Canonical(Validated(observation: "null"))) != goldenHash);
Check("embedded NUL retained distinctly", () => Hash(Canonical(Validated(observation: "a\0b"))) != Hash(Canonical(Validated(observation: "ab"))));
Check("Unicode Chinese/emoji stable", () => Hash(Canonical(Validated("虛構🙂", "合成內容🧪"))) == Hash(Canonical(Validated("虛構🙂", "合成內容🧪"))));
Check("no extra NFC normalization", () => Hash(Canonical(Validated("é"))) != Hash(Canonical(Validated("e\u0301"))));
Reject<EncoderFallbackException>("invalid surrogate is rejected rather than replaced", () => encoder.Encode(Canonical(Validated(observation: "bad\ud800"))));
Reject<ArgumentException>("empty normalized observation rejected", () => encoder.Encode(baseline with { Observation = "" }));
Reject<ArgumentException>("unnormalized observation rejected", () => encoder.Encode(baseline with { Observation = " note " }));
Reject<ArgumentException>("unnormalized name rejected", () => encoder.Encode(baseline with { RespondentName = " Alice " }));
Reject<ArgumentException>("missing complete name rejected", () => encoder.Encode(task, version, valid with { RespondentName = null }, "FATHER"));
Reject<ArgumentException>("missing complete date rejected", () => encoder.Encode(task, version, valid with { FilledOn = null }, "FATHER"));
foreach (var relationship in new string?[] { null, "", " ", "father", " FATHER", "UNKNOWN" })
    Reject<ArgumentException>($"invalid relationship [{relationship ?? "NULL"}] rejected", () => encoder.Encode(baseline with { RespondentRelationship = relationship! }));
Reject<ArgumentException>("empty Task rejected", () => encoder.Encode(baseline with { TaskId = Guid.Empty }));
Reject<ArgumentException>("empty version rejected", () => encoder.Encode(baseline with { QuestionnaireVersionId = Guid.Empty }));
Reject<ArgumentException>("duplicate question rejected", () => encoder.Encode(baseline with { Answers = [new("Q1", "0"), new("Q1", "1")] }));
Reject<ArgumentException>("null answer rejected", () => encoder.Encode(baseline with { Answers = [null!] }));
Reject<ArgumentException>("null option rejected", () => encoder.Encode(baseline with { Answers = [new("Q1", null)] }));
Reject<ArgumentException>("empty answers rejected", () => encoder.Encode(baseline with { Answers = [] }));
Reject<ArgumentNullException>("null canonical content rejected", () => encoder.Encode((ParentCanonicalSubmission)null!));
Reject<ArgumentNullException>("null validated content rejected", () => encoder.Encode(task, version, null!, "FATHER"));
Check("hash content has no revision/key/principal/author input", () => new[] {
    "ExpectedRevision", "IdempotencyKey", "Principal", "ActorUserId"
}.All(p => typeof(ParentCanonicalSubmission).GetProperty(p) == null));
var cmd1 = new ParentSubmitCommand(new(version, "Alice", "2026-10-30", [new("Q1", "0")], null), 0, "FATHER");
var cmd2 = cmd1 with { ExpectedRevision = ulong.MaxValue };
Check("command revision change does not change content hash", () =>
    encoder.ComputeHash(task, version, validator.Validate("""{"questions":[{"questionId":"Q1","options":[{"value":"0"}]}]}""", cmd1.Payload, new(2026, 10, 30), true), cmd1.RespondentRelationship)
        .SequenceEqual(encoder.ComputeHash(task, version, valid, cmd2.RespondentRelationship)));
var teacherBefore = valid.SubmissionHash(task, version);
encoder.ComputeHash(task, version, valid, "MOTHER");
Check("shared SubmissionHash unchanged by parent encoding", () => valid.SubmissionHash(task, version).SequenceEqual(teacherBefore));
Check("parent hash differs from legacy shared hash", () => !encoder.ComputeHash(baseline).SequenceEqual(teacherBefore));
var savedCulture = CultureInfo.CurrentCulture;
try
{
    foreach (var culture in new[] { "fr-FR", "ar-SA", "tr-TR", "zh-TW" })
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        Check($"canonical bytes/hash invariant under {culture}", () => Hash(baseline) == goldenHash && Convert.ToHexString(encoder.Encode(baseline)) == goldenHex);
    }
}
finally { CultureInfo.CurrentCulture = savedCulture; }
Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL; pure parent canonical bytes/hash only");
return failed == 0 ? 0 : 1;
