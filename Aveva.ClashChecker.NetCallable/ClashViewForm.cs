using Aveva.ClashChecker.NetCallable;
using Aveva.ClashChecker.NetCallable.Extensions;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.ClashChecker.NetCallable.Sql;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.PMLNet;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Windows;
using CC = global::ClashChecker.ClashChecker;
//using System.Windows.Forms;
using PML = Aveva.Core.Utilities.CommandLine.Command;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;

namespace ClashViewForm
{
    [PMLNetCallable]
    public class ClashViewForm
    {
        public Dictionary<string, DateTime> CheckedZones = [];
        public string TableName = "";
        public string CurrZone = "";
        public string CLASHdir = "";
        public string MyUlogId = Project.CurrentProject.LoginUser;
        public string MyDept = Project.CurrentProject.UserName;
        public double Format = 0;
        public DbElement DBO;
        public List<string> DataTable = [];
        public DateTime Dateto = DateTime.Now;
        public DateTime Dateform = DateTime.Now;
        public CC checker = new CC();


        [PMLNetCallable]
        public ClashViewForm()
        {

        }
        public string ClashConnectionString { get; set; } = "Data Source=10.177.6.99,1433;Initial Catalog=avevaclash;Persist Security Info=True;User ID=ClashAdmin;Password=AXBqMLz3mVER;Connection Timeout = 300;TrustServerCertificate=true";
       
        /// <summary>
        /// Стандартная конструкция для детекта класса авевой
        /// </summary>
        /// <param name="that"></param>

        private const string DefaultLogDirectoryPath = "C:\\AVEVA\\ClasherLogs\\ClashLog.log";
        private ClashLogger Logger { get; set; } = new ClashLogger(DefaultLogDirectoryPath);
        [PMLNetCallable]
        public void Assign(ClashViewForm that)
        {
        }
        [PMLNetCallable]


        private DateTime GetLastZoneClashDate(string zoneRef, string clashTableName)
        {
            using SqlConnection clashConnection = new(ClashConnectionString);
            clashConnection.Open();
            var lastDate = clashConnection.ExecuteScalar<DateTime?>($@"SELECT MAX([DT])
                                                                       FROM {clashTableName}
                                                                       WHERE [G1] = @zoneRef OR [G2] = @zoneRef",
                                                                       new { zoneRef });
            return lastDate ?? DateTime.MinValue;

        }

        [PMLNetCallable]
        public DateTime GetZoneLastModified(string zoneRef)
        {
            var zone = DbElement.GetElement(zoneRef);
            var lastModifiedAttribute = DbAttribute.GetDbAttribute("lastmod");
            DateTime lastModified = zone.GetDateTime(lastModifiedAttribute);

            foreach (DbElement element in new DBElementCollection(zone).Cast<DbElement>())
            {
                DateTime elementLastModified = element.GetDateTime(lastModifiedAttribute);
                if (elementLastModified > lastModified)
                    lastModified = elementLastModified;
            }

            return lastModified;
        }

        [PMLNetCallable]
        public void CheckZone(string zoneRef, bool testMode = true)
        {
           // Logger = new ClashLogger(logDirectoryPath);
            Logger.LogInPdmsConsole = testMode;
            var zone = DbElement.GetElement(zoneRef);
            if (zone.ElementType != DbElementTypeInstance.ZONE)
            {
                System.Windows.MessageBox.Show($"{zoneRef} не является зоной.");
                return;
            }
            string ProjectName = Project.CurrentProject.Name;
            string mdb = MDB.CurrentMDB.Name;
            var isAll = string.Equals(mdb, $"{ProjectName}.ALL");
            string clashTableName = $"clashtable{ProjectName}_TEST";
            if (mdb != "/ALL" && mdb != "/16310" && isAll)
            {
                var Answer = System.Windows.MessageBox.Show("Проверку зоны необходимо запускать в MDB \"ALL\". Сохраниться и перейти в MDB \"ALL\"?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (Answer == MessageBoxResult.Yes)
                {
                    MDB.CurrentMDB.SaveWork("");

                }
                else
                {
                    return;
                }

            }
            MDB.CurrentMDB.GetWork();
            using SqlConnection clashConnection = new(ClashConnectionString);
            clashConnection.Open();
            var notExistingCount = clashConnection.ExecuteScalar<int>(@$"select count(*)
                                                               from {clashTableName} 
                                                               WHERE [XT] = 0
                                                               AND ([G1] = @zoneRef OR [G2] = @zoneRef)",
                                                               new { zoneRef });

            if (notExistingCount > 0)
            {
                System.Windows.MessageBox.Show("действие отменено. проверка невозможна. обратитесь в ОАП");
                return;
            }

            checker.UpdateClashElementInfo(clashConnection, "", clashTableName, zoneRef);
            var clashesByZone = clashConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql}
                                                                       FROM {clashTableName}
                                                                       WHERE [G1] = @zoneRef OR [G2] = @zoneRef",
                                                                       new { zoneRef })
                                                                       .ToList();

            PML.CreateCommand($"$p Коллизий зоны {zoneRef} до проверки {clashesByZone.Count}").RunInPdms();

            checker.ColZone(clashConnection, clashTableName, zoneRef);

            var notExistingClashes = clashConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql}
                                                                            FROM {clashTableName}
                                                                            WHERE [XT] = 0
                                                                            AND ([G1] = @zoneRef OR [G2] = @zoneRef)",
                                                                            new { zoneRef })
                                                                            .ToList();
            foreach (var clash in notExistingClashes)
            {
                checker.DeleteById(clashConnection, clashTableName, clash, "AfterCheckZONE", ".CheckZone: коллизия больше не относится к зоне и удалена после проверки");
            }

            PML.CreateCommand($"$p Из зоны {zoneRef} удалено {notExistingClashes.Count} несуществующих коллизий").RunInPdms();

            UpdateZoneList();


            CheckedZones[zone.Name()] = DateTime.Now;


        }
        public List<ZoneComboItem> UpdateZoneList()
        {
            List<DbElement> zonesKomplect = [.. new DBElementCollection(
                    new TypeFilter(DbElementTypeInstance.ZONE))
                .Cast<DbElement>()
                .Where(zone =>
                {

                    string purpose = zone.GetAsString(DbAttributeInstance.PURP);
                     string name = zone.GetAsString(DbAttributeInstance.MEMB);
                    return (string.Equals(purpose, "PD", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(purpose, "RD", StringComparison.OrdinalIgnoreCase))
                        && !string.Equals(name, "unset", StringComparison.OrdinalIgnoreCase);
                })];
                

            var zoneItems = new List<ZoneComboItem>
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

            zoneItems.AddRange(zonesKomplect.Select(zone =>
                new ZoneComboItem
                {
                    ZoneElement = zone.Name(),
                    DisplayText = zone.Name()
                }));

            return zoneItems;


        }





        [PMLNetCallable]
        public void SetChange(SqlConnection sqlConnection, string clashTableName, string zoneRef)
        {
            //меняем цвет кнопки
            UpdateCheckedStatus();
            Show(clashTableName, zoneRef);
        }
        [PMLNetCallable]
        public void UpdateCheckedStatus()
        {
            //ищем текущую зону среди недавно проверенных зон
            //это специальный массив который изначально пустой он содержит имена элементов которые тока что проверены.он пополняется после чека

        }

        [PMLNetCallable]

        public List<ClashEntity> Show(string clashTableName, string zoneRef)
        {
            using SqlConnection sqlConnection = new(ClashConnectionString);
            sqlConnection.Open();

            if (zoneRef == "CE")
            {
                DbElement currentElement = CurrentElement.Element;
                string currentElementRef = currentElement.GetAsString(DbAttributeInstance.REF);
                return checker.QueryClashByEl(sqlConnection, clashTableName, currentElementRef);
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




        public void Report(string Gpset)
        {
            CurrZone = Gpset;
            var GpserRef = DbElement.GetElement(Gpset);
            LockKomplect(GpserRef);

        }
        public bool IsGreenZone(string zoneRef, string clashTableName)
        {
            if (zoneRef == "ALL" || zoneRef == "CE") return false;

            var zone = DbElement.GetElement(zoneRef);
            if (zone.IsNull || !zone.IsValid) return false;

            string zoneDepartment = checker.GetDepartment(zone, "");
            bool isForeignDepartment = zoneDepartment != MyDept && MyDept != "SYSTEM";
            if (isForeignDepartment)
            {
                System.Windows.MessageBox.Show($"{zoneRef} — это зона другого отдела");
                return false;
            }

            DateTime lastCheck = GetLastZoneClashDate(zoneRef, clashTableName);
            DateTime zoneLastModified = GetZoneLastModified(zoneRef);

            if (CheckedZones.TryGetValue(zoneRef, out DateTime sessionCheckTime))
            {
                if (sessionCheckTime > lastCheck)
                    lastCheck = sessionCheckTime;

            }
            if (lastCheck < zoneLastModified) return false;
            var deltaTime = (DateTime.Now - lastCheck).TotalDays;
            return deltaTime <= 2;
        }



        public void LockKomplect(DbElement GpsetRef)
        {
            List<string> GetKomplect = new List<string>();
            bool OKCLH = GpsetRef.GetBool(DbAttributeInstance.OKCLH);
            if (GpsetRef.GetString(DbAttributeInstance.TYPE) != "GPSET")
            {
                System.Windows.MessageBox.Show($"Ошибка! Тип элемента не GPSET");
                return;
            }


            if (!OKCLH)
            {
                System.Windows.MessageBox.Show($"Нельзя заблокировать комплект, пока один или несколько элементов в иерархии заклеймлено");
                return;
            }

            // var param = GetSqlParams(GpsetRef);
            //
            // using SqlConnection tdmsConnection = new(TDMSConnectionString);
            // {
            //
            //
            //     tdmsConnection.Open();
            //
            //     GetKomplect = tdmsConnection.Query<string>(@$"EXEC	[dbo].[PDMSGetStatus]		
            //                                                   @KOMPLECT, @VNCODE, @KKS",
            //                                                       new { KOMPLECT = param[2], VNCODE = param[1], KKS = param[3] })
            //                                                       .ToList();
            // }
            // if (GetKomplect.Count < 1)
            // {
            //     System.Windows.MessageBox.Show($"Комплект не найден в TDMS");
            //     return;
            // }
            // else if (GetKomplect.Count > 1)
            // {
            //     System.Windows.MessageBox.Show($"В TDMS найдено больше одного комплекта");
            //     return;
            // }
            //
            // if (GetKomplect[0] != "2253448")
            // {
            //     System.Windows.MessageBox.Show($"Нельзя заблокировать сданный в TDMS комплект {param[3]}");
            //     return;
            // }

            var Answer = System.Windows.MessageBox.Show("Выполнить Save Work", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (Answer == MessageBoxResult.Yes)
            {
                double old = GpsetRef.GetDouble(DbAttribute.GetDbAttribute(":UES_KSTATUS"));
                GpsetRef.SetAttribute(DbAttribute.GetDbAttribute(":UES_KSTATUS"), 10);



                //    SetPackagePdmsRightAttrByStageAndContract(param[1], param[2], param[4], param[3], true);
                //    GetKomplect = tdmsConnection.Query<string>(@$"EXEC	[dbo].[PDMSGetStatus]		
                //                                                  @KOMPLECT, @VNCODE, @KKS",
                //                                                         new { KOMPLECT = param[2], VNCODE = param[1], KKS = param[3] })
                //                                                         .ToList();
                //    if (GetKomplect[0] != "1")
                //    {
                //        GpsetRef.SetAttribute(DbAttribute.GetDbAttribute(":UES_KSTATUS"), old);
                //        System.Windows.MessageBox.Show($"Не удалось заблокировать комплект {param[3]}");
                //        return;
                //    }
                MDB.CurrentMDB.SaveWork("");
            }
        }





    }
}
