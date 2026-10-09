using ClashForKPI.Models;
using ClashForKPI.Sql;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.PMLNet;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using ClashService = global::ClashForKPI.ClashService;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;

namespace ClashForKPI
{
    [PMLNetCallable]
    public class ClashLauncher
    {
        public TimeSpan LastPreparationElapsed { get; private set; }
        public TimeSpan LastClashCheckElapsed { get; private set; }
        public TimeSpan LastSqlSyncElapsed { get; private set; }
        public TimeSpan LastCleanupElapsed { get; private set; }
        public TimeSpan LastTotalCheckElapsed { get; private set; }
        public TimeSpan LastStartupElapsed { get; private set; }
        public TimeSpan LastCurrentCollectionElapsed { get; private set; }
        public TimeSpan LastObstructionCollectionElapsed { get; private set; }
        public TimeSpan LastObstructionListElapsed { get; private set; }
        public TimeSpan LastCheckZonesElapsed { get; private set; }
        public TimeSpan LastCheckAttributeElapsed { get; private set; }
        public TimeSpan LastSaveWorkElapsed { get; private set; }
        public string MyUlogId = Project.CurrentProject.LoginUser;
        public string MyDept = Project.CurrentProject.UserName;
        public HashSet<string> MyDepartments{get;private set;} = new(StringComparer.OrdinalIgnoreCase);
        private readonly ClashService clashService = new ClashService();
        public ClashService Service => clashService;
        private static readonly string[] AvevaDateFormats = new[]
        {
            "HH:mm:ss d MMMM yyyy",
            "H:mm:ss d MMMM yyyy",
            "HH:mm d MMM yyyy",
            "H:mm d MMM yyyy"
        };


        [PMLNetCallable]
        public ClashLauncher()
        {
            MyDepartments = GetCurrentUserDepartments();
            Logger.WriteLine($"Отделы пользователя {MyUlogId}:" + string.Join(",", MyDepartments));

        }
        public string ClashConnectionString => clashService.ClashConnectionString;
       
        /// <summary>
        /// Стандартная конструкция для детекта класса авевой
        /// </summary>
        /// <param name="that"></param>

        private const string DefaultLogDirectoryPath = "C:\\AVEVA\\ClasherLogs\\ClashLog.log";
        private ClashLogger Logger { get; set; } = new ClashLogger(DefaultLogDirectoryPath);
        [PMLNetCallable]
        public void Assign(ClashLauncher that)
        {
        }
        private HashSet<string> GetCurrentUserDepartments()
        {
            DbElement ulog = DbElement.GetElement($"/{MyUlogId}");
            DbAttribute departmentAttribute = DbAttribute.GetDbAttribute(":UserDept");
            string value = ulog.GetAsString(departmentAttribute) ?? string.Empty;
            return value.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(department => department.Trim()).Where(department => department != "unset").ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public DateTime GetZoneElementsLastModified(string zoneRef)
        {
            DbElement zone = DbElement.GetElement(zoneRef);
            DbAttribute lastModifiedAttribute = DbAttribute.GetDbAttribute("lastmod");
            DateTime lastModified = DateTime.MinValue;

            foreach (DbElement element in new DBElementCollection(zone).Cast<DbElement>())
            {
                DateTime elementLastModified = GetAttributeDate(element, lastModifiedAttribute);

                if (elementLastModified > lastModified)
                    lastModified = elementLastModified;
            }

            return lastModified;
        }

        public DateTime GetZoneLastCheck(string zoneRef)
        {
            DbElement zone = DbElement.GetElement(zoneRef);
            DbAttribute lastCheckAttribute = DbAttribute.GetDbAttribute(":Check");

            return GetAttributeDate(zone, lastCheckAttribute);
        }

        private DateTime GetAttributeDate(DbElement element, DbAttribute attribute)
        {
            string dateValue = element.GetAsString(attribute) ?? string.Empty;

            if (DateTime.TryParseExact(dateValue.Trim(), AvevaDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime date))
                return date;

            if (element.ElementType == DbElementTypeInstance.ZONE)
                Logger.WriteLine($"Не удалось преобразовать дату '{dateValue}' зоны {element.Name()}");
            return DateTime.MinValue;
        }

        [PMLNetCallable]
        public void CheckZone(string zoneRef, bool logToPdmsConsole = true)
        {
            CheckZone(zoneRef, logToPdmsConsole, 1);
        }

        public void CheckZone(string zoneRef, bool logToPdmsConsole, int checkMode)
        {
            var totalCheckStopwatch = Stopwatch.StartNew();
            LastPreparationElapsed = TimeSpan.Zero;
            LastClashCheckElapsed = TimeSpan.Zero;
            LastSqlSyncElapsed = TimeSpan.Zero;
            LastCleanupElapsed = TimeSpan.Zero;
            LastTotalCheckElapsed = TimeSpan.Zero;
            LastStartupElapsed = TimeSpan.Zero;
            LastCurrentCollectionElapsed = TimeSpan.Zero;
            LastObstructionCollectionElapsed = TimeSpan.Zero;
            LastObstructionListElapsed = TimeSpan.Zero;
            LastCheckZonesElapsed = TimeSpan.Zero;
            LastCheckAttributeElapsed = TimeSpan.Zero;
            LastSaveWorkElapsed = TimeSpan.Zero;
           // Logger = new ClashLogger(logDirectoryPath);
            Logger.LogInPdmsConsole = logToPdmsConsole;
            DbElement zone = DbElement.GetElement(zoneRef);
            if (zone.ElementType != DbElementTypeInstance.ZONE)
            {
                System.Windows.MessageBox.Show($"{zoneRef} не является зоной.");
                return;
            }
            string projectName = Project.CurrentProject.Name;
            string mdb = MDB.CurrentMDB.Name;
            bool isPD = string.Equals(mdb, $"{projectName}.PD");
            bool isRD = string.Equals(mdb, $"{projectName}.RD");
            string clashTableName = $"clashtable{projectName}_TEST";
             if (!isPD && !isRD)
            {
               System.Windows.MessageBox.Show($"Проверку зоны необходимо запускать в MDB \"{projectName}.PD\" или \"{projectName}.RD\".");
               return; 
            }
                   
            using SqlConnection clashConnection = new(ClashConnectionString);
            clashConnection.Open();
            int notExistingCount = clashConnection.ExecuteScalar<int>(@$"select count(*)
                                                               from {clashTableName} 
                                                               WHERE [XT] = 0
                                                               AND ([G1] = @zoneRef OR [G2] = @zoneRef)",
                                                               new { zoneRef });

            if (notExistingCount > 0)
            {
                System.Windows.MessageBox.Show($"Действие отменено. Проверка невозможна.\n\nНайдено неподтверждённых коллизий: {notExistingCount} (XT = 0).\nОбратитесь к администратору AVEVA.");
                return;
            }

            LastStartupElapsed = totalCheckStopwatch.Elapsed;
            var preparationStopwatch = Stopwatch.StartNew();
            if (checkMode == 1)
                clashService.RefreshStoredClashElementInfo(clashConnection, clashTableName, zoneRef, true);

            int clashCountBeforeCheck = clashConnection.ExecuteScalar<int>($@"SELECT COUNT(*) FROM [{clashTableName}] WHERE [G1] = @zoneRef OR [G2] = @zoneRef", new { zoneRef });
            Logger.WriteLine($"Коллизий зоны {zoneRef} до проверки: {clashCountBeforeCheck}");

            clashConnection.Execute($@"UPDATE [{clashTableName}]
                                       SET [XT] = 0
                                       WHERE [G1] = @zoneRef OR [G2] = @zoneRef",
                                       new { zoneRef });
            preparationStopwatch.Stop();
            LastPreparationElapsed = preparationStopwatch.Elapsed;
            Logger.WriteLine($"Ручная проверка, режим {checkMode}: подготовка до clash-check заняла {LastPreparationElapsed.TotalSeconds:F3} сек.");

            bool checkSucceeded = clashService.CheckZones(clashConnection, clashTableName, zoneRef, syncMode: checkMode);
            LastClashCheckElapsed = clashService.LastClashCheckElapsed;
            LastSqlSyncElapsed = clashService.LastSqlSyncElapsed;
            LastCurrentCollectionElapsed = clashService.LastCurrentCollectionElapsed;
            LastObstructionCollectionElapsed = clashService.LastObstructionCollectionElapsed;
            LastObstructionListElapsed = clashService.LastObstructionListElapsed;
            LastCheckZonesElapsed = clashService.LastCheckZonesElapsed;
            if (!checkSucceeded)
            {
                Logger.WriteLine($"Проверка зоны {zoneRef} завершилась с ошибкой. "
                    + "Удаление коллизий и запись :Check не выполнены.", LogType.Error);
                System.Windows.MessageBox.Show(
                    $"Проверка зоны {zoneRef} завершилась с ошибкой.\n\n"
                    + "Коллизии с XT = 0 оставлены для разбора администратором.");
                return;
            }

            var cleanupStopwatch = Stopwatch.StartNew();
            var notExistingClashes = clashConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql}
                                                                            FROM {clashTableName}
                                                                            WHERE [XT] = 0
                                                                            AND ([G1] = @zoneRef OR [G2] = @zoneRef)",
                                                                            new { zoneRef })
                                                                            .ToList();
            int cleanupErrorCount = 0;
            foreach (var clash in notExistingClashes)
            {
                try
                {
                    clashService.RemoveClash(clashConnection, clashTableName, clash, ".CheckZone: коллизия больше не относится к зоне и удалена после проверки");
                }
                catch (Exception ex)
                {
                    cleanupErrorCount++;
                    Logger.WriteLine($"Не удалось обработать коллизию {clash.Id}: {ex.Message}", LogType.Error);
                }
            }

            int processedClashCount = notExistingClashes.Count - cleanupErrorCount;
            cleanupStopwatch.Stop();
            LastCleanupElapsed = cleanupStopwatch.Elapsed;
            Logger.WriteLine($"В зоне {zoneRef} обработано исчезнувших коллизий: {processedClashCount}");

            if (cleanupErrorCount > 0)
            {
                Logger.WriteLine($"Проверка зоны {zoneRef}: не обработано коллизий {cleanupErrorCount}. :Check не обновлён.", LogType.Error);
                return;
            }

            DateTime checkDate = DateTime.Now;
            string checkDateValue = checkDate.ToString("HH:mm:ss d MMMM yyyy", CultureInfo.InvariantCulture);
            var checkAttributeStopwatch = Stopwatch.StartNew();
            zone.SetAttribute(DbAttribute.GetDbAttribute(":Check"), checkDateValue);
            checkAttributeStopwatch.Stop();
            LastCheckAttributeElapsed = checkAttributeStopwatch.Elapsed;

            var saveWorkStopwatch = Stopwatch.StartNew();
            MDB.CurrentMDB.SaveWork("");
            saveWorkStopwatch.Stop();
            LastSaveWorkElapsed = saveWorkStopwatch.Elapsed;
            totalCheckStopwatch.Stop();
            LastTotalCheckElapsed = totalCheckStopwatch.Elapsed;
            Logger.WriteLine($"Замер ручной проверки, режим {checkMode}: до check {LastPreparationElapsed.TotalSeconds:F3} сек.; clash-check {LastClashCheckElapsed.TotalSeconds:F3} сек.; SQL {LastSqlSyncElapsed.TotalSeconds:F3} сек.; очистка {LastCleanupElapsed.TotalSeconds:F3} сек.; всего {LastTotalCheckElapsed.TotalSeconds:F3} сек.");
        }
        public List<ZoneComboItem> UpdateZoneList()
        {
            DbAttribute isCompleteAttribute = DbAttribute.GetDbAttribute(":IsComplete");

            List<DbElement> packageZones = [.. new DBElementCollection(
                    new TypeFilter(DbElementTypeInstance.ZONE))
                .Cast<DbElement>()
                .Where(zone =>
                {

                    string purpose = zone.GetAsString(DbAttributeInstance.PURP);
                     string membersValue = zone.GetAsString(DbAttributeInstance.MEMB);
                    return (string.Equals(purpose, "PD", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(purpose, "RD", StringComparison.OrdinalIgnoreCase))
                        && !string.Equals(membersValue, "unset", StringComparison.OrdinalIgnoreCase);
                })];
                

            List<ZoneComboItem> zoneItems = new List<ZoneComboItem>
            {
                new()
                {
                    ZoneElement = "ALL",
                    DisplayText = "ALL"
                },
                new()
                {
                    ZoneElement = "CE",
                    DisplayText = "CE"
                }
            };

            zoneItems.AddRange(packageZones.Select(zone =>
                new ZoneComboItem
                {
                    ZoneElement = zone.Name(),
                    DisplayText = zone.Name(),
                    IsComplete = zone.GetBool(isCompleteAttribute)
                }));

            return zoneItems;


        }





        public List<ClashEntity> GetClashes(string clashTableName, string zoneRef)
        {
            using SqlConnection sqlConnection = new(ClashConnectionString);
            sqlConnection.Open();

            if (zoneRef == "CE")
            {
                DbElement currentElement = CurrentElement.Element;
                string currentElementRef = currentElement.GetAsString(DbAttributeInstance.REF);
                return clashService.QueryClashesByElement(sqlConnection, clashTableName, currentElementRef);
            }

            if (zoneRef == "ALL")
            {
                return sqlConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql}
                                                            FROM {clashTableName}")
                                                            .ToList();
            }

            return sqlConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql}
                                                        FROM {clashTableName}
                                                        WHERE [G1] = @zoneRef
                                                           OR [G2] = @zoneRef",
                                                        new { zoneRef })
                                                        .ToList();
        }




        public bool IsZoneCheckActual(string zoneRef)
        {
            if (zoneRef == "ALL" || zoneRef == "CE")
                return false;

            DbElement zone = DbElement.GetElement(zoneRef);

            if (zone.IsNull || !zone.IsValid)
                return false;

            DateTime lastCheck = GetZoneLastCheck(zoneRef);

            if (lastCheck == DateTime.MinValue)
                return false;

            Stopwatch manualStopwatch = Stopwatch.StartNew();
            bool changedByManualScan = lastCheck < GetZoneElementsLastModified(zoneRef);
            manualStopwatch.Stop();

            Stopwatch filterStopwatch = Stopwatch.StartNew();
            bool? changedByFilter = null;
            try
            {
                changedByFilter = HasZoneChangedSince(zone, lastCheck);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Ошибка теста AttributeDateTimeFilter для зоны {zoneRef}: {ex.Message}", LogType.Error);
            }
            filterStopwatch.Stop();

            Logger.WriteLine($"Проверка LASTMOD зоны {zoneRef}: ручной перебор = {changedByManualScan}, {manualStopwatch.Elapsed.TotalMilliseconds:F1} мс; фильтр AVEVA = {changedByFilter?.ToString() ?? "ошибка"}, {filterStopwatch.Elapsed.TotalMilliseconds:F1} мс; совпадение = {changedByFilter.HasValue && changedByFilter.Value == changedByManualScan}.");

            if (changedByManualScan)
                return false;

            return (DateTime.Now - lastCheck).TotalDays <= 2;
        }

        private static bool HasZoneChangedSince(DbElement zone, DateTime lastCheck)
        {
            DbAttribute lastModifiedAttribute = DbAttribute.GetDbAttribute("LASTMOD");
            var modifiedFilter = new AttributeDateTimeFilter(lastModifiedAttribute, FilterOperator.GreaterThan, lastCheck);
            var modifiedElements = new DBElementCollection(zone, modifiedFilter) { IncludeRoot = false };
            return modifiedElements.Cast<DbElement>().Any();
        }



    }
}
