namespace NektoMe_MITM_text;

public static class NektoConsoleWait
{
    public static void WaitForStopCommand(string scopeName)
    {
        Console.WriteLine($"{scopeName} работает. Для остановки введи 'stop' и нажми Enter (или Ctrl+C).\n");

        while (true)
        {
            var line = (Console.ReadLine() ?? string.Empty).Trim();
            if (line.Equals("stop", StringComparison.OrdinalIgnoreCase))
                return;

            if (line.Length == 0)
            {
                Console.WriteLine("Пустой Enter не останавливает мост. Введи 'stop' для остановки.");
                continue;
            }

            Console.WriteLine("Неизвестная команда. Для остановки введи 'stop'.");
        }
    }
}
