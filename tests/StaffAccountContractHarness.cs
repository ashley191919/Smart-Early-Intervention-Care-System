using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using EarlyInterventionCare.Api.DTOs.StaffAccounts;
using EarlyInterventionCare.Api.Services.Authentication;
using EarlyInterventionCare.Api.Services.StaffAccounts;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

// Offline shape/JSON checks only: no application host, DB, business implementation or authorization fake.
// These checks do not prove future permission/scope enforcement, persistence or token revocation.
var checks = 0;
var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception(description);
}
void Fields(Type type, params string[] expected) => Check(
    type.GetProperties().Select(p => p.Name).ToHashSet().SetEquals(expected),
    type.Name + " exposes only the agreed fields");
T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, webJson)!;
void InvalidJson<T>(string json)
{
    var rejected = false;
    try { _ = Read<T>(json); }
    catch (JsonException) { rejected = true; }
    Check(rejected, typeof(T).Name + " rejects malformed or unknown fields");
}

Fields(typeof(CreateStaffAccountRequest), "Username", "Password", "RoleId", "OrganizationId", "Phone");
Fields(typeof(UpdateStaffAccountRequest), "Phone", "OrganizationId", "HasPhone", "HasOrganizationId");
Fields(typeof(StaffAccountResponse), "UserId", "Username", "RoleId", "RoleName", "OrganizationId",
    "OrganizationName", "Phone", "Status", "LastLoginAtUtc", "CreatedAtUtc", "UpdatedAtUtc");
Fields(typeof(StaffAccountQuery), "Page", "PageSize", "Status");
Fields(typeof(StaffAccountPagedResponse), "Items", "Page", "PageSize", "Total");
Fields(typeof(ChangeStaffAccountStatusRequest), "Status");
Fields(typeof(ChangeOwnStaffPasswordRequest), "CurrentPassword", "NewPassword");
Fields(typeof(ResetStaffPasswordRequest), "NewPassword");

foreach (var (type, property) in new[]
{
    (typeof(CreateStaffAccountRequest), "RoleId"), (typeof(CreateStaffAccountRequest), "OrganizationId"),
    (typeof(StaffAccountResponse), "UserId"), (typeof(StaffAccountResponse), "RoleId"),
    (typeof(StaffAccountResponse), "OrganizationId")
})
    Check(type.GetProperty(property)!.PropertyType == typeof(Guid), "identity references use Guid");
Check(typeof(UpdateStaffAccountRequest).GetProperty("OrganizationId")!.PropertyType == typeof(Guid?),
    "patch can preserve explicit organization null for rejection by the future service");

var organizationId = Guid.NewGuid();
var roleId = Guid.NewGuid();
// Synthetic fixtures only; failures and output never contain password values.
var create = new CreateStaffAccountRequest
{
    Username = "staff-fixture", Password = "  contract pass phrase  ", RoleId = roleId,
    OrganizationId = organizationId, Phone = "0123456789"
};
var restored = Read<CreateStaffAccountRequest>(JsonSerializer.Serialize(create, webJson));
Check(restored.RoleId == roleId && restored.OrganizationId == organizationId &&
    restored.Username == create.Username && restored.Phone == create.Phone, "creation fields survive JSON round trip");
Check(restored.Password == create.Password, "new password retains exact whitespace");

var omitted = Read<UpdateStaffAccountRequest>("{}");
Check(!omitted.HasPhone && !omitted.HasOrganizationId, "absent patch fields do not mean clear");
Check(JsonSerializer.Serialize(omitted, webJson) == "{}", "absent fields stay absent on serialization");
var clearPhone = Read<UpdateStaffAccountRequest>("{\"phone\":null}");
Check(clearPhone.HasPhone && clearPhone.Phone is null && !clearPhone.HasOrganizationId,
    "explicit null clears only the requested phone");
Check(JsonSerializer.Serialize(clearPhone, webJson) == "{\"phone\":null}", "clear operation survives round trip");
var emptyPhone = Read<UpdateStaffAccountRequest>("{\"phone\":\"\"}");
Check(emptyPhone.HasPhone && emptyPhone.Phone == "", "empty string remains distinct from null and absence");
var patch = Read<UpdateStaffAccountRequest>(JsonSerializer.Serialize(new { organizationId, phone = "123" }, webJson));
Check(patch.HasOrganizationId && patch.OrganizationId == organizationId && patch.HasPhone && patch.Phone == "123",
    "patch carries requested values without claiming authorization");
var nullOrganization = Read<UpdateStaffAccountRequest>("{\"organizationId\":null}");
Check(nullOrganization.HasOrganizationId && nullOrganization.OrganizationId is null,
    "explicit invalid organization clear is distinguishable from no change");
Check(Read<UpdateStaffAccountRequest>(JsonSerializer.Serialize(nullOrganization, webJson)).HasOrganizationId,
    "invalid organization clear is not silently dropped during serialization");
var emptyOrganization = Read<UpdateStaffAccountRequest>("{\"organizationId\":\"00000000-0000-0000-0000-000000000000\"}");
Check(emptyOrganization.HasOrganizationId && emptyOrganization.OrganizationId == Guid.Empty,
    "empty organization ID is preserved for future service rejection, never interpreted as scope");
var defaultJsonPatch = JsonSerializer.Deserialize<UpdateStaffAccountRequest>("{\"Phone\":null}")!;
Check(defaultJsonPatch.HasPhone && JsonSerializer.Serialize(defaultJsonPatch) == "{\"Phone\":null}",
    "patch presence also works without web naming options");
Check(Read<UpdateStaffAccountRequest>("{\"PHONE\":null}").HasPhone, "patch honors web case-insensitive property binding");

foreach (var invalid in new[]
{
    "{\"organizationId\":\"invalid\"}", "{\"phone\":42}", "[]",
    "{\"phone\":null,\"Phone\":\"123\"}", "{\"organizationId\":null,\"organizationId\":null}",
    "{\"hasPhone\":true}", "{\"isAdmin\":true}", "{\"actorUserId\":\"untrusted\"}",
    "{\"roleId\":null}", "{\"password\":\"untrusted\"}", "{\"scopeApproved\":true}"
}) InvalidJson<UpdateStaffAccountRequest>(invalid);
InvalidJson<CreateStaffAccountRequest>("{\"isAdmin\":true}");
InvalidJson<CreateStaffAccountRequest>("{\"actorUserId\":\"untrusted\"}");
InvalidJson<ResetStaffPasswordRequest>("{\"currentPassword\":\"untrusted\",\"newPassword\":\"untrusted\"}");
InvalidJson<ChangeOwnStaffPasswordRequest>("{\"userId\":\"untrusted\"}");
InvalidJson<ChangeStaffAccountStatusRequest>("{\"permissions\":[]}");
InvalidJson<StaffAccountQuery>("{\"scopeApproved\":true}");

var account = new StaffAccountResponse
{
    UserId = Guid.NewGuid(), Username = create.Username, RoleId = roleId, RoleName = "MEDICAL_STAFF",
    OrganizationId = organizationId, OrganizationName = "Organization fixture", Phone = create.Phone,
    Status = "ACTIVE", LastLoginAtUtc = null, CreatedAtUtc = DateTime.UnixEpoch, UpdatedAtUtc = DateTime.UnixEpoch
};
var page = new StaffAccountPagedResponse { Items = new[] { account }, Page = 1, PageSize = 20, Total = 1 };
var pageJson = JsonSerializer.Serialize(page, webJson);
Check(!pageJson.Contains("password", StringComparison.OrdinalIgnoreCase), "response JSON has no password or hash fields");
Check(!pageJson.Contains(create.Password, StringComparison.Ordinal), "response JSON never echoes the request password");
var restoredPage = Read<StaffAccountPagedResponse>(pageJson);
Check(restoredPage.Items.Count == 1 && restoredPage.Items[0].UserId == account.UserId &&
    restoredPage.Items[0].OrganizationId == organizationId && restoredPage.Total == 1 &&
    restoredPage.Page == 1 && restoredPage.PageSize == 20, "paged account JSON retains identifiers and paging metadata");
Check(new StaffAccountPagedResponse().Items.Count == 0, "default page does not fabricate an account");

var invalidPage = new StaffAccountQuery { Page = 0, PageSize = 101 };
var validation = new List<ValidationResult>();
Check(!Validator.TryValidateObject(invalidPage, new ValidationContext(invalidPage), validation, true) && validation.Count == 2,
    "query annotations bound page index and size");
validation.Clear();
var defaultQuery = new StaffAccountQuery();
Check(Validator.TryValidateObject(defaultQuery, new ValidationContext(defaultQuery), validation, true),
    "default query satisfies annotation constraints");

var own = new ChangeOwnStaffPasswordRequest { CurrentPassword = "old", NewPassword = create.Password };
var reset = new ResetStaffPasswordRequest { NewPassword = create.Password };
var policy = new StaffPasswordPolicy();
foreach (var newPassword in new[] { create.Password, own.NewPassword, reset.NewPassword })
    Check(policy.Validate(newPassword).IsValid, "all new-password contract paths can use existing policy unchanged");
Check(!policy.Validate(own.CurrentPassword).IsValid && own.CurrentPassword == "old",
    "existing short password is carried unchanged for BCrypt verification, not rejected by the DTO");
Check(policy.Validate(new CreateStaffAccountRequest { Password = "password123456" }.Password).Code == "PASSWORD_COMMON",
    "creation password can retain the existing policy error code");
var hash = BCrypt.Net.BCrypt.HashPassword(reset.NewPassword, workFactor: 12);
Check(hash != reset.NewPassword && BCrypt.Net.BCrypt.Verify(reset.NewPassword, hash),
    "new-password contract interoperates with existing BCrypt hash format offline");

var service = typeof(IStaffAccountService);
Check(service.IsInterface, "account service has no executable implementation in the contract");
var methods = service.GetMethods();
Check(methods.Select(m => m.Name).ToHashSet().SetEquals(new[]
{
    "CreateAsync", "GetByIdAsync", "GetPagedAsync", "UpdateAsync", "ChangeStatusAsync", "ChangeOwnPasswordAsync", "ResetPasswordAsync"
}), "service exposes the seven requested operations");
foreach (var method in methods)
{
    var parameters = method.GetParameters();
    Check(parameters[0].ParameterType == typeof(IStaffAccountActorContext), "each operation receives server-owned actor context");
    Check(parameters[^1].ParameterType == typeof(CancellationToken) && parameters[^1].HasDefaultValue,
        "each operation supports optional caller cancellation");
    var needsTarget = method.Name is "GetByIdAsync" or "UpdateAsync" or "ChangeStatusAsync" or "ResetPasswordAsync";
    Check(needsTarget
        ? parameters[1].Name == "targetUserId" && parameters[1].ParameterType == typeof(Guid)
        : parameters.All(p => p.ParameterType != typeof(Guid)), "target ID is explicit only for operations on another account");
}
Check(service.GetMethod("ChangeOwnPasswordAsync")!.GetParameters()[1].ParameterType == typeof(ChangeOwnStaffPasswordRequest) &&
    service.GetMethod("ResetPasswordAsync")!.GetParameters()[2].ParameterType == typeof(ResetStaffPasswordRequest),
    "self-change and administrative reset use separate request contracts");
Check(typeof(IStaffAccountActorContext).GetMethod("GetAuthenticatedUserId")!.ReturnType == typeof(Guid?),
    "actor context represents missing authentication without inventing an actor");
var assembly = service.Assembly;
Check(!assembly.GetTypes().Any(t => t.IsClass && (service.IsAssignableFrom(t) || typeof(IStaffAccountActorContext).IsAssignableFrom(t))),
    "API assembly contains no account service or actor adapter that can be activated by DI");

var parts = new ApplicationPartManager();
parts.ApplicationParts.Add(new AssemblyPart(assembly));
parts.FeatureProviders.Add(new ControllerFeatureProvider());
var controllers = new ControllerFeature();
parts.PopulateFeature(controllers);
Check(controllers.Controllers.Count > 0, "controller discovery inspects the existing API assembly");
bool IsAccountContract(Type type) => type.Namespace == typeof(CreateStaffAccountRequest).Namespace ||
    type == service || type == typeof(IStaffAccountActorContext) ||
    (type.IsGenericType && type.GetGenericArguments().Any(IsAccountContract));
Check(!controllers.Controllers.Any(c => c.Name.Contains("StaffAccount", StringComparison.OrdinalIgnoreCase) ||
    c.DeclaredMethods.Any(m => IsAccountContract(m.ReturnType) || m.GetParameters().Any(p => IsAccountContract(p.ParameterType))) ||
    c.DeclaredConstructors.Any(ctor => ctor.GetParameters().Any(p => IsAccountContract(p.ParameterType)))),
    "no discovered controller exposes or consumes account-management contracts");

Console.WriteLine($"PASS: {checks} offline staff account contract checks; no DB, authorization or revocation integration exercised.");
