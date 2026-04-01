using DraculaVanHelsing.Api.Models.Enums;

namespace DraculaVanHelsing.Api.Models.GameState
{
    public class GameRoomState
    {
        public Guid RoomId { get; set; }
        public string RoomCode { get; set; } = string.Empty;
        public RoomStatus Status { get; set; }
        public Guid? CurrentTurnUserId { get; set; }
        public int RoundNumber { get; set; } = 1;
        public Guid? WinnerId { get; set; }
        public string EndReason { get; set; } = string.Empty;

        // Dữ liệu bộ bài và bàn cờ
        public List<CardColor> ColorRanking { get; set; } = new List<CardColor>(); // [cite: 21, 52]
        public List<int> DrawPile { get; set; } = new List<int>(); // List CardId
        public List<int> DiscardPile { get; set; } = new List<int>(); // [cite: 70]
        public List<BoardZoneState> Zones { get; set; } = new List<BoardZoneState>();
        public List<PlayerInGame> Players { get; set; } = new List<PlayerInGame>();
    }

    public class PlayerInGame
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public FactionType? Faction { get; set; }
        public FactionType? RequestedFaction { get; set; } // Lưu lựa chọn Role lúc bắt đầu
        public int Health { get; set; } // [cite: 50, 102]
        public List<CardInHand> Hand { get; set; } = new List<CardInHand>(); // [cite: 73][Max 5 lá]
        public bool IsConnected { get; set; } = true;
        public CardInHand? DrawnCard { get; set; }
    }
    public class CardInHand
    {
        public int CardId { get; set; }
        public bool IsRevealed { get; set; } = false; // [cite: 74, 114]
    }

    public class BoardZoneState
    {
        public int ZoneIndex { get; set; } // 1-5
        public int VampireTokens { get; set; } = 0;
        public int HumanTokens { get; set; } = 0;
    }

    public class CardData
    {
        public int CardId { get; set; }
        public CardColor Color { get; set; }
        public int Value { get; set; }
        public SkillType Skill { get; set; }
    }
}