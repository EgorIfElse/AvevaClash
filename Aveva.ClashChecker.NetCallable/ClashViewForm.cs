using Aveva.ClashChecker.NetCallable;
using Aveva.ClashChecker.NetCallable.Extensions;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.ClashChecker.NetCallable.Sql;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
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
using System.Windows.Forms;
using static Aveva.ClashChecker.NetCallable.Exceptions;
using PML = Aveva.Core.Utilities.CommandLine.Command;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;
namespace ClashViewForm
{
    [PMLNetCallable]
    public partial class ClashViewForm
    {
        public List<string> Checkedsets = [];
        public List<string> CheckedsetsTIME = [];
        public string TableName = "";
        public string CurrGpset = "";
        public string CLASHdir = "";
        public string MyUlogId = "";
        public string MyDept = "";
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
        private DateTime GetLastProjectCheckDate()
        {
            var file = File.ReadAllLines("какой нибудь лог"); //lastcheck мы создаем из CheckAll
            return DateTime.Now;
        }
        private DateTime GetGpsetLastCheckTime(DbElement gpset)
        {
            return DateTime.Now;
        }
        private bool isGreenGpset(string GpsetRef)
        {
            bool retval = false;

            if (GpsetRef == "ALL" || GpsetRef == "CE") return false;

            var Gpset = DbElement.GetElement(GpsetRef);
            var User = Project.CurrentProject.LoginUser;
            var now = DateTime.Now;

            if (Gpset.IsNull || !Gpset.IsValid) return false;

            if (checker.GetDepartment(Gpset, "GPSET") != User && !(checker.GetDepartment(Gpset, "GPSET") == "SOT" && User == "OGS"))
            {
                PML.CreateCommand($"$p {GpsetRef} - это комплект другого отдела").RunInPdms();
                return false;
            }

            var gpsetlastmod = GetGpsetLastMode(GpsetRef);
            var deltaChange = (now - gpsetlastmod).TotalDays;
            PML.CreateCommand($"$p Комплект {GpsetRef} последний раз изменялся {gpsetlastmod} ({deltaChange} дней назад)").RunInPdms();
            var LastCheckAll = GetLastProjectCheckDate();

            return retval;
            //ДОПИСАТЬ
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
        private bool IsManualChecked(string GpsetRef)
        {
            bool retval = false;
            return retval;
            //ДОПИСАТЬ

        }
        private void CheckGpset(string GpsetRef, string DefaultLogDirectoryPath, double initialZoneIndex, bool testMode = true, string logDirectoryPath = DefaultLogDirectoryPath)
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
            var clashTableName = $"clashtable{ProjectName}";
            if (mdb != "/ALL" && mdb != "/16310" && isAll)
            {
                var Answer = MessageBox.Show("Проверку комплекта необходимо запускать в MDB \"ALL\". Сохраниться и перейти в MDB \"ALL\"?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (Answer == DialogResult.Yes)
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
            var NoteExist = clashConnection.Query<ClashEntity>($"select  count (*) from {clashTableName} where (existing = 'false') and (gpset1 = {Gpset} or gpset2 = {Gpset}").ToList();
            var now = DateTime.Now;
            List<ClashEntity> ClashesByGpset = [];
            if (NoteExist.Count != 0)
            {
                MessageBox.Show("действие отменено. проверка невозможна. обратитесь в ОАП");
                return;
            }
            if (Gpset.ElementType.ToString() == "GPSET")
            {

                checker.UpdateClashElementInfo(clashConnection, "", clashTableName, GpsetRef);
                ClashesByGpset = clashConnection.Query<ClashEntity>($"SELECT {SqlMapping.ClashSql} from {clashTableName} WHERE (gpset1 = '{Gpset}' or gpset2 = '{Gpset}')").ToList();
            }
            else
            {
                var ClashesByEl = checker.QueryClashByEl(clashConnection, clashTableName, GpsetRef, "");
            }
            checker.СolZone(clashConnection, initialZoneIndexInt, ProjectName, clashTableName, logDirectoryPath = DefaultLogDirectoryPath, GpsetRef);

            var ClashesByGpsetFalseExist = clashConnection.Query<ClashEntity>($"select  count (*) from {clashTableName} where (existing = 'false') and (gpset1 = {Gpset} or gpset2 = {Gpset}").ToList();
            foreach (var e in ClashesByGpset)
            {
                checker.DeleteById(clashConnection, clashTableName, e, "AfterCheckGPSET", ".checkGPSET коллизия не относится к комплекту. удалена по завершению проверки");

            }
            var ClashesByGpsetTrueExist = clashConnection.Query<ClashEntity>($"SELECT {SqlMapping.ClashSql} from {clashTableName} WHERE (gpset1 = '{Gpset}' or gpset2 = '{Gpset}')").ToList();
            PML.CreateCommand($"$p Из комплекта {GpsetRef} удалено {ClashesByGpsetFalseExist.Count - ClashesByGpsetTrueExist.Count} несуществующих").RunInPdms();

            UpdateGpsetList(ProjectName);
            //!this.selSet.select('Rtext', !gpsetname) ДОПИСАТЬ ПОСЛЕ WPF
            Show(clashConnection, clashTableName);

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
        private void UpdateGpsetList(string ProjectName)
        {
             List<string> SetName = ["ALL", "CE"];
            switch(ProjectName)
            {
                case "GCC":
                   
                    var type = new ActualTypeFilter(DbElementType.GetElementType("GPSET"));
                    var uW = DbElement.GetElement("/*");
                    var collection = new DBElementCollection(uW, type).Cast<DbElement>();
                    foreach (var c in collection)
                    {
                        var Depth = c.GetString(DbAttributeInstance.DEPTH);
                        var GpwlPurp = c.GetGpwl().GetString(DbAttributeInstance.PURP);
                        var GpsetMem = c.GetDouble(DbAttributeInstance.MCOU);
                        if (Depth == "4" && GpwlPurp == "KOMP" && GpsetMem != 0)
                        {
                            SetName.Add(c.GetString(DbAttributeInstance.NAME));
                        }
                      
                    }
                    break;
                case "ARM":
                    /*
                     * if !proj.eq('ARM') then
                    $* заплатка. 25/06/2025 так хотя бы работает.
                    !query = ||
                    !query = !query & | select clashtableARM1.gpset1|
                    !query = !query & | ,count (CASE WHEN (not isnull(clashtableARM1.approvereason,'!') = '!' and not clashtableARM1.approvereason = '') THEN 1 ELSE NULL end) as sogl|
                    !query = !query & | ,count (CASE WHEN (isnull(clashtableARM1.approvereason,'!') = '!' or clashtableARM1.approvereason = '') THEN 1 ELSE NULL end ) as nesog|
                    !query = !query & | , count (CASE WHEN ((clashtableARM1.dept1) = 'SYSTEM' or (clashtableARM1.dept2) = 'SYSTEM') and (isnull(clashtableARM1.approvereason,'!') <> '!' and clashtableARM1.approvereason <> '') THEN 1 ELSE NULL end )  as SOG_myotd |
                    !query = !query & | , count (CASE WHEN ((clashtableARM1.dept1) = 'SYSTEM' or (clashtableARM1.dept2) = 'SYSTEM') and (isnull(clashtableARM1.approvereason,'!') = '!' or clashtableARM1.approvereason = '') THEN 1 ELSE NULL end )  as NESOG_myotd |
                    !query = !query & | From |
                    !query = !query & | (SELECT Dept1, Gpset1,  Dept2,  ApproveReason |                    
                    !query = !query & | FROM            clashtableARM |
                    !query = !query & | Union ALL |
                    !query = !query & | SELECT   Dept1, Gpset2,  Dept2,  ApproveReason |                    
                    !query = !query & | FROM           clashtableARM where Gpset2 <> Gpset1) clashtableARM1 group by clashtableARM1.gpset1 order by 1 |
                    */

                    // ВСЮ ЭТУ ХУЙНЮ ЗАСУНУТЬ В ПРОЦЕДУРУ SQL И ПРОСТО ЕЕ ДЕРГАТЬ(Делать с параметрами, унифицированную)
                    break;
            }

                


        }
        private void GoToEl1() //
        {

        }
        [PMLNetCallable]
        public void SetChange(SqlConnection sqlConnection, string clashTableName)
        {
            //меняем цвет кнопки
            UpdateCheckedStatus();
            Show(sqlConnection, clashTableName);
        }
        [PMLNetCallable]
        public void UpdateCheckedStatus()
        {
            //ищем в массиве недавно проверенных комплектов текущий, и если нашли красим
            //это специальный массив который изначально пустой он содержит имена элементов которые тока что проверены.он пополняется после чека

        }
        public void Show(SqlConnection sqlConnection, string clashTableName)
        {
            // !this.currGpset = !this.SelSet.Selection()   ДОПИСАТЬ ПОСЛЕ WPF
            List<ClashEntity> GpsetTable = [];
            var Gpset = CurrGpset;
            if (CurrGpset == "CE")
            {
                CurrGpset = DbElement.GetElement(CurrGpset).Name();
            }
            TitleUpdate();
            var Wherestring = "where 1 = 1";
            if (Gpset != "ALL" && Gpset != "CE")
            {
                Wherestring = $"{Wherestring} and (gpset1 = {Gpset} or gpset2 = {Gpset})";
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
                GpsetTable = sqlConnection.Query<ClashEntity>($"select {SqlMapping.ClashSql} id, clashtype, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2, date, x, y, z, existing, RequestToDept, RequestUser, RequestDate, ApproveUser, ApproveDate, ApproveReason, InWorkUser, InWorkDate from {clashTableName} {Wherestring}").ToList();
            }
            sw.Stop();
            var ExecuteTime = sw.Elapsed.TotalSeconds;
            // тут оставил пока что все так, так как в clashviewform.pmlfrm далее GpsetTable засовывается в NetGridControl
            // ДОПИСАТЬ ПОСЛЕ WPF


        }
        public void TitleUpdate() // ДОПИСАТЬ ПОСЛЕ WPF
        {
            // !this.formTitle        = 'коллизии по ' & !this.currgpset & ' для...  ОТДЕЛ: ' & !this.MyDept & ' | пользователь: ' & !this.MyUlogId
            // !this.formTitle        = 'коллизии для...  ОТДЕЛ: ' & !this.MyDept & ' | пользователь: ' & !this.MyUlogId
        }
    }

}
