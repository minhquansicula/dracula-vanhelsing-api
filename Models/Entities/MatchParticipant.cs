using DraculaVanHelsing.Api.Models.Enums;

namespace DraculaVanHelsing.Api.Models.Entities
{
    public class MatchParticipant
    {
        public Guid MatchId { get; set; }
        public MatchHistory Match { get; set; } = null!;

        public Guid UserId { get; set; }
        public User User { get; set; } = null!;

        public FactionType Faction { get; set; }
        public MatchResult Result { get; set; }
    }
}