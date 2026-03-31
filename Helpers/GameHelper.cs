using DraculaVanHelsing.Api.Models.Enums;
using DraculaVanHelsing.Api.Models.GameState;

namespace DraculaVanHelsing.Api.Helpers
{
    public static class GameHelper
    {
        public static List<CardData> GenerateStandardDeck()
        {
            var deck = new List<CardData>();
            int idCounter = 1;

            foreach (CardColor color in Enum.GetValues(typeof(CardColor)))
            {
                for (int value = 1; value <= 8; value++)
                {
                    deck.Add(new CardData
                    {
                        CardId = idCounter++,
                        Color = color,
                        Value = value,
                        Skill = (SkillType)value // Giá trị bài từ 1-8 tương ứng với SkillType 1-8
                    });
                }
            }
            return deck;
        }

        public static List<int> Shuffle(List<int> source)
        {
            var random = new Random();
            return source.OrderBy(x => random.Next()).ToList();
        }

        public static List<CardColor> GenerateRandomColorRanking()
        {
            var colors = Enum.GetValues(typeof(CardColor)).Cast<CardColor>().ToList();
            var random = new Random();
            return colors.OrderBy(x => random.Next()).ToList();
        }

        public static string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6).Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}