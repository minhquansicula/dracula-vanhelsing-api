using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DraculaVanHelsing.Api.Models.Entities
{
    public class MatchHistory
    {
        [Key]
        public Guid MatchId { get; set; } = Guid.NewGuid();

        [Required, MaxLength(10)]
        public string RoomCode { get; set; } = string.Empty;

        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }

        public Guid? WinnerId { get; set; }

        [ForeignKey("WinnerId")]
        public virtual User? Winner { get; set; }

        public string EndReason { get; set; } = string.Empty; // "Surrender", "Normal"

        public virtual ICollection<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();
    }
}