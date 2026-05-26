using Aveva.ClashChecker.NetCallable;
using Aveva.ClashChecker.NetCallable.Extensions;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.ClashChecker.NetCallable.Sql;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Microsoft.Data.SqlClient;
using Aveva.Core.PMLNet;
using Aveva.Core3D.Clasher;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using CC = global::ClashChecker.ClashChecker;
using System.Diagnostics;
using ClashChecker;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using System.Windows.Forms;
using static Aveva.ClashChecker.NetCallable.Exceptions;
using PML = Aveva.Core.Utilities.CommandLine.Command;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;
using System.Windows;
using System.Collections;
using TDMS;


namespace ClashViewForm
{
    [PMLNetCallable]
    public class ClashViewForm
    {
        public List<string> Checkedsets = [];
        public List<string> CheckedsetsTIME = [];
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
        public void ShowWpf()
        {

            var window = new ViewForm.MainWindow();
            window.Show();


        }

        private DateTime GetLastProjectCheckDate(string Gpset, string clashTableName)
        {
            var gp = "/" + Gpset;
            using SqlConnection clashConnection = new(ClashConnectionString);
            clashConnection.Open();
            var lastDate = clashConnection.ExecuteScalar<DateTime?>($@"SELECT MAX([date])
                                                                       FROM {clashTableName}
                                                                       WHERE gpset1 = @gpset OR gpset2 = @gpset",
                                                                       new { gpset = Gpset });
            return lastDate ?? DateTime.MinValue;

        }
        private DateTime GetGpsetLastCheckTime(DbElement gpset)
        {
            return DateTime.Now;
        }
        // private bool isGreenGpset(string GpsetRef)
        // {
        //     bool retval = false;
        //
        //     if (GpsetRef == "ALL" || GpsetRef == "CE") return false;
        //
        //     var Gpset = DbElement.GetElement(GpsetRef);
        //     var User = Project.CurrentProject.LoginUser;
        //     var now = DateTime.Now;
        //
        //     if (Gpset.IsNull || !Gpset.IsValid) return false;
        //
        //     if (checker.GetDepartment(Gpset, "GPSET") != User && !(checker.GetDepartment(Gpset, "GPSET") == "SOT" && User == "OGS"))
        //     {
        //         PML.CreateCommand($"$p {GpsetRef} - это комплект другого отдела").RunInPdms();
        //         return false;
        //     }
        //
        //     var gpsetlastmod = GetGpsetLastMode(GpsetRef);
        //     var deltaChange = (now - gpsetlastmod).TotalDays;
        //     PML.CreateCommand($"$p Комплект {GpsetRef} последний раз изменялся {gpsetlastmod} ({deltaChange} дней назад)").RunInPdms();
        //     var LastCheckAll = GetLastProjectCheckDate();
        //
        //     return retval;
        //     //ДОПИСАТЬ
        // }
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
        [PMLNetCallable]
        public string GetGpsetLastModeTest(string GpsetRef)
        {
            var Gpset = DbElement.GetElement(GpsetRef);
            if (Gpset.Name() == "ALL") return DateTime.Now.ToString();
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
            return LastMode.ToString();

        }

        private bool IsManualChecked(string GpsetRef)
        {
            bool retval = false;
            foreach (var c in Checkedsets)
            {
                if (c == GpsetRef) retval = true;
                break;
            }
            return retval;
            //ДОПИСАТЬ

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
                Logger.WriteLine("Не удалось распознать начальный индекс зоны! Индексация будет начата с 0");
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
            var NoteExist = clashConnection.Query<ClashEntity>($"select  count (*) from {clashTableName} where (existing = 'false') and (gpset1 = '{GpsetRef}' or gpset2 = '{GpsetRef}')").ToList();
            var now = DateTime.Now;
            List<ClashEntity> ClashesByGpset = [];
            if (NoteExist.Count > 1)
            {
                System.Windows.MessageBox.Show("действие отменено. проверка невозможна. обратитесь в ОАП");
                return;
            }
            if (Gpset.ElementType.ToString() == "GPSET")
            {

                checker.UpdateClashElementInfo(clashConnection, "", clashTableName, GpsetRef);
                //ClashesByGpset = clashConnection.Query<ClashEntity>($"SELECT {SqlMapping.ClashSql} from {clashTableName} WHERE (gpset1 = '{GpsetRef}' or gpset2 = '{GpsetRef}')").ToList();
                ClashesByGpset = clashConnection.Query<ClashEntity>($"SELECT {SqlMapping.ClashSql} from {clashTableName} WHERE (gpset1 = '{GpsetRef}')").ToList();
            }
            else
            {
                var ClashesByEl = checker.QueryClashByEl(clashConnection, clashTableName, GpsetRef, "");
            }
            checker.СolZone(clashConnection, initialZoneIndexInt, ProjectName, clashTableName, logDirectoryPath = DefaultLogDirectoryPath, GpsetRef);

            var ClashesByGpsetFalseExist = clashConnection.Query<ClashEntity>($"select  count (*) from {clashTableName} where (existing = 'false') and (gpset1 = '{GpsetRef}' or gpset2 = '{GpsetRef}')").ToList();
            foreach (var e in ClashesByGpsetFalseExist)
            {
                checker.DeleteById(clashConnection, clashTableName, e, "AfterCheckGPSET", ".checkGPSET коллизия не относится к комплекту. удалена по завершению проверки");

            }
            //var ClashesByGpsetTrueExist = clashConnection.Query<ClashEntity>($"SELECT {SqlMapping.ClashSql} from {clashTableName} WHERE (gpset1 = '{GpsetRef}' or gpset2 = '{GpsetRef}')").ToList();
            var ClashesByGpsetTrueExist = clashConnection.Query<ClashEntity>($"SELECT {SqlMapping.ClashSql} from {clashTableName} WHERE (gpset1 = '{GpsetRef}')").ToList();
            PML.CreateCommand($"$p Из комплекта {GpsetRef} удалено {ClashesByGpsetTrueExist.Count - (ClashesByGpsetFalseExist.Count - 1)} несуществующих").RunInPdms();

            UpdateGpsetList(ProjectName, clashConnection);
            //!this.selSet.select('Rtext', !gpsetname) ДОПИСАТЬ ПОСЛЕ WPF
            //Show(clashTableName, GpsetRef);

            if (Gpset.Name() == "GPSET")
            {
                bool finded = false;
                for (int i = 0; i < Checkedsets.Count; i++)
                {
                    if (Checkedsets[i] == Gpset.Name())
                    {
                        CheckedsetsTIME[i] = DateTime.Now.ToString();
                        break;
                    }
                }
                if (!finded)
                {
                    Checkedsets.Add(Gpset.Name());
                    CheckedsetsTIME.Add(DateTime.Now.ToString());
                }
            }
            //this.UpdateCheckedStatus()  ДОПИСАТЬ ПОСЛЕ WPF

        }
        public List<GpsetComboItem> UpdateGpsetList(string ProjectName, SqlConnection sqlConnection)
        {
            List<string> SetName = ["ALL", "CE"];
            string Dept = Project.CurrentProject.LoginUser;



            // var type = new ActualTypeFilter(DbElementType.GetElementType("GPSET"));
            // var uW = DbElement.GetElement("/*");
            // var collection = new DBElementCollection(uW, type).Cast<DbElement>();
            // foreach (var c in collection)
            // {
            //     var Depth = c.GetString(DbAttributeInstance.DEPTH);                 //
            //     var GpwlPurp = c.GetGpwl().GetString(DbAttributeInstance.PURP);     //
            //     var GpsetMem = c.GetDouble(DbAttributeInstance.MCOU);               //
            //     if (Depth == "4" && GpwlPurp == "KOMP" && GpsetMem != 0)            // в GCC комплектов нет, не знаю для чего это тут, там только ALL и CE
            //     {                                                                   //
            //         SetName.Add(c.GetString(DbAttributeInstance.NAME));             //
            //     }                                                                   //
            //                                                                         //
            // }

            var GetClashStats = $"EXEC dbo.GetClashStats_{ProjectName}_TEST";



            var SqlGpset = sqlConnection.Query<ClashStats>($"{GetClashStats}").ToList();



            List<DbElement> AvevaGpset;
            AvevaGpset = [.. new DBElementCollection(new TypeFilter(DbElementTypeInstance.GPSET))
                          .Cast<DbElement>()
                          .Where(e =>
                          {
                              //var func = e.GetAsString(DbAttributeInstance.FUNC);
                              var User = e.GetAsString(DbAttribute.GetDbAttribute(":UES_USER"));
                              return User != "unset";
                          })];



            var gpsetItems = new List<GpsetComboItem>();
            gpsetItems.Add(new GpsetComboItem
            {
                GpsetElement = "ALL",
                DisplayText = "ALL общее(согл:0/несог:0) | для моего отдела(согл:0/несог:0)'"
            });
            gpsetItems.Add(new GpsetComboItem
            {
                GpsetElement = "CE",
                DisplayText = "CE общее(согл:0/несог:0) | для моего отдела(согл:0/несог:0)'"
            });

            foreach (var s in SqlGpset)
            {
                gpsetItems.Add(new GpsetComboItem
                {
                    GpsetElement = s.Gpset,
                    DisplayText = $"{s.Gpset} общее(согл:{s.sog1}/несог:{s.nesog1}) | для моего отдела(согл:{s.SOG_myotd}/несог:{s.NESOG_myotd})"
                });
            }

            foreach (var g in AvevaGpset)
            {
                var gpsetName = g.Name().ToString();
                if (SqlGpset.Any(s => s.Gpset == gpsetName))
                    continue;
                gpsetItems.Add(new GpsetComboItem
                {
                    GpsetElement = gpsetName,
                    DisplayText = $"{gpsetName} общее(согл:0/несог:0) | для моего отдела(согл:0/несог:0)'"

                });
            }



            /// Не проверял, процудуру создал только для Артема
            /// 

            /// Далее надо дописать после WPF



            return gpsetItems;


        }
        private void GoToEl1() //
        {

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
        #region
        public Hashtable ShowTest(string clashTableName, string Gpset)
        {
            using SqlConnection sqlConnection = new(ClashConnectionString);
            sqlConnection.Open();
            // !this.currGpset = !this.SelSet.Selection()   ДОПИСАТЬ ПОСЛЕ WPF
            List<ClashEntity> GpsetTable = [];

            if (Gpset == "CE")
            {
                Gpset = DbElement.GetElement(Gpset).Name();
            }
            TitleUpdate();

            var t = Gpset;

            var Wherestring = "where 1 = 1";
            if (Gpset != "ALL" && Gpset != "CE")
            {
                Wherestring = $"{Wherestring} and (gpset1 = '{Gpset}' or gpset2 = '{Gpset}')";
            }
            /*if !this.tMyDeptOnly.val then
                 !wherestring = !wherestring & | and (dept1 = '$!this.MYDEPT' or dept2 = '$!this.MYDEPT') |
               endif
               
               if !this.tOnlyWithRequestForMyDept.val then
                 !wherestring = !wherestring & | and (RequestToDept = '$!this.mydept' )|
               endif
               
               if !this.tHideApproved.val then
                 !wherestring = !wherestring & | and (approveReason is null or approveReason = '' )|
               endif
               
               if !this.tHideInWork.val then
               !wherestring = !wherestring & | and not (requestToDept <> '$!this.mydept' and (InWorkUser is not null ))|
               endif
               
               if !this.tDateFilter.val then
                 --получить дату из текста 
             	!dateA =  !this.tA.val.split('.')[2] & '.' & !this.tA.val.split('.')[1] & '.' & !this.tA.val.split('.')[3]
             	!dateB =  !this.tB.val.split('.')[2] & '.' & !this.tB.val.split('.')[1] & '.' & !this.tB.val.split('.')[3]
             	$P $!dateA $!dateB
                 !wherestring = !wherestring & | and date >= '$!dateA' and date <= '$!dateB' |
               endif
            */

            var sw = Stopwatch.StartNew();
            if (Gpset == "CE")
            {
                GpsetTable = checker.QueryClashByEl(sqlConnection, clashTableName, CurrGpset, Wherestring);
            }
            else
            {
                GpsetTable = sqlConnection.Query<ClashEntity>($"select {SqlMapping.ClashSql}, id, clashtype, El1, type1, usermod1, flnm1, dept1, gpset1, El2, type2, usermod2, flnm2, dept2, gpset2, date, x, y, z, existing, RequestToDept, RequestUser, RequestDate, ApproveUser, ApproveDate, ApproveReason, InWorkUser, InWorkDate from {clashTableName} {Wherestring}").ToList();
            }
            sw.Stop();
            var ExecuteTime = sw.Elapsed.TotalSeconds;
            // тут оставил пока что все так, так как в clashviewform.pmlfrm далее GpsetTable засовывается в NetGridControl
            // ДОПИСАТЬ ПОСЛЕ WPF
            Hashtable hash = new Hashtable(GpsetTable.ToDictionary(x => x.Id, x => x));
            return hash;
        }
        #endregion

        public List<ClashEntity> Show(string clashTableName, string Gpset)
        {
            using SqlConnection sqlConnection = new(ClashConnectionString);
            sqlConnection.Open();
            // !this.currGpset = !this.SelSet.Selection()   ДОПИСАТЬ ПОСЛЕ WPF
            List<ClashEntity> GpsetTable = [];

            if (Gpset == "CE")
            {
                Gpset = DbElement.GetElement(Gpset).Name();
            }
            TitleUpdate();
            var Wherestring = "where 1 = 1";
            if (Gpset != "ALL" && Gpset != "CE")
            {
                Wherestring = $"{Wherestring} and (gpset1 = '{Gpset}')";
                //Wherestring = $"{Wherestring} and (gpset1 = '{Gpset}' or gpset2 = '{Gpset}')";
            }
            /*if !this.tMyDeptOnly.val then
                 !wherestring = !wherestring & | and (dept1 = '$!this.MYDEPT' or dept2 = '$!this.MYDEPT') |
               endif
               
               if !this.tOnlyWithRequestForMyDept.val then
                 !wherestring = !wherestring & | and (RequestToDept = '$!this.mydept' )|
               endif
               
               if !this.tHideApproved.val then
                 !wherestring = !wherestring & | and (approveReason is null or approveReason = '' )|
               endif
               
               if !this.tHideInWork.val then
               !wherestring = !wherestring & | and not (requestToDept <> '$!this.mydept' and (InWorkUser is not null ))|
               endif
               
               if !this.tDateFilter.val then
                 --получить дату из текста 
             	!dateA =  !this.tA.val.split('.')[2] & '.' & !this.tA.val.split('.')[1] & '.' & !this.tA.val.split('.')[3]
             	!dateB =  !this.tB.val.split('.')[2] & '.' & !this.tB.val.split('.')[1] & '.' & !this.tB.val.split('.')[3]
             	$P $!dateA $!dateB
                 !wherestring = !wherestring & | and date >= '$!dateA' and date <= '$!dateB' |
               endif
            */
            var sw = Stopwatch.StartNew();
            if (Gpset == "CE")
            {
                GpsetTable = checker.QueryClashByEl(sqlConnection, clashTableName, CurrGpset, Wherestring);
            }
            else
            {
                GpsetTable = sqlConnection.Query<ClashEntity>($"select {SqlMapping.ClashSql}, id, clashtype, El1, type1, usermod1, flnm1, dept1, gpset1, El2, type2, usermod2, flnm2, dept2, gpset2, date, x, y, z, existing, RequestToDept, RequestUser, RequestDate, ApproveUser, ApproveDate, ApproveReason, InWorkUser, InWorkDate from {clashTableName} {Wherestring}").ToList();
            }
            sw.Stop();
            var ExecuteTime = sw.Elapsed.TotalSeconds;
            // тут оставил пока что все так, так как в clashviewform.pmlfrm далее GpsetTable засовывается в NetGridControl
            // ДОПИСАТЬ ПОСЛЕ WPF

            return GpsetTable;
        }
        public void TitleUpdate() // ДОПИСАТЬ ПОСЛЕ WPF
        {
            // !this.formTitle        = 'коллизии по ' & !this.currgpset & ' для...  ОТДЕЛ: ' & !this.MyDept & ' | пользователь: ' & !this.MyUlogId
            // !this.formTitle        = 'коллизии для...  ОТДЕЛ: ' & !this.MyDept & ' | пользователь: ' & !this.MyUlogId
        }

        public void SendMailByRequest(string opt)
        {

        }

        public void Report(string Gpset)
        {
            CurrGpset = Gpset;
            var GpserRef = DbElement.GetElement(Gpset);
            LockKomplect(GpserRef);

        }
        public bool IsGreenGpset(string Gpset, string clashTableName)
        {
            if (Gpset == "ALL" || Gpset == "CE") return false;
            if (DbElement.GetElement(Gpset).IsNull || !DbElement.GetElement(Gpset).IsValid) return false;


            var Gp = DbElement.GetElement(Gpset);

            bool isForeignDept = checker.GetDepartment(Gp, "GPSET") != MyDept && checker.GetDepartment(Gp, "GPSET") != "SOT" && MyDept == "OGS";
            if (isForeignDept)
            {
                System.Windows.MessageBox.Show($"{Gpset} - это комплект другого отдела");
                return false;
            }


            DateTime gpsetLastMode = GetGpsetLastMode(Gpset);
            DateTime lastCheck = GetLastProjectCheckDate(Gpset, clashTableName);

            if (IsManualChecked(Gpset))
            {
                var manualCheckTime = GetGpsetLastCheckTime(Gp);
                if (lastCheck < manualCheckTime)
                    lastCheck = manualCheckTime;

            }
            if (lastCheck < gpsetLastMode) return false;

            var delta = (DateTime.Now - lastCheck).TotalDays;
            return delta <= 2;

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

            var param = GetSqlParams(GpsetRef);

            using SqlConnection tdmsConnection = new(TDMSConnectionString);
            {


                tdmsConnection.Open();

                GetKomplect = tdmsConnection.Query<string>(@$"EXEC	[dbo].[PDMSGetStatus]		
                                                              @KOMPLECT, @VNCODE, @KKS",
                                                                  new { KOMPLECT = param[2], VNCODE = param[1], KKS = param[3] })
                                                                  .ToList();
            }
            if (GetKomplect.Count < 1)
            {
                System.Windows.MessageBox.Show($"Комплект не найден в TDMS");
                return;
            }
            else if (GetKomplect.Count > 1)
            {
                System.Windows.MessageBox.Show($"В TDMS найдено больше одного комплекта");
                return;
            }

            if (GetKomplect[0] != "2253448")
            {
                System.Windows.MessageBox.Show($"Нельзя заблокировать сданный в TDMS комплект {param[3]}");
                return;
            }

            var Answer = System.Windows.MessageBox.Show("Выполнить Save Work", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (Answer == MessageBoxResult.Yes)
            {
                double old = GpsetRef.GetDouble(DbAttribute.GetDbAttribute(":UES_KSTATUS"));
                GpsetRef.SetAttribute(DbAttribute.GetDbAttribute(":UES_KSTATUS"), 10);



                ExecuteScript("sqltep", "TDMS_TEP", "", "", "CMD_PDMS", "Set_Collision_Status", param[1], param[2], param[3], param[4], "1", "", "", "", "", "");
                GetKomplect = tdmsConnection.Query<string>(@$"EXEC	[dbo].[PDMSGetStatus]		
                                                              @KOMPLECT, @VNCODE, @KKS",
                                                                     new { KOMPLECT = param[2], VNCODE = param[1], KKS = param[3] })
                                                                     .ToList();
                if (GetKomplect[0] != "1")
                {
                    GpsetRef.SetAttribute(DbAttribute.GetDbAttribute(":UES_KSTATUS"), old);
                    System.Windows.MessageBox.Show($"Не удалось заблокировать комплект {param[3]}");
                    return;
                }
                MDB.CurrentMDB.SaveWork("");
            }
        }




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
            if (result.Count() < 1)
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


        private string ExecuteScript(string server, string db, string login, string pass, string cmd, string script, string p1, string p2, string p3, string p4, string p5, string p6, string p7, string p8, string p9, string p10)
        {
            try
            {
                TDMSApplication tDMSApplication;
                try
                {
                    tDMSApplication = (TDMSApplication)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("70CF901C-B492-4E86-AAD4-58E6330CBDE9")));
                    if (tDMSApplication.DatabaseName != db)
                    {
                        Console.WriteLine("tdmsApp.DatabaseName != db");
                        Console.WriteLine("найден сеанс с подключением " + tDMSApplication.DatabaseName + " Требуется подключение к бд " + db + ". Необходимо перезайти в TDMS");
                        return "";
                    }
                }
                catch
                {
                    tDMSApplication = (TDMSApplication)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("70CF901C-B492-4E86-AAD4-58E6330CBDE9")));
                    if (string.IsNullOrEmpty(login) && string.IsNullOrEmpty(pass))
                    {
                        tDMSApplication.Login("", Type.Missing, db, server, TDMSDatabaseType.tdmDatabaseMSSQL2000, TDMSAuthType.tdmAuthWindows);
                    }
                    else
                    {
                        tDMSApplication.Login(login, pass, db, server, TDMSDatabaseType.tdmDatabaseMSSQL2000, TDMSAuthType.tdmAuthSQL);
                    }
                }
                tDMSApplication.Visible = true;
                TDMSCommand source = tDMSApplication.Commands[cmd];
                string text = (string)(dynamic)tDMSApplication.ExecuteScript(source, script, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
                Console.WriteLine(text);
                return text ?? "";
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return "";
            }
        }
    }

}
