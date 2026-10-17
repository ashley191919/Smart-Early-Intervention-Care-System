using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EarlyInterventionCare.Api.DTOs.StaffAccounts;

/// <summary>
/// Omitted fields remain unchanged. Explicit phone null clears the phone.
/// Explicit organization null is retained for validation, but must be rejected by the future
/// service because User.OrganizationId is required. Neither value grants organization scope.
/// </summary>
[JsonConverter(typeof(UpdateStaffAccountRequestJsonConverter))]
public sealed class UpdateStaffAccountRequest
{
    private string? _phone;
    private Guid? _organizationId;

    [StringLength(20)]
    public string? Phone
    {
        get => _phone;
        set { _phone = value; HasPhone = true; }
    }

    public Guid? OrganizationId
    {
        get => _organizationId;
        set { _organizationId = value; HasOrganizationId = true; }
    }

    [JsonIgnore]
    public bool HasPhone { get; private set; }

    [JsonIgnore]
    public bool HasOrganizationId { get; private set; }
}

// Local converter preserves field presence in both directions without changing global JSON options.
internal sealed class UpdateStaffAccountRequestJsonConverter : JsonConverter<UpdateStaffAccountRequest>
{
    public UpdateStaffAccountRequestJsonConverter() { }

    public override UpdateStaffAccountRequest Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Account update must be an object.");

        var result = new UpdateStaffAccountRequest();
        var comparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, Name(nameof(result.Phone), options), comparison))
            {
                if (result.HasPhone || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new JsonException("Phone must be supplied once as a string or null.");
                result.Phone = property.Value.GetString();
            }
            else if (string.Equals(property.Name, Name(nameof(result.OrganizationId), options), comparison))
            {
                if (result.HasOrganizationId)
                    throw new JsonException("Organization must be supplied once.");
                if (property.Value.ValueKind == JsonValueKind.Null)
                    result.OrganizationId = null;
                else if (property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetGuid(out var id))
                    result.OrganizationId = id;
                else
                    throw new JsonException("Organization must be a UUID or null.");
            }
            else
                throw new JsonException("Unknown account update field.");
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, UpdateStaffAccountRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.HasPhone)
            writer.WriteString(Name(nameof(value.Phone), options), value.Phone);
        if (value.HasOrganizationId)
        {
            writer.WritePropertyName(Name(nameof(value.OrganizationId), options));
            if (value.OrganizationId is Guid id) writer.WriteStringValue(id);
            else writer.WriteNullValue();
        }
        writer.WriteEndObject();
    }

    private static string Name(string property, JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(property) ?? property;
}
