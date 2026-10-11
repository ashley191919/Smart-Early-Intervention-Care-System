using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EarlyInterventionCare.Api.DTOs.Questionnaires;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

public class QuestionnaireWorkflowException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed record ValidatedQuestionnaireContent(string? RespondentName, DateOnly? FilledOn,
    QuestionnaireAnswer[] Answers, string? Observation)
{
    private object[] JsonAnswers() => Answers.Select(a => (object)new { questionId = a.QuestionId!, optionValue = a.OptionValue! }).ToArray();
    public string AnswersJson => JsonSerializer.Serialize(JsonAnswers());

    public byte[] SubmissionHash(Guid taskId, Guid versionId) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        taskId, questionnaireVersionId = versionId, respondentName = RespondentName,
        filledOn = FilledOn!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        answers = JsonAnswers(), observation = Observation ?? ""
    }));
}

// Extracted from TeacherWorkspaceSubmission. Draft and submit use this single validator.
public sealed class QuestionnaireAnswerValidator
{
    public ValidatedQuestionnaireContent Validate(string snapshot, QuestionnaireSubmission input, DateOnly today, bool complete)
    {
        var name = input.RespondentName?.Trim();
        if (string.IsNullOrEmpty(name)) name = null;
        var observation = input.Observation?.Trim();
        if (string.IsNullOrEmpty(observation)) observation = null;
        DateOnly? filledOn = null;
        if (!string.IsNullOrWhiteSpace(input.FilledOn))
        {
            if (!DateOnly.TryParseExact(input.FilledOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) || parsed > today)
                throw InvalidInput();
            filledOn = parsed;
        }
        // Preserve the existing submission limits; no new clinical/scoring rules.
        if ((complete && (name is null || filledOn is null)) || name?.Length > 50 ||
            (name is not null && name.Any(char.IsControl)) || observation?.Length > 1000)
            throw InvalidInput();

        using var definition = JsonDocument.Parse(snapshot);
        var questions = definition.RootElement.GetProperty("questions").EnumerateArray().ToArray();
        if (questions.Length == 0) throw new InvalidOperationException("Questionnaire definition is empty.");
        var allowed = questions.ToDictionary(q => q.GetProperty("questionId").GetString()!,
            q => q.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()!).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var answers = input.Answers ?? (complete ? throw InvalidAnswers() : Array.Empty<QuestionnaireAnswer?>());
        if ((complete && answers.Length != questions.Length) || answers.Length > questions.Length ||
            answers.Any(a => a is null || a.QuestionId is null || a.OptionValue is null) ||
            answers.Select(a => a!.QuestionId).Distinct(StringComparer.Ordinal).Count() != answers.Length)
            throw InvalidAnswers();
        foreach (var answer in answers)
            if (!allowed.TryGetValue(answer!.QuestionId!, out var options) || !options.Contains(answer.OptionValue!))
                throw InvalidAnswers();
        return new(name, filledOn, answers.Select(a => a!).OrderBy(a => a.QuestionId, StringComparer.Ordinal).ToArray(), observation);
    }

    private static QuestionnaireWorkflowException InvalidInput() => new("INVALID_INPUT", "請填寫有效的姓名、日期及補充觀察；日期不能晚於今天。");
    private static QuestionnaireWorkflowException InvalidAnswers() => new("INVALID_ANSWERS", "答案必須符合指定問卷，且每題只能提交一個合法選項。");
}
