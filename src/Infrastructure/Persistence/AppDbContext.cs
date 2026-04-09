using Microsoft.EntityFrameworkCore;
using ProjectOS.Domain.Entities;

namespace ProjectOS.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<ProjectSummary> ProjectSummaries => Set<ProjectSummary>();
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Organization
        modelBuilder.Entity<Organization>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Website).HasMaxLength(500);
            e.Property(x => x.LogoUrl).HasMaxLength(500);
        });

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired().HasMaxLength(256);
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            e.Property(x => x.Role).IsRequired().HasMaxLength(50);

            e.HasIndex(x => new { x.OrganizationId, x.Email }).IsUnique();

            e.HasOne(x => x.Organization)
                .WithMany(o => o.Users)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Project
        modelBuilder.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Status).IsRequired()
                .HasConversion<string>().HasMaxLength(50);

            e.HasIndex(x => new { x.OrganizationId, x.Name });

            e.HasOne(x => x.Organization)
                .WithMany(o => o.Projects)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.OrganizationId);
        });

        // Contact
        modelBuilder.Entity<Contact>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Company).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);

            e.HasIndex(x => new { x.OrganizationId, x.Email });

            e.HasOne(x => x.Organization)
                .WithMany(o => o.Contacts)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EmailMessage
        modelBuilder.Entity<EmailMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Subject).IsRequired().HasMaxLength(500);
            e.Property(x => x.NormalizedSubject).IsRequired().HasMaxLength(500);
            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.FromAddress).IsRequired().HasMaxLength(256);
            e.Property(x => x.ToAddress).IsRequired().HasMaxLength(2000);
            e.Property(x => x.ProviderMessageId).HasMaxLength(500);
            e.Property(x => x.ProviderThreadId).HasMaxLength(500);
            e.Property(x => x.ToContactIds).HasMaxLength(4000);
            e.Property(x => x.AssignmentConfidence).HasPrecision(5, 2);
            e.Property(x => x.AssignmentSource).HasMaxLength(50);
            e.Property(x => x.AiSummary).HasMaxLength(2000);
            e.Property(x => x.AiSuggestedReply).HasMaxLength(2000);
            e.Property(x => x.AiCategory).HasMaxLength(50);
            e.Property(x => x.AiPriority).HasMaxLength(20);

            e.HasIndex(x => x.OrganizationId);
            e.HasIndex(x => new { x.OrganizationId, x.ProjectId });
            e.HasIndex(x => new { x.OrganizationId, x.ProviderMessageId }).IsUnique()
                .HasFilter(null);

            e.HasOne(x => x.Project)
                .WithMany(p => p.EmailMessages)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.FromContact)
                .WithMany(c => c.SentEmails)
                .HasForeignKey(x => x.FromContactId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ProjectSummary
        modelBuilder.Entity<ProjectSummary>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SummaryText).IsRequired();
            e.Property(x => x.CurrentStatus).IsRequired().HasMaxLength(500);
            e.Property(x => x.PendingItems).IsRequired();
            e.Property(x => x.SuggestedNextAction).IsRequired().HasMaxLength(1000);

            e.HasIndex(x => x.ProjectId);

            e.HasOne(x => x.Project)
                .WithMany(p => p.Summaries)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ActionItem
        modelBuilder.Entity<ActionItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(500);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Status).IsRequired().HasMaxLength(50).HasDefaultValue("Pending");

            e.HasIndex(x => x.ProjectId);

            e.HasOne(x => x.Project)
                .WithMany(p => p.ActionItems)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
