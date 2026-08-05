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
        public Dictionary<string, DateTime> Checkedsets = [];
        public string TableName = "";
        public string CurrGpset = "";
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
        public string ClashConnectionString { get; set; } = "Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;User ID=clashuser;Password=Qgh%fS45Nm;Connection Timeout = 300;TrustServerCertificate=true";
        public string TDMSConnectionString { get; set; } = "Data Source=sqltep;Initial Catalog=TDMS_TEP;Persist Security Info=True;User ID=Pdmstotdms;Password=PdMsToTdMs;Connection Timeout = 300;TrustServerCertificate=true";
        /// <summary>
        /// Стандартная конструкция для детекта класса авевой
        /// </summary>
        /// <param name="that"></param>

        private const string DefaultLogDirectoryPath = "D:\\AVEVA\\ClasherLogs\\ClashLog.log";
        private ClashLogger Logger { get; set; } = new ClashLogger(DefaultLogDirectoryPath);
        [PMLNetCallable]
        public void Assign(ClashViewForm that)
        {
        }
        [PMLNetCallable]


        private DateTime GetLastProjectCheckDate(string Gpset, string clashTableName)
        {
            using SqlConnection clashConnection = new(ClashConnectionString);
            clashConnection.Open();
            var lastDate = clashConnection.ExecuteScalar<DateTime?>($@"SELECT MAX([DT])
                                                                       FROM {clashTableName}
                                                                       WHERE [G1] = @gpset OR [G2] = @gpset",
                                                                       new { gpset = Gpset });
            return lastDate ?? DateTime.MinValue;

        }

        [PMLNetCallable]
        public DateTime GetGpsetLastMode(string GpsetRef)
        {
            var Gpset = DbElement.GetElement(GpsetRef);
            if (Gpset.Name() == "ALL") return DateTime.Now;
            //получаем lastmod hier комплекта
            var LastMode = Gpset.GetDateTime(DbAttribute.GetDbAttribute("lastmod"));
            var type = new ActualTypeFilter(DbElementType.GetElementType("gpitem"));
            List<DbElement> collection = [.. new DBElementCollection(Gpset, type).Cast<DbElement>()];
            foreach (DbElement Gpitem in collection)
            {
                var LastModeSitem = Gpitem.GetDateTime(DbAttribute.GetDbAttribute($"lastmod")); //   var lm = Gpitem.EvaluateDateTime(DbExpression.Parse($"lastmod hier of sitem of {Gpitem}"));
                if (LastModeSitem >= LastMode)
                {
                    LastMode = LastModeSitem;
                }
            }
            return LastMode;

        }


        public void CheckGpset(string GpsetRef, double initialZoneIndex, bool testMode = true, string logDirectoryPath = DefaultLogDirectoryPath)
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
                Logger.WriteLine($"Не удалось распознать начальный индекс зоны! {ex.Message}");
            }
            var Gpset = DbElement.GetElement(GpsetRef);
            string ProjectName = Project.CurrentProject.Name;
            string mdb = MDB.CurrentMDB.Name;
            var isAll = string.Equals(mdb, $"{ProjectName}.ALL");
            string clashTableName = $"clashtable{ProjectName}_TEST";
            if (mdb != "/ALL" && mdb != "/16310" && isAll)
            {
                var Answer = System.Windows.MessageBox.Show("Проверку комплекта необходимо запускать в MDB \"ALL\". Сохраниться и перейти в MDB \"ALL\"?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
            var NoteExist = clashConnection.ExecuteScalar<int>(@$"select  count (*) 
                                                               from {clashTableName} 
                                                               WHERE [XT] = 0
                                                               AND ([G1] = @Gpset OR [G2] = @Gpset)",
                                                               new { Gpset = GpsetRef });

            List<ClashEntity> ClashesByGpset = [];
            if (NoteExist > 1)
            {
                System.Windows.MessageBox.Show("действие отменено. проверка невозможна. обратитесь в ОАП");
                return;
            }
            if (Gpset.ElementType.ToString() == "GPSET")
            {

                checker.UpdateClashElementInfo(clashConnection, "", clashTableName, GpsetRef);
                //ClashesByGpset = clashConnection.Query<ClashEntity>($"SELECT {SqlMapping.ClashSql} from {clashTableName} WHERE (gpset1 = '{GpsetRef}' or gpset2 = '{GpsetRef}')").ToList();
                ClashesByGpset = clashConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql} 
                                                                   from {clashTableName} 
                                                                   WHERE [G1] = @gp",
                                                                   new { gp = GpsetRef })
                                                                   .ToList();

            }
            else
            {
                ClashesByGpset = checker.QueryClashByEl(clashConnection, clashTableName, GpsetRef);
            }
            PML.CreateCommand($"$p Коллизий комплекта {GpsetRef} до проверки {ClashesByGpset.Count}").RunInPdms();

            checker.ColZone(clashConnection, initialZoneIndexInt, ProjectName, clashTableName, logDirectoryPath, GpsetRef);

            var ClashesByGpsetFalseExist = clashConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql} 
                                                                             from {clashTableName} 
                                                                             WHERE [XT] = 0
                                                                             AND ([G1] = @Gpset OR [G2] = @Gpset)",
                                                                             new { Gpset = GpsetRef })
                                                                             .ToList();
            foreach (var e in ClashesByGpsetFalseExist)
            {
                checker.DeleteById(clashConnection, clashTableName, e, "AfterCheckGPSET", ".checkGPSET коллизия не относится к комплекту. удалена по завершению проверки");

            }
            //var ClashesByGpsetTrueExist = clashConnection.Query<ClashEntity>($"SELECT {SqlMapping.ClashSql} from {clashTableName} WHERE (gpset1 = '{GpsetRef}' or gpset2 = '{GpsetRef}')").ToList();
            var ClashesByGpsetTrueExist = clashConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql} 
                                                                             from {clashTableName} 
                                                                             WHERE [G1] = @Gpset",
                                                                             new { Gpset = GpsetRef })
                                                                             .ToList();
            PML.CreateCommand($"$p Из комплекта {GpsetRef} удалено {ClashesByGpsetTrueExist.Count - (ClashesByGpsetFalseExist.Count - 1)} несуществующих").RunInPdms();

            UpdateGpsetList();


            if (Gpset.ElementType.ToString() == "GPSET")
            {

                Checkedsets[Gpset.Name()] = DateTime.Now;

            }


        }
        public List<GpsetComboItem> UpdateGpsetList()
        {

            List<DbElement> AvevaGpset;
            AvevaGpset = [.. new DBElementCollection(new TypeFilter(DbElementTypeInstance.GPSET))
                          .Cast<DbElement>()
                          .Where(e =>
                          {
                              var User = e.GetAsString(DbAttribute.GetDbAttribute(":UES_USER"));
                              return User != "unset";
                          })];



            var gpsetItems = new List<GpsetComboItem>();
            gpsetItems.Add(new GpsetComboItem
            {
                GpsetElement = "ALL",
                DisplayText = "ALL"
            });
            gpsetItems.Add(new GpsetComboItem
            {
                GpsetElement = "CE",
                DisplayText = "CE"
            });



            foreach (var g in AvevaGpset)
            {
                var gpsetName = g.Name().ToString();
                if (gpsetItems.Any(s => s.GpsetElement.ToString() == gpsetName))
                    continue;
                gpsetItems.Add(new GpsetComboItem
                {
                    GpsetElement = gpsetName,
                    DisplayText = gpsetName

                });
            }

            return gpsetItems
                   .OrderBy(X =>
                   {
                       if (X.GpsetElement.ToString() == "ALL") return 2;
                       if (X.GpsetElement.ToString() == "CE") return 2;
                       return 1;
                   })
                   .ThenBy(X => X.DisplayText)
                   .ToList();


        }





        [PMLNetCallable]
        public void SetChange(SqlConnection sqlConnection, string clashTableName, string Gpset)
        {
            //меняем цвет кнопки
            UpdateCheckedStatus();
            Show(clashTableName, Gpset);
        }
        [PMLNetCallable]
        public void UpdateCheckedStatus()
        {
            //ищем в массиве недавно проверенных комплектов текущий, и если нашли красим
            //это специальный массив который изначально пустой он содержит имена элементов которые тока что проверены.он пополняется после чека

        }

        [PMLNetCallable]

        public List<ClashEntity> Show(string clashTableName, string Gpset)
        {
            using SqlConnection sqlConnection = new(ClashConnectionString);
            sqlConnection.Open();
            List<ClashEntity> GpsetTable = [];
            var sw = Stopwatch.StartNew();
            if (Gpset == "CE")
            {
                DbElement currentEl = CurrentElement.Element;
                string currentElRef = currentEl.GetAsString(DbAttributeInstance.REF);
                GpsetTable = checker.QueryClashByEl(sqlConnection, clashTableName, currentElRef);
            }

            else if (Gpset == "ALL")

            {
                GpsetTable = sqlConnection.Query<ClashEntity>(@$"select {SqlMapping.ClashSql} 
                                                              from {clashTableName}")
                                                              .ToList();

            }
            else
            {
                GpsetTable = sqlConnection.Query<ClashEntity>(@$"select {SqlMapping.ClashSql} 
                                                              from {clashTableName}
                                                              WHERE [G1] = @Gpset
                                                                 OR [G2] = @Gpset",
                                                              new { Gpset = Gpset })
                                                              .ToList();
            }
            sw.Stop();


            return GpsetTable;
        }




        public void Report(string Gpset)
        {
            CurrGpset = Gpset;
            var GpserRef = DbElement.GetElement(Gpset);
            LockKomplect(GpserRef);

        }
        public bool IsGreenGpset(string Gpset, string clashTableName)
        {
            var Gp = DbElement.GetElement(Gpset);

            if (Gpset == "ALL" || Gpset == "CE") return false;
            if (Gp.IsNull || !Gp.IsValid) return false;



            string gpsetDept = checker.GetDepartment(Gp, "GPSET");

            bool isForeignDept = (gpsetDept != MyDept && !(gpsetDept == "SOT" && MyDept == "OGS")) && MyDept != "SYSTEM";
            //не понятно зачем ОГС видеть коллизии СОТ
            if (isForeignDept)
            {
                System.Windows.MessageBox.Show($"{Gpset} - это комплект другого отдела");
                return false;
            }

            DateTime lastCheck = GetLastProjectCheckDate(Gpset, clashTableName);
            DateTime gpsetLastMode = GetGpsetLastMode(Gpset);

            if (Checkedsets.TryGetValue(Gpset, out DateTime sessionCheckTime))
            {
                if (sessionCheckTime > lastCheck)
                    lastCheck = sessionCheckTime;

            }
            if (lastCheck < gpsetLastMode) return false;
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



#if false
        public List<string> GetSqlParams(DbElement GpsetRef)
        {
            List<string> sqlParams = new List<string>();

            string komplect = GpsetRef.Name().ToString().Replace('_', '-').TrimStart('/');
            string GpOwner = GpsetRef.Owner.Name().ToString();
            string[] ownerParts = GpOwner.Split('-');
            string kks = ownerParts.Length > 1 ? ownerParts[1] : "";
            string gpwl = GpsetRef.GetGpwl().Name().ToString();
            string[] gpwlParts = gpwl.Split('-');
            string incode = 'N' + gpwlParts[0].Replace("/", "");
            string dogovor = gpwlParts[1];

            string contract = "";

            using SqlConnection tdmsConnection = new(TDMSConnectionString);
            tdmsConnection.Open();

            var result = tdmsConnection.Query<string>(@$"EXEC[dbo].[PDMSGetContractByStageInnerCode] @StageInnerCode", new { StageInnerCode = incode }).ToList();
            if (result.Count < 1)
            {
                System.Windows.MessageBox.Show($"Не найден договор для стадии {incode}");
                contract = dogovor;
            }
            else
            {
                contract = result[0];
            }

            if (dogovor == "18IM1D")
            {
                contract = dogovor;
            }

            sqlParams.Add(incode);
            sqlParams.Add(contract);
            sqlParams.Add(komplect);
            sqlParams.Add(kks);

            return sqlParams;
        }
#endif
#if false
        private static void SetPackagePdmsRightAttrByStageAndContract(string stageInnerCode, string contractCode, string buildingKKS, string setName, bool pdmsRight)
        {
            using var tdmsConnection = new SqlConnection("Server=SQLTEP;Database=TDMS_TEP;TrustServerCertificate=True;User ID=pdmstotdms;Password=PdMsToTdMs");
            tdmsConnection.Open();
            using var getSetIdWithStatusName = new SqlCommand(@"
      select top 1 setObj.F_objid as SetId, tstat.F_NAME as StatusName from TObject setObj 
       inner join TAttr stageLinkAttr on setObj.F_OBJID = stageLinkAttr.F_OBJID 
       inner join tattr contractLinkAttr on setObj.F_OBJID = contractLinkAttr.F_OBJID 
       inner join tattr pdmsAttr on setObj.F_OBJID = pdmsAttr.F_OBJID
      inner join TLinkAttr buildingLink on setObj.F_OBJID = buildingLink.F_LINKOBJID
      inner join tattr buildingKKS on buildingLink.F_OBJID = buildingKKS.F_OBJID
      inner join tattr setName on setObj.F_OBJID = setName.F_OBJID
      inner join TStatus tstat on setObj.F_STATUSID = tstat.F_STATUSID
      where 
      stageLinkAttr.F_ATTRDEFID = 214692471 and  stageLinkAttr.F_INT64VAL =
      (select top 1 stageObj.F_OBJID from TObject stageObj 
      inner join tattr stageInnerCode on stageObj.F_OBJID = stageInnerCode.F_OBJID 
      where stageInnerCode.F_ATTRDEFID = 6312 and stageInnerCode.F_STRVAL = @stageInnerCode) --Поиск стадии по коду
      and contractLinkAttr.F_ATTRDEFID = 5463 and contractLinkAttr.F_INT64VAL =
      (select top 1 contractObj.F_OBJID from TObject contractObj 
      inner join tattr contractCode on contractObj.F_OBJID = contractCode.F_OBJID 
      where contractCode.F_ATTRDEFID = 6373 and contractCode.F_STRVAL = @contractCode) --Поиск договора по коду
      and pdmsAttr.F_ATTRDEFID = 2004389 and pdmsAttr.F_INT64VAL = 1
      and setObj.F_VERSION = 0 and setObj.F_OBJTYPEID = 2492
      and buildingKKS.F_ATTRDEFID = 3975 and buildingKKS.F_STRVAL = @buildingKKS --Поиск сооружения по ККС
      and setName.F_ATTRDEFID  = 5066 and setName.F_STRVAL = @setName --Поиск комплекта по наименованию
      ", tdmsConnection);
            getSetIdWithStatusName.Parameters.AddWithValue("@stageInnerCode", stageInnerCode);
            getSetIdWithStatusName.Parameters.AddWithValue("@contractCode", contractCode);
            getSetIdWithStatusName.Parameters.AddWithValue("@buildingKKS", buildingKKS);
            getSetIdWithStatusName.Parameters.AddWithValue("@setName", setName);
            long setId = -1;
            string statusName = "";

            using (var tdmsReader = getSetIdWithStatusName.ExecuteReader())
            {
                if (tdmsReader.Read())
                {
                    //Если комплект найден
                    setId = tdmsReader.GetInt64(0);
                    statusName = tdmsReader.GetString(1);
                }
                else
                {
                    //Обработчик если комплект не найден в ТДМС
                    return;
                }
            }
            if (statusName != "STATUS_SET_WORK")
            {
                //Обработчик если статус комплекта не "В работе"
                return;
            }
            using var setPmsRightCommand = new SqlCommand(@"update tattr set F_INT64VAL = @pdmsRight where F_OBJID = @setId and F_ATTRDEFID = 15655994", tdmsConnection);
            setPmsRightCommand.Parameters.AddWithValue("@setId", setId);
            setPmsRightCommand.Parameters.AddWithValue("@pdmsRight", pdmsRight ? 1 : 0);
            setPmsRightCommand.ExecuteNonQuery();
            tdmsConnection.Close();
        }

#endif


    }
}
