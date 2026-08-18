using Aveva.ClashChecker.NetCallable;
using Aveva.ClashChecker.NetCallable.Extensions;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.PMLNet;
using Aveva.Core3D.Clasher;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
//using System.Windows.Forms;
using static Aveva.ClashChecker.NetCallable.Exceptions;
using PML = Aveva.Core.Utilities.CommandLine.Command;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;

namespace ClashChecker;

/// <summary>
/// Класс для обработки коллизий
/// </summary>
[PMLNetCallable]
public partial class ClashChecker
{

    [PMLNetCallable]
    public ClashChecker()
    {
    }


    // private static readonly HashSet<string> SpecProj = ["SVB", "DNS", "WXT"];
    public string ClashConnectionString { get; set; } = "Data Source=10.177.6.99,1433;Initial Catalog=avevaclash;Persist Security Info=True;User ID=ClashAdmin;Password=AXBqMLz3mVER;Connection Timeout = 300;TrustServerCertificate=true";
    public static readonly string TDMSConnectionString = "Data Source=sqltep;Initial Catalog=TDMS_TEP;Persist Security Info=True;User ID=Pdmstotdms;Password=PdMsToTdMs;Connection Timeout = 300;TrustServerCertificate=true";
    private static readonly DbElement NullElement = DbElement.GetElement("*");


    private static readonly string ClashSql =
    $"ID '{nameof(ClashEntity.Id)}', " +
    $"GL '{nameof(ClashEntity.Building)}', " +
    $"CT '{nameof(ClashEntity.ClashType)}', " +
    $"R1 '{nameof(ClashEntity.FirstElement)}', " +
    $"E1 '{nameof(ClashEntity.FirstType)}', " +
    $"U1 '{nameof(ClashEntity.FirstUserMode)}', " +
    //$"Flnm1 '{nameof(ClashEntity.Flnm1)}', " +
    $"D1 '{nameof(ClashEntity.FirstDept)}', " +
    $"G1 '{nameof(ClashEntity.FirstZone)}', " +
    $"R2 '{nameof(ClashEntity.SecondElement)}', " +
    $"E2 '{nameof(ClashEntity.SecondType)}', " +
    $"U2 '{nameof(ClashEntity.SecondUserMode)}', " +
    //$"Flnm2 '{nameof(ClashEntity.Flnm2)}', " +
    $"XT '{nameof(ClashEntity.Existing)}', " +
    $"D2 '{nameof(ClashEntity.SecondDept)}', " +
    $"G2 '{nameof(ClashEntity.SecondZone)}', " +
    $"DT '{nameof(ClashEntity.Date)}', " +
    $"X0 '{nameof(ClashEntity.X)}', " +
    $"Y0 '{nameof(ClashEntity.Y)}', " +
    $"Z0 '{nameof(ClashEntity.Z)}', " +

    //$"Sequence '{nameof(ClashEntity.Sequence)}', " +
    //$"Building '{nameof(ClashEntity.Building)}', " +
    $"RT '{nameof(ClashEntity.RequestToDept)}', " +
    $"RU '{nameof(ClashEntity.RequestUser)}', " +
    $"RD '{nameof(ClashEntity.RequestDate)}', " +
    $"AU '{nameof(ClashEntity.ApproveUser)}', " +
    $"AD '{nameof(ClashEntity.ApproveDate)}', " +
    $"AR '{nameof(ClashEntity.ApproveReason)}', " +
    $"WU '{nameof(ClashEntity.InWorkUser)}', " +
    $"WD '{nameof(ClashEntity.InWorkDate)}'";


    private const string DefaultLogDirectoryPath = "C:\\AVEVA\\ClasherLogs\\ClashLog.log";
    private ClashLogger Logger { get; set; } = new ClashLogger(DefaultLogDirectoryPath);

    /// <summary>
    /// Стандартная конструкция для детекта класса авевой
    /// </summary>
    /// <param name="that"></param>
    [PMLNetCallable]
    public void Assign(ClashChecker that)
    {
    }

    /// <summary>
    /// Точка входа
    /// </summary>
    [PMLNetCallable]
    public void CheckAll(string obstType, double initialZoneIndex, bool testMode = true, string logDirectoryPath = DefaultLogDirectoryPath)
    {
        try
        {

            Logger = new ClashLogger(logDirectoryPath);
            Logger.LogInPdmsConsole = testMode;
            int initialZoneIndexInt = 0;

            try
            {
                initialZoneIndexInt = (int)initialZoneIndex;

            }
            catch (Exception ex)
            {
                Logger.WriteLine("Не удалось распознать начальный индекс зоны! Индексация будет начата с 0");
            }
            DbElement world = DbElement.GetElement("*");

            //PML.CreateCommand("MAP BUILD MDB").RunInPdms();
            //PML.CreateCommand("SaveWork").RunInPdms();

            var projectCode = Project.CurrentProject.Name;
            if (testMode)
                projectCode += "_TEST";
            string clashTableName = $"clashtable{projectCode}";


            string ifcTableName = $"tableIfc{projectCode}";
            string clashRefUpdateLog = $"Clash{projectCode}_RefUpdateLog";

            Logger.WriteLine("Начало выполнения проверки...");
            Logger.WriteLine($"Тип коллизий: {obstType}. Проект: {projectCode}");

            using SqlConnection clashConnection = new(ClashConnectionString);
            clashConnection.Open();

            CreateClashDbIfNotExist(clashTableName, clashConnection);

            // ReplaceRefIFC(clashConnection, clashTableName, ifcTableName, clashRefUpdateLog, projectCode);

            UpdateClashElementInfo(clashConnection, "FULL", clashTableName, string.Empty);
            Logger.WriteLine("Выполнен UpdateClashElementInfo");
            clashConnection.Execute($"UPDATE [{clashTableName}] SET [XT] = 0");

            string initialClashesLogString = $"Коллизий до проверки: {clashConnection.ExecuteScalar<int>($"select top 1 COUNT(*) from {clashTableName}")}";

            Logger.WriteLine(initialClashesLogString);
            ColZone(clashConnection, initialZoneIndexInt, clashTableName, "");
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message, LogType.Error);
            Logger.FinishLog();

            return;
        }

    }

    [PMLNetCallable]
    public string ClashAllTest(bool useBoxCheck = false)
    {
        try
        {
            var clashOptions = ClashOptions.Create();
            clashOptions.Override = true;
            clashOptions.Midpoint = true;
            clashOptions.TouchGap = 0.0;
            clashOptions.TouchOverlap = 2.0;
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
                DbElementTypeInstance.VOLMODEL
            ]);

            var obstructionList = ObstructionList.Create();
            obstructionList.AllObstructions = true;

            var clashSet = ClashSet.Create();
            var stopwatch = Stopwatch.StartNew();

            bool success = useBoxCheck
                ? Clasher.Instance.BoxCheckAll(clashOptions, obstructionList, clashSet)
                : Clasher.Instance.CheckAll(clashOptions, obstructionList, clashSet);

            stopwatch.Stop();

            var clashes = clashSet.Clashes ?? [];
            var clashesByType = clashes
                .GroupBy(clash => clash.Type)
                .OrderBy(group => group.Key.ToString())
                .Select(group => $"{group.Key}: {group.Count()}");

            string result =
                $"Режим: {(useBoxCheck ? "BoxCheckAll" : "CheckAll")}; " +
                $"Успешно: {success}; " +
                $"Коллизий: {clashes.Length}; " +
                $"Время: {stopwatch.Elapsed}; " +
                $"Типы: {string.Join(", ", clashesByType)}";

            Logger.WriteLine(result);
            return result;
        }
        catch (Exception ex)
        {
            string error = $"ClashAllTest: {ex.Message}";
            Logger.WriteLine(error, LogType.Error);
            return error;
        }
    }

    public void ColZone(SqlConnection clashConnection, int initialZoneIndex, string clashTableName, string zoneRef)
    {
        try
        {


            List<DbElement> zones;

            zones = [.. new DBElementCollection(new TypeFilter(DbElementTypeInstance.ZONE)).Cast<DbElement>().Where(e =>
                {
                   DbElement site = e.Owner;
                   string siteName = site.Name();
                   if(siteName.Contains(".G")  || e.GetBool(DbAttribute.GetDbAttribute(":ClashIgnore")) || e.Name().Contains("1300-30UHJ-E003-TD"))
                       return false;
                   return true;

                })];


            Logger.WriteLine($"Всего зон {zones.Count} шт");

            var zoneWvolumes = zones.Select(e => e.GetDoubleArray(DbAttributeInstance.WVOL)).ToList();
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

            int zoneCount = zones.Count;
            GC.Collect();
            if (string.IsNullOrEmpty(zoneRef))
            {
                for (int i = initialZoneIndex; i < zones.Count; i++)
                {
                    if (IsBoilerZone(zones[i]))
                        continue;
                    if (zoneWvolumes[i].Length < 6)
                        continue;
                    var obstructionList = ObstructionList.Create();
                 
                    for (int j = i; j < zones.Count; j++)
                    {
                        if (zoneWvolumes[j].Length < 6)
                            continue;
                       //if (IsSomeGESite(zones[i], zones[j]))
                       //    continue;
                        if (WvolClash(zoneWvolumes[i], zoneWvolumes[j]))
                            obstructionList.AddObstructions([zones[j]]);

                    }
                    CheckZone(zones[i], obstructionList, clashOptions, clashConnection, clashTableName, i, zoneCount);
                }
            }
            else
            {
                var selectedZone = DbElement.GetElement(zoneRef);
                var selectedZoneWvol = selectedZone.GetDoubleArray(DbAttributeInstance.WVOL);
                var obstructionList = ObstructionList.Create();
                for (int i = 0; i < zones.Count; i++)
                {
                    if (zoneWvolumes[i].Length < 6)
                        continue;
                    if (WvolClash(selectedZoneWvol, zoneWvolumes[i]))
                        obstructionList.AddObstructions([zones[i]]);

                }

                int selectedZoneIndex = zones.FindIndex(zone => zone.Ref == selectedZone.Ref);
                CheckZone(selectedZone, obstructionList, clashOptions, clashConnection, clashTableName, selectedZoneIndex, zoneCount);
            }

            clashConnection.Close();
            Logger.WriteLine("Обработка завершена!");
            Logger.FinishLog();
            return;
        }


        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message, LogType.Error);
            Logger.FinishLog();

            return;
        }

    }
    private bool IsBoilerZone(DbElement zone)
    {
        return zone.GetAsString(DbAttributeInstance.DBNA).Contains("KPIGE");
    }
    private void CheckZone(DbElement zone,
                           ObstructionList obstructionList,
                           ClashOptions clashOptions,
                           SqlConnection clashConnection,
                           string clashTableName,
                           int index,
                           int zoneCount)
    {
        var clashSet = ClashSet.Create();
        Logger.WriteLine($"Начата проверка зоны {zone.Name()} [{index + 1}/{zoneCount}] ...");
        var checkResult = Clasher.Instance.Check([zone], clashOptions, obstructionList, clashSet);
        if (!checkResult)
        {
            Logger.WriteLine($"Desclash не удалось проверить зону {zone.Name()}! Переход к следующей зоне...");

            return;
        }
        Logger.WriteLine($"Зона {zone.Name()} Проверена!");

        CheckResultToBase(clashConnection, clashTableName, clashSet);
    }

    private static Dictionary<string, string> RequiredClashTableColumns = new Dictionary<string, string>
    {
        ["ID"] = "INT NOT NULL IDENTITY(100000,1)",
        ["GL"] = "NVARCHAR(12) NOT NULL",
        ["CT"] = "NVARCHAR(12) NOT NULL",
        ["R1"] = "NVARCHAR(24) NOT NULL",
        ["E1"] = "NVARCHAR(12) NOT NULL",
        ["U1"] = "NVARCHAR(32) NOT NULL",
        ["D1"] = "NVARCHAR(16)",
        ["G1"] = "NVARCHAR(16)",
        ["R2"] = "NVARCHAR(24) NOT NULL",
        ["E2"] = "NVARCHAR(12) NOT NULL",
        ["U2"] = "NVARCHAR(32) NOT NULL",
        ["D2"] = "NVARCHAR(16)",
        ["G2"] = "NVARCHAR(16)",
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


    private static Dictionary<string, string> RequiredHistoryClashTableColumns = new Dictionary<string, string>
    {

        ["id"] = "INT NOT NULL",
        ["LogDate"] = "DATETIME NOT NULL DEFAULT GETDATE()",
        ["LoginName"] = "NVARCHAR(50)",
        ["ActionType"] = "NVARCHAR(20)",
        ["Comment"] = "NVARCHAR(100)"
    };



    private void CreateClashDbIfNotExist(string clashTableName, SqlConnection clashConnection)
    {
        if (!clashConnection.TableExists(clashTableName))
        {
            Logger.WriteLine($@"Таблица {clashTableName} не найдена! Создание таблицы...");
            clashConnection.Execute($@"CREATE TABLE [{clashTableName}]( [ID] INT NOT NULL IDENTITY (100000,1), 
                [GL] NVARCHAR(12) NOT NULL,  
                [CT] NVARCHAR(12) NOT NULL,
                [R1] NVARCHAR(24) NOT NULL,
                [E1] NVARCHAR(12) NOT NULL,
                [U1] NVARCHAR(32) NOT NULL,
                [D1] NVARCHAR(16),
                [G1] NVARCHAR(16),
                [R2] NVARCHAR(24) NOT NULL,
                [E2] NVARCHAR(12) NOT NULL,
                [U2] NVARCHAR(32) NOT NULL,
                [D2] NVARCHAR(16),
                [G2] NVARCHAR(16),  
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

            //Logger.WriteLine($"Таблица {clashTableName} создана!");
            //Logger.WriteLine($"Генерация индексов...");
            //
            //clashConnection.Execute($"CREATE INDEX id_ind ON {clashTableName}(id)");
            //clashConnection.Execute($"CREATE INDEX el1_ind ON {clashTableName}(el1)");
            //clashConnection.Execute($"CREATE INDEX el2_ind ON {clashTableName}(el2)");
            //clashConnection.Execute($"CREATE INDEX dept1_ind ON {clashTableName}(dept1)");
            //clashConnection.Execute($"CREATE INDEX dept2_ind ON {clashTableName}(dept2)");
            //clashConnection.Execute($"CREATE INDEX gpset1_ind ON {clashTableName}(gpset1)");
            //clashConnection.Execute($"CREATE INDEX gpset2_ind ON {clashTableName}(gpset2)");
            //clashConnection.Execute($"CREATE INDEX Sequence_ind ON {clashTableName}(Sequence)");
            //clashConnection.Execute($"CREATE INDEX Building_ind ON {clashTableName}(Building)");
            //Logger.WriteLine($"Индексы для {clashTableName} сформированы!");
        }
        else
        {
            // Получаем существующие столбцы
            var existingColumns = clashConnection.Query<string>(
                @$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS 
                  WHERE TABLE_NAME = '{clashTableName}'").ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingColumns = RequiredClashTableColumns.Where(c => !existingColumns.Contains(c.Key)).ToList();

            if (missingColumns.Any())
                foreach (var column in missingColumns)
                    AddColumn(clashConnection, clashTableName, column.Key, column.Value);
        }

        string historyTableName = $"{clashTableName}_his";
        if (!clashConnection.TableExists(historyTableName))
        {
            Logger.WriteLine($"Таблица {historyTableName} не найдена! Создание таблицы...");

            clashConnection.Execute(@$"CREATE TABLE[{historyTableName}](
            [id] INT NOT NULL,
            [LogDate] DATETIME NOT NULL DEFAULT GETDATE(),
            [LoginName] NVARCHAR(50), 
            [ActionType] NVARCHAR(20),
            [Comment] NVARCHAR(100));");


            Logger.WriteLine($"Таблица {historyTableName} создана!");
            Logger.WriteLine($"Генерация индексов...");
            clashConnection.Execute($"CREATE INDEX id_ind ON {historyTableName}(id)");
            Logger.WriteLine($"Индексы для {historyTableName} сформированы!");
        }
        else
        {
            var existingColumns = clashConnection.Query<string>(
                @$"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS 
                  WHERE TABLE_NAME = '{historyTableName}'").ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingColumns = RequiredHistoryClashTableColumns.Where(c => !existingColumns.Contains(c.Key)).ToList();
            if (missingColumns.Any())
                foreach (var column in missingColumns)
                    AddColumn(clashConnection, historyTableName, column.Key, column.Value);
        }
    }


    /// <summary>
    /// Добавляет отдельный столбец в таблицу
    /// </summary>
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


    /// <summary>
    /// Обновляет таблицу tableIfc{ProjName}
    /// </summary>
    /// <param name="projectName"></param>
    /// <summary>
    /// Обновляет данные по коллизиям выбранной зоны или всех зон.
    /// </summary>
    public string UpdateClashElementInfo(SqlConnection clashConnection, string checkMode, string clashTablename, string zoneName)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            int deleteCount = 0;
            int updateCount = 0;
            int totalCount = 0;

            List<ClashEntity> clashes = [];

            if (zoneName != string.Empty)
            {
                var clashesByZone = clashConnection.Query<ClashEntity>($"SELECT {ClashSql} FROM [{clashTablename}] WHERE ([G1] = @zoneName OR [G2] = @zoneName)", new { zoneName }).ToList();
                var clashesByZoneElement = QueryClashByEl(clashConnection, clashTablename, zoneName);
                HashSet<int> ids = [.. clashesByZone.Select(e => e.Id)];
                clashes = [.. clashesByZone.Union(clashesByZoneElement.Where(e => !ids.Contains(e.Id)))];

            }
            else
            {
                clashes = [.. clashConnection.Query<ClashEntity>($"SELECT {ClashSql} from {clashTablename}")];
            }


            foreach (ClashEntity clash in clashes)
            {
                switch (UpdateOneClashElementInfo(clashConnection, clash, clashTablename, checkMode))
                {
                    case 1:
                        updateCount++;
                        break;
                    case -1:
                        deleteCount++;
                        break;
                    case 0:
                        break;
                }
                totalCount++;
            }

            stopwatch.Stop();
            var ellapsedSeconds = stopwatch.ElapsedMilliseconds * 0.001;
            return $"{ellapsedSeconds};{zoneName};{totalCount};{deleteCount};{updateCount};{checkMode}";
        }
        catch (Exception ex)
        {
            return ConvertExceptionToPmlMessage(ex, "");
        }
    }

    /// <summary>
    /// Проверяет вхождние одого объёма в другой
    /// </summary>
    public bool WvolClash(double[] volume1, double[] volume2)
    {
        return !(volume1[0] > volume2[3] || volume1[3] < volume2[0]) && !(volume1[1] > volume2[4] || volume1[4] < volume2[1]) && !(volume1[2] > volume2[5] || volume1[5] < volume2[2]);
    }

    /// <summary>
    /// Обновляет данные по коллизиям (по отдельным элементам)
    /// <returns>
    /// 0 - если обновление не требовалось(NONE)
    /// 1 - если было призведено обновление(UPDATE)
    /// -1 - если удалили коллизию(DELETE)
    /// </returns>
    /// </summary>
    public short UpdateOneClashElementInfo(SqlConnection clashConnection, ClashEntity clash, string tableName, string checkMode)
    {
        short retval = 0;
        try
        {
            if (IsNeedToDeleteClashSimple(clash))
            {

                string comment = "UpdateClashElementInfo один из элементов уже не существует";
                string type = "badref";
                DeleteById(clashConnection, tableName, clash, type, comment);
                retval = -1;

            }
            else
            {
                //если оба существуют то обновляем информацию если отличается
                string lastModifiedUserMod1;
                string lastModifiedUserMod2;
                var dbElem1 = DbElement.GetElement(clash.FirstElement);
                var dbElem2 = DbElement.GetElement(clash.SecondElement);


                string RealDept1 = GetDepartment(dbElem1, "");
                string RealDept2 = GetDepartment(dbElem2, "");
                string RealZone1 = GetZoneName(dbElem1);
                string RealZone2 = GetZoneName(dbElem2);
                string build = GetBuild(dbElem1);
                if (build == "") build = GetBuild(dbElem2);


                if (checkMode == "FULL")
                {
                    lastModifiedUserMod1 = History(dbElem1, "user");
                    lastModifiedUserMod2 = History(dbElem2, "user");
                }
                else
                {
                    lastModifiedUserMod1 = clash.FirstUserMode;
                    lastModifiedUserMod2 = clash.SecondUserMode;
                }
                if (clash.FirstUserMode != lastModifiedUserMod1 || clash.SecondUserMode != lastModifiedUserMod2 || clash.FirstZone != RealZone1 || clash.SecondZone != RealZone2 || clash.FirstDept != RealDept1 || clash.SecondDept != RealDept2 || clash.Building != build)
                {

                    var changes = new List<(bool rules, string message)>
                    {
                        (clash.FirstUserMode != lastModifiedUserMod1, $"FirstUserMode:{clash.FirstUserMode}->{lastModifiedUserMod1}"),
                        (clash.FirstDept != RealDept1,        $"FirstDept:{clash.FirstDept}->{RealDept1}"),
                        (clash.FirstZone != RealZone1,        $"FirstZone:{clash.FirstZone}->{RealZone1}"),
                        (clash.SecondUserMode != lastModifiedUserMod2,$"SecondUserMode:{clash.SecondUserMode}->{lastModifiedUserMod2}"),
                        (clash.SecondDept != RealDept2,       $"SecondDept:{clash.SecondDept}->{RealDept2}"),
                        (clash.SecondZone != RealZone2,       $"SecondZone:{clash.SecondZone}->{RealZone2}"),
                        (clash.Building != build,     $"Building:{clash.Building}->{build}")
                    };

                    var changed = changes.Where(c => c.rules).ToList();
                    if (changed.Count == 0)
                        return 0;

                    string str = $"будет обновлена {clash.Id}" +
                                 string.Join("; ", changed.Select(x => x.message));

                    clashConnection.Execute($@"UPDATE {tableName}
                                            SET [D1] = @dept1,
                                                [GL] = @build,             
                                                [G1] = @zone1,
                                                [U1] = @usermod1, 
                                                [D2] = @dept2, 
                                                [G2] = @zone2,
                                                [U2] = @usermod2 
                                            WHERE [ID] = @id",
                        new
                        {
                            id = clash.Id,
                            build = build,
                            dept1 = RealDept1,
                            dept2 = RealDept2,
                            usermod1 = lastModifiedUserMod1,
                            usermod2 = lastModifiedUserMod2,
                            zone1 = RealZone1,
                            zone2 = RealZone2
                        });


                }

            }

            return 1;
        }
        catch (Exception ex)
        {
            return retval;
        }

    }
    public string GetBuild(DbElement dbElement)
    {
        string kks = "";
        try
        {
            kks = dbElement.GetAsString(DbAttributeInstance.DBNA).Split('/')[1].Substring(0, 5);
        }
        catch
        {
            kks = "xxx";
            return "";
        }



        return kks;
    }

    /// <summary>
    /// функция возвращает автора последнего изменения но не pdmsadmin
    /// </summary>
    public string History(DbElement dbElement, string param)
    {
        var Hist = dbElement.GetAsString(DbAttributeInstance.HIST);
        string[] HistAr = Hist.Split(' ');
        string user = "";
        string date = "";
        for (int i = 0; i < HistAr.Length; i++)
        {
            user = dbElement.EvaluateAsString(DbExpression.Parse($"SessU {HistAr[i]}")).ToLower();

            date = dbElement.EvaluateAsString(DbExpression.Parse($"SessD {HistAr[i]}"));




        }

        return param switch
        {
            "user" => user,
            "date" => date,
            _ => "",
        };
    }
    /// <summary>
    /// Возвращает имя зоны или пустую строку.
    /// </summary>
    public string GetZoneName(DbElement dbElement)
    {
        if (dbElement == null || !dbElement.IsValid || dbElement.IsNull)
            return "";

        if (dbElement.ElementType == DbElementTypeInstance.ZONE)
            return dbElement.Name();

        var zone = dbElement.GetZone();

        if (zone == null || !zone.IsValid || zone.IsNull)
            return "";

        return zone.Name();
    }
    /// <summary>
    /// Функция возвращает отдел по элементу
    /// </summary>
    public string GetDepartment(DbElement dbElement, string hier)
    {
        var dept = "";
        try
        {
            dept = dbElement.GetAsString(DbAttributeInstance.DBNA).Split('/')[0];
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Не удалось распознать отдел! {ex.Message}");
            dept = "XXX";
        }
        return dept;
    }
        
    
    /// <summary>
    /// ТЕСТ
    /// </summary>
/// <summary>
/// ТЕСТ
/// </summary>
    public void DeleteById(SqlConnection clashConnection, string tableName, ClashEntity clash, string type, string comment)
    {
        var HistTableName = $"{tableName}_his";
        var login = Project.CurrentProject.LoginUser;

        //string CreateTableHist = $"insert into {HistTableName} select *, getdate(), '{login}', '{type}', '{comment}' from {tableName} where id = '{clash.Id}'";
        clashConnection.Execute($@"INSERT INTO {HistTableName}
                                   (id, LogDate, LoginName, ActionType, Comment)
                                   VALUES (@id, GETDATE(), @login, @type, @comment);",
                                new { id = clash.Id, login, type, comment });




        clashConnection.Execute($@"DELETE FROM {tableName} where id = @id;",
                                   new { id = clash.Id });


    }
    public bool IsNeedToDeleteClashSimple(ClashEntity clash)
    {
        return !(DbElement.GetElement(clash.FirstElement).IsValid && DbElement.GetElement(clash.SecondElement).IsValid);
    }

    private List<ClashEntity> QueryOneSqlClash(SqlConnection clashConnection, string clashTableName, string clashType, string firstElement, string secondElement)
    {

        string firstType = clashType.Substring(0, 1);
        string secondType = clashType.Substring(1, 1);
        string invt = $"{firstType}{secondType} CLASH";
        string query;
        if (clashType == "*" || firstType == secondType)
            query = $"SELECT {ClashSql} FROM [{clashTableName}] WHERE ([R1] = @firstElement AND [R2] = @secondElement OR [R1] = @secondElement AND [R2] = @firstElement)";
        else
            query = $"SELECT {ClashSql} FROM [{clashTableName}] WHERE ([R1] = @firstElement AND [R2] = @secondElement AND [CT] = @clashType OR [R1] = @secondElement AND [R2] = @firstElement AND [CT] = @invt)";

        return [.. clashConnection.Query<ClashEntity>(query, new { firstElement, secondElement, clashType, invt })];


    }

    public List<ClashEntity> QueryClashByEl(SqlConnection clashConnection, string clashTableName, string dbElementref)
    {
        var dbElement = DbElement.GetElement(dbElementref);

        //собираем элементы
        var collection = new DBElementCollection(dbElement).Cast<DbElement>().ToList();
        //Таблица в памяти, в кототорую через BulCopy отправим в SQL
        var ElTable = new DataTable();
        ElTable.Columns.Add("ElRef", typeof(string));
        var uniqueElements = new HashSet<string>();

        foreach (DbElement CurEl in collection)
        {
            string elementRef;
            try
            {
                elementRef = CurEl.GetAsString(DbAttributeInstance.REF);
            }
            catch
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(elementRef))
                continue;
            if (CurEl.ElementType.Description.ToString() == "Tubing")
                elementRef = $"ileav tube of {elementRef}";
            elementRef = elementRef.Replace("ileav rod of", "ileav tube of");
            //так HashSet не принимает повторяющиеся занчения
            if (uniqueElements.Add(elementRef))
                ElTable.Rows.Add(elementRef);
        }

        //Если таблица для BulkCopy пустая, то возвращаю пустой List<ClashEntity> и не буду открывать соединения и т.д
        if (ElTable.Rows.Count == 0)
            return new List<ClashEntity>();

        bool connWasClosed = clashConnection.State != ConnectionState.Open;

        if (connWasClosed)
            clashConnection.Open();


        clashConnection.Execute(@"Create table #Elements (ElRef NVARCHAR(40) NOT NULL);");

        using (var bulkCopy = new SqlBulkCopy(clashConnection))
        {
            bulkCopy.DestinationTableName = "#Elements";
            bulkCopy.ColumnMappings.Add("ElRef", "ElRef");
            bulkCopy.BatchSize = 5000;
            bulkCopy.BulkCopyTimeout = 0;

            bulkCopy.WriteToServer(ElTable);


        }

        var clashList = clashConnection.Query<ClashEntity>($@"Select {ClashSql}
                                                              From {clashTableName}
                                                              Where ( E1 IN (Select ElRef From #Elements)
                                                              OR E2 IN (Select ElRef From #Elements))")
                                                             .ToList();


        return clashList;
    }
    private void CheckResultToBase(SqlConnection sqlConnection, string clashTableName, ClashSet clashSet)
    {
        if (sqlConnection.State != ConnectionState.Open)
            sqlConnection.Open();
        Logger.WriteLine($"Запись колизий в базу...");
        Logger.WriteLine($"Количесво коллизий: {clashSet.Clashes.Length}");
        var notIgnoredClashes = new List<Clash>(clashSet.Clashes.Length);
        int ignored = 0;
        // var notIgnoredClashes = clashSet.Clashes.Where(e => !IsClashIgnore(e));

        foreach (var c in clashSet.Clashes)
        {
            if (IsClashIgnore(c))
            {
                ignored++;
                continue;
            }
            notIgnoredClashes.Add(c);

        }
        Logger.WriteLine($"Количесво проигнорированных коллизий: {ignored}");

        try
        {
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
            var Pairs = new DataTable();
            Pairs.Columns.Add("ClashType", typeof(string));
            Pairs.Columns.Add("El1", typeof(string));
            Pairs.Columns.Add("El2", typeof(string));
            Pairs.Columns.Add("X", typeof(int));
            Pairs.Columns.Add("Y", typeof(int));
            Pairs.Columns.Add("Z", typeof(int));


            foreach (var n in notIgnoredClashes)
            {
                Pairs.Rows.Add(

                    n.Type.ToString(),
                    n.First.GetAsString(DbAttributeInstance.REF),
                    n.Second.GetAsString(DbAttributeInstance.REF),
                    (int)n.ClashPosition.X,
                    (int)n.ClashPosition.Y,
                    (int)n.ClashPosition.Z
                );
            }

            //sqlConnection.Execute("INSERT INTO #pairs(ClashType,El1,El2) VALUES (@ClashType,@El1,@El2);", Pairs);
            using (var bulk = new SqlBulkCopy(sqlConnection))
            {
                bulk.DestinationTableName = "#pairs";
                bulk.ColumnMappings.Add("ClashType", "ClashType");
                bulk.ColumnMappings.Add("El1", "El1");
                bulk.ColumnMappings.Add("El2", "El2");
                bulk.ColumnMappings.Add("X", "X");
                bulk.ColumnMappings.Add("Y", "Y");
                bulk.ColumnMappings.Add("Z", "Z");
                bulk.WriteToServer(Pairs);
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



            Logger.WriteLine($"Количесво выполненных селектов: {existingRows.Count}");
            //Logger.WriteLine($"Количесво апдейтов existing: {existingUpdate.Count}");
            //создаю словарь для быстрого поиска по ключу
            var ExistingByKey = new Dictionary<string, ExistingRow>(existingRows.Count * 2, StringComparer.Ordinal);
            foreach (var e in existingRows)
            {
                string k1 = MakeKey(e.ClashType, e.El1, e.El2, e.X, e.Y, e.Z);
                string k2 = MakeKey(e.ClashType, e.El2, e.El1, e.X, e.Y, e.Z);
                ExistingByKey[k1] = e;
                ExistingByKey[k2] = e;
            }
            var dt = CreateTableForInsert();

            foreach (var clash in notIgnoredClashes)
            {

                var clashType = clash.Type.ToString();
                var El1Ref = clash.First.GetAsString(DbAttributeInstance.REF);
                var El2Ref = clash.Second.GetAsString(DbAttributeInstance.REF);
                int X = (int)clash.ClashPosition.X;
                int Y = (int)clash.ClashPosition.Y;
                int Z = (int)clash.ClashPosition.Z;
                string key1 = MakeKey(clashType, El1Ref, El2Ref, X, Y, Z);
                string key2 = MakeKey(clashType, El2Ref, El1Ref, X, Y, Z);
                if (!ExistingByKey.TryGetValue(key1, out var existing) && !ExistingByKey.TryGetValue(key2, out existing))
                    AdditionTableForInsert(clash, dt, X, Y, Z);
                //else SelectClash(sqlConnection, clashTableName, clash);

            }
            BulkInsertClashes(sqlConnection, dt, clashTableName);

            GC.Collect();
            PML.CreateCommand($"$p {notIgnoredClashes.Count()} коллизий подтвердилось после проверки").RunInPdms();

        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Ошибка в CheckResultToBase {ex.Message}");
        }
        finally
        {
            sqlConnection.Execute("DROP TABLE #pairs;");

        }
    }


    private static DataTable CreateTableForInsert()
    {
        var dt = new DataTable();
        dt.Columns.Add("Building", typeof(string));
        dt.Columns.Add("ClashType", typeof(string));
        dt.Columns.Add("El1", typeof(string));
        dt.Columns.Add("Type1", typeof(string));
        dt.Columns.Add("Usermod1", typeof(string));
        dt.Columns.Add("Flnm1", typeof(string));
        dt.Columns.Add("Dept1", typeof(string));
        dt.Columns.Add("Zone1", typeof(string));

        dt.Columns.Add("El2", typeof(string));
        dt.Columns.Add("Type2", typeof(string));
        dt.Columns.Add("Usermod2", typeof(string));
        dt.Columns.Add("Flnm2", typeof(string));
        dt.Columns.Add("Dept2", typeof(string));
        dt.Columns.Add("Zone2", typeof(string));

        dt.Columns.Add("Date", typeof(DateTime));
        dt.Columns.Add("X", typeof(int));
        dt.Columns.Add("Y", typeof(int));
        dt.Columns.Add("Z", typeof(int));
        dt.Columns.Add("Existing", typeof(bool));
        //dt.Columns.Add("Building", typeof(string));

        return dt;
    }

    /// <summary>
    /// Вставляем информацию о коллизии в SQL (без информации из ТДМС)
    /// TODO: Добавить инфу из тдмс (KKS здания, ответсвтенное лицо, плановую дату сдачи комплекта (если связь с таковым имеется))
    /// </summary>
    /// <param name="clashConnection"></param>
    /// <param name="clashTableName"></param>
    /// <param name="clash"></param>
    private void AdditionTableForInsert(Clash clash, DataTable dt, int x, int y, int z)
    {
        var clashType = clash.Type.ToString();
        var firstElement = clash.First.GetAsString(DbAttributeInstance.REF);
        var secondElement = clash.Second.GetAsString(DbAttributeInstance.REF);
        var firstType = clash.First.ElementType.ToString();
        var secondType = clash.Second.ElementType.ToString();
        var date = DateTime.Now;
        var flnm1 = clash.First.GetSite().ToString();
        var flnm2 = clash.Second.GetSite().ToString();
        var build = GetBuild(clash.First);
        var firstDept = GetDepartment(clash.First, " ");
        var secondDept = GetDepartment(clash.Second, " ");

        var firstZone = GetZoneName(clash.First);
        var secondZone = GetZoneName(clash.Second);

        var firstUserMode = History(clash.First, "user");
        var secondUserMode = History(clash.Second, "user");

        

        var row = dt.NewRow();
        row["Building"] = Cut(build, 12) ?? string.Empty;
        row["ClashType"] = Cut(clashType, 12);
        row["El1"] = Cut(firstElement, 24);
        row["Type1"] = Cut(firstType, 12);
        row["Usermod1"] = Cut(firstUserMode, 32);
        row["Flnm1"] = Cut(flnm1, 50);
        row["Dept1"] = Cut(firstDept, 16) != null ? Cut(firstDept, 16) : DBNull.Value;
        row["Zone1"] = Cut(firstZone, 16) != null ? Cut(firstZone, 16) : DBNull.Value;

        row["El2"] = Cut(secondElement, 24);
        row["Type2"] = Cut(secondType, 12);
        row["Usermod2"] = Cut(secondUserMode, 32);
        row["Flnm2"] = Cut(flnm2, 50);
        row["Dept2"] = Cut(secondDept, 16) != null ? Cut(secondDept, 16) : DBNull.Value;
        row["Zone2"] = Cut(secondZone, 16) != null ? Cut(secondZone, 16) : DBNull.Value;

        row["Date"] = DateTime.Now;
        row["X"] = x;
        row["Y"] = y;
        row["Z"] = z;
        row["Existing"] = true;
        //row["Building"] = building != null ? building : DBNull.Value;

        dt.Rows.Add(row);
    }
    private string Cut(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length > maxLength ? value.Substring(0, maxLength) : value;
    }
    private void BulkInsertClashes(SqlConnection sqlConnection, DataTable dt, string clashTableName)
    {
        if (dt.Rows.Count != 0)
            using (var bulk = new SqlBulkCopy(sqlConnection))
            {
                bulk.DestinationTableName = clashTableName;
                bulk.ColumnMappings.Add("Building", "GL");
                bulk.ColumnMappings.Add("ClashType", "CT");
                bulk.ColumnMappings.Add("El1", "R1");
                bulk.ColumnMappings.Add("Type1", "E1");
                bulk.ColumnMappings.Add("Usermod1", "U1");
                bulk.ColumnMappings.Add("Dept1", "D1");
                bulk.ColumnMappings.Add("Zone1", "G1");

                bulk.ColumnMappings.Add("El2", "R2");
                bulk.ColumnMappings.Add("Type2", "E2");
                bulk.ColumnMappings.Add("Usermod2", "U2");
                bulk.ColumnMappings.Add("Dept2", "D2");
                bulk.ColumnMappings.Add("Zone2", "G2");

                bulk.ColumnMappings.Add("Date", "DT");
                bulk.ColumnMappings.Add("X", "X0");
                bulk.ColumnMappings.Add("Y", "Y0");
                bulk.ColumnMappings.Add("Z", "Z0");
                bulk.ColumnMappings.Add("Existing", "XT");
                //bulk.ColumnMappings.Add("Building", "Building");
                var before = sqlConnection.ExecuteScalar<int>($"select count(*) from {clashTableName}");
                Logger.WriteLine($"before = {before}");
                bulk.WriteToServer(dt);
                var after = sqlConnection.ExecuteScalar<int>($"select count(*) from {clashTableName}");
                Logger.WriteLine($"after = {after}");
            }
    }
    /// <summary>
    /// Возвращает true, если коллизию следует игнорировать
    /// </summary>
    /// <param name="clash"></param>
    /// <returns></returns>
    private bool IsClashIgnore(Clash clash)
    {
        try
        {
            if (CheckPipeWithJntc(clash))
                return true;
            if (CheckGensecWithPane(clash))
                return true;
            if (CheckHangWithBranch(clash))
                return true;
            if (CheckRestWithStlr(clash))
                return true;
            if (CheckBranWithFrameWork(clash))
                return true;
            if (CheckGESite(clash))
                return true;

            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    #region Проверки для IsClashIgnore
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

    /// <summary>
    /// Ингорируем GENSEC с PANE, за исключением вхождений в STRU с CL в имени || FRMW с CC в имени
    /// </summary>
    /// <param name="clash"></param>
    /// <returns></returns>
    private bool CheckGensecWithPane(Clash clash)
    {

        var firstType = clash.First.ElementType;
        var secondType = clash.Second.ElementType;
        DbElement pane;
        DbElement sctn;

        if (firstType == DbElementTypeInstance.GENSEC && secondType == DbElementTypeInstance.PANEL)
        {
            pane = clash.Second;
            sctn = clash.First;
        }
        else if (firstType == DbElementTypeInstance.PANEL && secondType == DbElementTypeInstance.GENSEC)
        {
            pane = clash.First;
            sctn = clash.Second;
        }
        else
        {
            return false;
        }

        var stru = sctn.GetOwnerByType(DbElementTypeInstance.STRUCTURE);
        var zone = pane.GetOwnerByType(DbElementTypeInstance.ZONE);

        string zoneName = zone.Name();
        if (!zoneName.Contains('_') || zoneName.Split('_').Length < 3 || zoneName.Split('_')[2] != "CL")
            return false;

        string struName = stru.Name();
        if (!struName.Contains('_') || struName.Split('_').Length < 4 || struName.Split('_')[3] != "CL")
            return false;

        var frmw = pane.GetOwnerByType(DbElementTypeInstance.FRMWORK);
        if (frmw.IsNull)
            return false;

        string frmwName = frmw.Name();
        if (!frmwName.Contains('_') || frmwName.Split('_').Length < 5 || frmwName.Split('_')[4].Substring(0, 1) != "CC")
            return false;

        return true;

    }

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
    #endregion Проверки для IsClashIgnore

    private bool CheckGESite(Clash clash)
    {
        
        try
        {
            if (!clash.First.TryGetOwnerByType(DbElementTypeInstance.SITE, out DbElement site1))
                return false;
            if (!clash.Second.TryGetOwnerByType(DbElementTypeInstance.SITE, out DbElement site2))
                return false;

            if(site1 != site2)
                return false;

            var siteName = site1.Name();
            return siteName.Contains("-GE.");
        }
        catch (Exception)
        {
            return false;
        }



        return true;
    }
    private bool IsSomeGESite(DbElement el1, DbElement el2)
    {

        try
        {
            if (!el1.TryGetOwnerByType(DbElementTypeInstance.SITE, out DbElement site1))
                return false;
            if (!el2.TryGetOwnerByType(DbElementTypeInstance.SITE, out DbElement site2))
                return false;

            if (site1 != site2)
                return false;

            var siteName = site1.Name();
            return siteName.Contains("-GE.");
        }
        catch (Exception)
        {
            return false;
        }



    }

    private void CreateTableRefUpdateLog(string projectCode, SqlConnection sqlConnection)
    {
        string ClashRefUpdateLog = $"Clash{projectCode}_RefUpdateLog";
        sqlConnection.Execute($"CREATE TABLE [{ClashRefUpdateLog}]( [RunId] INT IDENTITY(1,1) NOT NULL, [UpdateTime] DATETIME NOT NULL, [RowId] INT NOT NULL, [OldEl1] NVARCHAR(50) NULL, [NewEl1] NVARCHAR(50) NULL, [flnm1] NVARCHAR(500) NULL);");
    }




    public static string MakeKey(string clashType, string el1, string el2, int X, int Y, int Z)
              => $"{clashType}|{el1}|{el2}|{X}|{Y}|{Z}";

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


