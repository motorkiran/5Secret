using Microsoft.EntityFrameworkCore;
using SecretManager.Domain.Agents;
using SecretManager.Domain.Auditing;
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Catalog;
using SecretManager.Domain.Environments;
using SecretManager.Domain.Installations;
using SecretManager.Domain.Topology;
using SecretManager.Domain.Users;

namespace SecretManager.Infrastructure.Persistence;

public sealed class SecretManagerDbContext(DbContextOptions<SecretManagerDbContext> options) : DbContext(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<AgentEnrollmentToken> AgentEnrollmentTokens => Set<AgentEnrollmentToken>();

    public DbSet<AgentRegistration> AgentRegistrations => Set<AgentRegistration>();

    public DbSet<ApplicationDefinition> Applications => Set<ApplicationDefinition>();

    public DbSet<ApplicationAssignment> ApplicationAssignments => Set<ApplicationAssignment>();

    public DbSet<PublishOperation> PublishOperations => Set<PublishOperation>();

    public DbSet<PublishedVersion> PublishedVersions => Set<PublishedVersion>();

    public DbSet<NamespaceDefinition> Namespaces => Set<NamespaceDefinition>();

    public DbSet<ConfigItemDefinition> ConfigItems => Set<ConfigItemDefinition>();

    public DbSet<SecretManager.Domain.Catalog.DraftValue> DraftValues => Set<SecretManager.Domain.Catalog.DraftValue>();

    public DbSet<EnvironmentDefinition> Environments => Set<EnvironmentDefinition>();

    public DbSet<NodeGroupDefinition> NodeGroups => Set<NodeGroupDefinition>();

    public DbSet<ManagedNodeRecord> ManagedNodes => Set<ManagedNodeRecord>();

    public DbSet<Installation> Installations => Set<Installation>();

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentEnrollmentToken>(entity =>
        {
            entity.ToTable("agent_enrollment_tokens");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ManagedNodeId);
            entity.Property(x => x.ManagedNodeId).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.ExpiresAtUtc).IsRequired();
            entity.Property(x => x.ConsumedAtUtc);
            entity.HasOne<ManagedNodeRecord>()
                .WithMany()
                .HasForeignKey(x => x.ManagedNodeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.IssuedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AgentRegistration>(entity =>
        {
            entity.ToTable("agent_registrations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ManagedNodeId).IsUnique();
            entity.Property(x => x.ManagedNodeId).IsRequired();
            entity.Property(x => x.CredentialHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.LastSeenAtUtc);
            entity.Property(x => x.CurrentPublishedVersionId);
            entity.Property(x => x.CurrentVersionNumber);
            entity.Property(x => x.HealthStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.EnrolledAtUtc).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne<ManagedNodeRecord>()
                .WithMany()
                .HasForeignKey(x => x.ManagedNodeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PublishedVersion>()
                .WithMany()
                .HasForeignKey(x => x.CurrentPublishedVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasIndex(x => x.ActorUserId);
            entity.Property(x => x.InstallationId).IsRequired();
            entity.Property(x => x.ActorUsername).HasMaxLength(64);
            entity.Property(x => x.Action).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TargetType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TargetIdentifier).HasMaxLength(256).IsRequired();
            entity.Property(x => x.TargetDisplayName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RemoteIpAddress).HasMaxLength(64);
            entity.Property(x => x.DetailsJson).HasColumnType("text");
            entity.Property(x => x.OccurredAtUtc).IsRequired();
        });

        modelBuilder.Entity<ApplicationDefinition>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasIndex(x => x.Slug)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(512).IsRequired();
            entity.Property(x => x.DefaultIntegrationMode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.IsDeleted).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ApplicationAssignment>(entity =>
        {
            entity.ToTable("application_assignments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ApplicationId, x.EnvironmentId, x.NodeGroupId, x.ManagedNodeId }).IsUnique();
            entity.Property(x => x.ApplicationId).IsRequired();
            entity.Property(x => x.EnvironmentId).IsRequired();
            entity.Property(x => x.Enabled).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne<ApplicationDefinition>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EnvironmentDefinition>()
                .WithMany()
                .HasForeignKey(x => x.EnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<NodeGroupDefinition>()
                .WithMany()
                .HasForeignKey(x => x.NodeGroupId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ManagedNodeRecord>()
                .WithMany()
                .HasForeignKey(x => x.ManagedNodeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PublishOperation>(entity =>
        {
            entity.ToTable("publish_operations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.EnvironmentId, x.ApplicationId, x.CreatedAtUtc });
            entity.Property(x => x.EnvironmentId).IsRequired();
            entity.Property(x => x.ApplicationId).IsRequired();
            entity.Property(x => x.ChangeSummary).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.CompletedAtUtc);
            entity.HasOne<EnvironmentDefinition>()
                .WithMany()
                .HasForeignKey(x => x.EnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationDefinition>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.InitiatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PublishedVersion>(entity =>
        {
            entity.ToTable("published_versions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.EnvironmentId, x.ApplicationId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => x.PublishOperationId);
            entity.Property(x => x.PublishOperationId).IsRequired();
            entity.Property(x => x.EnvironmentId).IsRequired();
            entity.Property(x => x.ApplicationId).IsRequired();
            entity.Property(x => x.VersionNumber).IsRequired();
            entity.Property(x => x.RolloutPolicy).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("text").IsRequired();
            entity.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.PublishedAtUtc).IsRequired();
            entity.HasOne<PublishOperation>()
                .WithMany()
                .HasForeignKey(x => x.PublishOperationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EnvironmentDefinition>()
                .WithMany()
                .HasForeignKey(x => x.EnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationDefinition>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.PublishedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<PublishedVersion>()
                .WithMany()
                .HasForeignKey(x => x.SupersedesVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NamespaceDefinition>(entity =>
        {
            entity.ToTable("namespaces");
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasIndex(x => new { x.ApplicationId, x.Path })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(x => x.ApplicationId).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Path).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(512).IsRequired();
            entity.Property(x => x.IsDeleted).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne<ApplicationDefinition>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConfigItemDefinition>(entity =>
        {
            entity.ToTable("config_items");
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasIndex(x => new { x.ApplicationId, x.FullPath })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(x => x.ApplicationId).IsRequired();
            entity.Property(x => x.NamespaceId).IsRequired();
            entity.Property(x => x.Key).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FullPath).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ValueType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.IsSecret).IsRequired();
            entity.Property(x => x.IsRequired).IsRequired();
            entity.Property(x => x.DefaultRolloutPolicy).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ValidationSchemaJson).HasColumnType("text");
            entity.Property(x => x.Description).HasMaxLength(512).IsRequired();
            entity.Property(x => x.IsDeleted).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne<ApplicationDefinition>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NamespaceDefinition>()
                .WithMany()
                .HasForeignKey(x => x.NamespaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecretManager.Domain.Catalog.DraftValue>(entity =>
        {
            entity.ToTable("draft_values");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ConfigItemId, x.ScopeType, x.ScopeId }).IsUnique();
            entity.Property(x => x.ConfigItemId).IsRequired();
            entity.Property(x => x.ScopeType).IsRequired();
            entity.Property(x => x.ScopeId).IsRequired();
            entity.Property(x => x.ValueJson).HasColumnType("text").IsRequired();
            entity.Property(x => x.IsSecret).IsRequired();
            entity.Property(x => x.ChangeNote).HasMaxLength(512).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne<ConfigItemDefinition>()
                .WithMany()
                .HasForeignKey(x => x.ConfigItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EnvironmentDefinition>(entity =>
        {
            entity.ToTable("environments");
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasIndex(x => x.Slug)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(512).IsRequired();
            entity.Property(x => x.IsProtected).IsRequired();
            entity.Property(x => x.IsDeleted).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<NodeGroupDefinition>(entity =>
        {
            entity.ToTable("node_groups");
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasIndex(x => new { x.EnvironmentId, x.Slug })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(x => x.EnvironmentId).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(512).IsRequired();
            entity.Property(x => x.IsDeleted).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne<EnvironmentDefinition>()
                .WithMany()
                .HasForeignKey(x => x.EnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ManagedNodeRecord>(entity =>
        {
            entity.ToTable("managed_nodes");
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasIndex(x => x.Hostname)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(x => x.EnvironmentId).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Hostname).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Platform).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.AgentVersion).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RolloutPolicyDefault).HasMaxLength(64).IsRequired();
            entity.Property(x => x.IsDeleted).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne<EnvironmentDefinition>()
                .WithMany()
                .HasForeignKey(x => x.EnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<NodeGroupDefinition>()
                .WithMany()
                .HasForeignKey(x => x.NodeGroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Installation>(entity =>
        {
            entity.ToTable("installations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.InitializedAtUtc).IsRequired();
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("user_accounts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(64).IsRequired();
            entity.Property(x => x.IsEnabled).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<RoleDefinition>(entity =>
        {
            entity.ToTable("role_definitions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(512).IsRequired();
            entity.Property(x => x.IsSystem).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasMany(x => x.Permissions)
                .WithOne(x => x.RoleDefinition)
                .HasForeignKey(x => x.RoleDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(x => new { x.RoleDefinitionId, x.Permission });
            entity.Property(x => x.Permission).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<RoleAssignment>(entity =>
        {
            entity.ToTable("role_assignments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.RoleDefinitionId, x.ScopeType, x.ScopeId }).IsUnique();
            entity.Property(x => x.ScopeType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.ScopeId).IsRequired();
            entity.Property(x => x.CreatedByUserId);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.ExpiresAtUtc);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.RoleDefinition)
                .WithMany()
                .HasForeignKey(x => x.RoleDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}