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
        if (logType == LogType.Error)
        {
            Logs.Add($"{DateTime.Now.TimeOfDay} <ОШИБКА> {log}");
            return;
        }
        Logs.Add($"{DateTime.Now.TimeOfDay} {log}");
    }

    public void FinishLog()
    {
        DateTime currentTime = DateTime.Now;
        string filePath = $"{LogDirectory}\\ClashChecker_{currentTime.ToString().Replace(" ", "").Replace('.','_').Replace(':','_')}.log";
        File.Create(filePath).Close();
        WriteLine($"Время выполнения: {(currentTime - StartTime).TotalSeconds} секунд");
        File.WriteAllLines(filePath, Logs);
    }

}


public enum LogType
{
    Message,
    Error,
}

