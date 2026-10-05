namespace EarlyInterventionCare.Api.Models.Authentication;

public class Organization
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "ACTIVE";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<User> Users { get; set; } = new List<User>();
}
