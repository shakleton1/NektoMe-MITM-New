using System.Text.Json;
using NektoMe_MITM_text;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== Главное меню ===");
            Console.WriteLine("1 - Текстовый чат MITM");
            Console.WriteLine("2 - AudioChat MITM (https://nekto.me/audiochat/)");
            Console.WriteLine("0 - Выход");
            Console.Write("Ваш выбор: ");

            var mode = Console.ReadLine()?.Trim();
            switch (mode)
            {
                case "1":
                    await RunTextModeAsync();
                    break;
                case "2":
                    await RunAudioChatModeAsync();
                    break;
                case "0":
                    return;
                default:
                    Console.WriteLine("Неизвестная команда.");
                    break;
            }

            Console.WriteLine("Нажмите Enter, чтобы вернуться в меню...");
            Console.ReadLine();
        }
    }

    private static async Task RunTextModeAsync()
    {
        var manager = new NektoChatManager();

        // Проверяем и получаем валидные токены
        string token1 = "e17940de41201874ce088da85f23efd742d405450468508c296ba9080ed6dfa4";
        string token2 = "4555ca935e223b3359a4b7585f5a973e39147c0767114beb1371bffbad5f378f";

        if (!IsTokenValid(token1) || !IsTokenValid(token2))
        {
            Console.WriteLine("Токены невалидны. Открываем браузеры для получения новых...");
            var tokens = GetValidTokensFromBrowsers();
            token1 = tokens.Item1;
            token2 = tokens.Item2;
        }

        Console.WriteLine("Открываю 2 окна браузера для визуального контроля текстовых чатов...");
        NektoCaptchaBrowser.OpenTextChatViewer(token1, "Client 1");
        NektoCaptchaBrowser.OpenTextChatViewer(token2, "Client 2");

        manager.AddMember(
            token1,
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            "M",
            "F",
            new[] { 0, 17 },
            new[] { 0, 17 }
        );

        manager.AddMember(
            token2,
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            "F",
            "M",
            new[] { 0, 17 },
            new[] { 0, 17 }
        );

        await manager.StartAsync();

        Console.WriteLine("Программа запущена. Нажмите Enter для выхода...");
        Console.ReadLine();
    }

    private static Task RunAudioChatModeAsync()
    {
        using var audioChat = new NektoAudioChatManager();
        audioChat.RunInteractive();
        return Task.CompletedTask;
    }

    private static bool IsTokenValid(string token)
    {
        // Простая проверка валидности токена (можно расширить)
        return !string.IsNullOrEmpty(token) && token.Length > 30;
    }

    private static (string, string) GetValidTokensFromBrowsers()
    {
        var options1 = new ChromeOptions();
        options1.AddArgument("--incognito");
        options1.AddArgument("--disable-blink-features=AutomationControlled");

        var options2 = new ChromeOptions();
        options2.AddArgument("--incognito");
        options2.AddArgument("--disable-blink-features=AutomationControlled");

        string token1 = null;
        string token2 = null;

        // Запускаем браузеры
        var driver1 = new ChromeDriver(options1);
        var driver2 = new ChromeDriver(options2);

        driver1.Navigate().GoToUrl("https://nekto.me/chat");
        driver2.Navigate().GoToUrl("https://nekto.me/chat");

        Console.WriteLine("Браузеры открыты. Войдите в аккаунты в обоих браузерах.");
        Console.WriteLine("Нажмите Enter после входа в оба аккаунта...");
        Console.ReadLine();

        // Получаем токены из localStorage
        token1 = (string)((IJavaScriptExecutor)driver1).ExecuteScript(
            "try { const storage = JSON.parse(localStorage.getItem('storage_v2')); return storage?.user?.authToken; } catch(e) { return null; }"
        );

        token2 = (string)((IJavaScriptExecutor)driver2).ExecuteScript(
            "try { const storage = JSON.parse(localStorage.getItem('storage_v2')); return storage?.user?.authToken; } catch(e) { return null; }"
        );

        if (string.IsNullOrEmpty(token1) || string.IsNullOrEmpty(token2))
        {
            throw new Exception("Не удалось получить токены из браузеров");
        }

        Console.WriteLine("Токены успешно получены!");
        return (token1, token2);
    }
}