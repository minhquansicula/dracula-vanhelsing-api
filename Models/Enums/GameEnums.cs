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
        Blue,
        Green,
        Yellow
    }

    public enum SkillType
    {
        None,
        Reveal, // Xem bài đối thủ
        Swap,   // Đổi bài
        Discard, // Bỏ bài
        Move     // Di chuyển token
    }

    public enum RoomStatus
    {
        Waiting,
        Playing,
        Finished
    }

    public enum MatchResult
    {
        Win,
        Lose,
        Draw
    }
}