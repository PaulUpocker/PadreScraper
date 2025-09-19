using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ebala;
class TelegramBotService
{
    private CancellationToken _cancellationToken;
    private List<Chat> _subscribers = new();
    private TelegramBotClient _botClient;
    
    public TelegramBotService(CancellationToken botCancellationToken = default)
    {
        _cancellationToken = botCancellationToken;
        _botClient = new TelegramBotClient("8156615586:AAEStI6burBuPjJ_KMTwk1qtO1XHFbV64lc", cancellationToken: _cancellationToken);
        _botClient.OnMessage += OnMessage;
    }
    
    public async Task ProceedNewAlphaCall(string message)
    {
        foreach (var subscriber in _subscribers)
        {
            await _botClient.SendMessage(subscriber, message, cancellationToken: _cancellationToken);
        }
    }
    
    async Task OnMessage(Message msg, UpdateType type)
    {
        if (msg.Text is null) return;
        if (msg.Text == "/start")
            await _botClient.SendMessage(msg.Chat, "Здарова, хуесос", cancellationToken: _cancellationToken);
        _subscribers.Add(msg.Chat);
    }
}
