using DraculaVanHelsing.Api.Helpers;
using DraculaVanHelsing.Api.Models.Enums;
using DraculaVanHelsing.Api.Models.GameState;
using DraculaVanHelsing.Api.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using DraculaVanhelsing.Api.Hubs;
using System.Collections.Concurrent;
using System.Threading;

namespace DraculaVanHelsing.Api.Services
{
    public class GameEngineService : IGameEngineService
    {
        private readonly IGameStateService _gameStateService;
        private readonly IHubContext<GameHub> _hubContext;
        private static readonly ConcurrentDictionary<string, Timer> _reviewTimers = new();
        private static readonly ConcurrentDictionary<string, Timer> _turnTimers = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _roomLocks = new();


        public GameEngineService(IGameStateService gameStateService, IHubContext<GameHub> hubContext)
        {
            _gameStateService = gameStateService;
            _hubContext = hubContext;
        }

        public async Task<GameRoomState> CreateRoomAsync(Guid userId, string connectionId)
        {
            var roomCode = GameHelper.GenerateRoomCode();

            var state = new GameRoomState
            {
                RoomId = Guid.NewGuid(),
                RoomCode = roomCode,
                Status = RoomStatus.Waiting,
                Players = new List<PlayerInGame>
                {
                    new PlayerInGame
                    {
                        UserId = userId,
                        ConnectionId = connectionId,
                        Health = 0
                    }
                }
            };

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            await _gameStateService.SetUserRoomAsync(userId, roomCode);
            return state;
        }

        public async Task<GameRoomState?> JoinRoomAsync(Guid userId, string connectionId, string roomCode)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Waiting || state.Players.Count >= 2)
            {
                return null;
            }

            state.Players.Add(new PlayerInGame
            {
                UserId = userId,
                ConnectionId = connectionId,
                Health = 0
            });

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            await _gameStateService.SetUserRoomAsync(userId, roomCode);
            return state;
        }

        public async Task<GameRoomState?> SelectRoleAsync(Guid userId, string roomCode, FactionType requestedFaction)
        {
            // 1. ÁP DỤNG KHÓA (LOCK) ĐỂ CHỐNG SPAM CLICK / RACE CONDITION
            var semaphore = _roomLocks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                var state = await _gameStateService.GetGameStateAsync(roomCode);

                // Bỏ qua nếu phòng không tồn tại hoặc không ở trạng thái Chờ
                if (state == null || state.Status != RoomStatus.Waiting) return null;

                var player = state.Players.FirstOrDefault(p => p.UserId == userId);
                if (player == null) return null;

                // BỔ SUNG: Nếu người chơi này đã chọn phe rồi thì chặn không cho chọn lại (tránh spam API đổi phe)
                if (player.RequestedFaction.HasValue) return state;

                // Ghi nhận lựa chọn của người chơi
                player.RequestedFaction = requestedFaction;

                // Nếu CẢ 2 người chơi đã chọn phe xong -> Bắt đầu chia bài
                if (state.Players.Count == 2 && state.Players.All(p => p.RequestedFaction.HasValue))
                {
                    var player1 = state.Players[0];
                    var player2 = state.Players[1];

                    // Nếu 2 người chọn 2 phe khác nhau -> Chiều theo ý họ
                    if (player1.RequestedFaction != player2.RequestedFaction)
                    {
                        player1.Faction = player1.RequestedFaction;
                        player2.Faction = player2.RequestedFaction;
                    }
                    else // Nếu 2 người tranh nhau 1 phe -> Quay random 50/50
                    {
                        var random = new Random();
                        bool player1KeepsRole = random.Next(2) == 0;

                        player1.Faction = player1KeepsRole ? player1.RequestedFaction : GetOppositeFaction(player1.RequestedFaction!.Value);
                        player2.Faction = player1KeepsRole ? GetOppositeFaction(player2.RequestedFaction!.Value) : player2.RequestedFaction;
                    }

                    // Khởi tạo bàn cờ, chia bài, gán CurrentTurnUserId = Dracula
                    InitializeGame(state);

                    state.Status = RoomStatus.Playing;
                    await _gameStateService.SaveGameStateAsync(roomCode, state);

                    // 2. KÍCH HOẠT TIMER CHỐNG AFK CHO NGƯỜI ĐI ĐẦU TIÊN (DRACULA)
                    StartTurnTimeout(roomCode, state.CurrentTurnUserId!.Value);
                }
                else
                {
                    // Nếu mới có 1 người chọn, chỉ lưu State lại và chờ người kia
                    await _gameStateService.SaveGameStateAsync(roomCode, state);
                }

                return state;
            }
            finally
            {
                // 3. XỬ LÝ XONG THÌ MỞ KHÓA CHO REQUEST TIẾP THEO ĐI VÀO
                semaphore.Release();
            }
        }

        private void InitializeGame(GameRoomState state)
        {
            state.Zones = new List<BoardZoneState>();
            for (int i = 1; i <= 5; i++)
            {
                state.Zones.Add(new BoardZoneState
                {
                    ZoneIndex = i,
                    HumanTokens = 4,
                    VampireTokens = 0
                });
            }

            state.ColorRanking = GameHelper.GenerateRandomColorRanking();

            var allCards = GameHelper.GenerateStandardDeck();
            var deckIds = allCards.Select(c => c.CardId).ToList();
            state.DrawPile = GameHelper.Shuffle(deckIds);
            state.DiscardPile = new List<int>();

            foreach (var p in state.Players)
            {
                p.Health = p.Faction == FactionType.Dracula ? 12 : 0;
                p.Hand = new List<CardInHand>();

                for (int i = 0; i < 5; i++)
                {
                    var cardId = state.DrawPile[0];
                    state.DrawPile.RemoveAt(0);

                    p.Hand.Add(new CardInHand { CardId = cardId, IsRevealed = false });
                }
            }

            var draculaPlayer = state.Players.First(p => p.Faction == FactionType.Dracula);
            state.CurrentTurnUserId = draculaPlayer.UserId;
            state.RoundNumber = 1;
        }

        public async Task<GameRoomState?> DrawCardAsync(Guid userId, string roomCode)
        {
            var semaphore = _roomLocks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(); // KHÓA CỬA PHÒNG
            try
            {
                var state = await _gameStateService.GetGameStateAsync(roomCode);

                if (state == null || state.Status != RoomStatus.Playing) return null;
                if (state.CurrentTurnUserId != userId) return null;
                if (state.PendingSkillValue != null) return null;

                var player = state.Players.FirstOrDefault(p => p.UserId == userId);
                // Dù client bấm 2 lần nhanh cỡ nào, vào đây check thấy != null là văng ra ngay
                if (player == null || player.DrawnCard != null) return null;

                if (state.DrawPile.Count == 0)
                {
                    return await CheckAndResolveRoundAsync(roomCode, state);
                }

                var cardId = state.DrawPile[0];
                state.DrawPile.RemoveAt(0);

                player.DrawnCard = new CardInHand { CardId = cardId, IsRevealed = false };
                state.IsTopDeckCardRevealed = false;

                await _gameStateService.SaveGameStateAsync(roomCode, state);
                return state;
            }
            finally
            {
                semaphore.Release(); // XỬ LÝ XONG MỞ KHÓA CHO REQUEST TIẾP THEO
            }
        }

        public async Task<GameRoomState?> PlayCardAsync(Guid userId, string roomCode, int discardedCardId)
        {
            // 1. KHÓA CHỐNG SPAM CLICK
            var semaphore = _roomLocks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                var state = await _gameStateService.GetGameStateAsync(roomCode);

                if (state == null || state.Status != RoomStatus.Playing) return null;
                if (state.CurrentTurnUserId != userId) return null;
                if (state.PendingSkillValue != null) return null;

                var player = state.Players.FirstOrDefault(p => p.UserId == userId);
                if (player == null || player.DrawnCard == null) return null;

                int playedCardValue = ((discardedCardId - 1) % 8) + 1;

                // Lá 8 yêu cầu phải có ít nhất 6 lá trong Mộ bài
                if (playedCardValue == 8 && state.DiscardPile.Count < 5)
                {
                    return null;
                }

                CardInHand? cardToDiscard = null;
                bool isDiscardingDrawnCard = player.DrawnCard.CardId == discardedCardId;

                if (isDiscardingDrawnCard)
                {
                    cardToDiscard = player.DrawnCard;
                    player.DrawnCard = null;
                }
                else
                {
                    var cardInHand = player.Hand.FirstOrDefault(c => c.CardId == discardedCardId);
                    if (cardInHand == null) return null;

                    cardToDiscard = cardInHand;
                    int index = player.Hand.IndexOf(cardInHand);

                    player.Hand[index] = player.DrawnCard;
                    player.DrawnCard = null;
                }

                state.DiscardPile.Add(cardToDiscard.CardId);

                bool requiresTarget = playedCardValue == 1 || playedCardValue == 3 || playedCardValue == 4 || playedCardValue == 6 || playedCardValue == 7;

                if (requiresTarget)
                {
                    state.PendingSkillValue = playedCardValue;
                    // Lưu ý: Lúc này CHƯA chuyển lượt, nên ta không gọi StartTurnTimeout ở đây.
                    // Để cho đồng hồ 60s của người chơi hiện tại tiếp tục chạy để họ chọn mục tiêu.
                }
                else
                {
                    ExecuteAutoSkill(state, playedCardValue);

                    if (playedCardValue != 5 && playedCardValue != 8)
                    {
                        var opponent = state.Players.First(p => p.UserId != userId);
                        state.CurrentTurnUserId = opponent.UserId; // Chuyển lượt

                        // 2. BẬT ĐỒNG HỒ ĐẾM NGƯỢC CHO ĐỐI THỦ
                        StartTurnTimeout(roomCode, state.CurrentTurnUserId.Value);
                    }
                    else if (playedCardValue == 5)
                    {
                        // LÁ 5 (THÊM LƯỢT): Vẫn là lượt của mình, nhưng được reset lại đồng hồ 60s
                        StartTurnTimeout(roomCode, state.CurrentTurnUserId.Value);
                    }
                }

                return await CheckAndResolveRoundAsync(roomCode, state);
            }
            finally
            {
                // 3. MỞ KHÓA KHI ĐÃ XỬ LÝ XONG
                semaphore.Release();
            }
        }

        private void ExecuteAutoSkill(GameRoomState state, int skillValue)
        {
            switch (skillValue)
            {
                case 2:
                    state.IsTopDeckCardRevealed = true;
                    break;
                case 8:
                    state.ForceEndRound = true;
                    break;
            }
        }

        public async Task<GameRoomState?> SubmitSkillActionAsync(Guid userId, string roomCode, SkillPayload payload)
        {
            // 1. KHÓA CHỐNG SPAM CLICK
            var semaphore = _roomLocks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                var state = await _gameStateService.GetGameStateAsync(roomCode);

                if (state == null || state.Status != RoomStatus.Playing) return null;
                if (state.CurrentTurnUserId != userId || state.PendingSkillValue == null) return null;

                int skillValue = state.PendingSkillValue.Value;
                var currentPlayer = state.Players.First(p => p.UserId == userId);
                var opponent = state.Players.First(p => p.UserId != userId);

                switch (skillValue)
                {
                    case 1:
                        if (payload.TargetCardId.HasValue)
                        {
                            var myCard = currentPlayer.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                            if (myCard != null && !myCard.IsRevealed) myCard.IsRevealed = true;
                            else return null;
                        }
                        break;

                    case 3:
                        if (payload.TargetCardId.HasValue)
                        {
                            var targetCard = opponent.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                            if (targetCard != null && !targetCard.IsRevealed) targetCard.IsRevealed = true;
                            else return null;
                        }
                        break;

                    case 4:
                        if (payload.TargetCardId.HasValue && payload.TargetCardId2.HasValue)
                        {
                            var card1 = currentPlayer.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                            var card2 = currentPlayer.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId2.Value);

                            if (card1 != null && card2 != null)
                            {
                                int idx1 = currentPlayer.Hand.IndexOf(card1);
                                int idx2 = currentPlayer.Hand.IndexOf(card2);
                                currentPlayer.Hand[idx1] = card2;
                                currentPlayer.Hand[idx2] = card1;
                            }
                        }
                        break;

                    case 6:
                        if (payload.TargetCardId.HasValue)
                        {
                            var opponentCard = opponent.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                            if (opponentCard != null)
                            {
                                int districtIndex = opponent.Hand.IndexOf(opponentCard);
                                var myCard = currentPlayer.Hand[districtIndex];
                                opponent.Hand[districtIndex] = myCard;
                                currentPlayer.Hand[districtIndex] = opponentCard;
                            }
                        }
                        break;

                    case 7:
                        if (payload.TargetColor1.HasValue && payload.TargetColor2.HasValue)
                        {
                            var idx1 = state.ColorRanking.IndexOf(payload.TargetColor1.Value);
                            var idx2 = state.ColorRanking.IndexOf(payload.TargetColor2.Value);

                            if (idx1 != -1 && idx2 != -1 && (idx1 == 0 || idx2 == 0))
                            {
                                state.ColorRanking[idx1] = payload.TargetColor2.Value;
                                state.ColorRanking[idx2] = payload.TargetColor1.Value;
                            }
                            else return null;
                        }
                        break;
                }

                // Sau khi dùng Skill xong, giải phóng trạng thái kẹt và CHUYỂN LƯỢT
                state.PendingSkillValue = null;
                state.CurrentTurnUserId = opponent.UserId;

                // 2. BẬT ĐỒNG HỒ ĐẾM NGƯỢC CHO ĐỐI THỦ
                StartTurnTimeout(roomCode, state.CurrentTurnUserId.Value);

                return await CheckAndResolveRoundAsync(roomCode, state);
            }
            finally
            {
                // 3. MỞ KHÓA
                semaphore.Release();
            }
        }

        public async Task<GameRoomState?> CallEndRoundAsync(Guid userId, string roomCode)
        {
            var semaphore = _roomLocks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                var state = await _gameStateService.GetGameStateAsync(roomCode);

                if (state == null || state.Status != RoomStatus.Playing) return null;
                if (state.CurrentTurnUserId != userId || state.PendingSkillValue != null) return null;

                // THÊM DÒNG NÀY: Nếu vòng đấu đã được gọi kết thúc trước đó rồi thì chặn lại
                if (state.IsLastTurn) return null;

                if (state.DiscardPile.Count < 6) return null;

                state.IsLastTurn = true;
                state.CalledEndRoundUserId = userId;

                var opponent = state.Players.First(p => p.UserId != userId);
                state.CurrentTurnUserId = opponent.UserId; // Chuyển lượt cho đối thủ đi nước cuối

                await _gameStateService.SaveGameStateAsync(roomCode, state);

                StartTurnTimeout(roomCode, state.CurrentTurnUserId.Value);

                return state;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<GameRoomState> CheckAndResolveRoundAsync(string roomCode, GameRoomState state)
        {
            bool isDeckEmpty = state.DrawPile.Count == 0 && state.Players.All(p => p.DrawnCard == null);
            bool isLastTurnFinished = state.IsLastTurn && state.CurrentTurnUserId == state.CalledEndRoundUserId;

            if (isDeckEmpty || state.ForceEndRound || isLastTurnFinished)
            {
                return await ResolveRoundAsync(roomCode, state);
            }

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            return state;
        }

        private async Task<GameRoomState> ResolveRoundAsync(string roomCode, GameRoomState state)
        {
            ClearTurnTimeout(roomCode);

            // LOGIC MỚI: Không gọi EndGame ở đây nữa, và CHƯA trừ máu hay token.
            // Đưa phòng vào trạng thái chờ xác nhận (CombatReview) ngay lập tức.
            // (Lưu ý: Client tự biết so 2 lá bài dựa trên ColorRanking và Value nên ta để Client tự chạy Animation diễn giải).

            state.Status = RoomStatus.CombatReview;
            foreach (var p in state.Players)
            {
                p.IsReadyForNextRound = false;
            }

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            StartReviewTimeout(roomCode);

            return state;
        }

        private void StartTurnTimeout(string roomCode, Guid currentUserId)
        {
            ClearTurnTimeout(roomCode); // Xóa timer cũ nếu có

            // Set 62 giây (Cho Frontend delay 2 giây)
            var timer = new Timer(async _ =>
            {
                // Vì chạy ngoài luồng request, cần lock lại để an toàn
                var semaphore = _roomLocks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));
                await semaphore.WaitAsync();
                try
                {
                    var state = await _gameStateService.GetGameStateAsync(roomCode);

                    // Nếu game đang Play và ông này giữ lượt quá 60s
                    if (state != null && state.Status == RoomStatus.Playing && state.CurrentTurnUserId == currentUserId)
                    {
                        Console.WriteLine($"[AFK Auto-Surrender] User {currentUserId} AFK phòng {roomCode}.");

                        var winner = state.Players.FirstOrDefault(p => p.UserId != currentUserId);
                        if (winner != null)
                        {
                            var finalState = await EndGameAsync(state, winner.UserId, "Đối thủ đã bỏ chạy do quá thời gian (AFK).");
                            await _hubContext.Clients.Group(roomCode).SendAsync("GameEnded", finalState);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                    ClearTurnTimeout(roomCode);
                }
            }, null, 62000, Timeout.Infinite);

            _turnTimers.TryAdd(roomCode, timer);
        }

        private void ClearTurnTimeout(string roomCode)
        {
            if (_turnTimers.TryRemove(roomCode, out var timer))
            {
                timer.Dispose();
            }
        }

        public async Task<GameRoomState?> ReadyForNextRoundAsync(Guid userId, string roomCode)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.CombatReview) return null;

            var player = state.Players.FirstOrDefault(p => p.UserId == userId);
            if (player != null)
            {
                player.IsReadyForNextRound = true;
            }

            // Kiểm tra xem cả 2 đã sẵn sàng chưa
            if (state.Players.All(p => p.IsReadyForNextRound))
            {
                ClearReviewTimeout(roomCode);
                return await ProceedToNextRoundAsync(roomCode, state);
            }

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            return state;
        }

        private async Task<GameRoomState> ProceedToNextRoundAsync(string roomCode, GameRoomState state)
        {
            // BÂY GIỜ MỚI THỰC SỰ TÍNH ĐIỂM VÀ ÁP DỤNG VÀO STATE
            var allCards = GameHelper.GenerateStandardDeck();
            var dracula = state.Players.First(p => p.Faction == FactionType.Dracula);
            var vanHelsing = state.Players.First(p => p.Faction == FactionType.VanHelsing);

            for (int i = 0; i < 5; i++)
            {
                var draculaCard = allCards.First(c => c.CardId == dracula.Hand[i].CardId);
                var vhCard = allCards.First(c => c.CardId == vanHelsing.Hand[i].CardId);

                int winner = CompareCards(draculaCard, vhCard, state.ColorRanking);
                var zone = state.Zones[i];

                if (winner > 0)
                {
                    if (zone.HumanTokens > 0)
                    {
                        zone.HumanTokens--;
                        zone.VampireTokens++;
                    }

                    if (zone.VampireTokens >= 4)
                    {
                        return await EndGameAsync(state, dracula.UserId, "Dracula turned 4 humans in a district.");
                    }
                }
                else
                {
                    dracula.Health--;

                    if (dracula.Health <= 0)
                    {
                        return await EndGameAsync(state, vanHelsing.UserId, "Van Helsing defeated Dracula.");
                    }
                }
            }

            // Nếu round này là round cuối và Dracula còn sống
            if (state.RoundNumber >= 5)
            {
                return await EndGameAsync(state, dracula.UserId, "Dracula survived 5 rounds.");
            }

            // CHUYỂN VÒNG SAU KHI ĐÃ CẬP NHẬT XONG MÁU/TOKEN
            state.RoundNumber++;
            state.Status = RoomStatus.Playing;
            SetupNextRound(state);
            await _gameStateService.SaveGameStateAsync(roomCode, state);

            StartTurnTimeout(roomCode, state.CurrentTurnUserId.Value);

            return state;
        }

        private void StartReviewTimeout(string roomCode)
        {
            ClearReviewTimeout(roomCode);

            var timer = new Timer(async _ =>
            {
                var state = await _gameStateService.GetGameStateAsync(roomCode);
                if (state != null && state.Status == RoomStatus.CombatReview)
                {
                    // Hết giờ -> Ép cả 2 người chơi Ready
                    foreach (var p in state.Players) p.IsReadyForNextRound = true;
                    var newState = await ProceedToNextRoundAsync(roomCode, state);

                    // Push thẳng State mới xuống Client vì Timer chạy ngoài luồng Request
                    await _hubContext.Clients.Group(roomCode).SendAsync("GameStateUpdated", newState);
                }
                ClearReviewTimeout(roomCode);
            }, null, 25000, Timeout.Infinite); // 25 giây

            _reviewTimers.TryAdd(roomCode, timer);
        }

        private void ClearReviewTimeout(string roomCode)
        {
            if (_reviewTimers.TryRemove(roomCode, out var timer))
            {
                timer.Dispose();
            }
        }

        private int CompareCards(CardData c1, CardData c2, List<CardColor> ranking)
        {
            CardColor trumpColor = ranking[0];
            bool c1IsTrump = c1.Color == trumpColor;
            bool c2IsTrump = c2.Color == trumpColor;

            if (c1IsTrump && !c2IsTrump) return 1;
            if (!c1IsTrump && c2IsTrump) return -1;

            if (c1.Value > c2.Value) return 1;
            if (c1.Value < c2.Value) return -1;

            int c1Rank = ranking.IndexOf(c1.Color);
            int c2Rank = ranking.IndexOf(c2.Color);
            return c1Rank < c2Rank ? 1 : -1;
        }

        private void SetupNextRound(GameRoomState state)
        {
            state.IsLastTurn = false;
            state.CalledEndRoundUserId = null;
            state.ForceEndRound = false;
            state.PendingSkillValue = null;
            state.IsTopDeckCardRevealed = false;

            var allCards = GameHelper.GenerateStandardDeck();
            var deckIds = allCards.Select(c => c.CardId).ToList();
            state.DrawPile = GameHelper.Shuffle(deckIds);
            state.DiscardPile = new List<int>();

            foreach (var p in state.Players)
            {
                p.DrawnCard = null;
                p.Hand.Clear();

                p.IsReadyForNextRound = false;

                for (int i = 0; i < 5; i++)
                {
                    var cardId = state.DrawPile[0];
                    state.DrawPile.RemoveAt(0);
                    p.Hand.Add(new CardInHand { CardId = cardId, IsRevealed = false });
                }
            }

            var draculaPlayer = state.Players.First(p => p.Faction == FactionType.Dracula);
            state.CurrentTurnUserId = draculaPlayer.UserId;
        }

        public async Task LeaveRoomAsync(Guid userId)
        {
            var roomCode = await _gameStateService.GetUserRoomAsync(userId);
            if (string.IsNullOrEmpty(roomCode)) return;

            var state = await _gameStateService.GetGameStateAsync(roomCode);
            if (state != null)
            {
                var player = state.Players.FirstOrDefault(p => p.UserId == userId);
                if (player != null)
                {
                    state.Players.Remove(player);
                    if (state.Players.Count == 0)
                    {
                        await _gameStateService.DeleteGameStateAsync(roomCode);
                    }
                    else
                    {
                        await _gameStateService.SaveGameStateAsync(roomCode, state);
                    }
                }
            }
            await _gameStateService.RemoveUserRoomAsync(userId);
        }

        private async Task<GameRoomState> EndGameAsync(GameRoomState state, Guid winnerId, string reason)
        {
            state.Status = RoomStatus.Finished;
            state.WinnerId = winnerId;
            state.EndReason = reason;

            await _gameStateService.SaveGameStateAsync(state.RoomCode, state);

            foreach (var player in state.Players)
            {
                await _gameStateService.RemoveUserRoomAsync(player.UserId);
            }

            ClearTurnTimeout(state.RoomCode);
            ClearReviewTimeout(state.RoomCode);

            if (_roomLocks.TryRemove(state.RoomCode, out var sem))
            {
                sem.Dispose();
            }

            return state;
        }

        public async Task<GameRoomState?> SurrenderAsync(Guid userId, string roomCode)
        {
            // 1. ÁP DỤNG KHÓA CHỐNG SPAM
            var semaphore = _roomLocks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                var state = await _gameStateService.GetGameStateAsync(roomCode);

                if (state == null || state.Status != RoomStatus.Playing) return null;

                var loser = state.Players.FirstOrDefault(p => p.UserId == userId);
                var winner = state.Players.FirstOrDefault(p => p.UserId != userId);

                if (loser == null || winner == null) return null;

                return await EndGameAsync(state, winner.UserId, "Surrender");
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<GameRoomState?> HandleDisconnectAsync(Guid userId, string connectionId)
        {
            var roomCode = await _gameStateService.GetUserRoomAsync(userId);
            if (string.IsNullOrEmpty(roomCode)) return null;

            var state = await _gameStateService.GetGameStateAsync(roomCode);
            if (state == null) return null;

            if (state.Status == RoomStatus.Waiting)
            {
                await LeaveRoomAsync(userId);
                return state;
            }

            return state;
        }

        private FactionType GetOppositeFaction(FactionType faction)
        {
            return faction == FactionType.Dracula ? FactionType.VanHelsing : FactionType.Dracula;
        }
    }
}