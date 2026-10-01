namespace EarlyInterventionCare.Api.Models.Authorization
{
    public class Permission
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public ICollection<RolePermission> RolePermissions { get; set; }
            = new List<RolePermission>();
    }
}