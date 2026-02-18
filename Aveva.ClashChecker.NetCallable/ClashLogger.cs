using System;
using System.Collections.Generic;
using System.IO;

namespace Aveva.ClashChecker.NetCallable;

/// <summary>
/// Класс для логгирования в файл
/// </summary>
public class ClashLogger
{
    /// <summary>
    /// Путь к лог файлу
    /// </summary>
    //public string FilePath { get; set; } = "";

    /// <summary>
    /// Строки лог файла
    /// </summary>
    public List<string> Logs = [];
    /// <summary>
    /// Дата и время начала записи
    /// </summary>
    public DateTime StartTime { get; set; }
    
    public string LogDirectory { get; set; }

    public bool LogInPdmsConsole { get; set; } = false;

    public ClashLogger(string logDirectory)
    {
        if (!Directory.Exists(logDirectory))
            Directory.CreateDirectory(logDirectory);

        StartTime = DateTime.Now;
        LogDirectory = logDirectory;
        //FilePath = $"{LogDirectory}\\ClashChecker_{currentTime}.log";
    }


    public void WriteLine(string log, LogType logType = LogType.Message)
    {

        string logMessage = $"{DateTime.Now.TimeOfDay}";
        if (logType == LogType.Error)
           logMessage +=$" <ОШИБКА> ";
        logMessage += " ";
        logMessage += log;
        if (LogInPdmsConsole)
            PmlHelper.WriteLine(logMessage);

        Logs.Add(logMessage);
    }

    public void FinishLog()
    {
        DateTime currentTime = DateTime.Now;
        var filetime = currentTime.ToString("yyyy-MM-dd_HH-mm-ss");
        string filePath = $"{LogDirectory}\\ClashChecker_{filetime}.log";
       // File.Create(filePath);
       Logs.Add($"Время выполнения: {(currentTime - StartTime).TotalSeconds} секунд");
        File.WriteAllLines(filePath, Logs);
    }

}


public enum LogType
{
    Message,
    Error,
}

