using Aveva.ClashChecker.NetCallable;
using Aveva.ClashChecker.NetCallable.Extensions;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.PMLNet;
using Aveva.Core3D.Clasher;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

    private static readonly HashSet<string> SpecProj = ["SVB", "DNS", "WXT"];
    public string ClashConnectionString { get; set; } = "Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;User ID=clashuser;Password=Qgh%fS45Nm;Connection Timeout = 300;TrustServerCertificate=true";

    private static readonly DbElement NullElement = DbElement.GetElement("*");

    private static readonly string ClashSql =
    $"Id '{nameof(ClashEntity.Id)}', " +
    $"ClashType '{nameof(ClashEntity.ClashType)}', " +
    $"El1 '{nameof(ClashEntity.FirstElement)}', " +
    $"Type1 '{nameof(ClashEntity.FirstType)}', " +
    $"Usermod1 '{nameof(ClashEntity.FirstUserMode)}', " +
    $"Flnm1 '{nameof(ClashEntity.Flnm1)}', " +
    $"Dept1 '{nameof(ClashEntity.FirstDept)}', " +
    $"Gpset1 '{nameof(ClashEntity.FirstGpset)}', " +
    $"El2 '{nameof(ClashEntity.SecondElement)}', " +
    $"Type2 '{nameof(ClashEntity.SecondType)}', " +
    $"Usermod2 '{nameof(ClashEntity.SecondUserMode)}', " +
    $"Flnm2 '{nameof(ClashEntity.Flnm2)}', " +
    $"Dept2 '{nameof(ClashEntity.SecondDept)}', " +
    $"Gpset2 '{nameof(ClashEntity.SecondGpset)}', " +
    $"Date '{nameof(ClashEntity.Date)}', " +
    $"X '{nameof(ClashEntity.X)}', " +
    $"Y '{nameof(ClashEntity.Y)}', " +
    $"Z '{nameof(ClashEntity.Z)}', " +
    $"Existing '{nameof(ClashEntity.Existing)}', " +
    $"Sequence '{nameof(ClashEntity.Sequence)}', " +
    $"Building '{nameof(ClashEntity.Building)}', " +
    $"RequestToDept '{nameof(ClashEntity.RequestToDept)}', " +
    $"RequestUser '{nameof(ClashEntity.RequestUser)}', " +
    $"RequestDate '{nameof(ClashEntity.RequestDate)}', " +
    $"ApproveUser '{nameof(ClashEntity.ApproveUser)}', " +
    $"ApproveDate '{nameof(ClashEntity.ApproveDate)}', " +
    $"ApproveReason '{nameof(ClashEntity.ApproveReason)}', " +
    $"InWorkUser '{nameof(ClashEntity.InWorkUser)}', " +
    $"InWorkDate '{nameof(ClashEntity.InWorkDate)}'";


    private const string DefaultLogDirectoryPath = "D:\\AVEVA\\ClasherLogs\\ClashLog.log";
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
    public void CheckAll(string obstType, double initialZoneIndex, bool testMode = true,  string logDirectoryPath = DefaultLogDirectoryPath)
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
            catch(Exception ex)
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

            ReplaceRefIFC(clashConnection, clashTableName, ifcTableName, clashRefUpdateLog, projectCode);

            UpdateClashElementInfo(clashConnection, "FULL", clashTableName, string.Empty);
            Logger.WriteLine("Выполнен UpdateClashElementInfo");
            clashConnection.Execute($"UPDATE {clashTableName} set Existing = 'false'");

            string initialClashesLogString = $"Коллизий до проверки: {clashConnection.ExecuteScalar<int>($"select top 1 COUNT(*) from {clashTableName}")}";

            Logger.WriteLine(initialClashesLogString);


            List<DbElement> colZone;

            //TODO: Проверить быстродействие составного фильтра против пост-обработки LinQ
            //var filter = new CompoundFilter();

            //Собираем коллекцию зон для obstructionList
            if (projectCode.Replace("_TEST", "") == "GCC")
            {
                colZone = [.. new DBElementCollection(new TypeFilter(DbElementTypeInstance.ZONE)).Cast<DbElement>().Where(e=> !e.Name().Contains("PO")
                && !e.Name().Contains("STUDY")
                && !e.Name().Contains("ZEMI")
                && !e.Name().Contains("/??"))];
            }
            else
            {
                //var filter = new CompoundFilter();
                //filter.AddShow(new TypeFilter(DbElementTypeInstance.ZONE));
                //filter.AddHide(new ExpressionFilter(DbExpression.Parse("MATCHWILD (name of site ,'*ZEMI*')")));
                //filter.AddHide(new ExpressionFilter(DbExpression.Parse("MATCHWILD (name of site ,'*/po*')")));
                //filter.AddHide(new ExpressionFilter(DbExpression.Parse("PURP of SITE eq NOCL")));
                //filter.AddHide(new AttributeDbDoubleFilter(DbAttributeInstance.MCOU, FilterOperator.Equals, DbDouble.Create(0)));

                //colZone = [.. new DBElementCollection(filter).Cast<DbElement>()];

                colZone = [.. new DBElementCollection(new TypeFilter(DbElementTypeInstance.ZONE)).Cast<DbElement>().Where(e =>
                {
                   DbElement site = e.Owner;
                   string siteName = site.Name();
                   if(siteName.Contains(".L") || siteName.Contains("ZEMI") || siteName.Contains("/po") || site.GetString(DbAttributeInstance.PURP) == "NOCL" || e.GetAsString(DbAttributeInstance.MCOU) == "0" || e.Name().Contains(".L"))
                       return false;
                   return true;

                })];
            }

            Logger.WriteLine($"Всего зон {colZone.Count} шт");

            var wvolArray = colZone.Select(e => e.GetDoubleArray(DbAttributeInstance.WVOL)).ToList();
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
            ]);

            int zoneCount = colZone.Count;
            GC.Collect();

            // Используем Partitioner для равномерного распределения работы
            for (int i = initialZoneIndexInt; i < colZone.Count; i++)
            {

                if (wvolArray[i].Length < 6)
                    continue;
                var obstructionList = ObstructionList.Create();
                for (int j = i; j < colZone.Count; j++)
                {
                    if (wvolArray[j].Length < 6) // как тут скиповать? i это double[]. Норм когда double[6] , стрем когда double[0]
                        continue;
                    if (WvolClash(wvolArray[i], wvolArray[j]))
                        obstructionList.AddObstructions([colZone[j]]);

                }
                CheckZone(colZone[i], obstructionList, clashOptions, clashConnection, clashTableName, i, zoneCount);
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

    /// <summary>
    /// Класс для хранения информации о пересечении зон
    /// </summary>
    public class ZoneIntersection
    {
        public int ZoneIndex { get; set; }
        public List<int> IntersectIndicies { get; set; }
    }


    private void CheckZone(DbElement zone, ObstructionList obstructionList, ClashOptions clashOptions, SqlConnection clashConnection, string clashTableName, int index, int zoneCount)
    {
        var clashSet = ClashSet.Create();
        Logger.WriteLine($"Начата проверка зоны {zone.Name()} [{index + 1}/{zoneCount}] ...");
        var checkResult = Clasher.Instance.CheckAll(clashOptions, obstructionList, clashSet);
        if (!checkResult)
        {
            Logger.WriteLine($"Desclash не удалось проверить зону {zone.Name()}! Переход к следующей зоне...");

            return;
        }
        Logger.WriteLine($"Зона {zone.Name()} Проверена!");

        _ = CheckResultToBase(clashConnection, clashTableName, clashSet);
    }

    private static Dictionary<string, string> RequiredClashTableColumns = new Dictionary<string, string>
    {
        ["id"] = "INT NOT NULL IDENTITY(1,1)",
        ["ClashType"] = "NVARCHAR(20) NOT NULL",
        ["El1"] = "NVARCHAR(40) NOT NULL",
        ["type1"] = "NVARCHAR(10) NOT NULL",
        ["usermod1"] = "NVARCHAR(20) NOT NULL",
        ["flnm1"] = "NVARCHAR(250) NULL",
        ["Dept1"] = "NVARCHAR(20) NULL",
        ["Gpset1"] = "NVARCHAR(100) NULL",
        ["El2"] = "NVARCHAR(40) NOT NULL",
        ["type2"] = "NVARCHAR(10) NOT NULL",
        ["usermod2"] = "NVARCHAR(20) NOT NULL",
        ["flnm2"] = "NVARCHAR(250) NULL",
        ["Dept2"] = "NVARCHAR(20) NULL",
        ["Gpset2"] = "NVARCHAR(100) NULL",
        ["date"] = "DATETIME NOT NULL",
        ["x"] = "INT NOT NULL",
        ["y"] = "INT NOT NULL",
        ["z"] = "INT NOT NULL",
        ["existing"] = "BIT NOT NULL",
        ["Building"] = "NVARCHAR(20) NULL",
        ["Sequence"] = "NVARCHAR(20) NULL",
        ["RequestToDept"] = "NVARCHAR(20) NULL",
        ["RequestUser"] = "NVARCHAR(20) NULL",
        ["RequestDate"] = "DATETIME NULL",
        ["ApproveUser"] = "NVARCHAR(20) NULL",
        ["ApproveDate"] = "DATETIME NULL",
        ["ApproveReason"] = "NVARCHAR(255) NULL",
        ["InWorkUser"] = "NVARCHAR(20) NULL",
        ["InWorkDate"] = "DATETIME NULL"
    };


    private static Dictionary<string, string> RequiredHistoryClashTableColumns = new Dictionary<string, string>
    {

        ["id"] = "INT NOT NULL",
        ["LogDate"] = "DATETIME NOT NULL DEFAULT GETDATE()",
        ["LoginName"] = "NVARCHAR(50)",
        ["ActionType"] = "NVARCHAR(20)",
        ["Comment"] = "NVARCHAR(100))"
    };



    private void CreateClashDbIfNotExist(string clashTableName, SqlConnection clashConnection)
    {
        if (!clashConnection.TableExists(clashTableName))
        {
            Logger.WriteLine($@"Таблица {clashTableName} не найдена! Создание таблицы...");
            clashConnection.Execute($@"CREATE TABLE [{clashTableName}]( [id] INT NOT NULL IDENTITY (1,1), 
                [ClashType] NVARCHAR(20) NOT NULL, 
                 [El1] NVARCHAR(40) NOT NULL, 
                [type1] NVARCHAR(10) NOT NULL, 
                [usermod1] NVARCHAR(20) NOT NULL, 
                [flnm1] NVARCHAR(250), 
                [Dept1] NVARCHAR(20), 
                [Gpset1] NVARCHAR(100), 
                [El2] NVARCHAR(40) NOT NULL, 
                [type2] NVARCHAR(10) NOT NULL, 
                [usermod2] NVARCHAR(20) NOT NULL, 
                [flnm2] NVARCHAR(250), 
                [Dept2] NVARCHAR(20), 
                [Gpset2] NVARCHAR(100), 
                [date] DATETIME NOT NULL, 
                [x] INT NOT NULL, 
                [y] INT NOT NULL, 
                [z] INT NOT NULL, 
                [existing] BIT NOT NULL, 
                [Building] NVARCHAR(20), 
                [Sequence] NVARCHAR(20) , 
                [RequestToDept] NVARCHAR(20), 
                [RequestUser] NVARCHAR(20), 
                [RequestDate] DATETIME, 
                [ApproveUser] NVARCHAR(20), 
                [ApproveDate] DATETIME, 
                [ApproveReason] NVARCHAR(255), 
                [InWorkUser] NVARCHAR(20), 
                [InWorkDate] DATETIME );");

            Logger.WriteLine($"Таблица {clashTableName} создана!");
            Logger.WriteLine($"Генерация индексов...");

            clashConnection.Execute($"CREATE INDEX id_ind ON {clashTableName}(id)");
            clashConnection.Execute($"CREATE INDEX el1_ind ON {clashTableName}(el1)");
            clashConnection.Execute($"CREATE INDEX el2_ind ON {clashTableName}(el2)");
            clashConnection.Execute($"CREATE INDEX dept1_ind ON {clashTableName}(dept1)");
            clashConnection.Execute($"CREATE INDEX dept2_ind ON {clashTableName}(dept2)");
            clashConnection.Execute($"CREATE INDEX gpset1_ind ON {clashTableName}(gpset1)");
            clashConnection.Execute($"CREATE INDEX gpset2_ind ON {clashTableName}(gpset2)");
            clashConnection.Execute($"CREATE INDEX Sequence_ind ON {clashTableName}(Sequence)");
            clashConnection.Execute($"CREATE INDEX Building_ind ON {clashTableName}(Building)");
            Logger.WriteLine($"Индексы для {clashTableName} сформированы!");
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
    private async Task ReplaceRefIFC(SqlConnection clashConnection, string clashTableName, string tableIfcName, string clashRefUpdateLog, string pprojectCode)
    {
        try
        {
            Logger.WriteLine("Начато выполнение ReplaceRefIFC...");

            if (!clashConnection.TableExists(tableIfcName) || !clashConnection.TableExists(clashTableName))
            {
                Logger.WriteLine($"Не найдены таблицы. IFC: {tableIfcName}");
                return;
            }

            if (!clashConnection.TableExists(clashRefUpdateLog))
                CreateTableRefUpdateLog(pprojectCode, clashConnection);

            int j = 3;
            for (int i = 1; i < 3; i++)
            {
                j--;
                //Идём по элементам в таблице коллизий
                string updateFirstQuery = @$"WITH ClashElemsE{i} AS(SELECT DISTINCT El{i} AS OldE{i}, flnm{i} AS flnm{i} FROM {clashTableName} WHERE type{i} = 'GENPRI'), 
                    ClashWithMemE{i} AS(SELECT OldE{i}, flnm{i}, LEFT(flnm{i}, CHARINDEX(' of ', flnm{i} + ' of ') - 1) AS mempos{i} FROM ClashElemsE{i}), 
                    OldWithUuidE{i} AS(SELECT c.OldE{i}, c.flnm{i}, i.UUIDowner FROM ClashWithMemE{i} c JOIN {tableIfcName} i ON i.ELEM = c.OldE{i} AND i.fdelnm = c.mempos{i}), 
                    LatestPerUUIDE{i} AS(SELECT UUIDowner, ELEM AS NewE{i}, ROW_NUMBER() OVER (PARTITION BY UUIDowner ORDER BY [DATE] DESC ) AS rn FROM {tableIfcName} ), 
                    MapOldNewE{i} AS(SELECT o.OldE{i}, o.flnm{i}, l.NewE{i} FROM OldWithUuidE{i} o JOIN LatestPerUUIDE{i} l ON l.UUIDowner = o.UUIDowner AND l.rn = 1) 
                    UPDATE c SET c.El{i} = m.NewE{i} OUTPUT deleted.id, deleted.El{i}, inserted.El{i}, inserted.flnm{i}, GETDATE() INTO
                    {clashRefUpdateLog}(RowId, OldEl{j}, NewEl{j}, flnm{j}, UpdateTime) FROM {clashTableName} c JOIN MapOldNewE{i} m ON c.El{i} = m.OldE{i} AND c.flnm{i} = m.flnm{i} WHERE c.type{i} = 'GENPRI' AND m.NewE{i}<> c.El{i} 
                    SELECT @@ROWCOUNT AS UpdatedRows;";
                clashConnection.Execute(updateFirstQuery, commandTimeout: 600);
            }

            Logger.WriteLine("Выполнен ReplaceRefIFC");
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }

    }

    /// <summary>
    /// Обновляет данные по коллизиям (по отдельным комплектам/ по всем комлпекта)
    /// </summary>
    public string UpdateClashElementInfo(SqlConnection clashConnection, string checkMode, string clashTablename, string gpsetName)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            int deleteCount = 0;
            int updateCount = 0;
            int totalCount = 0;

            List<ClashEntity> clashes = [];

            if (gpsetName != string.Empty)
            {
                var clashesByGpset = clashConnection.Query<ClashEntity>($"SELECT {ClashSql} from {clashTablename} WHERE (gpset1 = '{gpsetName}' or gpset2 = '{gpsetName}')").ToList();
                var clashesByGpsetElement = QueryClashByEl(clashConnection, clashTablename, gpsetName, "");
                HashSet<int> ids = [.. clashesByGpset.Select(e => e.Id)];
                clashes = [.. clashesByGpset.Union(clashesByGpsetElement.Where(e => !ids.Contains(e.Id)))];

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
            return $"{ellapsedSeconds};{gpsetName};{totalCount};{deleteCount};{updateCount};{checkMode}";
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
                string RealGpset1 = GetGroups(dbElem1);
                string RealGpset2 = GetGroups(dbElem2);

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
                if (clash.FirstUserMode != lastModifiedUserMod1 || clash.SecondUserMode != lastModifiedUserMod2 || clash.FirstGpset != RealGpset1 || clash.SecondGpset != RealGpset2 || clash.FirstDept != RealDept1 || clash.SecondDept != RealDept2)
                {

                    var changes = new List<(bool rules, string message)>
                    {
                        (clash.FirstUserMode != lastModifiedUserMod1, $"FirstUserMode:{clash.FirstUserMode}->{lastModifiedUserMod1}"),
                        (clash.FirstDept != RealDept1,        $"FirstDept:{clash.FirstDept}->{RealDept1}"),
                        (clash.FirstGpset != RealGpset1,      $"FirstGpset:{clash.FirstGpset}->{RealGpset1}"),
                        (clash.SecondUserMode != lastModifiedUserMod2,$"SecondUserMode:{clash.SecondUserMode}->{lastModifiedUserMod2}"),
                        (clash.SecondDept != RealDept2,       $"SecondDept:{clash.SecondDept}->{RealDept2}"),
                        (clash.SecondGpset != RealGpset2,     $"SecondGpset:{clash.SecondGpset}->{RealGpset2}")
                    };

                    var changed = changes.Where(c => c.rules).ToList();
                    if (changed.Count == 0)
                        return 0;

                    string str = $"будет обновлена {clash.Id}" +
                                 string.Join("; ", changed.Select(x => x.message));

                    clashConnection.Execute($@"UPDATE {tableName}
                        SET dept1 = @dept1, 
                        gpset1 = @gpset1, 
                        usermod1 = @usermod1, 
                        dept2 = @dept2, 
                        gpset2 = @gpset2, 
                        usermod2 = @usermod2, 
                        WHERE id = @id",
                        new
                        {
                            id = clash.Id,
                            dept1 = RealDept1,
                            dept2 = RealDept2,
                            usermod1 = lastModifiedUserMod1,
                            usermod2 = lastModifiedUserMod2,
                            gpset1 = RealGpset1,
                            gpset2 = RealGpset2
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
            //date = dbElement.EvaluateAsString(DbExpression.Parse($"SessD {HistAr[i]}"));
            date = dbElement.EvaluateAsString(DbExpression.Parse($"SessD {HistAr[i]}"));


            // var FilterAllUser = new TypeFilter(DbElementTypeInstance.ULOGID);
            // var uW = DbElement.GetElement("/*U");
            // var AllUser = new DBElementCollection(uW,FilterAllUser).Cast<DbElement>().ToList();
            // foreach (var u in AllUser)
            // {
            //     var e = u.GetString(DbAttributeInstance.USRLI);
            //     if (e.Contains("BIM") || e.Contains("BIM")) {
            // }
            //
            // 
            if (user != "balashovan" && user != "goncharenkoea" && user != "pdmsadmin")
            {
                break;
            }
            else
            {
                user = "admin";
            }
        }

        return param switch
        {
            "user" => user,
            "date" => date,
            _ => "",
        };
    }
    /// <summary>
    /// функция возвращает имя комплекта или пустую строку
    /// </summary>
    public string GetGroups(DbElement dbElement)
    {
        if (!dbElement.IsValid || !dbElement.IsNull) return "";
        int ElementDepth = dbElement.GetInteger(DbAttributeInstance.DEPTH);

        var Site = dbElement.GetSite();
        for (int i = 0; i <= ElementDepth; i++)
        {
            var DbElType = dbElement.GetString(DbAttributeInstance.TYPE);
            var DbElGroups = dbElement.GetString(DbAttributeInstance.GROUPS);

            if (DbElType == "GPSET") return dbElement.Name();
            else if (DbElGroups != null) return DbElGroups;
            else if (DbElType == "GENPRI" || DbElType == "GENCUR")
            {
                try
                {
                    var Gpref = dbElement.GetString(DbAttribute.GetDbAttribute(":UES_GPREF"));
                    return Gpref;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }

            }
            var dbElementOwner = dbElement.GetAsString(DbAttributeInstance.OWNER);
            dbElement = DbElement.GetElement(dbElementOwner);


        }
        return "";


    }
    /// <summary>
    /// Функция возвращает отдел по элементу
    /// </summary>
    public string GetDepartment(DbElement dbElement, string hier)
    {
        string ProjectName = Project.CurrentProject.Name;
        string DbFileName = dbElement.GetString(DbAttributeInstance.DBFI);
        string DbRef = dbElement.GetAsString(DbAttributeInstance.REF);
        string result = DbFileName.Split('%')[1].Substring(0, 3);
        string SiteIFC = dbElement.GetSite().ToString();

        if (SiteIFC.Contains("IFC"))
        {
            int i = SiteIFC.LastIndexOf('_');
            string index = i >= 0 ? SiteIFC.Substring(i) : "";
            if (DepartmentLookup.MarkToDept.TryGetValue(index, out string dept))
            {
                return dept;
            }
            else
            {
                return "";
            }

        }

        switch (result)
        {
            case "TUE":
            case "YKE":
                string DbName = dbElement.Db.DbItem.ToString();
                return DbName.Substring(0, 3);
                break;

            case "GCC":

                string usermod = History(dbElement, "user").ToLower();
                var type = new ActualTypeFilter(DbElementType.GetElementType("ULOGID"));
                var uW = DbElement.GetElement("/*U");
                var collection = new DBElementCollection(uW, type).Cast<DbElement>();
                //var logid = collection.FirstOrDefault(i => i.GetString(DbAttributeInstance.NAME) == usermod);
                Dictionary<string, string> LognameByDept;
                LognameByDept = [];
                foreach (var el in collection)
                {
                    string name = el.GetString(DbAttributeInstance.NAME);
                    LognameByDept[name] = name;
                    string dept = el.GetString(DbAttributeInstance.USEF);
                    LognameByDept[dept] = dept;
                }

                //var logid = collection.Tr
                if (LognameByDept.TryGetValue(usermod, out string deptGCC))
                {
                    return deptGCC;
                }
                else
                {
                    return "";
                }



                break;

            default:

                string site = hier == "GPSET" ? dbElement.Ref.ToString() : dbElement.EvaluateAsString(DbExpression.Parse($"SITE of {dbElement}"));
                //:UES_DEPART надо ли? vсмотрел, его со времен царя гороха никто не заполняет
                //isnullorEmpty
                if (site.Length > 0)
                {
                    int i = site.LastIndexOf('_');
                    string index = i > 0 ? site.Substring(site.IndexOf('_'), 3) : "XXX";
                    var dept = DepartmentInfo.Departments.Where(d => d.Mark.Contains(index)).ToList();
                    bool IsBool = SpecProj.Contains(ProjectName);
                    foreach (var d in dept)
                    {
                        if (IsBool) return d.Tdept;
                        else return d.Dept;

                    }
                }
                break;
        }
        return null;
    }
    /// <summary>
    /// ТЕСТ
    /// </summary>
    [PMLNetCallable]
    public string GetDepartmentTest(string dbElementRef, string hier)
    {
        var dbElement = DbElement.GetElement(dbElementRef);
        string ProjectName = Project.CurrentProject.Name;
        string DbFileName = dbElement.GetString(DbAttributeInstance.DBFI);
        var DbRef = dbElement.GetElement(DbAttributeInstance.REF);
        string result = DbFileName.Split('%')[1].Substring(0, 3);
        string SiteIFC = dbElement.GetSite().ToString();

        if (SiteIFC.Contains("IFC"))
        {
            int i = SiteIFC.LastIndexOf('_');
            string index = i >= 0 ? SiteIFC.Substring(i) : "";
            if (DepartmentLookup.MarkToDept.TryGetValue(index, out string dept))
            {
                return dept;
            }
            else
            {
                return "";
            }

        }

        switch (result)
        {
            case "TUE":
            case "YKE":
                string DbName = dbElement.Db.DbItem.ToString();
                return DbName.Substring(0, 3);
            case "GCC":

                string usermod = History(dbElement, "user").ToLower();
                var type = new ActualTypeFilter(DbElementType.GetElementType("ULOGID"));
                var uW = DbElement.GetElement("/*U");
                List<DbElement> collection = [.. new DBElementCollection(uW, type).Cast<DbElement>()];
                var logid = collection.FirstOrDefault(i => i.GetString(DbAttributeInstance.NAMN) == usermod);
                var deptGCC = logid.GetElement(DbAttributeInstance.USEF);
                return deptGCC.ToString();
            default:

                string site = hier == "GPSET" ? dbElement.Ref.ToString() : dbElement.EvaluateAsString(DbExpression.Parse($"SITE of {dbElement}"));
                //:UES_DEPART надо ли? vсмотрел, его со времен царя гороха никто не заполняет
                //isnullorEmpty
                if (site.Length > 0)
                {
                    string index = site.Substring(site.IndexOf('_'), 3);
                    var dept = DepartmentInfo.Departments.Where(d => d.Mark.Contains(index)).ToList();
                    bool IsBool = SpecProj.Contains(ProjectName);
                    foreach (var d in dept)
                    {
                        if (IsBool) return d.Tdept;
                        else return d.Dept;

                    }
                }
                break;
        }
        return null;
    }
    /// <summary>
    /// ТЕСТ
    /// </summary>
    [PMLNetCallable]
    public string GetGroupsTest(string dbElementref)
    {
        var dbElement = DbElement.GetElement(dbElementref);
        if (!dbElement.IsValid || dbElement.IsNull) return "";
        var ElementDepth = dbElement.GetInteger(DbAttribute.GetDbAttribute("DbDepth"));
        var DbElType = dbElement.GetString(DbAttributeInstance.TYPE);
        var Site = dbElement.GetSite();
        var Zone = dbElement.GetZone();

        for (int i = 0; i <= ElementDepth; i++)
        {
            if (DbElType == "GPSET") return dbElement.Name();

            else if (DbElType == "GENPRI" || DbElType == "GENCUR" && Site.Name().Contains("_AC")) //// ?????
            {
                var sdvds = DbElement.GetElement(Zone.Ref);
                var Gpref = sdvds.GetElement(DbAttribute.GetDbAttribute(":UES_GPREF")).ToString();

                return Gpref;
            }
            else
            {
                try
                {
                    var DbElGroups = dbElement.GetElement(DbAttribute.GetDbAttribute(":UES_GPREF"));
                    if (DbElGroups.IsValid)
                    {
                        return DbElGroups.ToString();
                    }

                }
                catch
                {
                    dbElement = dbElement.Owner;
                }
            }


        }
        return "";


    }

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
            query = $"select {ClashSql} from {clashTableName} where (el1 = '{firstElement}' and el2 = '{secondElement}' or el1 = '{secondElement}' and el2 = '{firstElement}')";
        else
            query = $"select {ClashSql} from {clashTableName} where (el1 = '{firstElement}' and el2 = '{secondElement}' and ClashType = '{clashType}'  or el1 = '{secondElement}' and el2 = '{firstElement}' and ClashType = '{invt}')";

        return [.. clashConnection.Query<ClashEntity>(query)];


    }

    public List<ClashEntity> QueryClashByEl(SqlConnection clashConnection, string clashTableName, string dbElementref, string wherestring)
    {
        var dbElement = DbElement.GetElement(dbElementref);

        //собираем элементы
        //var CurEl = DbElement.GetElement(dbElement.ToString());
        var collection = new DBElementCollection(dbElement).Cast<DbElement>().ToList();
        string[] els = [];
        string tableName = "";
        var projectUserLogin = Project.CurrentProject.LoginUser;
        for (int i = 0; i <= collection.Count - 1; i++)
        {
            var tmp = "";
            var CE = collection[i];
            if (CE.ElementType.Description.ToString() == "Tubing")
            {
                tmp = $"ileav tube of {CE.ToString()}";
            }

            els.Append(tmp.Replace("ileav rod of", "ileav tube of"));

        }

        string hostName = Environment.MachineName;

        if (hostName.Contains("GPU") || hostName.Contains("GRU") || hostName.Contains("0047"))
        {
            var file = File.ReadAllLines("X:\\App\\PDMS\\PDMS_TEP\\ADMIN\\User_TO_VM.csv");

            foreach (string line in file)
            {
                string trimUS = line.Trim('"');
                string[] User = trimUS.Split('\t');
                if (projectUserLogin.ToLower() == User[0].ToLower())
                    tableName = $"{User[1]}{User[0]}";
            }

        }
        else
        {
            tableName = $"{hostName}{projectUserLogin}";
        }
        //string SelectTableName = $"SELECT * FROM INFORMATION_SCHEMA.TABLES where TABLE_NAME = '{tableName}'";
        //var select = clashConnection.Execute(SelectTableName);

        if (!clashConnection.TableExists(tableName))
            clashConnection.Execute($"CREATE TABLE {tableName} ( [El] NVARCHAR(40) )");

        //1 очистить tmptable

        string DeleteTable = $"delete from {tableName}";
        clashConnection.Execute(DeleteTable);

        //2 получить всех потомков и записать refno в таблицу
        for (int i = 0; i <= els.Length - 1; i++)
        {
            string InsertTable = $"insert into {tableName} ( el) values ({els[i]})";
            clashConnection.Execute(InsertTable);
        }
        //clashConnection.Close();

        //получить из базы всё по этому элементу

        string GetRowTableName = $"select id 'Id', clashtype 'ClashType', El1 'FirstElement', type1 'FirstType', usermod1 'FirstUserMode', dept1 'FirstDept', gpset1 'FirstGpset', El2 'SecondElement', type2 'SecondType', usermod2 'SecondUserMode', dept2 'SecondDept', gpset2 'SecondGpset', date 'Date', x 'X', y 'Y', z 'Z', existing 'Existing',Building 'Building', Sequence 'Sequence', RequestToDept 'RequestToDept', RequestUser 'RequestUser', RequestDate 'RequestDate', ApproveUser 'ApproveUser', ApproveDate 'ApproveDate', ApproveReason 'ApproveReason', InWorkUser 'InWorkUser', InWorkDate 'InWorkDate' " +
            $"from {clashTableName} where ((el1 in (select el from {tableName}) or el2 in (select el from {tableName})) {wherestring} )";
        var clashList = clashConnection.Query<ClashEntity>(GetRowTableName).ToList();

        if (clashConnection.TableExists(tableName))
            clashConnection.Execute($"drop TABLE {tableName}");

        return clashList;

    }


    private async Task CheckResultToBase(SqlConnection sqlConnection, string clashTableName, ClashSet clashSet)
    {
        Logger.WriteLine($"Запись колизий в базу...");
        Logger.WriteLine($"Количесво коллизий: {clashSet.Clashes.Length}");
        var notIgnoredClashes = clashSet.Clashes.Where(e => !IsClashIgnore(e));
        Logger.WriteLine($"Количесво проигнорированных коллизий: {clashSet.Clashes.Length - notIgnoredClashes.Count()}");

        foreach (var clash in notIgnoredClashes)
            InsertOneCLash(sqlConnection, clashTableName, clash);

        PML.CreateCommand($"$p {notIgnoredClashes.Count()} коллизий подтвердилось после проверки").RunInPdms();
        GC.Collect();
    }

    /// <summary>
    /// Вставляем информацию о коллизии в SQL (без информации из ТДМС)
    /// TODO: Добавить инфу из тдмс (KKS здания, ответсвтенное лицо, плановую дату сдачи комплекта (если связь с таковым имеется))
    /// </summary>
    /// <param name="clashConnection"></param>
    /// <param name="clashTableName"></param>
    /// <param name="clash"></param>
    private void InsertOneCLash(SqlConnection clashConnection, string clashTableName, Clash clash)
    {
        var clashType = clash.Type.ToString();
        var firstElement = clash.First;
        var secondElement = clash.Second;
        var firstType = clash.First.ElementType;
        var secondType = clash.Second.ElementType;
        var date = DateTime.Now;
        var flnm1 = clash.First.GetAsString(DbAttributeInstance.FLNM).Replace(" ' ", " ");
        var flnm2 = clash.Second.GetAsString(DbAttributeInstance.FLNM).Replace(" ' ", " ");

        var firstDept = GetDepartment(clash.First, " ");
        var secondDept = GetDepartment(clash.Second, " ");

        var firstGroups = GetGroups(clash.First);
        var secondGroups = GetGroups(clash.Second);

        var firstUserMode = History(clash.First, "user");
        var secondUserMode = History(clash.Second, "user");

        var building = "";
        var buildingFirst = clash.First.GetAsString(DbAttributeInstance.DBNA);
        if (buildingFirst == clash.Second.GetAsString(DbAttributeInstance.DBNA))
            building = buildingFirst.Split('/')[1].Split('_')[0];

        var clashesFromBase = QueryOneSqlClash(clashConnection, clashTableName, clashType, firstElement.ToString(), secondElement.ToString());

        if (clashesFromBase.Count == 0)
        {
            try
            {
                clashConnection.ExecuteAsync($@"INSERT INTO {clashTableName} 
                                                        ( clashtype, el1, type1, usermod1, flnm1, dept1, gpset1, el2, type2, usermod2, flnm2, dept2, gpset2, date, x, y, z, existing, Building)
                                        VALUES 
                                                         ( @clashtype ,@el1, @type1, @usermod1, @flnm1, @dept1, @gpset1, @el2, @type2, @usermod2, @flnm2, @dept2, @gpset2, @date, @x, @y, @z, 'true', @Building);",
                new
                {
                    ClashType = clashType,
                    el1 = firstElement.GetAsString(DbAttributeInstance.REF),
                    el2 = secondElement.GetAsString(DbAttributeInstance.REF),
                    type1 = firstType.ToString(),
                    type2 = secondType.ToString(),
                    usermod1 = firstUserMode,
                    usermod2 = secondUserMode,
                    flnm1,
                    flnm2,
                    dept1 = firstDept,
                    dept2 = secondDept,
                    gpset1 = firstGroups,
                    gpset2 = secondGroups,
                    x = clash.ClashPosition.X,
                    y = clash.ClashPosition.Y,
                    z = clash.ClashPosition.Z,
                    date,
                    Building = building
                });
            }

            catch (Exception ex)
            {
                Logger.WriteLine($"Ошибка в InsertOneCLash {ex.Message}");
            }

        }
        else if (clashesFromBase.Count > 1)
        {
            Logger.WriteLine($"ВНИМАНИЕ в базе более чем одна такая коллизия {firstElement} {secondElement}");
        }
        else
        {
            var resultClash = clashesFromBase.First();
            double x0 = Convert.ToDouble(resultClash.X);
            double y0 = Convert.ToDouble(resultClash.Y);
            double z0 = Convert.ToDouble(resultClash.Z);

            if ((Math.Abs(x0 - clash.ClashPosition.X) >= 25) && (Math.Abs(y0 - clash.ClashPosition.Y)) >= 25 && (Math.Abs(z0 - clash.ClashPosition.Z)) >= 25)
            {
                var Id = clashesFromBase[0];
                string QueryDel = $"DELETE FROM {clashTableName} where id = '{Id}'";
                clashConnection.Execute(QueryDel);
                clashConnection.ExecuteAsync($@"INSERT INTO {clashTableName} 
                                                        ( clashtype, el1, type1, usermod1, flnm1, dept1, gpset1, el2, type2, usermod2, flnm2, dept2, gpset2, date, x, y, z, existing, Building)
                                        VALUES 
                                                         ( @clashtype ,@el1, @type1, @usermod1, @flnm1, @dept1, @gpset1, @el2, @type2, @usermod2, @flnm2, @dept2, @gpset2, @date, @x, @y, @z, 'true', @Building);",
                new
                {
                    ClashType = clashType,
                    el1 = firstElement.GetAsString(DbAttributeInstance.REF),
                    el2 = secondElement.GetAsString(DbAttributeInstance.REF),
                    type1 = firstType.ToString(),
                    type2 = secondType.ToString(),
                    usermod1 = firstUserMode,
                    usermod2 = secondUserMode,
                    flnm1,
                    flnm2,
                    dept1 = firstDept,
                    dept2 = secondDept,
                    gpset1 = firstGroups,
                    gpset2 = secondGroups,
                    x = clash.ClashPosition.X,
                    y = clash.ClashPosition.Y,
                    z = clash.ClashPosition.Z,
                    date,
                    Building = building
                });



                //более подробная информация о перезаписываемом клеше пока не удалили его

                //Тут надо переписать лог файл. ПОЗЖЕ
            }
            else
            {
                //ничего не делаем т.к. эта коллизия с этой же координатой
                //тут надо обновить статус existing потомучто перед чеком он сбрасывается(в false)
                //а потом удаляются все которые не existing
                //этот метод воскрешения для CheckGpset (он им воспользуется после вставки всех коллизий) для checkall
                //он будет отрабатывать впустую
                string Id = resultClash.Id.ToString();
                ResurRectClash(Id);
                if (!resultClash.Existing)
                {
                    //воскрешаем коллизию
                    //этот запрос можно не делать если точно знать что это проверка комплекта, а не checkall()
                    //тоесть эта функция вызывается из двух мест и эта строка для подстраховки, тк она обязательно нужна для CheckAll
                    string QueryUpdate = $"update {clashTableName} SET existing = 'True' WHERE id = '{Id}'";
                    clashConnection.ExecuteAsync(QueryUpdate);
                }

            }
        }


    }

    public void ResurRectClash(string id)
    {
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
                    secondBran = frmw.GetElement(DbAttributeInstance.SUPR).Members().First().GetElement(DbAttributeInstance.HREF).Owner;
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

    private void CreateTableRefUpdateLog(string projectCode, SqlConnection sqlConnection)
    {
        string ClashRefUpdateLog = $"Clash{projectCode}_RefUpdateLog";
        sqlConnection.Execute($"CREATE TABLE [{ClashRefUpdateLog}]( [RunId] INT IDENTITY(1,1) NOT NULL, [UpdateTime] DATETIME NOT NULL, [RowId] INT NOT NULL, [OldEl1] NVARCHAR(50) NULL, [NewEl1] NVARCHAR(50) NULL, [flnm1] NVARCHAR(500) NULL);");
    }



}