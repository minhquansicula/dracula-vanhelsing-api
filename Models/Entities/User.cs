using System.ComponentModel.DataAnnotations;

namespace DraculaVanHelsing.Api.Models.Entities
{
    public class User
    {
        [Key]
        public Guid UserId { get; set; } = Guid.NewGuid();

        [Required, MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        public int TotalWins { get; set; } = 0;
        public int TotalLosses { get; set; } = 0;
        public int EloRating { get; set; } = 1000;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual ICollection<MatchParticipant> MatchParticipants { get; set; } = new List<MatchParticipant>();
    }
}