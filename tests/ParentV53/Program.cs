using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Authentication;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
async Task Error(Func<Task> work, string code) { try { await work(); } catch (CaseAccessException e) { Check(e.Code == code, code); return; } throw new Exception("Expected " + code); }
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "."));
// No User Secrets/configuration, SMTP or MySQL. Only this in-memory SQLite connection.
using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
var initialOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
await using var conventions = new ApplicationDbContext(initialOptions);
var model = new ModelBuilder(Microsoft.EntityFrameworkCore.Metadata.Conventions.ConventionSet.CreateConventionSet(conventions));
model.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly); AuthorizationSeed.Seed(model);
foreach (var entity in model.Model.GetEntityTypes())
{
    entity.SetCollation(null);
    foreach (var property in entity.GetProperties())
    {
        property.SetCollation(null); var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        property.SetColumnType(type == typeof(byte[]) ? "BLOB" : type == typeof(bool) || type == typeof(int) || type == typeof(uint) ? "INTEGER" : "TEXT");
    }
}
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).UseModel(model.FinalizeModel()).Options;
await using var db = new ApplicationDbContext(options); await db.Database.EnsureCreatedAsync();
var clock = new FixtureClock(); var now = clock.GetUtcNow().UtcDateTime;
var org = new Organization { Id = Guid.NewGuid(), Code = "V53_TEST", Name = "Isolated fixture", Status = "ACTIVE" };
var foreignOrg = new Organization { Id = Guid.NewGuid(), Code = "OTHER", Name = "Other fixture", Status = "ACTIVE" };
db.Organizations.AddRange(org, foreignOrg); await db.SaveChangesAsync();
var parentRole = await db.Roles.SingleAsync(r => r.Name == "PARENT");
var users = Enumerable.Range(1, 4).Select(i => new User { Id = Guid.NewGuid(), Username = "fixture-parent-" + i,
    OrganizationId = org.Id, RoleId = parentRole.Id, Status = "ACTIVE", Email = $"parent{i}@example.invalid",
    NormalizedEmail = $"parent{i}@example.invalid", EmailVerifiedAtUtc = now }).ToArray();
db.Users.AddRange(users);
var child = new CaseRecord { CaseId = Guid.NewGuid(), OrganizationId = org.Id, CaseCode = "V53-FAKE-CHILD",
    ChildName = "虛構孩子", BirthDate = new DateOnly(2022, 1, 1), Sex = "UNKNOWN", NationalId = "A123456789", Address = "隔離測試地址", CaseStatus = "WAITING_FORM" };
var foreignChild = new CaseRecord { CaseId = Guid.NewGuid(), OrganizationId = foreignOrg.Id, CaseCode = "OTHER-FAKE-CHILD",
    ChildName = "其他虛構孩子", BirthDate = new DateOnly(2022, 1, 1), Sex = "UNKNOWN", NationalId = "A123456789", Address = "隔離測試地址", CaseStatus = "WAITING_FORM" };
db.Cases.AddRange(child, foreignChild);
for (var i = 0; i < users.Length; i++)
{
    var g = new GuardianContact { GuardianId = Guid.NewGuid(), OrganizationId = org.Id, ParentUserId = users[i].Id,
        DisplayName = "虛構照顧者", Phone = "090000000" + i, Email = users[i].Email! };
    db.Guardians.Add(g); db.CaseGuardians.Add(new CaseGuardian { CaseId = child.CaseId, GuardianId = g.GuardianId,
        Relationship = "FATHER", IsPrimaryContact = i == 0, BoundAtUtc = now });
}
var questionnaire = new Questionnaire { QuestionnaireId = Guid.NewGuid(), QuestionnaireCode = "V53_FIXTURE", Title = "隔離契約問卷" };
var version = new QuestionnaireVersion { QuestionnaireVersionId = Guid.NewGuid(), QuestionnaireId = questionnaire.QuestionnaireId,
    RespondentRole = "PARENT", VersionNumber = "fixture-1", VersionStatus = "PUBLISHED", DefinitionSnapshot = JsonSerializer.Serialize(new {
        title = "隔離契約問卷", instructions = "僅限測試", questions = new[] { new { questionId = "Q1", order = 1, text = "隔離測試題目", type = "singleChoice", required = true,
            options = new[] { new { value = "0", label = "選項零" }, new { value = "1", label = "選項一" } } } } }) };
db.Questionnaires.Add(questionnaire); db.QuestionnaireVersions.Add(version);
var tasks = Enumerable.Range(1, 4).Select(i => new QuestionnaireTask { TaskId = Guid.NewGuid(), CaseId = child.CaseId,
    QuestionnaireId = questionnaire.QuestionnaireId, QuestionnaireVersionId = version.QuestionnaireVersionId,
    RespondentRole = "PARENT", AssignmentRound = (uint)i, IsRequired = true, TaskStatus = "PENDING" }).ToArray();
db.QuestionnaireTasks.AddRange(tasks);
var doc = new ConsentDocumentVersion { DocumentVersionId = Guid.NewGuid(), DocumentCode = "FIXTURE_ONLY", Version = "test-1",
    Title = "隔離測試文件（非院方核准本文）", Content = "僅為離線契約測試使用的合成文件。", ScopeCode = "ACCOUNT", ApprovalStatus = "DRAFT" };
doc.ContentHash = ParentConsentService.Hash(doc.Content); db.Add(doc); await db.SaveChangesAsync();
var settings = new ParentModuleOptions { ConsentEnabled = true, DocumentVersionId = doc.DocumentVersionId, ScopeCode = "ACCOUNT" };
var access = new CaseAccessService(db); var actors = new ParentActorProvider(access);
var consents = new ParentConsentService(db, actors, access, Options.Create(settings), clock);
ClaimsPrincipal Principal(User u, string role = "PARENT") => new(new ClaimsIdentity([new Claim("userId", u.Id.ToString()), new Claim("role", role)], "isolated-test"));
var principal = Principal(users[0]);
await Error(async () => { await new ParentConsentService(db, actors, access, Options.Create(new ParentModuleOptions()), clock).ContextAsync(principal); }, "CONSENT_NOT_CONFIGURED");
await Error(async () => { await consents.ContextAsync(principal); }, "CONSENT_NOT_CONFIGURED");
doc.ApprovalStatus = "APPROVED"; doc.ApprovalReference = "ISOLATED TEST ONLY, NOT HOSPITAL APPROVAL"; doc.ApprovedAtUtc = now.AddDays(-1); doc.EffectiveFromUtc = now.AddDays(-1); await db.SaveChangesAsync();
var bootstrap = await consents.ContextAsync(principal);
Check(bootstrap.Targets.Single().TeacherParticipation == "UNDECIDED", "teacher initially undecided");
Check(!JsonSerializer.Serialize(bootstrap).Contains(child.ChildName) && !JsonSerializer.Serialize(bootstrap).Contains(child.NationalId), "bootstrap contains no clinical details");
await Error(async () => { await actors.ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", users[0].Id.ToString())]))); }, "UNAUTHENTICATED");
var actor = await actors.ResolveAsync(principal); await Error(async () => { await consents.RequireAsync(actor, child.CaseId); }, "CONSENT_REQUIRED");
var record = new RecordParentConsentRequest(doc.DocumentVersionId, doc.ContentHash, null, "AGREED", "DECLINED", Guid.NewGuid());
var receipt = await consents.RecordAsync(principal, record);
Check(receipt.TeacherParticipation == "DECLINED" && (await consents.RequireAsync(actor, child.CaseId)).ConsentId == receipt.ConsentId, "teacher refusal does not block parent");
Check((await consents.RecordAsync(principal, record)).Replayed, "consent identical retry");
await Error(async () => { await consents.RecordAsync(principal, record with { TeacherParticipation = "AGREED" }); }, "CONSENT_CONFLICT");
await Error(async () => { await consents.RecordAsync(principal, record with { ContentHash = "changed" }); }, "CONSENT_VERSION_CHANGED");
await Error(async () => { await consents.RecordAsync(principal, record with { CaseId = child.CaseId }); }, "CONSENT_SCOPE_REQUIRED");
await consents.RecordAsync(Principal(users[1]), record with { RequestId = Guid.NewGuid(), TeacherParticipation = "UNDECIDED" });
await consents.RecordAsync(Principal(users[2]), record with { RequestId = Guid.NewGuid(), DataUseDecision = "DECLINED", TeacherParticipation = "UNDECIDED" });
await Error(async () => { await consents.RequireAsync(await actors.ResolveAsync(Principal(users[2])), child.CaseId); }, "CONSENT_REQUIRED");
var policy = new ParentQuestionnaireAccess(db, actors, access, consents);
var context = await policy.RequireAsync(principal, tasks[0].TaskId, true, db);
var other = await policy.RequireAsync(Principal(users[1]), tasks[0].TaskId, true, db);
await Error(async () => { await policy.RequireInTransactionAsync(principal, tasks[0].TaskId, true, db); }, "PARENT_TRANSACTION_REQUIRED");
await using (var transaction = await db.Database.BeginTransactionAsync())
{
    Check((await policy.RequireInTransactionAsync(principal, tasks[0].TaskId, true, db)).ActorUserId == users[0].Id, "real SQLite transaction policy callback");
    await transaction.CommitAsync();
}
await using (var differentContext = new ApplicationDbContext(options))
{
    try { await policy.RequireAsync(principal, tasks[0].TaskId, true, differentContext); throw new Exception("different context allowed"); }
    catch (InvalidOperationException) { Check(true, "same DbContext contract enforced"); }
}
var fakeState = new FakeParentGatewayState(); var fake = new FakeParentGateway(db, policy, fakeState, clock, new QuestionnaireAnswerValidator());
await ParentGatewayContractSuite.RunAsync(fake, context, other, tasks[0].TaskId, version.QuestionnaireVersionId, "2026-10-22", Check);
Check(await db.QuestionnaireResponses.CountAsync() == 0, "Fake never stores production Responses");
await Error(async () => { await new UnavailableParentQuestionnaireGateway().SaveDraftAsync(context, tasks[1].TaskId,
    new(version.QuestionnaireVersionId, "0", null, null, "FATHER", [], null)); }, "PARENT_GATEWAY_UNAVAILABLE");
var defaultServices = new ServiceCollection(); defaultServices.AddParentV53(); defaultServices.AddParentV53();
Check(defaultServices.Count(x => x.ServiceType == typeof(IParentQuestionnaireGateway)) == 1, "idempotent DI registration");
await using (var provider = defaultServices.BuildServiceProvider())
    Check(provider.GetRequiredService<IParentQuestionnaireGateway>() is UnavailableParentQuestionnaireGateway, "production DI never registers Fake Gateway");
var originalContent = doc.Content; doc.Content = "tampered"; await db.SaveChangesAsync();
await Error(async () => { await consents.ContextAsync(principal); }, "CONSENT_NOT_CONFIGURED");
doc.Content = originalContent; await db.SaveChangesAsync();
await db.Users.Where(u => u.Id == users[0].Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "DISABLED"));
await Error(async () => { await consents.ContextAsync(principal); }, "UNAUTHENTICATED");
await db.Users.Where(u => u.Id == users[0].Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "ACTIVE"));

// CASE scope is explicitly tested, not selected as a production policy.
var caseDoc = new ConsentDocumentVersion { DocumentVersionId = Guid.NewGuid(), DocumentCode = "FIXTURE_CASE", Version = "test-1", Title = doc.Title,
    Content = doc.Content, ContentHash = doc.ContentHash, ScopeCode = "CASE", ApprovalStatus = "APPROVED", ApprovalReference = doc.ApprovalReference,
    ApprovedAtUtc = doc.ApprovedAtUtc, EffectiveFromUtc = doc.EffectiveFromUtc };
db.Add(caseDoc); await db.SaveChangesAsync();
var caseSettings = Options.Create(new ParentModuleOptions { ConsentEnabled = true, DocumentVersionId = caseDoc.DocumentVersionId, ScopeCode = "CASE" });
var caseConsents = new ParentConsentService(db, actors, access, caseSettings, clock);
Check((await caseConsents.ContextAsync(principal)).Targets.Single().CaseId == child.CaseId, "CASE bootstrap opaque scope only");
await Error(async () => { await caseConsents.RecordAsync(principal, record with { DocumentVersionId = caseDoc.DocumentVersionId, RequestId = Guid.NewGuid(), CaseId = foreignChild.CaseId }); }, "CASE_NOT_FOUND");
await caseConsents.RecordAsync(principal, record with { DocumentVersionId = caseDoc.DocumentVersionId, RequestId = Guid.NewGuid(), CaseId = child.CaseId });
Check((await caseConsents.RequireAsync(actor, child.CaseId)).CaseId == child.CaseId, "CASE consent persisted");

// HTTP host uses real signed JWT validation, real EF Consent and real Guardian adapter.
// Only the Questionnaire Gateway is Fake; runtime config and secrets are not loaded.
var serve = args.Contains("--serve");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetFullPath("."), Args = [] });
builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
if (serve) builder.WebHost.UseUrls("http://127.0.0.1:5185"); else builder.WebHost.UseTestServer();
builder.Services.AddScoped(_ => new ApplicationDbContext(options));
builder.Services.AddSingleton<TimeProvider>(clock); builder.Services.AddScoped<CaseAccessService>(); builder.Services.AddScoped<CaseGuardianService>();
builder.Services.AddScoped<IQuestionnaireStore, EfQuestionnaireStore>(); builder.Services.AddScoped<QuestionnaireService>();
builder.Services.AddParentQuestionnaireDefinitions(); builder.Services.PostConfigure<ParentModuleOptions>(o => { o.ConsentEnabled = true; o.DocumentVersionId = doc.DocumentVersionId; o.ScopeCode = "ACCOUNT"; });
builder.Services.AddSingleton(fakeState); builder.Services.AddScoped<IParentQuestionnaireGateway, FakeParentGateway>();
builder.Services.AddControllers().AddApplicationPart(typeof(ParentConsentController).Assembly);
builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("parent-binding", x => { x.PermitLimit = 100; x.Window = TimeSpan.FromMinutes(1); }));
var key = new SymmetricSecurityKey(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
var jwtOptions = new JwtOptions { Issuer = "v53-isolated", Audience = "v53-isolated", ExpirationMinutes = 30 };
var tokens = new JwtTokenService(jwtOptions, key);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = key,
        ValidIssuer = jwtOptions.Issuer, ValidAudience = jwtOptions.Audience, ValidateIssuer = true, ValidateAudience = true,
        ValidateLifetime = true, RoleClaimType = "role", ValidAlgorithms = [SecurityAlgorithms.HmacSha256] }; });
builder.Services.AddAuthorization();
await using var app = builder.Build();
if (serve)
{
    await db.QuestionnaireTasks.Where(t => t.TaskId != tasks[1].TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TaskStatus, "CANCELLED"));
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath("EarlyInterventionCare.Api/wwwroot")) });
    app.MapGet("/fixture-login", () => Microsoft.AspNetCore.Http.Results.Content("<meta charset='utf-8'><script src='/js/parent-session.js'></script><script>EarlyCareParentSession.signIn({role:'PARENT',accessToken:'" + tokens.CreateToken(users[3].Id, users[3].Username, "PARENT").AccessToken + "'});</script>", "text/html"));
}
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapControllers(); await app.StartAsync();
if (serve) { Console.WriteLine("ISOLATED SQLite Consent + Fake answer Gateway: http://127.0.0.1:5185/fixture-login. No MySQL/SMTP. Consent text NOT hospital-approved."); await app.WaitForShutdownAsync(); return; }
var http = app.GetTestClient();
Check((await http.GetAsync("/api/parent/consent/context")).StatusCode == HttpStatusCode.Unauthorized, "anonymous blocked");
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateToken(users[3].Id, users[3].Username, "ADMIN").AccessToken);
foreach (var path in new[] { "/api/parent/cases", "/api/parent/profile", "/api/cases", "/api/cases/" + child.CaseId,
    "/api/parent/tasks/" + tasks[1].TaskId + "/questionnaire", "/api/parent/tasks/" + tasks[1].TaskId + "/draft" })
    Check((await http.GetAsync(path)).StatusCode == HttpStatusCode.Forbidden, "unconsented protected read blocked: " + path);
var bootstrapHttp = await http.GetAsync("/api/parent/consent/context");
Check(bootstrapHttp.IsSuccessStatusCode && !(await bootstrapHttp.Content.ReadAsStringAsync()).Contains(child.NationalId), "HTTP bootstrap no clinical data");
var spoofed = new { documentVersionId = doc.DocumentVersionId, contentHash = doc.ContentHash, caseId = (Guid?)null,
    dataUseDecision = "AGREED", teacherParticipation = "DECLINED", requestId = Guid.NewGuid(), actorUserId = users[0].Id, organizationId = foreignOrg.Id };
Check((await http.PostAsJsonAsync("/api/parent/consent", spoofed)).IsSuccessStatusCode, "consent accepts trusted JWT actor only");
Check(await db.Set<ParentConsentRecord>().AnyAsync(r => r.ActorUserId == users[3].Id && r.OrganizationId == org.Id && r.RequestId == spoofed.requestId), "body actor/org cannot override JWT/DB actor");
Check((await http.GetAsync("/api/parent/cases")).IsSuccessStatusCode, "consented clinical read");
Check((await http.PostAsJsonAsync("/api/cases", new { childName = "不應建立", nationalId = "A123456789", address = "測試", birthDate = "2022-01-01", sex = "UNKNOWN" })).StatusCode == HttpStatusCode.Forbidden, "forged ADMIN claim cannot grant medical CRUD");
var route = "/api/parent/tasks/" + tasks[1].TaskId;
var draftHttp = await http.GetFromJsonAsync<ParentDraftResponse>(route + "/draft"); Check(draftHttp?.Revision == "0", "HTTP draft");
var saveRequest = new ParentDraftRequest(version.QuestionnaireVersionId, "0", "虛構填表人", "2026-10-22", "MOTHER", [new("Q1", "0")], null);
Check((await http.PutAsJsonAsync(route + "/draft", saveRequest)).IsSuccessStatusCode, "HTTP save through Gateway");
Check((await http.PutAsJsonAsync(route + "/draft", saveRequest)).StatusCode == HttpStatusCode.Conflict, "HTTP stale revision");
Check((await http.PutAsJsonAsync(route + "/draft", saveRequest with { ExpectedRevision = "1", RespondentRelationship = "ADMIN" })).StatusCode == HttpStatusCode.BadRequest, "relationship cannot grant authority");
var submitRequest = new ParentSubmitRequest(version.QuestionnaireVersionId, "1", "虛構填表人", "2026-10-22", "MOTHER", saveRequest.Answers, null);
Check((await http.PostAsJsonAsync(route + "/submit", submitRequest)).StatusCode == HttpStatusCode.BadRequest, "idempotency header required");
http.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
var submitted = await http.PostAsJsonAsync(route + "/submit", submitRequest); Check(submitted.IsSuccessStatusCode, "HTTP submit");
Check((await http.PostAsJsonAsync(route + "/submit", submitRequest)).IsSuccessStatusCode, "HTTP repeated submit");
Check((await http.GetAsync(route + "/receipt")).IsSuccessStatusCode, "HTTP own receipt");
Check((await db.QuestionnaireTasks.AsNoTracking().SingleAsync(t => t.TaskId == tasks[1].TaskId)).TaskStatus == "SUBMITTED", "fixture server task state refreshed");
await db.CaseGuardians.Where(l => l.CaseId == child.CaseId && db.Guardians.Any(g => g.GuardianId == l.GuardianId && g.ParentUserId == users[3].Id))
    .ExecuteUpdateAsync(s => s.SetProperty(l => l.LinkStatus, "REVOKED"));
Check((await http.GetAsync(route + "/receipt")).StatusCode == HttpStatusCode.NotFound, "Guardian revoke blocks receipt");
await db.Set<ParentConsentRecord>().Where(r => r.ActorUserId == users[0].Id && r.DocumentVersionId == doc.DocumentVersionId).ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAtUtc, (DateTime?)now));
await Error(async () => { await fake.GetReceiptAsync(context, tasks[0].TaskId); }, "CONSENT_REQUIRED");
Check(await db.QuestionnaireResponses.CountAsync() == 0 && fakeState.Responses.Count == 2, "no real Response rows; Fake results explicitly separate");
await app.StopAsync();
Console.WriteLine($"PASS: {checks} V5.3 checks; real SQLite Consent/Guardian + signed JWT HTTP, reusable Fake Gateway contract. No MySQL/SMTP/migration; no production answer persistence certified.");

public sealed class FixtureClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
}
