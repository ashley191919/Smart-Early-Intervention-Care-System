using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Questionnaires;

public static class ParentReaderEvidenceSuite
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        var actor = Guid.NewGuid(); var task = Guid.NewGuid(); var version = Guid.NewGuid();
        var missing = new ParentAuthorRowMetadata(false, null, false);
        var mine = new ParentAuthorRowMetadata(true, actor, false);
        foreach (var pair in new[] { new ParentTaskAuthorMetadata(missing,missing), new(mine,missing), new(missing,mine), new(mine,mine) })
        {
            var reader = new ProbeReader((context,id,ct) => { check(ReferenceEquals(context,db) && id==task, "reader receives original scoped context/task"); return Task.FromResult(pair); });
            check(await new MetadataParentAuthorVisibility(reader).CanRevealAsync(db,actor,task,default), "valid metadata passes author check only");
        }
        foreach (var denied in new[] {new ParentAuthorRowMetadata(true,null,false), new(true,Guid.Empty,false), new(true,Guid.NewGuid(),false), new(true,actor,true), new(false,actor,false), new(false,null,true)})
        foreach (var pair in new[] { new ParentTaskAuthorMetadata(denied,mine), new(mine,denied) })
            check(!await new MetadataParentAuthorVisibility(new ProbeReader((_,_,_)=>Task.FromResult(pair))).CanRevealAsync(db,actor,task,default), "either unknown/foreign/teacher/inconsistent row denies");
        var failure = new MetadataParentAuthorVisibility(new ProbeReader((_,_,_)=>throw new TimeoutException("synthetic")));
        try { await failure.CanRevealAsync(db,actor,task,default); throw new Exception("reader failure allowed"); }
        catch (TimeoutException) { check(true,"reader failure propagates, never missing rows"); }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await failure.CanRevealAsync(db,actor,task,cancellation.Token); throw new Exception("cancel allowed"); }
        catch (OperationCanceledException) { check(true,"cancellation propagates before query"); }
        check(!await failure.CanRevealAsync(db,Guid.Empty,task,default),"empty actor cannot pass missing-row check");
        var evidence = new ParentEligibilityEvidence(ParentEligibilityEvidenceChecks.CandidateContractVersion,task,version,null,null,null,null,null,null,null,null);
        var absent = ParentEligibilityEvidenceChecks.FindMissingCandidateFields(evidence);
        check(absent.Contains("BindingGeneration") && absent.Contains("Aba") && absent.Contains("EligibilityStamp"),"unknown evidence is explicitly incomplete");
        check(evidence.BindingGeneration is null && evidence.EligibilityStamp is null,"shape checks never invent generations/stamp");
        try { ParentEligibilityEvidenceChecks.ValidateStructure(evidence with { TaskId=Guid.Empty }); throw new Exception("invalid evidence allowed"); }
        catch (ArgumentException) { check(true,"invalid evidence identifiers rejected"); }
        try { ParentEligibilityEvidenceChecks.ValidateStructure(evidence with { ContractVersion="unknown" }); throw new Exception("unknown contract allowed"); }
        catch (ArgumentException) { check(true,"unknown evidence version rejected"); }
    }
    private sealed class ProbeReader(Func<ApplicationDbContext,Guid,CancellationToken,Task<ParentTaskAuthorMetadata>> read) : IParentAuthorMetadataReader
    { public Task<ParentTaskAuthorMetadata> ReadAsync(ApplicationDbContext db, Guid taskId, CancellationToken ct)=>read(db,taskId,ct); }
}
