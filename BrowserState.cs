using PuppeteerSharp;
using System.Text.Json;

public class BrowserState
{
    // Данные из Local Storage в виде JSON-строки
    public string? LocalStorageJson { get; set; }

    // Данные из Session Storage в виде JSON-строки
    public string? SessionStorageJson { get; set; }

    // Данные из IndexedDB, также сериализованные в JSON
    public JsonElement? IndexedDbJson { get; set; }
    
    // Куки на всякий случай, они тоже важны
    public CookieParam[]? Cookies { get; set; }
}