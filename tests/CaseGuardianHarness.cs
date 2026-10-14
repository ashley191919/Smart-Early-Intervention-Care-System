using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Encodings.Web;
using EarlyInterventionCare.Api.Services.Questionnaires;
using System.Security.Claims;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Cases;
using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
var checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
async Task Error(Func<Task> action,string code){try{await action();}catch(CaseAccessException e){Check(e.Code==code,code);return;}throw new Exception("Expected "+code);}
// Disposable SQLite relational store; normalize provider-specific column affinities only.
// Production mapping is separately inspected with Pomelo below. No application DB/secrets touched.
using var conn=new SqliteConnection("Data Source=:memory:");await conn.OpenAsync();
await using var conventionDb=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conn).Options);
var builder=new ModelBuilder(Microsoft.EntityFrameworkCore.Metadata.Conventions.ConventionSet.CreateConventionSet(conventionDb));builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);AuthorizationSeed.Seed(builder);
foreach(var e in builder.Model.GetEntityTypes())
{
 e.SetCollation(null);
 foreach(var p in e.GetProperties())
 {
  p.SetCollation(null);var t=Nullable.GetUnderlyingType(p.ClrType)??p.ClrType;
  p.SetColumnType(t==typeof(byte[])?"BLOB":t==typeof(bool)||t==typeof(int)||t==typeof(uint)?"INTEGER":"TEXT");
 }
}
var testModel=builder.FinalizeModel();
var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conn).UseModel(testModel).Options;
await using var db=new ApplicationDbContext(options);await db.Database.EnsureCreatedAsync();
var clock=new Clock();var access=new CaseAccessService(db);var service=new CaseGuardianService(db,access,clock);
var org=new Organization{Id=Guid.NewGuid(),Code="TEST",Name="Test",Status="ACTIVE"};var otherOrg=new Organization{Id=Guid.NewGuid(),Code="OTHER",Name="Other",Status="ACTIVE"};db.Organizations.AddRange(org,otherOrg);await db.SaveChangesAsync();
async Task<CaseActor> Account(string role,string name,string phone,bool verified,Guid organization)
{
 var r=await db.Roles.SingleAsync(x=>x.Name==role);var u=new User{Id=Guid.NewGuid(),RoleId=r.Id,OrganizationId=organization,Username=name,Phone=phone,PhoneVerifiedAtUtc=verified?clock.GetUtcNow().UtcDateTime:null,PasswordHash="test-only-not-a-password"};db.Users.Add(u);await db.SaveChangesAsync();return await access.ActorAsync(Principal(u.Id));
}
ClaimsPrincipal Principal(Guid id)=>new(new ClaimsIdentity(new[]{new Claim("userId",id.ToString()),new Claim("role","ADMIN")},"test"));
var staff=await Account("CASE_MANAGER","staff","0900000000",false,org.Id);var parent=await Account("PARENT","parent","0911111111",true,org.Id);var wrong=await Account("PARENT","wrong","0922222222",true,org.Id);var unverified=await Account("PARENT","unverified","0933333333",false,org.Id);var outsider=await Account("CASE_MANAGER","outsider","0944444444",false,otherOrg.Id);
Check(parent.Role=="PARENT","DB role wins over forged claim");
var c=await service.CreateAsync(staff,new CaseInput{NationalId="A123456789",Address="測試地址",ChildName="測試孩子",BirthDate=new(2022,1,1),Sex="UNKNOWN"});Check(c.CaseStatus=="NEW","new case state");
Check((await service.ListAsync(parent)).Count==0,"unbound parent sees no cases");
await Error(async()=>{await service.ReadAsync(parent,c.CaseId);},"CASE_NOT_FOUND");await Error(async()=>{await service.ReadAsync(outsider,c.CaseId);},"CASE_NOT_FOUND");
await Error(async()=>{await service.CreateAsync(parent,new CaseInput{NationalId="A123456789",Address="測試地址",ChildName="不能新增",BirthDate=new(2020,1,1)});},"FORBIDDEN");
await Error(async()=>{await service.CreateAsync(staff,new CaseInput{NationalId="A123456789",Address="測試地址",ChildName="未來",BirthDate=new(2100,1,1)});},"INVALID_INPUT");
await Error(async()=>{await service.CreateAsync(staff,new CaseInput{ChildName="缺必填",BirthDate=new(2020,1,1)});},"INVALID_INPUT");
await Error(async()=>{await service.CreateAsync(staff,new CaseInput{NationalId="A123456788",Address="測試地址",ChildName="錯誤檢查碼",BirthDate=new(2020,1,1)});},"INVALID_INPUT");
await Error(async()=>{await service.CreateAsync(staff,new CaseInput{NationalId="A923456789",Address="測試地址",ChildName="不符國民身分證格式",BirthDate=new(2020,1,1)});},"INVALID_INPUT");
var updated=await service.UpdateAsync(staff,c.CaseId,new CaseUpdateInput{NationalId="A123456789",Address="測試地址",ChildName="修改孩子",BirthDate=c.BirthDate,Sex=c.Sex,Revision=c.Revision});Check(updated.Revision==1&&updated.CaseStatus=="NEW","revision without workflow changes");
await Error(async()=>{await service.UpdateAsync(staff,c.CaseId,new CaseUpdateInput{NationalId="A123456789",Address="測試地址",ChildName="舊版",BirthDate=c.BirthDate,Sex=c.Sex,Revision=0});},"REVISION_CONFLICT");
var g=await service.AddGuardianAsync(staff,c.CaseId,new GuardianInput{Email="guardian@example.invalid",DisplayName="測試家長",Phone="+886911111111",Relationship="MOTHER",IsPrimaryContact=true});Check(g.Phone==parent.Phone,"phone normalization");
await Error(async()=>{await service.AddGuardianAsync(staff,c.CaseId,new GuardianInput{Email="second@example.invalid",DisplayName="第二位",Phone="0999999999",Relationship="OTHER"});},"PRIMARY_CONTACT_EXISTS");
await Error(async()=>{await service.AddGuardianAsync(staff,c.CaseId,new GuardianInput{DisplayName="缺Email",Phone="0999999999",Relationship="OTHER"});},"INVALID_EMAIL");
var invite=await service.InviteAsync(staff,c.CaseId,g.GuardianId);Check(invite.Code.Length==32,"128-bit invitation");Check((await db.ParentBindingInvitations.AsNoTracking().SingleAsync()).CodeHash!=invite.Code,"hash only storage");
await Error(async()=>{await service.BindAsync(unverified,invite.Code);},"PHONE_VERIFICATION_REQUIRED");await Error(async()=>{await service.BindAsync(wrong,invite.Code);},"INVITATION_UNAVAILABLE");Check((await db.ParentBindingInvitations.AsNoTracking().SingleAsync()).ConsumedAtUtc==null,"wrong phone does not consume");
await service.BindAsync(parent,invite.Code);Check((await service.ListAsync(parent)).Single().CaseId==c.CaseId,"bound child list");
var ownRelations=(await service.ParentCasesAsync(parent)).Single().Relationships;Check(ownRelations.Count==1&&ownRelations[0].Relationship=="MOTHER"&&ownRelations[0].IsPrimaryContact,"parent own relation and primary contact");
Check((await service.ParentCasesAsync(wrong)).Count==0,"unbound parent has no relation data");await Error(async()=>{await service.BindAsync(parent,invite.Code);},"INVITATION_UNAVAILABLE");Check((await db.ParentBindingInvitations.AsNoTracking().SingleAsync()).ConsumedByUserId==parent.UserId,"consumed with account");
var profile=(await service.ProfilesAsync(parent)).Single();await service.UpdateProfileAsync(parent,profile.GuardianId,new ProfileInput{DisplayName="家長新姓名",Revision=profile.Revision});Check((await service.ProfilesAsync(parent)).Single().DisplayName=="家長新姓名","own contact update");
var medicalProfile=(await service.GuardiansAsync(staff,c.CaseId)).Single();
await Error(async()=>{await service.UpdateGuardianAsync(staff,c.CaseId,g.GuardianId,new GuardianUpdateInput{Email="guardian@example.invalid",DisplayName="家長",Phone="0999999999",Relationship="MOTHER",Revision=medicalProfile.Revision});},"PHONE_CHANGE_REQUIRES_REVERIFICATION");
await service.UpdateGuardianAsync(staff,c.CaseId,g.GuardianId,new GuardianUpdateInput{Email="guardian@example.invalid",DisplayName="醫護確認姓名",Phone=parent.Phone!,Relationship="GUARDIAN",Revision=medicalProfile.Revision});Check((await service.GuardiansAsync(staff,c.CaseId)).Single().Relationship=="GUARDIAN","medical guardian update");
await Error(async()=>{await service.UpdateGuardianAsync(staff,c.CaseId,g.GuardianId,new GuardianUpdateInput{Email="guardian@example.invalid",DisplayName="舊版",Phone=parent.Phone!,Relationship="GUARDIAN",Revision=medicalProfile.Revision});},"REVISION_CONFLICT");
await Error(async()=>{await service.UpdateProfileAsync(wrong,g.GuardianId,new ProfileInput{DisplayName="越權",Revision=0});},"PROFILE_NOT_FOUND");
var form=new Questionnaire{QuestionnaireId=Guid.NewGuid(),QuestionnaireCode="TEST",Title="測試問卷"};db.Questionnaires.Add(form);
var v=new QuestionnaireVersion{QuestionnaireVersionId=Guid.NewGuid(),QuestionnaireId=form.QuestionnaireId,RespondentRole="TEACHER",VersionNumber="1",VersionStatus="PUBLISHED",DefinitionSnapshot="{}"};db.QuestionnaireVersions.Add(v);
var task=new QuestionnaireTask{TaskId=Guid.NewGuid(),CaseId=c.CaseId,QuestionnaireId=form.QuestionnaireId,QuestionnaireVersionId=v.QuestionnaireVersionId,RespondentRole="TEACHER",AssignmentRound=1,TaskStatus="SUBMITTED",IsRequired=true};db.QuestionnaireTasks.Add(task);await db.SaveChangesAsync();
var completion=(await service.TeacherCompletionAsync(parent,c.CaseId)).Single();Check(completion.IsCompleted,"teacher status only");Check(typeof(TeacherCompletionDto).GetProperties().Length==4&&!JsonSerializer.Serialize(completion).Contains("Answer"),"no teacher answers/scores/notes");Check(!await access.CanWriteParentTaskAsync(Principal(parent.UserId),task.TaskId),"parent cannot write teacher task");
var pv=new QuestionnaireVersion{QuestionnaireVersionId=Guid.NewGuid(),QuestionnaireId=form.QuestionnaireId,RespondentRole="PARENT",VersionNumber="1",VersionStatus="PUBLISHED",DefinitionSnapshot="{}"};db.QuestionnaireVersions.Add(pv);var pt=new QuestionnaireTask{TaskId=Guid.NewGuid(),CaseId=c.CaseId,QuestionnaireId=form.QuestionnaireId,QuestionnaireVersionId=pv.QuestionnaireVersionId,RespondentRole="PARENT",AssignmentRound=1,TaskStatus="PENDING"};db.QuestionnaireTasks.Add(pt);await db.SaveChangesAsync();Check(await access.CanWriteParentTaskAsync(Principal(parent.UserId),pt.TaskId),"16 parent scope interface");
var archiveTask=new QuestionnaireTask{TaskId=Guid.NewGuid(),CaseId=c.CaseId,QuestionnaireId=form.QuestionnaireId,QuestionnaireVersionId=pv.QuestionnaireVersionId,RespondentRole="PARENT",AssignmentRound=2,TaskStatus="SUBMITTED"};
db.QuestionnaireTasks.Add(archiveTask);db.QuestionnaireDrafts.Add(new QuestionnaireDraft{TaskId=pt.TaskId,QuestionnaireVersionId=pv.QuestionnaireVersionId,AnswersJson="{\"Q1\":\"1\"}"});var archivedResponse=new QuestionnaireResponse{ResponseId=Guid.NewGuid(),TaskId=archiveTask.TaskId,QuestionnaireVersionId=pv.QuestionnaireVersionId,RespondentName="保存紀錄",AnswersJson="{\"Q1\":\"2\"}",PayloadHash=new byte[32],IdempotencyKey=Guid.NewGuid(),FilledOn=new(2026,10,13)};db.QuestionnaireResponses.Add(archivedResponse);await db.SaveChangesAsync();
Check(await access.CanReadParentTaskAsync(Principal(parent.UserId),archiveTask.TaskId),"submitted parent task is readable in active scope");Check(!await access.CanWriteParentTaskAsync(Principal(parent.UserId),archiveTask.TaskId),"submitted parent task cannot be changed");
await service.RevokeLinkAsync(staff,c.CaseId,g.GuardianId);Check(!await access.CanReadAsync(Principal(parent.UserId),c.CaseId),"revocation read immediately denied");Check(!await access.CanWriteParentTaskAsync(Principal(parent.UserId),pt.TaskId),"revocation blocks parent form");await Error(async()=>{await service.TeacherCompletionAsync(parent,c.CaseId);},"CASE_NOT_FOUND");
Check(!await access.CanReadParentTaskAsync(Principal(parent.UserId),archiveTask.TaskId),"revocation blocks submitted task reads");Check(await db.QuestionnaireDrafts.AnyAsync(d=>d.TaskId==pt.TaskId)&&await db.QuestionnaireResponses.AnyAsync(r=>r.ResponseId==archivedResponse.ResponseId),"revocation retains drafts and responses");
await service.LinkGuardianAsync(staff,c.CaseId,g.GuardianId,new GuardianLinkInput{Relationship="GUARDIAN"});Check(!await access.CanReadAsync(Principal(parent.UserId),c.CaseId),"medical reapproval still requires new binding");await service.BindAsync(parent,(await service.InviteAsync(staff,c.CaseId,g.GuardianId)).Code);Check(await access.CanReadParentTaskAsync(Principal(parent.UserId),archiveTask.TaskId),"new approved binding restores task read scope");Check(await db.QuestionnaireDrafts.AnyAsync(d=>d.TaskId==pt.TaskId)&&await db.QuestionnaireResponses.AnyAsync(r=>r.ResponseId==archivedResponse.ResponseId),"rebind keeps original records");await service.RevokeLinkAsync(staff,c.CaseId,g.GuardianId);

var second=await service.CreateAsync(staff,new CaseInput{NationalId="A123456789",Address="測試地址",ChildName="第二個孩子",BirthDate=new(2021,1,1)});var sg=await service.LinkGuardianAsync(staff,second.CaseId,g.GuardianId,new GuardianLinkInput{Relationship="GUARDIAN"});Check(!await access.CanReadAsync(Principal(parent.UserId),second.CaseId),"shared contact does not automatically grant child access");var expired=await service.InviteAsync(staff,second.CaseId,sg.GuardianId);Check(expired.ExpiresAtUtc==null,"invitation has no expiry");
await db.ParentBindingInvitations.Where(i=>i.InvitationId==expired.InvitationId).ExecuteUpdateAsync(s=>s.SetProperty(i=>i.ExpiresAtUtc,clock.GetUtcNow().UtcDateTime.AddDays(-1)));
await Error(async()=>{await service.BindAsync(parent,expired.Code);},"INVITATION_UNAVAILABLE");var revoked=await service.InviteAsync(staff,second.CaseId,sg.GuardianId);await service.RevokeInviteAsync(staff,revoked.InvitationId);await Error(async()=>{await service.BindAsync(parent,revoked.Code);},"INVITATION_UNAVAILABLE");
var valid=await service.InviteAsync(staff,second.CaseId,sg.GuardianId);clock.Advance(TimeSpan.FromDays(365));await service.BindAsync(parent,valid.Code);Check((await service.ListAsync(parent)).Single().CaseId==second.CaseId,"another child bound");
var third=await service.CreateAsync(staff,new CaseInput{NationalId="A123456789",Address="測試地址",ChildName="第三個孩子",BirthDate=new(2020,1,1)});var tg=await service.LinkGuardianAsync(staff,third.CaseId,g.GuardianId,new GuardianLinkInput{Relationship="FATHER"});await service.BindAsync(parent,(await service.InviteAsync(staff,third.CaseId,tg.GuardianId)).Code);Check((await service.ListAsync(parent)).Count==2,"one parent multiple children");Check(sg.GuardianId==tg.GuardianId,"same contact shared across children");
await service.UpdateProfileAsync(parent,g.GuardianId,new ProfileInput{DisplayName="共用家長姓名",Revision=(await service.ProfilesAsync(parent)).Single().Revision});Check((await service.GuardiansAsync(staff,third.CaseId)).Single().DisplayName=="共用家長姓名","contact edits shared across children");
// A case may have multiple caregivers; uniqueness applies only to its active primary contact.
var contactsCase=await service.CreateAsync(staff,new CaseInput{NationalId="A123456789",Address="測試地址",ChildName="多照顧者測試",BirthDate=new(2020,1,1)});
var primaryContact=await service.AddGuardianAsync(staff,contactsCase.CaseId,new GuardianInput{DisplayName="主要照顧者",Phone="0966666666",Email="primary@example.invalid",Relationship="MOTHER",IsPrimaryContact=true});
var secondaryContact=await service.AddGuardianAsync(staff,contactsCase.CaseId,new GuardianInput{DisplayName="其他照顧者",Phone="0977777777",Email="secondary@example.invalid",Relationship="FATHER",IsPrimaryContact=false});
Check((await service.GuardiansAsync(staff,contactsCase.CaseId)).Count==2&&!secondaryContact.IsPrimaryContact,"multiple caregivers preserve primary designation");
await Error(async()=>{await service.UpdateGuardianAsync(staff,contactsCase.CaseId,secondaryContact.GuardianId,new GuardianUpdateInput{DisplayName="其他照顧者",Phone=secondaryContact.Phone,Email=secondaryContact.Email,Relationship="FATHER",IsPrimaryContact=true,Revision=secondaryContact.Revision});},"PRIMARY_CONTACT_EXISTS");
await Error(async()=>{await service.UpdateGuardianAsync(staff,contactsCase.CaseId,secondaryContact.GuardianId,new GuardianUpdateInput{DisplayName="其他照顧者",Phone=primaryContact.Phone,Email=secondaryContact.Email,Relationship="FATHER",IsPrimaryContact=false,Revision=secondaryContact.Revision});},"CONTACT_ALREADY_EXISTS");
await service.UpdateGuardianAsync(staff,contactsCase.CaseId,secondaryContact.GuardianId,new GuardianUpdateInput{DisplayName="修改其他照顧者",Phone=secondaryContact.Phone,Email=secondaryContact.Email,Relationship="FATHER",IsPrimaryContact=false,Revision=secondaryContact.Revision});
Check(!(await service.GuardiansAsync(staff,contactsCase.CaseId)).Single(x=>x.GuardianId==secondaryContact.GuardianId).IsPrimaryContact,"secondary update never silently promotes contact");
var extraLink=await service.LinkGuardianAsync(staff,contactsCase.CaseId,g.GuardianId,new GuardianLinkInput{Relationship="RELATIVE",IsPrimaryContact=false});
Check(!extraLink.IsPrimaryContact,"existing shared caregiver can be added as secondary");
await Error(async()=>{await service.LinkGuardianAsync(staff,contactsCase.CaseId,g.GuardianId,new GuardianLinkInput{Relationship="RELATIVE",IsPrimaryContact=false});},"LINK_ALREADY_EXISTS");
await service.RevokeLinkAsync(staff,contactsCase.CaseId,g.GuardianId);
extraLink=await service.LinkGuardianAsync(staff,contactsCase.CaseId,g.GuardianId,new GuardianLinkInput{Relationship="RELATIVE",IsPrimaryContact=false});
Check(!extraLink.IsPrimaryContact&&!await access.CanReadAsync(Principal(parent.UserId),contactsCase.CaseId),"secondary reapproval still requires per-child binding");
await service.RevokeLinkAsync(staff,contactsCase.CaseId,g.GuardianId);
// Reuse the revoked row to exercise the database's primary-contact index independently of prechecks.
try{await db.CaseGuardians.Where(l=>l.CaseId==contactsCase.CaseId&&l.GuardianId==g.GuardianId).ExecuteUpdateAsync(s=>s.SetProperty(l=>l.LinkStatus,"ACTIVE").SetProperty(l=>l.IsPrimaryContact,true));throw new Exception("Second primary unexpectedly persisted");}
catch(SqliteException e)when(e.SqliteErrorCode==19){Check(true,"database unique constraint rejects second active primary");}
// Actual HTTP routing, authorization and JSON projection through an isolated TestServer.
var browserFixture=args.Contains("--serve-browser-fixture");
var apiBuilder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development",WebRootPath=Path.GetFullPath("EarlyInterventionCare.Api/wwwroot")});
if(browserFixture)apiBuilder.WebHost.UseUrls("http://127.0.0.1:5182");else apiBuilder.WebHost.UseTestServer();
apiBuilder.Logging.ClearProviders();
apiBuilder.Services.AddControllers().AddApplicationPart(typeof(EarlyInterventionCare.Api.Controllers.CasesController).Assembly);
apiBuilder.Services.AddScoped(_=>new ApplicationDbContext(options));apiBuilder.Services.AddScoped<CaseAccessService>();apiBuilder.Services.AddScoped<CaseGuardianService>();apiBuilder.Services.AddSingleton<TimeProvider>(clock);
apiBuilder.Services.AddScoped<IQuestionnaireStore,EfQuestionnaireStore>();apiBuilder.Services.AddScoped<QuestionnaireService>();
apiBuilder.Services.AddSingleton<EarlyInterventionCare.Api.Services.Authentication.IParentSmsVerification,EarlyInterventionCare.Api.Services.Authentication.ParentSmsUnavailable>();
apiBuilder.Services.AddSingleton(new EarlyInterventionCare.Api.Services.Authentication.JwtTokenService(new(){Issuer="fixture",Audience="fixture"},new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))));
apiBuilder.Services.AddScoped<EarlyInterventionCare.Api.Services.Authentication.ParentOtpAuthenticationService>();
apiBuilder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,TestAuth>("Test",_=>{});apiBuilder.Services.AddAuthorization();
apiBuilder.Services.AddRateLimiter(o=>{foreach(var name in new[]{"parent-binding","parent-otp-request","parent-otp-verify"})o.AddFixedWindowLimiter(name,x=>{x.PermitLimit=100;x.Window=TimeSpan.FromMinutes(1);});});
await using var app=apiBuilder.Build();
if(browserFixture)
{
 // Only this disposable test executable hosts fixture authentication; never registered in the application.
 await db.Cases.Where(x=>x.CaseId==second.CaseId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CaseStatus,"WAITING_FORM"));
 foreach(var item in new[]{(Round:1u,Status:"PENDING"),(Round:2u,Status:"IN_PROGRESS"),(Round:3u,Status:"SUBMITTED")})db.QuestionnaireTasks.Add(new QuestionnaireTask{TaskId=Guid.NewGuid(),CaseId=second.CaseId,QuestionnaireId=form.QuestionnaireId,QuestionnaireVersionId=pv.QuestionnaireVersionId,RespondentRole="PARENT",AssignmentRound=item.Round,TaskStatus=item.Status,IsRequired=true});
 await db.SaveChangesAsync();
 app.UseDefaultFiles();app.UseStaticFiles();
 app.MapGet("/fixture-login",()=>Results.Content("<html><meta charset='utf-8'><script src='/js/parent-session.js'></script><script>EarlyCareParentSession.signIn({role:'PARENT',accessToken:'"+parent.UserId+"'});</script></html>","text/html"));
 app.MapPost("/fixture-profile-change",async()=>{await using var context=new ApplicationDbContext(options);await context.Guardians.Where(g=>g.GuardianId==sg.GuardianId).ExecuteUpdateAsync(s=>s.SetProperty(g=>g.DisplayName,"其他工作階段更新姓名").SetProperty(g=>g.Revision,g=>g.Revision+1));return Results.NoContent();});
}
app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();app.MapControllers();await app.StartAsync();
if(browserFixture){Console.WriteLine("Disposable SQLite browser fixture: http://127.0.0.1:5182/fixture-login; no real MySQL, SMS or login credentials.");await app.WaitForShutdownAsync();return;}
var client=app.GetTestClient();
Check((await client.GetAsync("/api/parent/cases")).StatusCode==HttpStatusCode.Unauthorized,"HTTP anonymous 401");
client.DefaultRequestHeaders.Add("X-Test-User",parent.UserId.ToString());
Check((await client.GetAsync("/api/parent/cases")).IsSuccessStatusCode,"HTTP bound cases");
Check((await client.GetAsync("/api/cases/"+c.CaseId)).StatusCode==HttpStatusCode.NotFound,"HTTP revoked case hidden");
Check((await client.PostAsync("/api/cases",new StringContent("{\"childName\":\"X\",\"birthDate\":\"2020-01-01\",\"sex\":\"UNKNOWN\",\"nationalId\":\"A123456789\",\"address\":\"測試地址\"}",System.Text.Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.Forbidden,"HTTP parent cannot create case");
client.DefaultRequestHeaders.Remove("X-Test-User");client.DefaultRequestHeaders.Add("X-Test-User",staff.UserId.ToString());
var invalidCaseResponse=await client.PostAsync("/api/cases",new StringContent("{\"childName\":\"錯誤檢查碼\",\"birthDate\":\"2020-01-01\",\"sex\":\"UNKNOWN\",\"nationalId\":\"A123456788\",\"address\":\"測試地址\"}",System.Text.Encoding.UTF8,"application/json"));
Check(invalidCaseResponse.StatusCode==HttpStatusCode.BadRequest,"HTTP validates national-ID checksum without trusting frontend");
var forgedCaseJson=JsonSerializer.Serialize(new { childName="API測試孩子",birthDate="2020-01-01",sex="UNKNOWN",nationalId="A123456789",address="測試地址",caseId=Guid.NewGuid(),organizationId=otherOrg.Id,caseStatus="COMPLETED" });
var createdResponse=await client.PostAsync("/api/cases",new StringContent(forgedCaseJson,System.Text.Encoding.UTF8,"application/json"));
Check(createdResponse.StatusCode==HttpStatusCode.Created,"HTTP medical creates case");
var createdCase=JsonSerializer.Deserialize<CaseDto>(await createdResponse.Content.ReadAsStringAsync(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
Check(createdCase.CaseStatus=="NEW"&&(await service.ReadAsync(staff,createdCase.CaseId)).OrganizationId==staff.OrganizationId,"HTTP server chooses status and organization despite spoofed fields");
Check((await client.GetAsync("/api/cases?page=1&pageSize=100")).IsSuccessStatusCode,"HTTP medical case list");
Check((await client.GetAsync("/api/cases?page=0")).StatusCode==HttpStatusCode.BadRequest,"HTTP invalid pagination rejected");
var updateJson=JsonSerializer.Serialize(new { childName="API修改孩子",birthDate="2020-01-01",sex="UNKNOWN",nationalId="A123456789",address="修改測試地址",revision=createdCase.Revision });
Check((await client.PutAsync($"/api/cases/{createdCase.CaseId}",new StringContent(updateJson,System.Text.Encoding.UTF8,"application/json"))).IsSuccessStatusCode,"HTTP medical updates core data");
Check((await client.PutAsync($"/api/cases/{createdCase.CaseId}",new StringContent(updateJson,System.Text.Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.Conflict,"HTTP stale core update rejected");
Check((await client.GetAsync($"/api/cases/{c.CaseId}/tasks/completion")).IsSuccessStatusCode,"16 completion real Case scope");
client.DefaultRequestHeaders.Remove("X-Test-User");client.DefaultRequestHeaders.Add("X-Test-User",outsider.UserId.ToString());
Check((await client.GetAsync($"/api/cases/{c.CaseId}/tasks/completion")).StatusCode==HttpStatusCode.Forbidden,"16 cross-organization summary forbidden");
Check((await client.GetAsync($"/api/cases/{createdCase.CaseId}")).StatusCode==HttpStatusCode.NotFound,"HTTP cross-organization case hidden");
client.DefaultRequestHeaders.Remove("X-Test-User");client.DefaultRequestHeaders.Add("X-Test-User",unverified.UserId.ToString());Check((await client.GetAsync("/api/parent/cases")).StatusCode==HttpStatusCode.Forbidden,"HTTP unverified phone 403");
await app.StopAsync();
// A second isolated HTTP host uses actual JWT signature/issuer/audience/lifetime validation.
// Test keys exist only in memory; no User Secrets, SMS or real database is accessed.
var jwtOptions=new EarlyInterventionCare.Api.Options.JwtOptions{Issuer="case-tests",Audience="parent-tests",ExpirationMinutes=30};
var testKey=new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
var tokens=new EarlyInterventionCare.Api.Services.Authentication.JwtTokenService(jwtOptions,testKey);
var testSms=new TestSms();
var otpParent=await Account("PARENT","otp-parent","0955555555",false,org.Id);
var jwtBuilder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});
jwtBuilder.WebHost.UseTestServer();jwtBuilder.Logging.ClearProviders();
jwtBuilder.Services.AddControllers().AddApplicationPart(typeof(EarlyInterventionCare.Api.Controllers.CasesController).Assembly);
jwtBuilder.Services.AddScoped(_=>new ApplicationDbContext(options));jwtBuilder.Services.AddScoped<CaseAccessService>();jwtBuilder.Services.AddScoped<CaseGuardianService>();jwtBuilder.Services.AddSingleton<TimeProvider>(clock);
jwtBuilder.Services.AddScoped<IQuestionnaireStore,EfQuestionnaireStore>();jwtBuilder.Services.AddScoped<QuestionnaireService>();
jwtBuilder.Services.AddSingleton(tokens);jwtBuilder.Services.AddSingleton<EarlyInterventionCare.Api.Services.Authentication.IParentSmsVerification>(testSms);
jwtBuilder.Services.AddScoped<EarlyInterventionCare.Api.Services.Authentication.ParentOtpAuthenticationService>();
jwtBuilder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>
{
 o.MapInboundClaims=false;o.IncludeErrorDetails=false;
 o.TokenValidationParameters=new Microsoft.IdentityModel.Tokens.TokenValidationParameters{ValidateIssuerSigningKey=true,IssuerSigningKey=testKey,ValidAlgorithms=new[]{Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256},ValidateIssuer=true,ValidIssuer=jwtOptions.Issuer,ValidateAudience=true,ValidAudience=jwtOptions.Audience,ValidateLifetime=true,RequireExpirationTime=true,RequireSignedTokens=true,ClockSkew=TimeSpan.FromSeconds(30),RoleClaimType="role",NameClaimType="username"};
});
jwtBuilder.Services.AddAuthorization();jwtBuilder.Services.AddRateLimiter(o=>{foreach(var name in new[]{"parent-binding","parent-otp-request","parent-otp-verify"})o.AddFixedWindowLimiter(name,l=>{l.PermitLimit=100;l.Window=TimeSpan.FromMinutes(1);});});
await using(var jwtApp=jwtBuilder.Build())
{
 jwtApp.UseAuthentication();jwtApp.UseAuthorization();jwtApp.UseRateLimiter();jwtApp.MapControllers();await jwtApp.StartAsync();var jwtClient=jwtApp.GetTestClient();
 Check((await jwtClient.GetAsync("/api/parent/cases")).StatusCode==HttpStatusCode.Unauthorized,"JWT anonymous denied");
 var requestJson=new StringContent("{\"phone\":\"0955555555\"}",System.Text.Encoding.UTF8,"application/json");
 Check((await jwtClient.PostAsync("/api/parent/auth/otp/request",requestJson)).StatusCode==HttpStatusCode.ServiceUnavailable,"unconfigured SMS never pretends to send code");
 Check((await jwtClient.PostAsync("/api/parent/auth/otp/verify",new StringContent("{\"challengeId\":\"fake\",\"code\":\"123456\"}",System.Text.Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.ServiceUnavailable,"unconfigured verification cannot issue JWT");
 Check((await db.Users.AsNoTracking().SingleAsync(u=>u.Id==otpParent.UserId)).PhoneVerifiedAtUtc is null,"SMS unavailable does not verify phone");
 testSms.Configured=true;
 var challengeResponse=await jwtClient.PostAsync("/api/parent/auth/otp/request",new StringContent("{\"phone\":\"+886955555555\"}",System.Text.Encoding.UTF8,"application/json"));
 var challengeBody=await challengeResponse.Content.ReadAsStringAsync();
 var challenge=JsonSerializer.Deserialize<EarlyInterventionCare.Api.Services.Authentication.ParentOtpChallenge>(challengeBody,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
 Check(challengeResponse.IsSuccessStatusCode&&!challengeBody.Contains("654321")&&!challengeBody.Contains("0955555555"),"OTP API returns no phone or secret code");
 Check((await jwtClient.PostAsync("/api/parent/auth/otp/verify",new StringContent(JsonSerializer.Serialize(new{challengeId=challenge.ChallengeId,code="000000"}),System.Text.Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.Unauthorized,"incorrect SMS proof rejected");
 var verifiedResponse=await jwtClient.PostAsync("/api/parent/auth/otp/verify",new StringContent(JsonSerializer.Serialize(new{challengeId=challenge.ChallengeId,code="654321",phone=parent.Phone,userId=parent.UserId,organizationId=otherOrg.Id}),System.Text.Encoding.UTF8,"application/json"));
 var login=JsonSerializer.Deserialize<EarlyInterventionCare.Api.Services.Authentication.ParentOtpLoginResponse>(await verifiedResponse.Content.ReadAsStringAsync(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
 Check(verifiedResponse.IsSuccessStatusCode&&login.UserId==otpParent.UserId&&login.Role=="PARENT","OTP login trusts provider phone, not forged client identity");
 Check((await db.Users.AsNoTracking().SingleAsync(u=>u.Id==otpParent.UserId)).PhoneVerifiedAtUtc.HasValue,"provider proof updates verified timestamp");
 Check((await jwtClient.PostAsync("/api/parent/auth/otp/verify",new StringContent(JsonSerializer.Serialize(new{challengeId=challenge.ChallengeId,code="654321"}),System.Text.Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.Unauthorized,"provider single-use proof cannot replay");
 jwtClient.DefaultRequestHeaders.Authorization=new("Bearer",login.AccessToken);
 Check((await jwtClient.GetAsync("/api/parent/cases")).IsSuccessStatusCode,"OTP-issued JWT passes real HTTP signature validation");
 Check(await (await jwtClient.GetAsync("/api/parent/cases")).Content.ReadAsStringAsync()=="[]","phone verification does not auto-bind any child");
 jwtClient.DefaultRequestHeaders.Authorization=null;
 var parentToken=tokens.CreateToken(parent.UserId,"test-parent","ADMIN").AccessToken; // forged role cannot overrule DB
 jwtClient.DefaultRequestHeaders.Authorization=new("Bearer",parentToken);
 Check((await jwtClient.GetAsync("/api/parent/cases")).IsSuccessStatusCode,"signed JWT reads bound parent cases using current DB role");
 Check((await jwtClient.PostAsync("/api/cases",new StringContent(forgedCaseJson,System.Text.Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.Forbidden,"JWT parent claiming ADMIN cannot create case");
 Check((await jwtClient.GetAsync($"/api/cases/{c.CaseId}")).StatusCode==HttpStatusCode.NotFound,"JWT revoked child hidden");
 var wrongKey=new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
 jwtClient.DefaultRequestHeaders.Authorization=new("Bearer",new EarlyInterventionCare.Api.Services.Authentication.JwtTokenService(jwtOptions,wrongKey).CreateToken(parent.UserId,"test","PARENT").AccessToken);
 Check((await jwtClient.GetAsync("/api/parent/cases")).StatusCode==HttpStatusCode.Unauthorized,"JWT wrong signature denied");
 jwtClient.DefaultRequestHeaders.Authorization=new("Bearer",new EarlyInterventionCare.Api.Services.Authentication.JwtTokenService(new(){Issuer="wrong-issuer",Audience=jwtOptions.Audience},testKey).CreateToken(parent.UserId,"test","PARENT").AccessToken);
 Check((await jwtClient.GetAsync("/api/parent/cases")).StatusCode==HttpStatusCode.Unauthorized,"JWT wrong issuer denied");
 jwtClient.DefaultRequestHeaders.Authorization=new("Bearer",new EarlyInterventionCare.Api.Services.Authentication.JwtTokenService(new(){Issuer=jwtOptions.Issuer,Audience="wrong-audience"},testKey).CreateToken(parent.UserId,"test","PARENT").AccessToken);
 Check((await jwtClient.GetAsync("/api/parent/cases")).StatusCode==HttpStatusCode.Unauthorized,"JWT wrong audience denied");
 var expiredJwt=new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(jwtOptions.Issuer,jwtOptions.Audience,new[]{new Claim("userId",parent.UserId.ToString())},DateTime.UtcNow.AddHours(-2),DateTime.UtcNow.AddHours(-1),new Microsoft.IdentityModel.Tokens.SigningCredentials(testKey,Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
 jwtClient.DefaultRequestHeaders.Authorization=new("Bearer",new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(expiredJwt));
 Check((await jwtClient.GetAsync("/api/parent/cases")).StatusCode==HttpStatusCode.Unauthorized,"JWT expired token denied");
 jwtClient.DefaultRequestHeaders.Authorization=new("Bearer",tokens.CreateToken(unverified.UserId,"unverified","PARENT").AccessToken);
 Check((await jwtClient.GetAsync("/api/parent/cases")).StatusCode==HttpStatusCode.Forbidden,"JWT signature alone does not verify phone");
 await db.Users.Where(u=>u.Id==parent.UserId).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Status,"DISABLED"));
 jwtClient.DefaultRequestHeaders.Authorization=new("Bearer",parentToken);
 Check((await jwtClient.GetAsync("/api/parent/cases")).StatusCode==HttpStatusCode.Unauthorized,"already issued JWT blocked after account disabled");
 await db.Users.Where(u=>u.Id==parent.UserId).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Status,"ACTIVE"));
 await jwtApp.StopAsync();
}
// Two independent relational contexts redeem the same code; only one succeeds.
var raceCase=await service.CreateAsync(staff,new CaseInput{NationalId="A123456789",Address="測試地址",ChildName="競爭綁定",BirthDate=new(2020,1,1)});
var raceGuardian=await service.LinkGuardianAsync(staff,raceCase.CaseId,g.GuardianId,new GuardianLinkInput{Relationship="GUARDIAN"});var raceInvite=await service.InviteAsync(staff,raceCase.CaseId,raceGuardian.GuardianId);
var file=Path.Combine(Path.GetTempPath(),"earlycare-binding-test-"+Guid.NewGuid().ToString("N")+".db");
try{
 using(var copy=new SqliteConnection("Data Source="+file)){copy.Open();conn.BackupDatabase(copy);}
 async Task<bool> Redeem(){await using var raceDb=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source="+file+";Default Timeout=5").UseModel(testModel).Options);var scope=new CaseAccessService(raceDb);try{await new CaseGuardianService(raceDb,scope,clock).BindAsync(parent,raceInvite.Code);return true;}catch(CaseAccessException e)when(e.Code=="INVITATION_UNAVAILABLE"){return false;}}
 var races=await Task.WhenAll(Task.Run(Redeem),Task.Run(Redeem));Check(races.Count(ok=>ok)==1,"concurrent invitation redemption exactly once");
 await using var readBack=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source="+file).UseModel(testModel).Options);Check((await readBack.ParentBindingInvitations.AsNoTracking().SingleAsync(i=>i.InvitationId==raceInvite.InvitationId)).ConsumedByUserId==parent.UserId,"binding persists in fresh context");
}finally{SqliteConnection.ClearAllPools();File.Delete(file);}
var principal=Principal(parent.UserId);await db.Users.Where(u=>u.Id==parent.UserId).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Status,"DISABLED"));await Error(async()=>{await access.ActorAsync(principal);},"UNAUTHENTICATED");
await using var mysql=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseMySql("Server=localhost;Database=offline;User=x;Password=x",new MySqlServerVersion(new Version(8,4,11))).Options);
Check(mysql.Model.FindEntityType(typeof(CaseGuardian))!.FindPrimaryKey()!.Properties.Count==2,"production composite key");Check(mysql.Model.FindEntityType(typeof(ParentBindingInvitation))!.GetIndexes().Any(i=>i.IsUnique),"production unique hash");Check(mysql.Model.GetEntityTypes().SelectMany(e=>e.GetForeignKeys()).All(f=>f.DeleteBehavior==DeleteBehavior.Restrict),"restricted deletes");
Console.WriteLine($"PASS: {checks} relational Case/Guardian checks; scope, binding/replay/expiry/revoke, profile revision, teacher privacy, multi-child and disabled account. MySQL mapping inspected; no real DB migration performed.");
sealed class Clock:TimeProvider{DateTimeOffset now=new(2026,10,13,0,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan duration)=>now+=duration;}

// Provider double exists solely inside this executable; never registered by the application.
sealed class TestSms:EarlyInterventionCare.Api.Services.Authentication.IParentSmsVerification
{
 public bool Configured {get;set;}
 readonly Dictionary<string,string> pending=new();
 public Task<EarlyInterventionCare.Api.Services.Authentication.ParentOtpChallenge> RequestAsync(string phone,CancellationToken ct)
 {
  if(!Configured)return new EarlyInterventionCare.Api.Services.Authentication.ParentSmsUnavailable().RequestAsync(phone,ct);
  var id=Guid.NewGuid().ToString("N");pending[id]=phone;
  return Task.FromResult(new EarlyInterventionCare.Api.Services.Authentication.ParentOtpChallenge(id,DateTimeOffset.UtcNow.AddMinutes(5),60));
 }
 public Task<EarlyInterventionCare.Api.Services.Authentication.VerifiedParentPhone?> VerifyAsync(string id,string code,CancellationToken ct)
 {
  if(!Configured)return new EarlyInterventionCare.Api.Services.Authentication.ParentSmsUnavailable().VerifyAsync(id,code,ct);
  if(code=="654321"&&pending.Remove(id,out var phone))return Task.FromResult<EarlyInterventionCare.Api.Services.Authentication.VerifiedParentPhone?>(new(phone));
  return Task.FromResult<EarlyInterventionCare.Api.Services.Authentication.VerifiedParentPhone?>(null);
 }
}

sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
 protected override Task<AuthenticateResult> HandleAuthenticateAsync()
 {
  string id=Request.Headers["X-Test-User"].ToString();
  if(string.IsNullOrEmpty(id)&&Request.Headers.Authorization.ToString().StartsWith("Bearer "))id=Request.Headers.Authorization.ToString()[7..];
  if(!Guid.TryParse(id,out _))return Task.FromResult(AuthenticateResult.NoResult());
  var identity=new ClaimsIdentity(new[]{new Claim("userId",id.ToString()),new Claim("role","ADMIN")},Scheme.Name);
  return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),Scheme.Name)));
 }
}
