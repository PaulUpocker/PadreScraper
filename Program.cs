using PadreScraper;
using PuppeteerSharp;
using PuppeteerSharp.Input;
using System.Text.Json;
using System.Xml.Linq;

namespace PadreScraperApp;

class Program
{
    // Константы для конфигурации
    private const string BaseUrl = "https://trade.padre.gg";
    private const string TargetUrl = "https://trade.padre.gg/trenches";
    private const string LoginSuccessSelector = "#button-top-bar-options-popover";
    private const string HeadlessSuccessSelector = "#button-solana-global-wallet-select";
    private const string DbName = "firebaseLocalStorageDb";

    public static async Task Main(string[] args)
    {
        Console.WriteLine("Инициализация PuppeteerSharp...");
        await new BrowserFetcher().DownloadAsync();

        // --- ЭТАП 1: ИЗВЛЕЧЕНИЕ СОСТОЯНИЯ ---
        BrowserState? capturedState = await CaptureBrowserStateAsync();

        if (capturedState == null)
        {
            Console.WriteLine("Не удалось захватить состояние браузера. Завершение.");
            return;
        }

        // --- ЭТАП 2: ВНЕДРЕНИЕ И СКРЕЙПИНГ ---
        List<AlphaTrackerCoin> trackerData = await ScrapeDataWithInjectedStateAsync(capturedState);

        // --- ВЫВОД РЕЗУЛЬТАТОВ ---
        Console.WriteLine("\n--- Извлеченные данные ---");
        if (trackerData.Any())
        {
            foreach (var coin in trackerData)
            {
                Console.WriteLine(coin.ToString());
            }
        }
        else
        {
            Console.WriteLine("Данные не найдены.");
        }
    }

    /// <summary>
    /// Этап 1: Запускает видимый браузер для аутентификации и захватывает состояние (Cookies, Storage, IndexedDB).
    /// </summary>
    private static async Task<BrowserState?> CaptureBrowserStateAsync()
    {
        Console.WriteLine("--- Этап 1: Аутентификация и извлечение состояния ---");

        await using var headfulBrowser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = false,
            DefaultViewport = null
        });

        await using var page = await headfulBrowser.NewPageAsync();
        await page.GoToAsync(TargetUrl);

        Console.WriteLine("Пожалуйста, войдите в аккаунт. Ожидание завершения...");

        try
        {
            await page.WaitForSelectorAsync(LoginSuccessSelector, new WaitForSelectorOptions { Timeout = 180000 });
            Console.WriteLine("Вход выполнен. Извлекаю состояние хранилищ...");
            await Task.Delay(2000); // Даем время на запись всех данных

            var capturedState = new BrowserState
            {
                // 1. Извлекаем Local Storage
                LocalStorageJson = await page.EvaluateExpressionAsync<string>("JSON.stringify(localStorage)"),

                // 2. Извлекаем Session Storage
                SessionStorageJson = await page.EvaluateExpressionAsync<string>("JSON.stringify(sessionStorage)"),

                // 3. Извлекаем IndexedDB
                IndexedDbJson = await IndexedDbHelper.GetAllIndexedDbDataAsync(page, DbName),

                // 4. Извлекаем Cookies
                Cookies = await page.GetCookiesAsync()
            };

            Console.WriteLine("Состояние всех хранилищ успешно скопировано.");
            return capturedState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка на этапе извлечения: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Этап 2: Запускает фоновый браузер, внедряет состояние и извлекает данные.
    /// </summary>
    private static async Task<List<AlphaTrackerCoin>> ScrapeDataWithInjectedStateAsync(BrowserState state)
    {
        Console.WriteLine("\n--- Этап 2: Запуск фонового браузера и внедрение состояния ---");
        var trackerData = new List<AlphaTrackerCoin>();

        await using var headlessBrowser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
        await using var page = await headlessBrowser.NewPageAsync();

        try
        {
            await InjectStateAsync(page, state);

            Console.WriteLine("Состояние восстановлено. Перезагружаю страницу для применения...");
            await page.ReloadAsync();

            await VerifyLoginAndClickButtonAsync(page);

            trackerData = await ExtractTrackerDataAsync(page);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Критическая ошибка в фоновом браузере: {ex.Message}");
        }

        return trackerData;
    }

    /// <summary>
    /// Внедряет сохраненное состояние (Cookies, Storage, IndexedDB) в страницу.
    /// </summary>
    private static async Task InjectStateAsync(IPage page, BrowserState state)
    {
        // Важно: сначала переходим на страницу, чтобы у домена был контекст для хранилищ
        await page.GoToAsync(BaseUrl);

        Console.WriteLine("Восстанавливаю состояние в фоновом браузере...");

        // 1. Внедряем куки
        if (state.Cookies?.Length > 0)
        {
            await page.SetCookieAsync(state.Cookies);
        }

        // 2. Внедряем Local & Session Storage
        await page.EvaluateFunctionAsync(@"(localStorageData, sessionStorageData) => {
            localStorage.clear();
            sessionStorage.clear();
            
            const localData = JSON.parse(localStorageData);
            for (const key in localData) {
                localStorage.setItem(key, localData[key]);
            }

            const sessionData = JSON.parse(sessionStorageData);
            for (const key in sessionData) {
                sessionStorage.setItem(key, sessionData[key]);
            }
        }", state.LocalStorageJson, state.SessionStorageJson);

        // 3. Внедряем IndexedDB
        if (state.IndexedDbJson.HasValue)
        {
            bool success = await IndexedDbHelper.SetAllIndexedDbDataAsync(page, DbName, state.IndexedDbJson.Value);
            if (!success)
            {
                Console.WriteLine("Произошла ошибка при восстановлении IndexedDB.");
            }
        }
    }

    /// <summary>
    /// Проверяет успешность входа после внедрения состояния и выполняет необходимые клики.
    /// Делает скриншоты при ошибках.
    /// </summary>
    private static async Task VerifyLoginAndClickButtonAsync(IPage page)
    {
        try
        {
            // Проверка входа
            await page.WaitForSelectorAsync(HeadlessSuccessSelector, new WaitForSelectorOptions { Timeout = 15000 });
            Console.WriteLine("Успешный вход в фоновом режиме подтвержден.");
            await Task.Delay(2000);
            await page.ScreenshotAsync("headless_success_page.png");

            // Клик по кнопке (например, переключение фильтра)
            await ClickButtonIfExistsAsync(page, ".css-ognuvg", "Кнопка фильтра");
        }
        catch (Exception)
        {
            Console.WriteLine("Селектор входа не найден. Сохраняю текущее состояние страницы для анализа...");
            await page.ScreenshotAsync("headless_error_page.png");
            await File.WriteAllTextAsync("headless_error_page.html", await page.GetContentAsync());
            Console.WriteLine("Скриншот 'headless_error_page.png' и HTML 'headless_error_page.html' сохранены.");
            throw; // Пробрасываем исключение, так как без входа продолжать нельзя
        }
    }

    /// <summary>
    /// Вспомогательный метод для безопасного клика по элементу, если он существует.
    /// </summary>
    private static async Task ClickButtonIfExistsAsync(IPage page, string selector, string buttonName)
    {
        try
        {
            // ClickAsync сам дождется появления элемента, но можно добавить Wait для надежности
            await page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Timeout = 5000 });
            await page.ClickAsync(selector, new ClickOptions { Delay = 50 });
            Console.WriteLine($"{buttonName} ({selector}) успешно нажата.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Не удалось нажать на кнопку ({selector}): {ex.Message}");
            // Не пробрасываем, если клик некритичен
        }
    }


    /// <summary>
    /// Извлекает данные трекера (список монет) со страницы.
    /// </summary>
    private static async Task<List<AlphaTrackerCoin>> ExtractTrackerDataAsync(IPage page)
    {
        const string componentSelector = ".css-1j135c3 div[role='gridcell']";
        var trackerData = new List<AlphaTrackerCoin>();

        try
        {
            // Ждем появления контейнера с данными
            await page.WaitForSelectorAsync(componentSelector, new WaitForSelectorOptions { Timeout = 10000 });

            // Находим все родительские div-элементы
            var components = await page.QuerySelectorAllAsync(componentSelector);
            Console.WriteLine($"Найдено {components.Length} записей. Начинаю извлечение...");

            foreach (var component in components)
            {
                // Извлекаем данные для каждого элемента с помощью JS Evaluate
                var jsonCoinData = await component.EvaluateFunctionAsync<JsonElement?>(@"
                    (el) => {
                        const nameElement = el.querySelector('h2.css-1wz1i5j');
                        const name = nameElement ? nameElement.innerText.replace('•', '').trim() : null;

                        const mentionedInElement = el.querySelector('.css-1r9kwv0 > span.css-e9h5tp');
                        const mentionedInGroup = mentionedInElement ? mentionedInElement.innerText : null;

                        const timeAgoElement = el.querySelector('span.css-1wutgjf');
                        const timeAgo = timeAgoElement ? timeAgoElement.innerText : null;
                        
                        let link = null;
                        const imgElement = el.querySelector('.MuiAvatar-img');
                        if (imgElement && imgElement.src) {
                            // Пример URL: .../SOLANA-A69a5...
                            const srcParts = imgElement.src.split('SOLANA-');
                            if (srcParts.length > 1) {
                                const potentialToken = srcParts[1];
                                // ИСПРАВЛЕНИЕ: Это корректное регулярное выражение для поиска токена в начале строки.
                                const tokenAddressMatch = potentialToken.match(/^[a-zA-Z0-9]+/);

                                if (tokenAddressMatch && tokenAddressMatch[0]) {
                                    link = `https://trade.padre.gg/trade/solana/${tokenAddressMatch[0]}`;
                                }
                            }
                        }

                        return {
                            Name: name, 
                            MentionedInGroup: mentionedInGroup,
                            TimeAgo: timeAgo,
                            Link: link
                        };
                    }");

                if (jsonCoinData.HasValue)
                {
                    try
                    {
                        // Десериализуем, обрабатывая возможные null значения в JSON
                        var coin = jsonCoinData.Value.Deserialize<AlphaTrackerCoin>(new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (coin != null)
                        {
                            trackerData.Add(coin);
                        }
                    }
                    catch (Exception deserializeEx)
                    {
                        Console.WriteLine($"Ошибка десериализации элемента: {deserializeEx.Message}. JSON: {jsonCoinData}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Произошла ошибка при извлечении данных: {ex.Message}");
        }

        return trackerData;
    }
}