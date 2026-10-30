namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>Pure wrapper over the reviewed strict UInt64 codec; not registered in DI.</summary>
public sealed class StrictParentRevisionCodec : IParentRevisionCodec
{
    public bool TryParse(string? text, out ulong revision) =>
        RevisionStringConverter.TryParse(text, out revision);

    public string Format(ulong revision) => RevisionStringConverter.Format(revision);
}
