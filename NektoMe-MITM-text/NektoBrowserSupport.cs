using OpenQA.Selenium.Chrome;

namespace NektoMe_MITM_text;

public enum BrowserKind
{
    Chrome = 1,
    Brave = 2,
}

public static class NektoBrowserSupport
{
    public static BrowserKind PromptBrowserSelection()
    {
        Console.WriteLine();
        Console.WriteLine("=== Выбор браузера ===");
        Console.WriteLine("1 - Google Chrome");
        Console.WriteLine("2 - Brave Browser");

        while (true)
        {
            Console.Write("Какой браузер использовать: ");
            var input = (Console.ReadLine() ?? string.Empty).Trim();
            if (input == "1")
            {
                Console.WriteLine("Выбран браузер: Google Chrome");
                return BrowserKind.Chrome;
            }

            if (input == "2")
            {
                var bravePath = ResolveBraveExecutable();
                if (string.IsNullOrWhiteSpace(bravePath))
                {
                    Console.WriteLine("Brave не найден. Установите Brave или выберите Chrome.");
                    continue;
                }

                Console.WriteLine("Выбран браузер: Brave Browser");
                return BrowserKind.Brave;
            }

            Console.WriteLine("Неверный ввод. Введите 1 или 2.");
        }
    }

    public static ChromeOptions BuildOptions(BrowserKind browser)
    {
        var options = new ChromeOptions();
        options.AddArgument("--incognito");
        options.AddArgument("--disable-blink-features=AutomationControlled");

        if (browser == BrowserKind.Brave)
        {
            var bravePath = ResolveBraveExecutable();
            if (!string.IsNullOrWhiteSpace(bravePath))
            {
                options.BinaryLocation = bravePath;
            }
            else
            {
                throw new InvalidOperationException("Brave Browser выбран, но не найден в системе.");
            }
        }

        return options;
    }

    public static ChromeDriver CreateDriver(BrowserKind browser, ChromeOptions options)
    {
        return new ChromeDriver(options);
    }

    private static string? ResolveBraveExecutable()
    {
        var candidates = new[]
        {
            @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
            @"C:\Program Files (x86)\BraveSoftware\Brave-Browser\Application\brave.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
