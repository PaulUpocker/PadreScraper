using PadreScraper;
using PuppeteerSharp;
using PuppeteerSharp.Input;
using System.Text.Json;

namespace PadreScraperApp;

class Program
{
    // Константы для конфигурации
    private const string BaseUrl = "https://trade.padre.gg"; // Явный URL для перехода
    private const string LoginSuccessSelector = "#button-top-bar-options-popover";
    private const string HeadlessSuccessSelector = "#button-solana-global-wallet-select";
    private const string DbName = "firebaseLocalStorageDb";
    private const int CheckIntervalMilliseconds = 5000; // 5 секунд

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

        // --- ЭТАП 2: ЗАПУСК ПОСТОЯННОГО МОНИТОРИНГА ---
        await RunRealTimeMonitoringAsync(capturedState);
    }

    // ===================================================================================
    // МОНИТОРИНГ В РЕАЛЬНОМ ВРЕМЕНИ (НОВАЯ ЛОГИКА)
    // ===================================================================================

    /// <summary>
    /// Запускает фоновый браузер, внедряет состояние и входит в бесконечный цикл для мониторинга новых записей БЕЗ перезагрузки страницы.
    /// </summary>
    private static async Task RunRealTimeMonitoringAsync(BrowserState state)
    {
        Console.WriteLine("\n--- Этап 2: Запуск мониторинга в реальном времени ---");

        // HashSet для эффективного отслеживания уникальных ссылок на монеты, которые мы уже видели.
        var seenLinks = new HashSet<string>();
        bool isFirstRun = true;

        // Запускаем браузер ОДИН РАЗ за пределами цикла
        await using var headlessBrowser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
        await using var page = await headlessBrowser.NewPageAsync();

        try
        {
            // Внедряем состояние и проверяем логин ОДИН РАЗ
            await InjectStateAsync(page, state);

            // ВАЖНО: Переходим на нужную страницу после инъекции состояния
            Console.WriteLine($"Перехожу на целевую страницу: {BaseUrl}");
            await page.GoToAsync(BaseUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });

            await VerifyLoginAndClickButtonAsync(page);

            // Бесконечный цикл мониторинга
            while (true)
            {
                if (!isFirstRun) // Не выводим сообщение при самом первом запуске
                {
                    Console.WriteLine($"\n({DateTime.Now:HH:mm:ss}) Проверяю DOM на наличие новых записей...");
                }

                var currentData = await ExtractTrackerDataAsync(page);
                var newCoins = new List<AlphaTrackerCoin>();

                foreach (var coin in currentData)
                {
                    // Ссылка - наш уникальный идентификатор. Проверяем, что она есть.
                    if (!string.IsNullOrEmpty(coin.Link))
                    {
                        // Метод Add у HashSet возвращает true, если элемент был успешно добавлен 
                        // (т.е. его раньше не было в коллекции).
                        if (seenLinks.Add(coin.Link))
                        {
                            newCoins.Add(coin);
                        }
                    }
                }

                // Логика вывода в консоль
                if (isFirstRun && newCoins.Any())
                {
                    Console.WriteLine($"--- Первоначальный запуск: Найдено {newCoins.Count} записей ---");
                    foreach (var coin in newCoins)
                    {
                        Console.WriteLine(coin.ToString());
                    }
                    isFirstRun = false;
                }
                else if (newCoins.Any())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"--- ОБНАРУЖЕНЫ НОВЫЕ ЗАПИСИ ({newCoins.Count}) ---");
                    foreach (var coin in newCoins)
                    {
                        Console.WriteLine(coin.ToString());
                    }
                    Console.ResetColor();
                }
                else
                {
                    if (!isFirstRun) Console.WriteLine("Новых записей не найдено.");
                }

                // Ждем 5 секунд перед следующей проверкой
                await Task.Delay(CheckIntervalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nКритическая ошибка в цикле мониторинга: {ex.Message}");
            Console.WriteLine("Приложение будет закрыто.");
            Console.ResetColor();
            await page.ScreenshotAsync("monitoring_critical_error.png");
        }
    }


    // ===================================================================================
    // Вспомогательные методы (без критических изменений)
    // ===================================================================================

    /// <summary>
    /// Этап 1: Запускает видимый браузер для аутентификации и захватывает состояние (Cookies, Storage, IndexedDB).
    /// </summary>
    private static async Task<BrowserState?> CaptureBrowserStateAsync()
    {
        Console.WriteLine("--- Этап 1: Аутентификация и извлечение состояния ---");
        await using var headfulBrowser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = false, DefaultViewport = null });
        await using var page = await headfulBrowser.NewPageAsync();

        // Сначала идем на базовый URL для логина
        await page.GoToAsync(BaseUrl);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n========================================================================");
        Console.WriteLine("Пожалуйста, выполните вход в свой аккаунт в открывшемся окне браузера.");
        Console.WriteLine("ПОСЛЕ УСПЕШНОГО ВХОДА вернитесь сюда и НАЖМИТЕ ENTER.");
        Console.WriteLine("========================================================================");
        Console.ResetColor();
        Console.ReadLine();

        try
        {
            var loginCheck = await page.QuerySelectorAsync(LoginSuccessSelector);
            if (loginCheck == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Вход не подтвержден! Перезапустите программу.");
                Console.ResetColor();
                await Task.Delay(3000);
                return null;
            }

            Console.WriteLine("\nВход подтвержден. Извлекаю состояние хранилищ...");

            var capturedState = new BrowserState
            {
                LocalStorageJson = await page.EvaluateExpressionAsync<string>("JSON.stringify(localStorage)"),
                SessionStorageJson = await page.EvaluateExpressionAsync<string>("JSON.stringify(sessionStorage)"),
                IndexedDbJson = await IndexedDbHelper.GetAllIndexedDbDataAsync(page, DbName),
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
    /// Внедряет сохраненное состояние (Cookies, Storage, IndexedDB) в страницу.
    /// </summary>
    private static async Task InjectStateAsync(IPage page, BrowserState state)
    {
        await page.GoToAsync(BaseUrl); // Идем на базовый домен, чтобы установить куки и хранилища
        Console.WriteLine("Восстанавливаю состояние в фоновом браузере...");

        if (state.Cookies?.Length > 0)
            await page.SetCookieAsync(state.Cookies);

        await page.EvaluateFunctionAsync(@"(localStorageData, sessionStorageData) => {
            localStorage.clear(); sessionStorage.clear();
            const localData = JSON.parse(localStorageData);
            for (const key in localData) localStorage.setItem(key, localData[key]);
            const sessionData = JSON.parse(sessionStorageData);
            for (const key in sessionData) sessionStorage.setItem(key, sessionData[key]);
        }", state.LocalStorageJson, state.SessionStorageJson);

        if (state.IndexedDbJson.HasValue)
        {
            bool success = await IndexedDbHelper.SetAllIndexedDbDataAsync(page, DbName, state.IndexedDbJson.Value);
            if (!success) Console.WriteLine("Произошла ошибка при восстановлении IndexedDB.");
        }
    }

    /// <summary>
    /// Проверяет успешность входа после внедрения состояния и выполняет необходимые клики.
    /// </summary>
    private static async Task VerifyLoginAndClickButtonAsync(IPage page)
    {
        try
        {
            await page.WaitForSelectorAsync(HeadlessSuccessSelector, new WaitForSelectorOptions { Timeout = 15000 });
            Console.WriteLine("Успешный вход в фоновом режиме подтвержден.");
            await page.ScreenshotAsync("headless_success_page.png");
            await ClickButtonIfExistsAsync(page, ".css-ognuvg", "Кнопка фильтра");
        }
        catch (Exception)
        {
            Console.WriteLine("Селектор входа не найден. Сохраняю текущее состояние страницы для анализа...");
            await page.ScreenshotAsync("headless_error_page.png");
            await File.WriteAllTextAsync("headless_error_page.html", await page.GetContentAsync());
            Console.WriteLine("Скриншот 'headless_error_page.png' и HTML 'headless_error_page.html' сохранены.");
            throw;
        }
    }

    /// <summary>
    /// Вспомогательный метод для безопасного клика по элементу, если он существует.
    /// </summary>
    private static async Task ClickButtonIfExistsAsync(IPage page, string selector, string buttonName)
    {
        try
        {
            await page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Timeout = 5000 });
            await page.ClickAsync(selector, new ClickOptions { Delay = 50 });
            Console.WriteLine($"{buttonName} ({selector}) успешно нажата.");
        }
        catch (Exception)
        {
            Console.WriteLine($"Не удалось нажать на кнопку ({selector}), возможно, она уже нажата или отсутствует.");
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
            // Просто ищем элементы. Если их нет, вернется пустой список, что нормально.
            var components = await page.QuerySelectorAllAsync(componentSelector);

            foreach (var component in components)
            {
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
                            const srcParts = imgElement.src.split('SOLANA-');
                            if (srcParts.length > 1) {
                                const potentialToken = srcParts[1];
                                const tokenAddressMatch = potentialToken.match(/^[a-zA-Z0-9]+/);
                                if (tokenAddressMatch && tokenAddressMatch[0]) {
                                    link = `https://trade.padre.gg/trade/solana/${tokenAddressMatch[0]}`;
                                }
                            }
                        }
                        return { Name: name, MentionedInGroup: mentionedInGroup, TimeAgo: timeAgo, Link: link };
                    }");

                if (jsonCoinData.HasValue)
                {
                    var coin = jsonCoinData.Value.Deserialize<AlphaTrackerCoin>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (coin != null) trackerData.Add(coin);
                }
            }
        }
        catch (Exception ex)
        {
            // Ошибки здесь могут быть, если страница внезапно перегрузится. Просто сообщаем.
            Console.WriteLine($"Произошла временная ошибка при сборе данных: {ex.Message}");
        }
        return trackerData;
    }
}