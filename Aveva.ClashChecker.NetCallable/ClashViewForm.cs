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
        public Dictionary<string,DateTime> Checkedsets = [];
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
            using SqlConnection clashConnection = new(ClashConnectionString);
            clashConnection.Open();
            var lastDate = clashConnection.ExecuteScalar<DateTime?>($@"SELECT MAX([date])
                                                                       FROM {clashTableName}
                                                                       WHERE gpset1 = @gpset OR gpset2 = @gpset",
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
                                                               where (existing = 0) 
                                                               and (gpset1 = @Gpset or gpset2 = @Gpset)",
                                                               new {Gpset = GpsetRef});

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
                                                                   WHERE gpset1 = @gp",
                                                                   new { gp = GpsetRef })
                                                                   .ToList();
                                                                 
            }
            else
            {
                ClashesByGpset = checker.QueryClashByEl(clashConnection, clashTableName, GpsetRef, "");
            }
            PML.CreateCommand($"$p Коллизий комплекта {GpsetRef} до проверки {ClashesByGpset.Count}").RunInPdms();

            checker.ColZone(clashConnection, initialZoneIndexInt, ProjectName, clashTableName, logDirectoryPath, GpsetRef);

            var ClashesByGpsetFalseExist = clashConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql} 
                                                                             from {clashTableName} 
                                                                             where existing = 0 
                                                                             and (gpset1 = @Gpset or gpset2 = @Gpset)",
                                                                             new {Gpset = GpsetRef})
                                                                             .ToList();
            foreach (var e in ClashesByGpsetFalseExist)
            {
                checker.DeleteById(clashConnection, clashTableName, e, "AfterCheckGPSET", ".checkGPSET коллизия не относится к комплекту. удалена по завершению проверки");

            }
            //var ClashesByGpsetTrueExist = clashConnection.Query<ClashEntity>($"SELECT {SqlMapping.ClashSql} from {clashTableName} WHERE (gpset1 = '{GpsetRef}' or gpset2 = '{GpsetRef}')").ToList();
            var ClashesByGpsetTrueExist = clashConnection.Query<ClashEntity>(@$"SELECT {SqlMapping.ClashSql} 
                                                                             from {clashTableName} 
                                                                             WHERE gpset1 = @Gpset",
                                                                             new {Gpset = GpsetRef})
                                                                             .ToList();
            PML.CreateCommand($"$p Из комплекта {GpsetRef} удалено {ClashesByGpsetTrueExist.Count - (ClashesByGpsetFalseExist.Count - 1)} несуществующих").RunInPdms();

            UpdateGpsetList(ProjectName, clashConnection);


            if (Gpset.ElementType.ToString() == "GPSET")
            {
                
                    Checkedsets[Gpset.Name()] = DateTime.Now;

            }
           

        }
        public List<GpsetComboItem> UpdateGpsetList(string ProjectName, SqlConnection sqlConnection)
        {
          

            var GetClashStats = $"EXEC dbo.GetClashStats_{ProjectName}_TEST";



            var SqlGpset = sqlConnection.Query<ClashStats>($"{GetClashStats}").ToList();



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



            /// Не проверял, процудуру создал только для Артема EXEC dbo.GetClashStats_{ProjectName}_TEST";
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
        public Hashtable ShowTest(string clashTableName, string Gpset)
        {
            var list = Show(clashTableName, Gpset);
            Dictionary<int,ClashEntity> dict = list.ToDictionary(y => y.id, y=>y);
            Hashtable hash = new Hashtable(dict);
            return hash;

           // return new HashSet(list.ToHashSet(x=> x.id, x =>x));
        }

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
                Wherestring = $"{Wherestring} and gpset1 = @Gpset";
                
            }
            
            var sw = Stopwatch.StartNew();
            if (Gpset == "CE")
            {
                GpsetTable = checker.QueryClashByEl(sqlConnection, clashTableName, CurrGpset, Wherestring);
            }
            else
            {
                GpsetTable = sqlConnection.Query<ClashEntity>(@$"select {SqlMapping.ClashSql} 
                                                              from {clashTableName} {Wherestring}",
                                                              new {Gpset = Gpset})
                                                              .ToList();
            }
            sw.Stop();


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
            var Gp = DbElement.GetElement(Gpset);

            if (Gpset == "ALL" || Gpset == "CE") return false;
            if (Gp.IsNull || !Gp.IsValid) return false;


            
            string gpsetDept = checker.GetDepartment(Gp,"GPSET");

            bool isForeignDept = gpsetDept != MyDept && !(gpsetDept == "SOT" && MyDept == "OGS");
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
            if(lastCheck<gpsetLastMode) return false;
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
