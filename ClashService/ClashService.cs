using ClashForKPI.Extensions;
using ClashForKPI.Models;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.PMLNet;
using Aveva.Core3D.Clasher;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using Aveva.Core.Utilities.CommandLine;
using static ClashForKPI.Sql.SqlMapping;
using PML = Aveva.Core.Utilities.CommandLine.Command;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;

namespace ClashForKPI;

/// <summary>
/// Класс для обработки коллизий
/// </summary>
[PMLNetCallable]
public class ClashService
{
    #region Создание сервиса для PML.NET
    [PMLNetCallable]
    public ClashService()
    {
    }
    #endregion

    private static readonly DbElement NullElement = DbElement.GetElement("*");
    public string ClashConnectionString = new Func<string>(() =>
    {
        Command commsnd = Command.CreateCommand("!!Connect = !!LoginToSQL('Admin')");
        commsnd.RunInPdms();
        return Command.GetStringFromPML("!!Connect").Trim('/', '"');
    })();


    private const string DefaultLogDirectoryPath = "C:\\AVEVA\\ClasherLogs\\ClashLog.log";
    private ClashLogger Logger { get; set; } = new ClashLogger(DefaultLogDirectoryPath);

    #region Поддержка присваивания объектов PML.NET
    /// <summary>
    /// Стандартная конструкция для детекта класса авевой
    /// </summary>
    /// <param name="that"></param>
    [PMLNetCallable]
    public void Assign(ClashService that)
    {
    }
    #endregion

    #region Полная проверка проекта и обработка результатов
    /// <summary>
    /// Точка входа
    /// </summary>
    [PMLNetCallable]
    public void CheckAll(bool testMode = true, string logDirectoryPath = DefaultLogDirectoryPath)
    {
        try
        {

            Logger = new ClashLogger(logDirectoryPath);
            Logger.LogInPdmsConsole = testMode;
            var projectCode = Project.CurrentProject.Name;
            if (testMode)
                projectCode += "_TEST";
            string clashTableName = $"clashtable{projectCode}";

            Logger.WriteLine("Начало выполнения проверки...");
            Logger.WriteLine($"Проект: {projectCode}");

            using SqlConnection sqlConnection = new(ClashConnectionString);
            sqlConnection.Open();

            EnsureClashTables(clashTableName, sqlConnection);

            RefreshStoredClashElementInfo(sqlConnection, clashTableName, string.Empty);
            Logger.WriteLine("Информация об элементах существующих коллизий обновлена.");
            int initialClashCount = sqlConnection.ExecuteScalar<int>($"SELECT COUNT(*) FROM [{clashTableName}]");
            sqlConnection.Execute($"UPDATE [{clashTableName}] SET [XT] = 0");

            Logger.WriteLine($"Коллизий до проверки: {initialClashCount}");

            var failedZoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool zoneCheckCompleted = CheckZones(sqlConnection, clashTableName, string.Empty, failedZoneNames);
            if (!zoneCheckCompleted)
            {
                Logger.WriteLine("Ночная очистка отменена: проверка зон завершилась с ошибкой. Строки с XT = 0 сохранены.", LogType.Error);
                Logger.FinishLog();
                return;
            }

            CleanupNightCheckResults(sqlConnection, clashTableName, initialClashCount, failedZoneNames);
            Logger.FinishLog();
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message, LogType.Error);
            Logger.FinishLog();

            return;
        }

    }
    #endregion

    #region Сбор зон и последовательная clash-проверка
    public bool CheckZones(
        SqlConnection clashConnection,
        string clashTableName,
        string zoneRef,
        HashSet<string>? failedZoneNames = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        //TimeSpan currentCollectionTime = TimeSpan.Zero;
        //TimeSpan obstructionCollectionTime = TimeSpan.Zero;
        //TimeSpan sortingTime = TimeSpan.Zero;
        TimeSpan obstructionListBuildTime = TimeSpan.Zero;
        TimeSpan clashCheckTime = TimeSpan.Zero;
        //int purposeReadCount = 0;
        //int purposeReadCountDuringSorting = 0;
        int currentZoneCount = 0;
        int initialObstructionZoneCount = 0;

        try
        {
            DbAttribute clashIgnoreAttribute = DbAttribute.GetDbAttribute(":ClashIgnore");

            // Current содержит только комплекты PD/RD, которые запускаются как проверяемая сторона.
            //var currentCollectionStopwatch = Stopwatch.StartNew();
            List<DbElement> currentZones = [.. new DBElementCollection(
                    new TypeFilter(DbElementTypeInstance.ZONE))
                .Cast<DbElement>()
                .Where(zone =>
                {
                    //purposeReadCount++;
                    string purpose = zone.GetAsString(DbAttributeInstance.PURP);
                    return string.Equals(purpose, "PD", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(purpose, "RD", StringComparison.OrdinalIgnoreCase);
                })];
            //currentCollectionStopwatch.Stop();
            //currentCollectionTime = currentCollectionStopwatch.Elapsed;
            currentZoneCount = currentZones.Count;

            // Obstruction содержит все разрешённые зоны, включая зоны, которые сами не проверяются.
            //var obstructionCollectionStopwatch = Stopwatch.StartNew();
            List<DbElement> initialObstructionZones = [.. new DBElementCollection(
                    new TypeFilter(DbElementTypeInstance.ZONE))
                .Cast<DbElement>()
                .Where(zone => !zone.GetBool(clashIgnoreAttribute))];
            //obstructionCollectionStopwatch.Stop();
            //obstructionCollectionTime = obstructionCollectionStopwatch.Elapsed;
            initialObstructionZoneCount = initialObstructionZones.Count;

            List<DbElement> remainingObstructionZones = [.. initialObstructionZones];

            Logger.WriteLine($"Зон Current (PURPOSE PD/RD): {currentZoneCount} шт");
            Logger.WriteLine($"Зон в исходной Obstruction-коллекции: {initialObstructionZoneCount} шт");

            var clashOptions = ClashOptions.Create();
            clashOptions.Override = true;
            clashOptions.Midpoint = true;
            clashOptions.TouchGap = 0.0;
            clashOptions.TouchOverlap = 2;
            clashOptions.Clearance = 0.0;
            clashOptions.IncludeTouches = false;
            clashOptions.BranchCheckType = BranchCheck.BCHECK;
            clashOptions.IncludeConnections = false;
            clashOptions.NoCheckWithin(
            [
                DbElementTypeInstance.EQUIPMENT,
                DbElementTypeInstance.STRUCTURE,
                DbElementTypeInstance.BRANCH,
                DbElementTypeInstance.RESTRAINT,
                DbElementTypeInstance.VOLMODEL,
            ]);

            int zoneCount = currentZones.Count;
            if (string.IsNullOrEmpty(zoneRef))
            {
                for (int i = 0; i < currentZones.Count; i++)
                {
                    DbElement currentZone = currentZones[i];
                    string currentZoneRef = currentZone.GetAsString(DbAttributeInstance.REF);
                    try
                    {
                        double[] currentZoneWvolume = currentZone.GetDoubleArray(DbAttributeInstance.WVOL);
                        if (currentZoneWvolume.Length < 6)
                        {
                            Logger.WriteLine($"Зона {currentZone.Name()} пропущена: некорректный WVOL");
                            failedZoneNames?.Add(currentZone.Name());
                            continue;
                        }

                        int removedCount = remainingObstructionZones.RemoveAll(zone => zone.GetAsString(DbAttributeInstance.REF) == currentZoneRef);
                        if (removedCount == 0 && !currentZone.GetBool(clashIgnoreAttribute))
                            Logger.WriteLine($"Внимание: зона {currentZone.Name()} не найдена в коллекции препятствий.");

                        Logger.WriteLine($"Проверка зоны {currentZone.Name()} [{i + 1}/{zoneCount}], препятствий: {remainingObstructionZones.Count}");

                        var obstructionListStopwatch = Stopwatch.StartNew();
                        ObstructionList obstructionList = CreateObstructionList(currentZoneWvolume, remainingObstructionZones, out int obstructionCount);
                        obstructionListStopwatch.Stop();
                        obstructionListBuildTime += obstructionListStopwatch.Elapsed;
                        Logger.WriteLine($"Зона {currentZone.Name()}: в Obstruction List добавлено "
                            + $"{obstructionCount} зон; формирование заняло "
                            + $"{obstructionListStopwatch.Elapsed.TotalSeconds:F3} сек");

                        if (obstructionCount == 0)
                        {
                            Logger.WriteLine($"Зона {currentZone.Name()} пропущена: Obstruction List пуст");
                            failedZoneNames?.Add(currentZone.Name());
                            continue;
                        }

                        bool zoneCheckSucceeded = CheckZone(currentZone, obstructionList, clashOptions, clashConnection, clashTableName, out TimeSpan currentClashCheckTime);
                        clashCheckTime += currentClashCheckTime;
                        if (!zoneCheckSucceeded)
                            failedZoneNames?.Add(currentZone.Name());
                        Logger.WriteLine($"Зона {currentZone.Name()}: clash-проверка заняла "
                            + $"{currentClashCheckTime.TotalSeconds:F3} сек");
                    }
                    catch (Exception ex)
                    {
                        failedZoneNames?.Add(currentZone.Name());
                        Logger.WriteLine($"Ошибка обработки зоны {currentZone.Name()}: {ex.Message}. Переход к следующей зоне.", LogType.Error);
                    }
                }
            }
            else
            {
                var selectedZone = DbElement.GetElement(zoneRef);
                var selectedZoneWvol = selectedZone.GetDoubleArray(DbAttributeInstance.WVOL);
                string selectedZoneRef = selectedZone.GetAsString(DbAttributeInstance.REF);
                int removedCount = remainingObstructionZones.RemoveAll(zone => zone.GetAsString(DbAttributeInstance.REF) == selectedZoneRef);
                if (removedCount == 0 && !selectedZone.GetBool(clashIgnoreAttribute))
                    Logger.WriteLine($"Внимание: зона {selectedZone.Name()} не найдена в коллекции препятствй.");

                Logger.WriteLine($"Проверка зоны {selectedZone.Name()}, препятствий: {remainingObstructionZones.Count}");

                var obstructionListStopwatch = Stopwatch.StartNew();
                ObstructionList obstructionList = CreateObstructionList(selectedZoneWvol, remainingObstructionZones, out int obstructionCount);
                obstructionListStopwatch.Stop();
                obstructionListBuildTime += obstructionListStopwatch.Elapsed;
                Logger.WriteLine($"Зона {selectedZone.Name()}: в Obstruction List добавлено "
                    + $"{obstructionCount} зон; формирование заняло "
                    + $"{obstructionListStopwatch.Elapsed.TotalSeconds:F3} сек");

                if (obstructionCount == 0)
                {
                    Logger.WriteLine($"Зона {selectedZone.Name()} пропущена: Obstruction List пуст");
                    failedZoneNames?.Add(selectedZone.Name());
                    return false;
                }
                else
                {
                    bool zoneCheckSucceeded = CheckZone(selectedZone, obstructionList, clashOptions, clashConnection, clashTableName, out TimeSpan currentClashCheckTime);
                    clashCheckTime += currentClashCheckTime;
                    Logger.WriteLine($"Зона {selectedZone.Name()}: clash-проверка заняла "
                        + $"{currentClashCheckTime.TotalSeconds:F3} сек");
                    if (!zoneCheckSucceeded)
                    {
                        failedZoneNames?.Add(selectedZone.Name());
                        return false;
                    }
                }
            }

            clashConnection.Close();

            totalStopwatch.Stop();
            Logger.WriteLine($"CheckZones: общее время: {totalStopwatch.Elapsed.TotalMilliseconds:F0} мс");
            //Logger.WriteLine($"CheckZones: сбор Current: {currentCollectionTime.TotalMilliseconds:F0} мс");
            //Logger.WriteLine($"CheckZones: сбор исходной Obstruction-коллекции: {obstructionCollectionTime.TotalMilliseconds:F0} мс");
            //Logger.WriteLine($"CheckZones: сортировка Obstruction-коллекции: {sortingTime.TotalMilliseconds:F0} мс; зон: {initialObstructionZoneCount}; обращений к PURPOSE: {purposeReadCountDuringSorting}");
            Logger.WriteLine($"CheckZones: формирование Obstruction List: {obstructionListBuildTime.TotalMilliseconds:F0} мс");
            Logger.WriteLine($"CheckZones: clash-проверка: {clashCheckTime.TotalMilliseconds:F0} мс");
            //Logger.WriteLine($"CheckZones: всего обращений к PURPOSE при сборе Current: {purposeReadCount}");
            if (string.IsNullOrEmpty(zoneRef) && failedZoneNames != null)
                Logger.WriteLine($"Итог по зонам: проверено {zoneCount - failedZoneNames.Count}; с ошибкой или пропущено {failedZoneNames.Count}; всего {zoneCount}.");
            Logger.WriteLine("Обработка завершена!");
            Logger.FinishLog();
            return true;
        }


        catch (Exception ex)
        {
            totalStopwatch.Stop();
            Logger.WriteLine($"CheckZones: общее время до ошибки: {totalStopwatch.Elapsed.TotalMilliseconds:F0} мс");
            Logger.WriteLine(ex.Message, LogType.Error);
            Logger.FinishLog();

            return false;
        }

    }
    #endregion

    #region Формирование списка препятствий по пересечению габаритов
    private ObstructionList CreateObstructionList(double[] currentZoneWvolume, IEnumerable<DbElement> obstructionZones, out int obstructionCount)
    {
        var obstructionList = ObstructionList.Create();
        obstructionCount = 0;

        foreach (DbElement obstructionZone in obstructionZones)
        {
            double[] obstructionZoneWvolume = obstructionZone.GetDoubleArray(DbAttributeInstance.WVOL);
            if (obstructionZoneWvolume.Length < 6)
                continue;

            if (VolumesOverlap(currentZoneWvolume, obstructionZoneWvolume))
            {
                obstructionList.AddObstructions([obstructionZone]);
                obstructionCount++;
            }
        }

        return obstructionList;
    }
    #endregion

    #region Проверка одной зоны и сохранение результата
    private bool CheckZone(DbElement zone, ObstructionList obstructionList, ClashOptions clashOptions, SqlConnection clashConnection, string clashTableName, out TimeSpan elapsed)
    {
        var clashCheckStopwatch = Stopwatch.StartNew();
        try
        {
            var clashSet = ClashSet.Create();
            bool checkSucceeded = Clasher.Instance.Check([zone], clashOptions, obstructionList, clashSet);
            clashCheckStopwatch.Stop();
            elapsed = clashCheckStopwatch.Elapsed;
            if (!checkSucceeded)
            {
                Logger.WriteLine($"AVEVA не удалось проверить зону {zone.Name()}.", LogType.Error);
                return false;
            }

            Logger.WriteLine($"Зона {zone.Name()} проверена.");
            return CheckResultToBase(clashConnection, clashTableName, clashSet);
        }
        catch (Exception ex)
        {
            clashCheckStopwatch.Stop();
            elapsed = clashCheckStopwatch.Elapsed;
            Logger.WriteLine($"Ошибка проверки зоны {zone.Name()}: {ex.Message}", LogType.Error);
            return false;
        }
    }
    #endregion

    private static readonly Dictionary<string, string> RequiredClashTableColumns = new()
    {
        ["ID"] = "INT NOT NULL IDENTITY(100000,1)",
        ["GL"] = "NVARCHAR(12) NOT NULL",
        ["CT"] = "NVARCHAR(12) NOT NULL",
        ["R1"] = "NVARCHAR(64) NOT NULL",
        ["E1"] = "NVARCHAR(12) NOT NULL",
        ["U1"] = "NVARCHAR(32) NOT NULL",
        ["D1"] = "NVARCHAR(16)",
        ["G1"] = "NVARCHAR(64)",
        ["R2"] = "NVARCHAR(64) NOT NULL",
        ["E2"] = "NVARCHAR(12) NOT NULL",
        ["U2"] = "NVARCHAR(32) NOT NULL",
        ["D2"] = "NVARCHAR(16)",
        ["G2"] = "NVARCHAR(64)",
        ["XT"] = "BIT NOT NULL",
        ["DT"] = "DATETIME NOT NULL",
        ["X0"] = "INT NOT NULL",
        ["Y0"] = "INT NOT NULL",
        ["Z0"] = "INT NOT NULL",
        ["RT"] = "NVARCHAR(16)",
        ["RU"] = "NVARCHAR(32)",
        ["RD"] = "DATETIME",
        ["AU"] = "NVARCHAR(32)",
        ["AD"] = "DATETIME",
        ["AR"] = "NVARCHAR(255)",
        ["WU"] = "NVARCHAR(32)",
        ["WD"] = "DATETIME"

    };


    private static readonly Dictionary<string, string> RequiredHistoryClashTableColumns = new()
    {
        ["ID"] = "INT NOT NULL",
        ["GL"] = "NVARCHAR(12) NOT NULL",
        ["CT"] = "NVARCHAR(12) NOT NULL",
        ["R1"] = "NVARCHAR(64) NOT NULL",
        ["E1"] = "NVARCHAR(12) NOT NULL",
        ["U1"] = "NVARCHAR(32) NOT NULL",
        ["D1"] = "NVARCHAR(16)",
        ["G1"] = "NVARCHAR(64)",
        ["R2"] = "NVARCHAR(64) NOT NULL",
        ["E2"] = "NVARCHAR(12) NOT NULL",
        ["U2"] = "NVARCHAR(32) NOT NULL",
        ["D2"] = "NVARCHAR(16)",
        ["G2"] = "NVARCHAR(64)",
        ["XT"] = "BIT NOT NULL",
        ["DT"] = "DATETIME NOT NULL",
        ["X0"] = "INT NOT NULL",
        ["Y0"] = "INT NOT NULL",
        ["Z0"] = "INT NOT NULL",
        ["RT"] = "NVARCHAR(16)",
        ["RU"] = "NVARCHAR(32)",
        ["RD"] = "DATETIME",
        ["AU"] = "NVARCHAR(32)",
        ["AD"] = "DATETIME",
        ["AR"] = "NVARCHAR(255)",
        ["WU"] = "NVARCHAR(32)",
        ["WD"] = "DATETIME",
        ["HistoryDate"] = "DATETIME NOT NULL DEFAULT GETDATE()",
        ["HistoryReason"] = "NVARCHAR(255)"
    };



    #region Создание и актуализация SQL-таблиц коллизий
    private void EnsureClashTables(string clashTableName, SqlConnection clashConnection)
    {
        if (!clashConnection.TableExists(clashTableName))
        {
            Logger.WriteLine($@"Таблица {clashTableName} не найдена! Создание таблицы...");
            clashConnection.Execute($@"CREATE TABLE [{clashTableName}]( [ID] INT NOT NULL IDENTITY (100000,1), 
                [GL] NVARCHAR(12) NOT NULL,  
                [CT] NVARCHAR(12) NOT NULL,
                [R1] NVARCHAR(64) NOT NULL,
                [E1] NVARCHAR(12) NOT NULL,
                [U1] NVARCHAR(32) NOT NULL,
                [D1] NVARCHAR(16),
                [G1] NVARCHAR(64),
                [R2] NVARCHAR(64) NOT NULL,
                [E2] NVARCHAR(12) NOT NULL,
                [U2] NVARCHAR(32) NOT NULL,
                [D2] NVARCHAR(16),
                [G2] NVARCHAR(64),
                [XT] BIT NOT NULL,
                [DT] DATETIME NOT NULL,
                [X0] INT NOT NULL,
                [Y0] INT NOT NULL,
                [Z0] INT NOT NULL,
                [RT] NVARCHAR(16),
                [RU] NVARCHAR(32),
                [RD] DATETIME,
                [AU] NVARCHAR(32),
                [AD] DATETIME,
                [AR] NVARCHAR(255),
                [WU] NVARCHAR(32), 
                [WD] DATETIME );");

        }
        else
        {
            var existingColumns = clashConnection.Query<string>(
                @"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @clashTableName",
                new { clashTableName }).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingColumns = RequiredClashTableColumns.Where(c => !existingColumns.Contains(c.Key)).ToList();

            foreach (var column in missingColumns)
                AddColumn(clashConnection, clashTableName, column.Key, column.Value);
        }

        EnsureReferenceColumnSize(clashConnection, clashTableName);

        string historyTableName = $"{clashTableName}_his";
        if (clashConnection.TableExists(historyTableName))
        {
            var existingColumns = clashConnection.Query<string>(
                @"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_NAME = @historyTableName",
                new { historyTableName })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingColumns = RequiredHistoryClashTableColumns.Where(column => !existingColumns.Contains(column.Key)).ToList();

            foreach (var column in missingColumns)
                AddColumn(clashConnection, historyTableName, column.Key, column.Value);
        }

        if (!clashConnection.TableExists(historyTableName))
        {
            Logger.WriteLine($"Таблица {historyTableName} не найдена! Создание таблицы...");

            clashConnection.Execute(@$"CREATE TABLE [{historyTableName}](
                [ID] INT NOT NULL,
                [GL] NVARCHAR(12) NOT NULL,
                [CT] NVARCHAR(12) NOT NULL,
                [R1] NVARCHAR(64) NOT NULL,
                [E1] NVARCHAR(12) NOT NULL,
                [U1] NVARCHAR(32) NOT NULL,
                [D1] NVARCHAR(16),
                [G1] NVARCHAR(64),
                [R2] NVARCHAR(64) NOT NULL,
                [E2] NVARCHAR(12) NOT NULL,
                [U2] NVARCHAR(32) NOT NULL,
                [D2] NVARCHAR(16),
                [G2] NVARCHAR(64),
                [XT] BIT NOT NULL,
                [DT] DATETIME NOT NULL,
                [X0] INT NOT NULL,
                [Y0] INT NOT NULL,
                [Z0] INT NOT NULL,
                [RT] NVARCHAR(16),
                [RU] NVARCHAR(32),
                [RD] DATETIME,
                [AU] NVARCHAR(32),
                [AD] DATETIME,
                [AR] NVARCHAR(255),
                [WU] NVARCHAR(32),
                [WD] DATETIME,
                [HistoryDate] DATETIME NOT NULL DEFAULT GETDATE(),
                [HistoryReason] NVARCHAR(255));");

            clashConnection.Execute(
                $"CREATE INDEX [IX_{historyTableName}_ID] ON [{historyTableName}] ([ID])");
            Logger.WriteLine($"Таблица {historyTableName} создана!");
        }

        EnsureReferenceColumnSize(clashConnection, historyTableName);
    }
    #endregion

    #region Расширение столбцов ссылок R1 и R2
    private void EnsureReferenceColumnSize(SqlConnection connection, string tableName)
    {
        foreach (string columnName in new[] { "R1", "R2" })
        {
            int? currentLength = connection.QuerySingleOrDefault<int?>(@"SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName", new { tableName, columnName });

            if (!currentLength.HasValue || currentLength.Value >= 64)
                continue;

            connection.Execute($"ALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] NVARCHAR(64) NOT NULL");
            Logger.WriteLine($"Столбец {tableName}.{columnName} расширен до NVARCHAR(64).");
        }
    }


    /// <summary>
    /// Добавляет отдельный столбец в таблицу
    /// </summary>
    #endregion

    #region Добавление отсутствующего столбца SQL
    private static void AddColumn(SqlConnection connection, string tableName, string columnName, string columnDefinition)
    {
        try
        {
            string addColumnSql = $@"
                ALTER TABLE [{tableName}] 
                ADD [{columnName}] {columnDefinition}";

            connection.Execute(addColumnSql);
        }
        catch (Exception ex)
        {
            throw new Exception($"Ошибка при добавлении столбца {columnName}: {ex.Message}", ex);
        }
    }


    #endregion

    #region Актуализация сохранённых данных элементов
    /// <summary>
    /// Обновляет данные по коллизиям выбранной зоны или всех зон.
    /// </summary>
    public void RefreshStoredClashElementInfo(SqlConnection sqlConnection, string clashTableName, string zoneRef)
    {
        var stopwatch = Stopwatch.StartNew();
        int deleteCount = 0;
        int updateCount = 0;
        List<ClashEntity> clashes;

        if (!string.IsNullOrEmpty(zoneRef))
        {
            var clashesByZone = sqlConnection.Query<ClashEntity>($"SELECT {ClashSql} FROM [{clashTableName}] WHERE [G1] = @zoneRef OR [G2] = @zoneRef", new { zoneRef }).ToList();
            var clashesByZoneElement = QueryClashesByElement(sqlConnection, clashTableName, zoneRef);
            HashSet<int> zoneClashIds = [.. clashesByZone.Select(clash => clash.Id)];
            clashes = [.. clashesByZone.Concat(clashesByZoneElement.Where(clash => !zoneClashIds.Contains(clash.Id)))];
        }
        else
        {
            clashes = [.. sqlConnection.Query<ClashEntity>($"SELECT {ClashSql} FROM [{clashTableName}]")];
        }

        foreach (ClashEntity clash in clashes)
        {
            int updateResult = RefreshClashElementInfo(sqlConnection, clash, clashTableName);

            if (updateResult == 1)
                updateCount++;
            else if (updateResult == -1)
                deleteCount++;
        }

        stopwatch.Stop();
        Logger.WriteLine($"Актуализация данных SQL: просмотрено {clashes.Count}, обновлено {updateCount}, удалено {deleteCount}, время {stopwatch.Elapsed.TotalSeconds:F3} сек.");
    }

    #endregion

    #region Проверка пересечения габаритных объёмов
    private static bool VolumesOverlap(double[] firstVolume, double[] secondVolume)
    {
        bool overlapX = firstVolume[0] <= secondVolume[3] && firstVolume[3] >= secondVolume[0];
        bool overlapY = firstVolume[1] <= secondVolume[4] && firstVolume[4] >= secondVolume[1];
        bool overlapZ = firstVolume[2] <= secondVolume[5] && firstVolume[5] >= secondVolume[2];
        return overlapX && overlapY && overlapZ;
    }

    #endregion

    #region Актуализация одной сохранённой коллизии
    /// <summary>
    /// Обновляет данные по коллизиям (по отдельным элементам)
    /// <returns>
    /// 0 - если обновление не требовалось(NONE)
    /// 1 - если было призведено обновление(UPDATE)
    /// -1 - если удалили коллизию(DELETE)
    /// </returns>
    /// </summary>
    public int RefreshClashElementInfo(SqlConnection sqlConnection, ClashEntity clash, string clashTableName)
    {
        if (HasMissingElement(clash))
        {
            const string reason = "Один из элементов коллизии больше не существует в AVEVA";
            RemoveClash(sqlConnection, clashTableName, clash, reason);
            return -1;
        }

        DbElement firstElement = DbElement.GetElement(clash.FirstElement);
        DbElement secondElement = DbElement.GetElement(clash.SecondElement);

        string actualFirstDepartment = GetDepartment(firstElement);
        string actualSecondDepartment = GetDepartment(secondElement);
        string actualFirstZone = GetZoneName(firstElement);
        string actualSecondZone = GetZoneName(secondElement);
        string building = GetBuildingCode(firstElement);

        string firstDesigner = GetDesignerOrLastUser(firstElement);
        string secondDesigner = GetDesignerOrLastUser(secondElement);

        bool informationChanged = clash.FirstUserMode != firstDesigner
            || clash.SecondUserMode != secondDesigner
            || clash.FirstZone != actualFirstZone
            || clash.SecondZone != actualSecondZone
            || clash.FirstDept != actualFirstDepartment
            || clash.SecondDept != actualSecondDepartment
            || clash.Building != building;

        if (!informationChanged)
            return 0;

        sqlConnection.Execute($@"UPDATE [{clashTableName}]
                                SET [D1] = @firstDepartment,
                                    [GL] = @building,
                                    [G1] = @firstZone,
                                    [U1] = @firstDesigner,
                                    [D2] = @secondDepartment,
                                    [G2] = @secondZone,
                                    [U2] = @secondDesigner
                                WHERE [ID] = @id",
            new
            {
                id = clash.Id,
                building,
                firstDepartment = actualFirstDepartment,
                secondDepartment = actualSecondDepartment,
                firstDesigner,
                secondDesigner,
                firstZone = actualFirstZone,
                secondZone = actualSecondZone
            });

        return 1;
    }
    #endregion

    #region Получение кода здания из имени базы
    private static string GetBuildingCode(DbElement element)
    {
        string databaseName = element.GetAsString(DbAttributeInstance.DBNA);
        return databaseName.Split('/')[1].Substring(0, 5);
    }

    #endregion

    #region Получение пользователя последнего изменения
    private static string GetLastModifiedUser(DbElement element)
    {
        string history = element.GetAsString(DbAttributeInstance.HIST);
        string[] historyEntries = history.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string lastModifiedUser = string.Empty;

        foreach (string historyEntry in historyEntries)
            lastModifiedUser = element.EvaluateAsString(DbExpression.Parse($"SessU {historyEntry}")).ToLowerInvariant();

        return lastModifiedUser;
    }

    #endregion

    #region Безопасная очистка результатов ночной проверки
    private void CleanupNightCheckResults(SqlConnection clashConnection, string clashTableName, int initialClashCount, HashSet<string> failedZoneNames)
    {
        if (clashConnection.State != ConnectionState.Open)
            clashConnection.Open();

        List<ClashEntity> notConfirmedClashes = clashConnection.Query<ClashEntity>($@"
            SELECT {ClashSql}
            FROM [{clashTableName}]
            WHERE [XT] = 0;").ToList();

        double notConfirmedPercent = initialClashCount == 0
            ? 0
            : notConfirmedClashes.Count * 100.0 / initialClashCount;

        Logger.WriteLine($"Ночная проверка: строк до проверки {initialClashCount}; "
                       + $"XT = 0 после проверки {notConfirmedClashes.Count} ({notConfirmedPercent:F2}%); "
                       + $"зон с ошибками {failedZoneNames.Count}.");

        if (notConfirmedPercent > 25.0)
        {
            Logger.WriteLine("Ночная очистка отменена: доля XT = 0 превысила безопасный порог 25%. "
                            + "Строки оставлены с XT = 0 для разбора администратором.", LogType.Error);
            return;
        }

        int deletedCount = 0;
        int protectedCount = 0;
        int errorCount = 0;

        foreach (ClashEntity clash in notConfirmedClashes)
        {
            if (failedZoneNames.Contains(clash.FirstZone ?? string.Empty)
                || failedZoneNames.Contains(clash.SecondZone ?? string.Empty))
            {
                protectedCount++;
                continue;
            }

            try
            {
                RemoveClash(clashConnection, clashTableName, clash, "Коллизия не подтвердилась при ночной проверке");
                deletedCount++;
            }
            catch (Exception ex)
            {
                errorCount++;
                Logger.WriteLine($"Не удалось обработать коллизию {clash.Id}: {ex.Message}", LogType.Error);
            }
        }

        Logger.WriteLine($"Ночная очистка: обработано {deletedCount}; "
            + $"оставлено из-за ошибок зон {protectedCount}; ошибок SQL {errorCount}.");
    }

    #endregion

    #region Определение разработчика элемента
    private static string GetDesignerOrLastUser(DbElement element)
    {
        DbElement zone = element.GetZone();

        if (zone.IsNull || !zone.IsValid)
            return GetLastModifiedUser(element);

        DbAttribute designerAttribute = DbAttribute.GetDbAttribute(":Designer");
        string designerValue = zone.GetAsString(designerAttribute);

        if (!string.IsNullOrWhiteSpace(designerValue)
            && !string.Equals(designerValue.Trim(), "<Undefined>", StringComparison.OrdinalIgnoreCase))
        {
            string description = designerAttribute.GetAllowedUDAValueDescription(
                zone.ElementType,
                designerValue);

            if (!string.IsNullOrWhiteSpace(description))
            {
                description = description.Trim();
                int atIndex = description.IndexOf('@');

                if (atIndex > 0)
                    return description.Substring(0, atIndex).Trim();
            }
        }

        return GetLastModifiedUser(element);
    }

    #endregion

    #region Получение зоны элемента
    /// <summary>
    /// Возвращает имя зоны или пустую строку.
    /// </summary>
    private static string GetZoneName(DbElement element)
    {
        if (element == null || !element.IsValid || element.IsNull)
            return string.Empty;

        DbElement zone = element.GetZone();

        if (zone == null || !zone.IsValid || zone.IsNull)
            return string.Empty;

        return zone.Name();
    }
    #endregion

    #region Получение отдела из имени базы
    private static string GetDepartment(DbElement element)
    {
        string databaseName = element.GetAsString(DbAttributeInstance.DBNA);
        return databaseName.Split('/')[0];
    }

    #endregion

    #region Формирование ссылки элемента с поддержкой Tubing
    private static string GetClashElementReference(DbElement element)
    {
        string elementRef = element.GetAsString(DbAttributeInstance.REF);

        if (element.ElementType.Description.ToString() == "Tubing")
            return $"ileav tube of {elementRef}";

        return elementRef.Replace("ileav rod of", "ileav tube of");
    }


    #endregion

    #region Удаление или архивирование коллизии
    public void RemoveClash(SqlConnection clashConnection, string clashTableName, ClashEntity clash, string reason)
    {
        if (HasApproveOrInWork(clash))
        {
            MoveClashToHistory(clashConnection, clashTableName, clash.Id, reason);
            Logger.WriteLine($"Коллизия {clash.Id} перенесена в {clashTableName}_his. Причина: {reason}");
            return;
        }

        clashConnection.Execute($"DELETE FROM [{clashTableName}] WHERE [ID] = @id;", new { id = clash.Id });

        Logger.WriteLine($"Коллизия {clash.Id} удалена без переноса в History: принятие в работу и согласование отсутствуют. Причина: {reason}");
    }

    #endregion

    #region Проверка наличия принятия в работу или согласования
    private static bool HasApproveOrInWork(ClashEntity clash)
    {
        return !string.IsNullOrWhiteSpace(clash.InWorkUser) || clash.InWorkDate.HasValue
            || !string.IsNullOrWhiteSpace(clash.ApproveUser) || clash.ApproveDate.HasValue
            || !string.IsNullOrWhiteSpace(clash.ApproveReason);
    }

    #endregion

    #region Перенос коллизии в History
    private static void MoveClashToHistory(SqlConnection clashConnection, string clashTableName, int clashId, string historyReason)
    {
        string historyTableName = $"{clashTableName}_his";
        using SqlTransaction transaction = clashConnection.BeginTransaction();

        try
        {
            int insertedCount = clashConnection.Execute(
                $@"INSERT INTO [{historyTableName}]
                    (ID, GL, CT, R1, E1, U1, D1, G1,
                     R2, E2, U2, D2, G2, XT, DT, X0, Y0, Z0,
                     RT, RU, RD, AU, AD, AR, WU, WD,
                     HistoryDate, HistoryReason)
                    SELECT
                     ID, GL, CT, R1, E1, U1, D1, G1,
                     R2, E2, U2, D2, G2, XT, DT, X0, Y0, Z0,
                     RT, RU, RD, AU, AD, AR, WU, WD,
                     GETDATE(), @historyReason
                    FROM [{clashTableName}]
                    WHERE [ID] = @id;",
                new { id = clashId, historyReason },
                transaction);

            if (insertedCount != 1)
                throw new InvalidOperationException(
                    $"Коллизия {clashId} не была перенесена в {historyTableName}.");

            int deletedCount = clashConnection.Execute(
                $"DELETE FROM [{clashTableName}] WHERE [ID] = @id;",
                new { id = clashId },
                transaction);

            if (deletedCount != 1)
                throw new InvalidOperationException(
                    $"Коллизия {clashId} не была удалена из {clashTableName}.");

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    #endregion

    #region Проверка существования элементов коллизии
    private static bool HasMissingElement(ClashEntity clash)
    {
        DbElement firstElement = DbElement.GetElement(clash.FirstElement);
        DbElement secondElement = DbElement.GetElement(clash.SecondElement);
        return firstElement.IsNull || !firstElement.IsValid || secondElement.IsNull || !secondElement.IsValid;
    }

    #endregion

    #region Получение коллизий выбранного элемента и его содержимого
    public List<ClashEntity> QueryClashesByElement(SqlConnection clashConnection, string clashTableName, string elementRef)
    {
        DbElement rootElement = DbElement.GetElement(elementRef);
        // Для CE нужен как сам выбранный элемент, так и всё его содержимое.
        List<DbElement> elements = [rootElement, .. new DBElementCollection(rootElement).Cast<DbElement>()];
        var elementTable = new DataTable();
        elementTable.Columns.Add("ElRef", typeof(string));
        var uniqueElementRefs = new HashSet<string>();

        foreach (DbElement element in elements)
        {
            string currentElementRef;

            try
            {
                currentElementRef = GetClashElementReference(element);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentElementRef))
                continue;

            if (uniqueElementRefs.Add(currentElementRef))
                elementTable.Rows.Add(currentElementRef);
        }

        if (elementTable.Rows.Count == 0)
            return [];

        if (clashConnection.State != ConnectionState.Open)
            clashConnection.Open();

        clashConnection.Execute("IF OBJECT_ID('tempdb..#Elements') IS NOT NULL DROP TABLE #Elements; CREATE TABLE #Elements (ElRef NVARCHAR(64) NOT NULL);");

        using var bulkCopy = new SqlBulkCopy(clashConnection);
        bulkCopy.DestinationTableName = "#Elements";
        bulkCopy.ColumnMappings.Add("ElRef", "ElRef");
        bulkCopy.BatchSize = 5000;
        bulkCopy.BulkCopyTimeout = 0;
        bulkCopy.WriteToServer(elementTable);

        List<ClashEntity> clashes = clashConnection.Query<ClashEntity>($@"SELECT {ClashSql}
            FROM [{clashTableName}]
            WHERE [R1] IN (SELECT [ElRef] FROM #Elements)
               OR [R2] IN (SELECT [ElRef] FROM #Elements)").ToList();

        clashConnection.Execute("DROP TABLE #Elements;");
        return clashes;
    }
    #endregion

    #region Сопоставление результатов AVEVA с SQL
    private bool CheckResultToBase(SqlConnection sqlConnection, string clashTableName, ClashSet clashSet)
    {
        if (sqlConnection.State != ConnectionState.Open)
            sqlConnection.Open();
        Logger.WriteLine("Запись коллизий в базу...");
        Logger.WriteLine($"Количество коллизий: {clashSet.Clashes.Length}");
        var notIgnoredClashes = new List<Clash>(clashSet.Clashes.Length);
        int ignoredCount = 0;

        foreach (Clash clash in clashSet.Clashes)
        {
            if (IsClashIgnore(clash))
            {
                ignoredCount++;
                continue;
            }
            notIgnoredClashes.Add(clash);
        }
        Logger.WriteLine($"Количество проигнорированных коллизий: {ignoredCount}");

        try
        {
            sqlConnection.Execute("IF OBJECT_ID('tempdb..#pairs') IS NOT NULL DROP TABLE #pairs;");
            sqlConnection.Execute(@"
                            CREATE TABLE #pairs
                                  ( 
                                      ClashType NVARCHAR(20) NOT NULL,
                                      El1 NVARCHAR(100) NOT NULL,
                                      El2 NVARCHAR(100) NOT NULL,
                                      X INT NOT NULL,
                                      Y INT NOT NULL,
                                      Z INT NOT NULL

                                   );
                             ");
            sqlConnection.Execute($"CREATE INDEX IX_pairs on #pairs(ClashType,El1,El2,X,Y,Z)");
            var pairsTable = new DataTable();
            pairsTable.Columns.Add("ClashType", typeof(string));
            pairsTable.Columns.Add("El1", typeof(string));
            pairsTable.Columns.Add("El2", typeof(string));
            pairsTable.Columns.Add("X", typeof(int));
            pairsTable.Columns.Add("Y", typeof(int));
            pairsTable.Columns.Add("Z", typeof(int));

            foreach (Clash clash in notIgnoredClashes)
            {
                pairsTable.Rows.Add(GetSqlClashType(clash), GetClashElementReference(clash.First), GetClashElementReference(clash.Second),
                    (int)clash.ClashPosition.X, (int)clash.ClashPosition.Y, (int)clash.ClashPosition.Z);
            }

            using (var bulk = new SqlBulkCopy(sqlConnection))
            {
                bulk.DestinationTableName = "#pairs";
                bulk.ColumnMappings.Add("ClashType", "ClashType");
                bulk.ColumnMappings.Add("El1", "El1");
                bulk.ColumnMappings.Add("El2", "El2");
                bulk.ColumnMappings.Add("X", "X");
                bulk.ColumnMappings.Add("Y", "Y");
                bulk.ColumnMappings.Add("Z", "Z");
                bulk.WriteToServer(pairsTable);
            }
            var existingRows = sqlConnection.Query<ExistingRow>($@"
                    SELECT c.id AS Id,
                           c.[CT] AS ClashType,
                           c.[R1] AS El1,
                           c.[R2] AS El2,
                           c.[X0] AS X, c.[Y0] AS Y, c.[Z0] AS Z,
                           c.[XT] AS Existing
                    FROM [{clashTableName}] c
                    JOIN #pairs p
                    ON p.ClashType = c.[CT]
                    AND ((p.El1 = c.[R1] and p.El2 = c.[R2])
                    OR  (p.El1 = c.[R2] and p.El2 = c.[R1]))
                    AND (p.X = c.[X0] and p.Y = c.[Y0] and p.Z = c.[Z0])
                                                                 ").ToList();
            // Точные совпадения подтверждаются по типу, обеим ссылкам и координатам.
            var existingUpdate = sqlConnection.Execute($@"
                    UPDATE C
                    SET c.[XT] = 1
                    FROM [{clashTableName}] c
                    JOIN #pairs p
                    ON p.ClashType = c.[CT]
                    AND ((p.El1 = c.[R1] and p.El2 = c.[R2])
                    OR  (p.El1 = c.[R2] and p.El2 = c.[R1]))
                    AND (p.X = c.[X0] and p.Y = c.[Y0] and p.Z = c.[Z0])
                                                       ");

            var gensecCandidates = sqlConnection.Query<GensecCandidate>($@"
                 SELECT ID AS Id,R1 AS Ref1, E1 AS Type1, G1 AS Zone1, R2 AS Ref2, E2 AS Type2, G2 AS Zone2, X0 AS X, Y0 AS Y, Z0 AS Z
                 FROM [{clashTableName}]
                 WHERE XT = 0
                 AND (E1 IN ('GENSEC', 'PANEL') OR E2 IN ('GENSEC', 'PANEL'))
                 AND (NULLIF(RT, '') IS NOT NULL OR NULLIF(RU, '') IS NOT NULL OR RD IS NOT NULL
                 OR NULLIF(WU, '') IS NOT NULL OR WD IS NOT NULL
                 OR NULLIF(AU, '') IS NOT NULL OR AD IS NOT NULL OR NULLIF(AR, '') IS NOT NULL)")
                 .ToList();

            Logger.WriteLine($"Точно подтверждено строк SQL: {existingUpdate}");
            Logger.WriteLine($"Кандидатов GENSEC/PANEL для замены RefNo: {gensecCandidates.Count}");
            var ExistingByKey = new Dictionary<string, ExistingRow>(existingRows.Count * 2, StringComparer.Ordinal);

            foreach (var e in existingRows)
            {
                string k1 = MakeKey(e.ClashType, e.El1, e.El2, e.X, e.Y, e.Z);
                string k2 = MakeKey(e.ClashType, e.El2, e.El1, e.X, e.Y, e.Z);
                ExistingByKey[k1] = e;
                ExistingByKey[k2] = e;
            }

            var MakeGensecByKey = new Dictionary<string, GensecCandidate>(gensecCandidates.Count * 2, StringComparer.Ordinal);

            foreach (var g in gensecCandidates)
            {
                string k1 = MakeGensecKey(g.Zone1, g.Type1, g.Zone2, g.Type2, g.X, g.Y, g.Z);
                string k2 = MakeGensecKey(g.Zone2, g.Type2, g.Zone1, g.Type1, g.X, g.Y, g.Z);
                MakeGensecByKey[k1] = g;
                MakeGensecByKey[k2] = g;

            }
            var clashesToInsert = CreateClashInsertTable();
            int gensecRebindCount = 0;

            foreach (Clash clash in notIgnoredClashes)
            {
                string clashType = GetSqlClashType(clash);
                string firstElementRef = GetClashElementReference(clash.First);
                string secondElementRef = GetClashElementReference(clash.Second);
                int x = (int)clash.ClashPosition.X;
                int y = (int)clash.ClashPosition.Y;
                int z = (int)clash.ClashPosition.Z;
                string keyRef = MakeKey(clashType, firstElementRef, secondElementRef, x, y, z);

                if (ExistingByKey.ContainsKey(keyRef))
                    continue;

                string type1 = clash.First.ElementType.ToString();
                string type2 = clash.Second.ElementType.ToString();

                if (IsGensecOrPanel(type1) || IsGensecOrPanel(type2))
                {
                    // Для повторно импортированных GENSEC/PANEL сохраняем workflow старой строки и меняем только RefNo.
                    string zone1 = GetZoneName(clash.First);
                    string zone2 = GetZoneName(clash.Second);
                    string keyGensec = MakeGensecKey(zone1, type1, zone2, type2, x, y, z);

                    if (MakeGensecByKey.TryGetValue(keyGensec, out GensecCandidate row))
                    {
                        bool directOrder = row.Zone1 == zone1 && row.Type1 == type1;
                        if (directOrder)
                        {
                            if (IsGensecOrPanel(row.Type1))
                            {
                                sqlConnection.Execute($@"UPDATE [{clashTableName}] SET [R1] = @firstElementRef, [XT] = 1 WHERE ID = @id", new { firstElementRef, id = row.Id });
                                Logger.WriteLine($"Перепривязка ID={row.Id}, прямой порядок: R1 {row.Ref1} -> {firstElementRef}; тип {row.Type1}; зона {row.Zone1}.");
                            }

                            if (IsGensecOrPanel(row.Type2))
                            {
                                sqlConnection.Execute($@"UPDATE [{clashTableName}] SET [R2] = @secondElementRef, [XT] = 1 WHERE ID = @id", new { secondElementRef, id = row.Id });
                                Logger.WriteLine($"Перепривязка ID={row.Id}, прямой порядок: R2 {row.Ref2} -> {secondElementRef}; тип {row.Type2}; зона {row.Zone2}.");
                            }

                            gensecRebindCount++;
                            continue;
                        }

                        bool reverseOrder = row.Zone1 == zone2 && row.Type1 == type2;
                        if (reverseOrder)
                        {
                            if (IsGensecOrPanel(row.Type1))
                            {
                                sqlConnection.Execute($@"UPDATE [{clashTableName}] SET [R1] = @secondElementRef, [XT] = 1 WHERE ID = @id", new { secondElementRef, id = row.Id });
                                Logger.WriteLine($"Перепривязка ID={row.Id}, обратный порядок: R1 {row.Ref1} -> {secondElementRef}; тип {row.Type1}; зона {row.Zone1}.");
                            }

                            if (IsGensecOrPanel(row.Type2))
                            {
                                sqlConnection.Execute($@"UPDATE [{clashTableName}] SET [R2] = @firstElementRef, [XT] = 1 WHERE ID = @id", new { firstElementRef, id = row.Id });
                                Logger.WriteLine($"Перепривязка ID={row.Id}, обратный порядок: R2 {row.Ref2} -> {firstElementRef}; тип {row.Type2}; зона {row.Zone2}.");
                            }

                            gensecRebindCount++;
                            continue;
                        }
                    }
                }

                AddClashToInsertTable(clash, clashesToInsert, x, y, z);
            }

            Logger.WriteLine($"Перепривязано строк GENSEC/PANEL: {gensecRebindCount}");
            Logger.WriteLine($"Новых коллизий подготовлено к записи: {clashesToInsert.Rows.Count}");

            BulkInsertClashes(sqlConnection, clashesToInsert, clashTableName);

            PML.CreateCommand(
                $"$p Результат проверки: найдено {notIgnoredClashes.Count}, " +
                $"подтверждено точных {existingUpdate}, " +
                $"перепривязано GENSEC/PANEL {gensecRebindCount}, " +
                $"добавлено новых {clashesToInsert.Rows.Count}")
                .RunInPdms();

            return true;

        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Ошибка в CheckResultToBase {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                sqlConnection.Execute("IF OBJECT_ID('tempdb..#pairs') IS NOT NULL DROP TABLE #pairs;");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Не удалось удалить временную таблицу #pairs: {ex.Message}");
            }

        }
    }


    #endregion

    #region Создание таблицы данных для Bulk Insert
    private static DataTable CreateClashInsertTable()
    {
        var table = new DataTable();
        table.Columns.Add("Building", typeof(string));
        table.Columns.Add("ClashType", typeof(string));
        table.Columns.Add("El1", typeof(string));
        table.Columns.Add("Type1", typeof(string));
        table.Columns.Add("Usermod1", typeof(string));
        table.Columns.Add("Dept1", typeof(string));
        table.Columns.Add("Zone1", typeof(string));

        table.Columns.Add("El2", typeof(string));
        table.Columns.Add("Type2", typeof(string));
        table.Columns.Add("Usermod2", typeof(string));
        table.Columns.Add("Dept2", typeof(string));
        table.Columns.Add("Zone2", typeof(string));

        table.Columns.Add("Date", typeof(DateTime));
        table.Columns.Add("X", typeof(int));
        table.Columns.Add("Y", typeof(int));
        table.Columns.Add("Z", typeof(int));
        table.Columns.Add("Existing", typeof(bool));
        return table;
    }

    #endregion

    #region Подготовка новой коллизии к записи
    private void AddClashToInsertTable(Clash clash, DataTable table, int x, int y, int z)
    {
        string clashType = GetSqlClashType(clash);
        var firstElement = GetClashElementReference(clash.First);
        var secondElement = GetClashElementReference(clash.Second);
        var firstType = clash.First.ElementType.ToString();
        var secondType = clash.Second.ElementType.ToString();
        var building = GetBuildingCode(clash.First);
        var firstDepartment = GetDepartment(clash.First);
        var secondDepartment = GetDepartment(clash.Second);

        var firstZone = GetZoneName(clash.First);
        var secondZone = GetZoneName(clash.Second);

        var firstUser = GetDesignerOrLastUser(clash.First);
        var secondUser = GetDesignerOrLastUser(clash.Second);

        DataRow row = table.NewRow();
        row["Building"] = Truncate(building, 12) ?? string.Empty;
        row["ClashType"] = Truncate(clashType, 12);
        row["El1"] = Truncate(firstElement, 64);
        row["Type1"] = Truncate(firstType, 12);
        row["Usermod1"] = Truncate(firstUser, 32);
        row["Dept1"] = (object?)Truncate(firstDepartment, 16) ?? DBNull.Value;
        row["Zone1"] = (object?)Truncate(firstZone, 64) ?? DBNull.Value;

        row["El2"] = Truncate(secondElement, 64);
        row["Type2"] = Truncate(secondType, 12);
        row["Usermod2"] = Truncate(secondUser, 32);
        row["Dept2"] = (object?)Truncate(secondDepartment, 16) ?? DBNull.Value;
        row["Zone2"] = (object?)Truncate(secondZone, 64) ?? DBNull.Value;

        row["Date"] = DateTime.Now;
        row["X"] = x;
        row["Y"] = y;
        row["Z"] = z;
        row["Existing"] = true;
        table.Rows.Add(row);
    }
    #endregion

    #region Ограничение длины строк для SQL
    private static string? Truncate(string? value, int maxLength)
    {
        if (value == null || value.Length == 0) return null;
        return value.Length > maxLength ? value.Substring(0, maxLength) : value;
    }
    #endregion

    #region Пакетная запись новых коллизий
    private static void BulkInsertClashes(SqlConnection sqlConnection, DataTable table, string clashTableName)
    {
        if (table.Rows.Count == 0)
            return;

        using var bulkCopy = new SqlBulkCopy(sqlConnection);
        bulkCopy.DestinationTableName = $"[{clashTableName}]";
        bulkCopy.ColumnMappings.Add("Building", "GL");
        bulkCopy.ColumnMappings.Add("ClashType", "CT");
        bulkCopy.ColumnMappings.Add("El1", "R1");
        bulkCopy.ColumnMappings.Add("Type1", "E1");
        bulkCopy.ColumnMappings.Add("Usermod1", "U1");
        bulkCopy.ColumnMappings.Add("Dept1", "D1");
        bulkCopy.ColumnMappings.Add("Zone1", "G1");

        bulkCopy.ColumnMappings.Add("El2", "R2");
        bulkCopy.ColumnMappings.Add("Type2", "E2");
        bulkCopy.ColumnMappings.Add("Usermod2", "U2");
        bulkCopy.ColumnMappings.Add("Dept2", "D2");
        bulkCopy.ColumnMappings.Add("Zone2", "G2");

        bulkCopy.ColumnMappings.Add("Date", "DT");
        bulkCopy.ColumnMappings.Add("X", "X0");
        bulkCopy.ColumnMappings.Add("Y", "Y0");
        bulkCopy.ColumnMappings.Add("Z", "Z0");
        bulkCopy.ColumnMappings.Add("Existing", "XT");
        bulkCopy.WriteToServer(table);
    }
    #endregion

    #region Применение правил игнорирования коллизий
    /// <summary>
    /// Возвращает true, если коллизию следует игнорировать
    /// </summary>
    /// <param name="clash"></param>
    /// <returns></returns>
    private bool IsClashIgnore(Clash clash)
    {
        try
        {
            //if (CheckPipeWithJntc(clash))
            // return true;
            //if (CheckHangWithBranch(clash))
            //return true;
            //if (CheckRestWithStlr(clash))
            //return true;
            //if (CheckBranWithFrameWork(clash))
            //return true;


            return false;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Ошибка IsClashIgnore: {ex.Message}", LogType.Error);
            return false;
        }
    }

    #endregion

    #region Фильтр пересечения BRANCH и FRAMEWORK
    private bool CheckBranWithFrameWork(Clash clash)
    {
        DbElement firstCheckRef;
        DbElement secondCheckRef;
        for (int i = 0; i < 2; i++)
        {
            if (i == 0)
            {
                firstCheckRef = clash.First;
                secondCheckRef = clash.Second;
            }
            else
            {
                firstCheckRef = clash.Second;
                secondCheckRef = clash.First;
            }
            if (!firstCheckRef.TryGetOwnerByType(DbElementTypeInstance.BRANCH, out DbElement firstBran) && firstBran.ElementType == DbElementTypeInstance.BRANCH)
                firstBran = firstCheckRef;
            if (firstBran.IsNull)
                break;
            DbElement secondBran = DbElement.GetElement("");
            if ((secondCheckRef.Owner.ElementType == DbElementTypeInstance.FRMWORK || secondCheckRef.Owner.ElementType == DbElementTypeInstance.SBFRAMEWORK) && secondCheckRef.TryGetOwnerByType(DbElementTypeInstance.FRMWORK, out DbElement frmw))
            {
                try
                {
                    var mem = frmw.GetElement(DbAttributeInstance.SUPR);
                    if (!mem.IsNull)
                    {
                        secondBran = frmw.GetElement(DbAttributeInstance.SUPR).Members().First().GetElement(DbAttributeInstance.HREF).Owner;
                    }


                }
                catch (Exception)
                {
                    continue;
                }
            }
            if (secondBran.IsNull)
                break;

            if (firstBran == secondBran)
                return true;

        }

        return false;


    }


    #endregion

    #region Фильтр пересечения RESTRAINT и STLR
    private bool CheckRestWithStlr(Clash clash)
    {
        DbElement hmem = NullElement;
        if (clash.First.TryGetOwnerByType(DbElementTypeInstance.FRMWORK, out DbElement frameWork))
            hmem = clash.Second;
        else if (clash.Second.TryGetOwnerByType(DbElementTypeInstance.FRMWORK, out frameWork))
            hmem = clash.First;

        if (!frameWork.IsNull && !hmem.IsNull && hmem.TryGetOwnerByType(DbElementTypeInstance.RESTRAINT, out DbElement rest) && rest.GetElement(DbAttributeInstance.STLR) == frameWork)
            return true;

        return false;
    }

    #endregion

    #region Фильтр соединения PIPE по JNTC
    /// <summary>
    /// Проверка PIPE и её футляра
    /// </summary>
    private bool CheckPipeWithJntc(Clash clash)
    {
        DbElement firstPipe = NullElement;
        DbElement secondPipe = NullElement;

        if (clash.First.Owner.Owner.ElementType == DbElementTypeInstance.PIPE)
            firstPipe = clash.First.Owner.Owner;
        else if (clash.First.Owner.ElementType == DbElementTypeInstance.PIPE)
            firstPipe = clash.First.Owner.Owner;

        if (clash.Second.Owner.Owner.ElementType == DbElementTypeInstance.PIPE)
            secondPipe = clash.Second.Owner.Owner;
        else if (clash.Second.Owner.ElementType == DbElementTypeInstance.PIPE)
            secondPipe = clash.Second.Owner.Owner;
        if (!firstPipe.IsNull && !secondPipe.IsNull)
        {
            var firstDrrf = firstPipe.GetElement(DbAttributeInstance.DRRF);
            var secondDrrf = secondPipe.GetElement(DbAttributeInstance.DRRF);
            var firstJntc = firstPipe.GetDbDouble(DbAttributeInstance.JNTC).Value;
            var secondJntc = secondPipe.GetDbDouble(DbAttributeInstance.JNTC).Value;
            if (!firstDrrf.IsNull && firstJntc == 2 && secondJntc == 1 && firstDrrf == secondPipe)
                return true;
            else if (!secondDrrf.IsNull && secondJntc == 2 && firstJntc == 1 && secondDrrf == firstPipe)
                return true;

        }

        return false;

    }

    #endregion

    #region Фильтр пересечения HANGER и BRANCH
    /// <summary>
    /// Проверка подвески с её бранчем
    /// </summary>
    /// <param name="clash"></param>
    /// <returns></returns>
    private bool CheckHangWithBranch(Clash clash)
    {
        DbElement hMem;
        DbElement bMem;
        if (clash.First.Owner.ElementType == DbElementTypeInstance.HANGER && (clash.Second.Owner.ElementType == DbElementTypeInstance.BRANCH || clash.Second.ElementType == DbElementTypeInstance.BRANCH))
        {
            hMem = clash.First;
            bMem = clash.Second;
        }
        else if (clash.Second.Owner.ElementType == DbElementTypeInstance.HANGER && (clash.First.Owner.ElementType == DbElementTypeInstance.BRANCH || clash.First.ElementType == DbElementTypeInstance.BRANCH))
        {
            hMem = clash.Second;
            bMem = clash.First;
        }
        else
        {
            return false;
        }

        if (!hMem.TryGetOwnerByType(DbElementTypeInstance.HANGER, out DbElement hanger))
            return false;
        try
        {
            var b1 = hanger.Owner.Members().First().GetElement(DbAttributeInstance.HREF).Owner;
            if (b1.IsNull)
                return false;
            if (!bMem.TryGetOwnerByType(DbElementTypeInstance.BRANCH, out DbElement bran))
                return false;
            if (b1 == bran)
                return true;
        }
        catch (Exception)
        {
            return false;
        }



        return true;
    }
    #endregion




    #region Преобразование типа коллизии в Hard или Soft
    private static string GetSqlClashType(Clash clash)
    {
        return string.Equals(clash.Type.ToString(), "HH", StringComparison.OrdinalIgnoreCase)
            ? "Hard"
            : "Soft";
    }

    #endregion

    #region Определение импортируемых элементов GENSEC и PANEL
    private static bool IsGensecOrPanel(string elementType)
    {
        return string.Equals(elementType, "GENSEC", StringComparison.OrdinalIgnoreCase) || string.Equals(elementType, "PANEL", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Формирование ключа перепривязки GENSEC и PANEL
    private static string MakeGensecKey(string z1, string t1, string z2, string t2, int x, int y, int z)
    {
        return $"{z1}|{t1}|{x}|{y}|{z}|{z2}|{t2}";
    }

    #endregion

    #region Формирование точного ключа коллизии
    public static string MakeKey(string clashType, string el1, string el2, int X, int Y, int Z)
        => $"{clashType}|{el1}|{el2}|{X}|{Y}|{Z}";
    #endregion

}

public class ExistingRow
{
    public int Id { get; set; }
    public string ClashType { get; set; } = "";
    public string El1 { get; set; } = "";
    public string El2 { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Existing { get; set; }
}
public class GensecCandidate
{
    public int Id { get; set; }

    public string Ref1 { get; set; } = "";
    public string Type1 { get; set; } = "";
    public string Zone1 { get; set; } = "";

    public string Ref2 { get; set; } = "";
    public string Type2 { get; set; } = "";
    public string Zone2 { get; set; } = "";

    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}
