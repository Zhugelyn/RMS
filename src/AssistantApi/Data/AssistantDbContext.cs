using Microsoft.EntityFrameworkCore;

namespace AssistantApi.Data;

public sealed class AssistantDbContext : DbContext
{
    public AssistantDbContext(DbContextOptions<AssistantDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserProfileEntity> UserProfiles => Set<UserProfileEntity>();
    public DbSet<HarnessEpisodeEntity> HarnessEpisodes => Set<HarnessEpisodeEntity>();
    public DbSet<ResearchSettingsEntity> ResearchSettings => Set<ResearchSettingsEntity>();
    public DbSet<ResearchSnapshotEntity> ResearchSnapshots => Set<ResearchSnapshotEntity>();
    public DbSet<ResearchPlanEntity> ResearchPlans => Set<ResearchPlanEntity>();
    public DbSet<ResearchScheduleRunEntity> ResearchScheduleRuns => Set<ResearchScheduleRunEntity>();
    public DbSet<FileObjectEntity> FileObjects => Set<FileObjectEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfileEntity>(e =>
        {
            e.ToTable("user_profiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Locale).HasMaxLength(32);
            e.Property(x => x.Timezone).HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<HarnessEpisodeEntity>(e =>
        {
            e.ToTable("harness_episodes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.Domain).HasMaxLength(64).IsRequired();
            e.Property(x => x.Task).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Result).HasMaxLength(2000).IsRequired();
            e.Property(x => x.ConversationId).HasMaxLength(128);
            e.Property(x => x.TraceId).HasMaxLength(128);
            e.Property(x => x.At).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Domain, x.At })
                .HasDatabaseName("ix_harness_episodes_user_domain_at");
        });

        modelBuilder.Entity<ResearchSettingsEntity>(e =>
        {
            e.ToTable("research_settings");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.InstagramHandle).HasMaxLength(128);
            e.Property(x => x.VkCommunitiesJson).HasMaxLength(4000);
            e.Property(x => x.Enabled).IsRequired();
            e.Property(x => x.CadenceDays).IsRequired().HasDefaultValue(14);
            e.Property(x => x.Timezone).HasMaxLength(64);
            e.Property(x => x.NotifyChatId).HasMaxLength(128);
            e.Property(x => x.LastError).HasMaxLength(512);
            e.Property(x => x.UpdatedAt).IsRequired();
            e.HasIndex(x => new { x.Enabled, x.NextRunAt })
                .HasDatabaseName("ix_research_settings_enabled_next_run");
        });

        modelBuilder.Entity<ResearchScheduleRunEntity>(e =>
        {
            e.ToTable("research_schedule_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.PeriodKey).HasMaxLength(32).IsRequired();
            e.Property(x => x.CompletedAt).IsRequired();
            e.HasIndex(x => new { x.UserId, x.PeriodKey })
                .IsUnique()
                .HasDatabaseName("ux_research_schedule_runs_user_period");
        });

        modelBuilder.Entity<ResearchSnapshotEntity>(e =>
        {
            e.ToTable("research_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.CapturedAt).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => new { x.UserId, x.CapturedAt })
                .HasDatabaseName("ix_research_snapshots_user_captured");
        });

        modelBuilder.Entity<ResearchPlanEntity>(e =>
        {
            e.ToTable("research_plans");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.WindowStart).IsRequired();
            e.Property(x => x.WindowEnd).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => new { x.UserId, x.CreatedAt })
                .HasDatabaseName("ix_research_plans_user_created");
        });

        modelBuilder.Entity<FileObjectEntity>(e =>
        {
            e.ToTable("file_objects");
            e.HasKey(x => x.FileId);
            e.Property(x => x.FileId).ValueGeneratedNever();
            e.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.Domain).HasMaxLength(32).IsRequired();
            e.Property(x => x.Bucket).HasMaxLength(128).IsRequired();
            e.Property(x => x.ObjectKey).HasMaxLength(128).IsRequired();
            e.Property(x => x.OriginalFilename).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
            e.Property(x => x.SizeBytes).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Domain, x.CreatedAt })
                .HasDatabaseName("ix_file_objects_user_domain_created");
            e.HasIndex(x => new { x.Bucket, x.ObjectKey })
                .IsUnique()
                .HasDatabaseName("ux_file_objects_bucket_object");
        });
    }
}
