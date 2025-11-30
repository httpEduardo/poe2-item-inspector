using System;
using System.IO;

namespace PoE2Inspector.Services.Logging;

public class LogService : ILogService
{
    private readonly string _logPath;
    private readonly object _lockObj = new();

    public LogService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "PoE2Inspector");
        Directory.CreateDirectory(appFolder);
        _logPath = Path.Combine(appFolder, $"log_{DateTime.Now:yyyyMMdd}.txt");
    }

    public void Info(string msg)
    {
        WriteLog("INFO", msg);
    }

    public void Warn(string msg)
    {
        WriteLog("WARN", msg);
    }

    public void Error(string msg, Exception? ex = null)
    {
        var fullMsg = ex != null ? $"{msg}\n{ex}" : msg;
        WriteLog("ERROR", fullMsg);
    }

    private void WriteLog(string level, string msg)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logLine = $"[{timestamp}] [{level}] {msg}";

            lock (_lockObj)
            {
                File.AppendAllText(_logPath, logLine + Environment.NewLine);
            }

            Console.WriteLine(logLine);
        }
        catch
        {

        }
    }
}
