using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace NektoMe_MITM_text;

public static class NektoCaptchaBrowser
{
    public static void OpenTextChatViewer(string token, string viewerName)
    {
        _ = Task.Run(() =>
        {
            var options = new ChromeOptions();
            options.AddArgument("--incognito");
            options.AddArgument("--disable-blink-features=AutomationControlled");

            var driver = new ChromeDriver(options);
            try
            {
                driver.Navigate().GoToUrl("https://nekto.me");

                ((IJavaScriptExecutor)driver).ExecuteScript(
                    @"const authToken = arguments[0];
const storageKey = 'storage_v2';
let storage = {};
try {
  storage = JSON.parse(localStorage.getItem(storageKey) || '{}') || {};
} catch (e) {
  storage = {};
}
if (typeof storage !== 'object' || storage === null) storage = {};
if (!storage.user || typeof storage.user !== 'object') storage.user = {};
storage.user.authToken = authToken;
localStorage.setItem(storageKey, JSON.stringify(storage));",
                    token
                );

                driver.Navigate().GoToUrl("https://nekto.me/chat");
                driver.Navigate().Refresh();

                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
                wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString() == "complete");

                ((IJavaScriptExecutor)driver).ExecuteScript("document.title = arguments[0];", $"Nekto Text Viewer: {viewerName}");
                Console.WriteLine($"[VIEWER] Открыто окно текстового чата: {viewerName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VIEWER] Не удалось открыть окно {viewerName}: {ex.Message}");
                try
                {
                    driver.Quit();
                }
                catch
                {
                    // Ignore cleanup errors.
                }
            }
        });
    }

    public static Task OpenChatForTokenAsync(string token, string publicKey = null)
    {
        return Task.Run(() =>
        {
            var options = new ChromeOptions();
            options.AddArgument("--incognito");
            options.AddArgument("--disable-blink-features=AutomationControlled");

            var driver = new ChromeDriver(options);

            try
            {
                driver.Navigate().GoToUrl("https://nekto.me");

                ((IJavaScriptExecutor)driver).ExecuteScript(
                    @"const authToken = arguments[0];
const storageKey = 'storage_v2';
let storage = {};
try {
  storage = JSON.parse(localStorage.getItem(storageKey) || '{}') || {};
} catch (e) {
  storage = {};
}
if (typeof storage !== 'object' || storage === null) storage = {};
if (!storage.user || typeof storage.user !== 'object') storage.user = {};
storage.user.authToken = authToken;
localStorage.setItem(storageKey, JSON.stringify(storage));",
                    token
                );

                driver.Navigate().GoToUrl("https://nekto.me/chat");
                driver.Navigate().Refresh();

                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
                wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString() == "complete");

                var detected = false;
                for (var i = 0; i < 8; i++)
                {
                    detected = IsCaptchaVisible(driver);
                    if (detected)
                        break;

                    TriggerSearchUi(driver);
                    Thread.Sleep(1500);
                }

                var keyText = string.IsNullOrWhiteSpace(publicKey) ? "n/a" : publicKey;
                if (detected)
                {
                    Console.WriteLine($"[CAPTCHA] Капча найдена в окне (siteKey={keyText}). Решите ее в браузере.");
                }
                else
                {
                    Console.WriteLine($"[CAPTCHA] Окно открыто (siteKey={keyText}), но капча не обнаружена автоматически. Нажмите на кнопки поиска на странице вручную.");
                }
            }
            catch
            {
                driver.Quit();
                throw;
            }
        });
    }

    private static void TriggerSearchUi(IWebDriver driver)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript(
            @"const normalize = (s) => (s || '').toLowerCase();
const texts = ['ищу', 'искать', 'найти', 'поиск', 'начать'];
const clickCandidates = [];

for (const el of document.querySelectorAll('button, a, [role=""button""], .btn, .button')) {
  const text = normalize(el.innerText || el.textContent || '');
    if (texts.some(t => text.includes(t))) clickCandidates.push(el);
}

for (const el of clickCandidates) {
  try {
    el.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
    el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    el.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
    el.click();
  } catch (e) {}
}

const forms = document.querySelectorAll('form');
for (const form of forms) {
  try { form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); } catch (e) { }
}"
        );
    }

    private static bool IsCaptchaVisible(IWebDriver driver)
    {
        var result = ((IJavaScriptExecutor)driver).ExecuteScript(
            @"const hasIframe = !!document.querySelector('iframe[src*=""recaptcha""], iframe[title*=""captcha"" i]');
const hasRecaptchaContainer = !!document.querySelector('.g-recaptcha, [data-sitekey], [class*=""captcha"" i]');
return hasIframe || hasRecaptchaContainer;"
        );

        return result is bool b && b;
    }
}
