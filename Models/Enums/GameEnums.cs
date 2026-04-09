namespace DraculaVanHelsing.Api.Models.Enums
{
    public enum FactionType
    {
        Dracula,
        VanHelsing
    }

    public enum CardColor
    {
        Red,
        Purple, // Luật game sử dụng Purple thay vì Blue [cite: 139]
        Green,
        Yellow
    }

    public enum SkillType
    {
        RevealOwnCard = 1,      // 1: Reveal one of your cards [cite: 114]
        RevealDeckTop = 2,      // 2: Reveal the top card of the deck [cite: 117]
        RevealOpponentCard = 3, // 3: Reveal one of your opponent's cards [cite: 119]
        SwapOwnCards = 4,       // 4: Swap two of your cards [cite: 121]
        ExtraTurn = 5,          // 5: Play another turn [cite: 124]
        SwapOpponentCard = 6,   // 6: Swap one of your cards with your opponent [cite: 126]
        SwapTrumpColor = 7,     // 7: Swap the Trump Color Token with another [cite: 130]
        EndRound = 8            // 8: Immediately end the round [cite: 132]
    }

    public enum RoomStatus
    {
        Waiting,
        Playing,
        Finished,
        CombatReview
    }

    public enum MatchResult
    {
        Win,
        Lose
    }
}