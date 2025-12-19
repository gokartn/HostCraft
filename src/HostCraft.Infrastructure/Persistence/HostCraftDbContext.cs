using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Entities;

namespace HostCraft.Infrastructure.Persistence;

public class HostCraftDbContext : DbContext
{
    public HostCraftDbContext(DbContextOptions<HostCraftDbContext> options) : base(options)
    {
    }
    
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<DeploymentLog> DeploymentLogs => Set<DeploymentLog>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<EnvironmentVariable> EnvironmentVariables => Set<EnvironmentVariable>();
    public DbSet<PrivateKey> PrivateKeys => Set<PrivateKey>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Backup> Backups => Set<Backup>();
    public DbSet<HealthCheck> HealthChecks => Set<HealthCheck>();
    public DbSet<Volume> Volumes => Set<Volume>();
    public DbSet<GitProvider> GitProviders => Set<GitProvider>();
    public DbSet<GitProviderSettings> GitProviderSettings => Set<GitProviderSettings>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        ConfigureServer(modelBuilder);
        ConfigureApplication(modelBuilder);
        ConfigureDeployment(modelBuilder);
        ConfigureRegion(modelBuilder);
        ConfigureBackup(modelBuilder);
        ConfigureHealthCheck(modelBuilder);
        ConfigureVolume(modelBuilder);
        ConfigureProject(modelBuilder);
        ConfigureEnvironmentVariable(modelBuilder);
        ConfigurePrivateKey(modelBuilder);
        ConfigureUser(modelBuilder);
        ConfigureSystemSettings(modelBuilder);
        ConfigureGitProviderSettings(modelBuilder);
    }
    
    private void ConfigureServer(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Server>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Host).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            // Removed unique constraint - allow duplicate server names
            entity.HasIndex(e => e.Name);
            
            entity.HasOne(e => e.PrivateKey)
                .WithMany(e => e.Servers)
                .HasForeignKey(e => e.PrivateKeyId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.HasOne(e => e.Region)
                .WithMany(e => e.Servers)
                .HasForeignKey(e => e.RegionId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.Ignore(e => e.IsSwarm);
            entity.Ignore(e => e.CanDeployApplications);
            entity.Ignore(e => e.CanManageSwarm);
        });
    }
    
    private void ConfigureApplication(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Uuid).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.HasIndex(e => new { e.ServerId, e.Name }).IsUnique();
            
            entity.HasOne(e => e.Project)
                .WithMany(e => e.Applications)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Server)
                .WithMany(e => e.Applications)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.Ignore(e => e.IsSwarmMode);
        });
    }
    
    private void ConfigureDeployment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Deployment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Uuid).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.StartedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.HasIndex(e => e.ApplicationId);
            entity.HasIndex(e => e.Status);
            
            entity.HasOne(e => e.Application)
                .WithMany(e => e.Deployments)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.Ignore(e => e.Duration);
        });
        
        modelBuilder.Entity<DeploymentLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Message).IsRequired();
            entity.Property(e => e.Timestamp).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.DeploymentId);
            
            entity.HasOne(e => e.Deployment)
                .WithMany(e => e.Logs)
                .HasForeignKey(e => e.DeploymentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
    
    private void ConfigureProject(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Uuid).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.HasIndex(e => e.Name).IsUnique();
        });
    }
    
    private void ConfigureEnvironmentVariable(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EnvironmentVariable>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => new { e.ApplicationId, e.Key }).IsUnique();
            
            entity.HasOne(e => e.Application)
                .WithMany(e => e.EnvironmentVariables)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
    
    private void ConfigurePrivateKey(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PrivateKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.KeyData).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.Name).IsUnique();
        });
    }
    
    private void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Uuid).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });
    }
    
    private void ConfigureRegion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Region>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.Code).IsUnique();
        });
    }
    
    private void ConfigureBackup(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Backup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Uuid).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.StartedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.HasIndex(e => e.ApplicationId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAt);
            
            entity.HasOne(e => e.Application)
                .WithMany(e => e.Backups)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
    
    private void ConfigureHealthCheck(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HealthCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CheckedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.ApplicationId);
            entity.HasIndex(e => e.ServerId);
            entity.HasIndex(e => e.CheckedAt);
            
            entity.HasOne(e => e.Application)
                .WithMany(e => e.HealthChecks)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Server)
                .WithMany()
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
    
    private void ConfigureVolume(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Volume>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Uuid).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.HasIndex(e => new { e.ServerId, e.Name }).IsUnique();
            
            entity.HasOne(e => e.Application)
                .WithMany(e => e.Volumes)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.HasOne(e => e.Server)
                .WithMany()
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
    
    private void ConfigureSystemSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // We'll use Id = 1 always (singleton)
            entity.Property(e => e.HostCraftDomain).HasMaxLength(255);
            entity.Property(e => e.HostCraftApiDomain).HasMaxLength(255);
            entity.Property(e => e.HostCraftLetsEncryptEmail).HasMaxLength(255);
            entity.Property(e => e.CertificateStatus).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }

    private void ConfigureGitProviderSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GitProviderSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Unique constraint on Type + ApiUrl (allows one config per provider type, or multiple for self-hosted)
            entity.HasIndex(e => new { e.Type, e.ApiUrl }).IsUnique();

            entity.Ignore(e => e.IsConfigured);
        });
    }
}
