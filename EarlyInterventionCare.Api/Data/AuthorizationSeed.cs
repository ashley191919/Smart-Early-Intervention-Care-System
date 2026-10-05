using EarlyInterventionCare.Api.Models.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Data
{
    public static class AuthorizationSeed
    {
        // Registers fixed role data in the EF model; this method does not write to the database.
        // Existing database initialization must be reconciled before applying any migrations.
        public static void Seed(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Role>().HasData(
                new Role
                {
                    Id = 1,
                    Name = "ADMIN",
                    Description = "系統管理員"
                },
                new Role
                {
                    Id = 2,
                    Name = "CASE_MANAGER",
                    Description = "個案管理師"
                },
                new Role
                {
                    Id = 3,
                    Name = "MEDICAL_STAFF",
                    Description = "醫療人員"
                },
                new Role
                {
                    Id = 4,
                    Name = "PARENT",
                    Description = "家長"
                },
                new Role
                {
                    Id = 5,
                    Name = "TEACHER",
                    Description = "教師"
                }
            );
        }
    }
}