using Microsoft.EntityFrameworkCore;
using WebApi.Models;

namespace WebApi.Data;

public sealed class SmsManageDbContext : DbContext
{
    public SmsManageDbContext(DbContextOptions<SmsManageDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<DeviceComSnapshot> DeviceComSnapshots => Set<DeviceComSnapshot>();
    public DbSet<UserComAllocation> UserComAllocations => Set<UserComAllocation>();
    public DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();
    public DbSet<SmsSendRecord> SmsSendRecords => Set<SmsSendRecord>();
    public DbSet<CallHangupRecord> CallHangupRecords => Set<CallHangupRecord>();
    public DbSet<MessageReadReceipt> MessageReadReceipts => Set<MessageReadReceipt>();
    public DbSet<Note> Notes => Set<Note>();

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(x => x.UserName).IsUnique();
            entity.HasQueryFilter(x => !x.IsDelete);
        });

        modelBuilder.Entity<DeviceComSnapshot>(entity =>
        {
            // 设备唯一：一个设备只保留一份"覆盖式"的 COM 快照
            entity.HasIndex(x => x.DeviceId).IsUnique();
        });

        modelBuilder.Entity<UserComAllocation>(entity =>
        {
            entity.HasQueryFilter(x => !x.IsDelete);
        });

        modelBuilder.Entity<SmsMessage>(entity =>
        {
            entity.HasIndex(x => x.DeviceId);
            entity.HasIndex(x => x.ComPort);
            entity.HasIndex(x => x.SenderNumber);
            entity.HasIndex(x => x.ReceivedTime);
            entity.HasQueryFilter(x => !x.IsDelete);
        });

        modelBuilder.Entity<SmsSendRecord>(entity =>
        {
            entity.HasIndex(x => x.DeviceId);
            entity.HasIndex(x => x.ComPort);
            entity.HasIndex(x => x.TargetNumber);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CreateTime);
            entity.HasQueryFilter(x => !x.IsDelete);
        });

        modelBuilder.Entity<CallHangupRecord>(entity =>
        {
            entity.HasIndex(x => x.DeviceId);
            entity.HasIndex(x => x.ComPort);
            entity.HasIndex(x => x.CallerNumber);
            entity.HasIndex(x => x.HangupTime);
            entity.HasQueryFilter(x => !x.IsDelete);
        });

        modelBuilder.Entity<MessageReadReceipt>(entity =>
        {
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => new { x.UserId, x.MessageType, x.SourceId }).IsUnique();
            entity.HasIndex(x => x.ReadTimeUtc);
            entity.HasQueryFilter(x => !x.IsDelete);
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.CreateTime);
            entity.HasIndex(x => x.IsPinned);
            entity.HasQueryFilter(x => !x.IsDelete);
        });

        base.OnModelCreating(modelBuilder);
    }

    private void UpdateTimestamps()
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty)
                {
                    entry.Entity.Id = Guid.NewGuid();
                }

                entry.Entity.CreateTime = utcNow;
                entry.Entity.UpdateTime = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdateTime = utcNow;
            }
        }
    }
}
