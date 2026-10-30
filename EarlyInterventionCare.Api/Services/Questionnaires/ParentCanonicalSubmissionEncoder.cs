using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>
/// Pure, unregistered parent-only canonical bytes v1. Call after complete=true validation.
/// This does not authenticate an author, authorize a Task, resolve replay, or migrate hashes.
/// </summary>
public sealed class ParentCanonicalSubmissionEncoder : IParentCanonicalSubmissionEncoder
{
    public const string FormatVersion = "earlycare.parent-submit.canonical/v1";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public byte[] Encode(Guid taskId, Guid versionId, ValidatedQuestionnaireContent content,
        string respondentRelationship)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.RespondentName is null || content.FilledOn is null)
            throw new ArgumentException("Complete validated submission content is required.", nameof(content));
        return Encode(new ParentCanonicalSubmission(taskId, versionId, content.RespondentName,
            content.FilledOn.Value, content.Answers, content.Observation, respondentRelationship));
    }

    public byte[] Encode(ParentCanonicalSubmission content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.TaskId == Guid.Empty || content.QuestionnaireVersionId == Guid.Empty)
            throw new ArgumentException("Nonempty Task and questionnaire version IDs are required.", nameof(content));
        if (string.IsNullOrEmpty(content.RespondentName) || content.RespondentName.Length > 50 ||
            content.RespondentName != content.RespondentName.Trim() || content.RespondentName.Any(char.IsControl))
            throw new ArgumentException("Use the existing validator's normalized respondent name.", nameof(content));
        if (content.Observation is not null && (content.Observation.Length is 0 or > 1000 ||
            content.Observation != content.Observation.Trim()))
            throw new ArgumentException("Use null or the existing validator's normalized observation.", nameof(content));
        if (content.RespondentRelationship is not ("FATHER" or "MOTHER" or "GRANDPARENT" or "RELATIVE" or "GUARDIAN" or "CAREGIVER" or "OTHER"))
            throw new ArgumentException("A valid explicit respondent relationship is required.", nameof(content));
        ArgumentNullException.ThrowIfNull(content.Answers);
        var answers = content.Answers.ToArray();
        if (answers.Length == 0 || answers.Any(a => a is null || string.IsNullOrWhiteSpace(a.QuestionId) || string.IsNullOrWhiteSpace(a.OptionValue)) ||
            answers.Select(a => a.QuestionId).Distinct(StringComparer.Ordinal).Count() != answers.Length)
            throw new ArgumentException("Complete validated answers with unique question IDs are required.", nameof(content));
        Array.Sort(answers, (a, b) => StringComparer.Ordinal.Compare(a.QuestionId, b.QuestionId));

        using var stream = new MemoryStream();
        WriteText(stream, FormatVersion);
        WriteText(stream, content.TaskId.ToString("D"));
        WriteText(stream, content.QuestionnaireVersionId.ToString("D"));
        WriteText(stream, content.RespondentName);
        WriteText(stream, content.FilledOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        WriteCount(stream, checked((uint)answers.Length));
        foreach (var answer in answers)
        {
            WriteText(stream, answer.QuestionId!);
            WriteText(stream, answer.OptionValue!);
        }
        stream.WriteByte(content.Observation is null ? (byte)0 : (byte)1);
        if (content.Observation is not null) WriteText(stream, content.Observation);
        WriteText(stream, content.RespondentRelationship);
        return stream.ToArray();
    }

    public byte[] ComputeHash(Guid taskId, Guid versionId, ValidatedQuestionnaireContent content,
        string respondentRelationship) => SHA256.HashData(Encode(taskId, versionId, content, respondentRelationship));

    public byte[] ComputeHash(ParentCanonicalSubmission content) => SHA256.HashData(Encode(content));

    private static void WriteText(Stream stream, string text)
    {
        var bytes = Utf8.GetBytes(text); // Throw on malformed UTF-16; never replace and collapse values.
        WriteCount(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteCount(Stream stream, uint count)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, count);
        stream.Write(buffer);
    }
}
