using DraculaVanHelsing.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DraculaVanHelsing.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // Định nghĩa các bảng trong Database
        public DbSet<User> Users { get; set; }
        public DbSet<MatchHistory> MatchHistories { get; set; }
        public DbSet<MatchParticipant> MatchParticipants { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. Cấu hình khóa chính hỗn hợp (Composite Key) cho MatchParticipant
            modelBuilder.Entity<MatchParticipant>()
                .HasKey(mp => new { mp.MatchId, mp.UserId });

            // 2. Cấu hình mối quan hệ giữa MatchHistory và MatchParticipant
            modelBuilder.Entity<MatchParticipant>()
                .HasOne(mp => mp.Match)
                .WithMany(m => m.Participants)
                .HasForeignKey(mp => mp.MatchId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa trận đấu thì xóa luôn danh sách tham gia

            // 3. Cấu hình mối quan hệ giữa User và MatchParticipant
            modelBuilder.Entity<MatchParticipant>()
                .HasOne(mp => mp.User)
                .WithMany(u => u.MatchParticipants)
                .HasForeignKey(mp => mp.UserId)
                .OnDelete(DeleteBehavior.Restrict); // Không xóa user nếu họ đã có lịch sử đấu

            // 4. Cấu hình mối quan hệ Winner (MatchHistory -> User)
            modelBuilder.Entity<MatchHistory>()
                .HasOne(m => m.Winner)
                .WithMany() // Một User có thể thắng nhiều trận, nhưng không cần list ngược lại ở đây nếu đã có MatchParticipants
                .HasForeignKey(m => m.WinnerId)
                .OnDelete(DeleteBehavior.SetNull); // Nếu User bị xóa, thông tin Winner trong lịch sử sẽ thành Null
        }
    }
}