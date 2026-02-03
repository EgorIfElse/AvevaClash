using Aveva.ClashChecker.NetCallable.Extensions;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.PMLNet;
using Aveva.Core3D.Clasher;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Aveva.ClashChecker.NetCallable.Exceptions;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;
namespace ClashChecker;

/// <summary>
/// Класс для обработки коллизий
/// </summary>
[PMLNetCallable]
public class ClashChecker
{
    [PMLNetCallable]
    public ClashChecker()
    {
    }

    [PMLNetCallable]
    public void Assign(ClashChecker that)
    {
    }

    private static readonly HashSet<string> SpecProj = ["SVB", "DNS", "WXT"];
    public string ClashConnectionString { get; set; } = "Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;User ID=clashuser;Password=Qgh%fS45Nm;Connection Timeout = 300";
    private static string ClashSql = $"id '{nameof(ClashEntity.Id)}'," +
        $" clashtype '{nameof(ClashEntity.ClashType)}'," +
        $" El1 '{nameof(ClashEntity.FirstElement)}'," +
        $" type1 '{nameof(ClashEntity.FirstType)}'," +
        $" usermod1 '{nameof(ClashEntity.FirstUserMode)}'," +
        $" dept1 '{nameof(ClashEntity.FirstDept)}'," +
        $" gpset1 '{nameof(ClashEntity.FirstGpset)}'," +
        $" El2 '{nameof(ClashEntity.SecondElement)}'," +
        $" type2 '{nameof(ClashEntity.SecondType)}'," +
        $" usermod2 '{nameof(ClashEntity.SecondUserMode)}'," +
        $" dept2 '{nameof(ClashEntity.SecondDept)}'," +
        $" gpset2 '{nameof(ClashEntity.SecondGpset)}'" +
        "" +
        "";

    [PMLNetCallable]
    public async Task CheckAll(string obstType)
    {
        try
        {
            DbElement world = DbElement.GetElement("*");
            var projectCode = Project.CurrentProject.Name;
            string clashTableName = $"clashtable{projectCode}";

            string ifcTableName = $"tableIfc{projectCode}";
            string clashRefUpdateLog = $"Clash{projectCode}_RefUpdateLog";

            using SqlConnection clashConnection = GetClashSqlConnection();
            clashConnection.Open();

            ReplaceRefIFC(clashConnection, clashTableName, ifcTableName, clashRefUpdateLog, projectCode);
            UpdateClashElementInfo(clashConnection, "FULL", clashTableName);

            await clashConnection.ExecuteAsync($"UPDATE {clashTableName} set Existing = 'false'");
            int initialClashCount = clashConnection.Query<int>($"SELECT top 1 @@ROWCOUNT from {clashTableName}").First();


            string initialClashesLogString = $"Коллизий до проверки: {initialClashCount}";


            List<DbElement> colZone;

            //TODO: Проверить быстродействие составного фильтра против пост-обработки LinQ
            //var filter = new CompoundFilter();
            //filter.AddShow(new TypeFilter(DbElementTypeInstance.ZONE));

            //Собираем коллекцию зон для obstructionList
            if (projectCode == "GCC")
            {
                colZone = [.. new DBElementCollection(new TypeFilter(DbElementTypeInstance.ZONE)).Cast<DbElement>().Where(e=> !e.Name().Contains("PO")
                && !e.Name().Contains("STUDY")
                && !e.Name().Contains("ZEMI")
                && !e.Name().Contains("/??"))];
            }
            else
            {
                colZone = [.. new DBElementCollection(new TypeFilter(DbElementTypeInstance.ZONE)).Cast<DbElement>().Where(e => !e.Owner.Name().Contains(".L")
                && e.Owner.GetString(DbAttributeInstance.PURP) != "NOCL"
                && e.GetDouble(DbAttributeInstance.MCOU) != 0
                && !e.Owner.Name().Contains("ZEMI"))];
            }

            string zoneCountLogString = $"Всего зон {colZone.Count} шт";
            var obstructionList = ObstructionList.Create();
            for (int i = 0; i < colZone.Count; i++)
            {
                switch (obstType.ToUpper())
                {
                    case "ZONE":
                        List<DbElement> obstructionZones = [];
                        var wvolArray = colZone.Select(e => e.GetDoubleArray(DbAttributeInstance.WVOL)).ToList();

                        for (int j = i; j < colZone.Count; j++)
                            if (WvolClash(wvolArray[i], wvolArray[j]))
                                obstructionZones.Add(colZone[j]);
                        obstructionList.AddObstructions([.. obstructionZones]);

                        break;
                    case "VOL":
                        obstructionList.AddObstructions([.. new DBElementCollection(new InVolumeFilter(colZone[i], false)).Cast<DbElement>().Where(e =>
                        {
                            var site = e.GetOwnerByType(DbElementTypeInstance.SITE);
                            if (site.Name().Contains(".L"))
                                return false;
                            string purpose = site.GetString(DbAttributeInstance.PURP);
                            if (purpose == "AXES" || purpose == "NOCL")
                                return false;
                            if (e.Name().Contains("CLASH"))
                                return false;

                            return false;
                        })]);
                        break;
                }
            }

            var clashOptions = ClashOptions.Create();
            clashOptions.TouchOverlap = 2;
            clashOptions.IncludeTouches = false;
            clashOptions.IncludeConnections = false;
            clashOptions.Clearance = 0;
            clashOptions.BranchCheckType = BranchCheck.BCHECK;

            clashOptions.NoCheckWithin(
            [
                DbElementTypeInstance.EQUIPMENT,
                DbElementTypeInstance.STRUCTURE,
                DbElementTypeInstance.BRANCH,
                DbElementTypeInstance.RESTRAINT,
            ]);

            var clashSet = ClashSet.Create();
            var checkResult = Clasher.Instance.CheckAll(clashOptions, obstructionList, clashSet);
            if (!checkResult)
                return;
            CheckResultToBase(clashSet);
            clashConnection.Close();

        }
        catch (Exception ex)
        {

        }

    }

    /// <summary>
    /// Обновляет таблицу tableIfc{ProjName}
    /// </summary>
    /// <param name="projectName"></param>
    private void ReplaceRefIFC(SqlConnection clashConnection, string clashTableName, string tableIfcName, string clashRefUpdateLog, string pprojectCode)
    {

        clashConnection.Open();
        if (!clashConnection.TableExists(tableIfcName) || !clashConnection.TableExists(clashTableName))
            return;

        if (!clashConnection.TableExists(clashRefUpdateLog))
            CreateTableRefUpdateLog(pprojectCode, clashConnection);

        int j = 3;
        for (int i = 1; i < 3; i++)
        {
            j--;
            //Идём по элементам в таблице коллизий
            string updateFirstQuery = $"WITH ClashElemsE{i} AS(SELECT DISTINCT El{i} AS OldE{i}, flnm{i} AS flnm{i} FROM {clashTableName} WHERE type{i} = 'GENPRI'), " +
                $"ClashWithMemE{i} AS(SELECT OldE{i}, flnm{i}, LEFT(flnm{i}, CHARINDEX(' of ', flnm{i} + ' of ') - 1) AS mempos{i} FROM ClashElemsE{i}), " +
                $"OldWithUuidE{i} AS(SELECT c.OldE{i}, c.flnm{i}, i.UUIDowner FROM ClashWithMemE{i} c JOIN {tableIfcName} i ON i.ELEM = c.OldE{i} AND i.fdelnm = c.mempos{i}), " +
                $"LatestPerUUIDE{i} AS(SELECT UUIDowner, ELEM AS NewE{i}, ROW_NUMBER() OVER (PARTITION BY UUIDowner ORDER BY [DATE] DESC ) AS rn FROM {tableIfcName} ), " +
                $"MapOldNewE{i} AS(SELECT o.OldE{i}, o.flnm{i}, l.NewE{i} FROM OldWithUuidE{i} o JOIN LatestPerUUIDE{i} l ON l.UUIDowner = o.UUIDowner AND l.rn = 1) " +
                $"UPDATE c SET c.El{i} = m.NewE{i} OUTPUT deleted.id, deleted.El{i}, inserted.El{i}, inserted.flnm{i}, GETDATE() INTO" +
                $"{clashRefUpdateLog}(RowId, OldEl{j}, NewEl{j}, flnm{j}, UpdateTime) FROM {clashTableName} c JOIN MapOldNewE{i} m ON c.El{i} = m.OldE{i} AND c.flnm{i} = m.flnm{i} WHERE c.type{i} = 'GENPRI' AND m.NewE{i}<> c.El{i} " +
                "SELECT @@ROWCOUNT AS UpdatedRows;";
            clashConnection.Execute(updateFirstQuery, commandTimeout: 600);
        }
        clashConnection.Close();
    }

    /// <summary>
    /// Обновляет данные по коллизиям (по отдельным комплектам/ по всем комлпекта)
    /// </summary>
    /// <param name="checkMode"></param>
    /// <param name="projectName"></param>
    /// <param name="clashDir"></param>
    /// <param name="gpsetName"></param>
    [PMLNetCallable]
    public string UpdateClashElementInfo(SqlConnection clashConnection, string checkMode, string tableName, string gpsetName = "")
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            int deleteCount = 0;
            int updateCount = 0;
            int totalCount = 0;
            //using SqlConnection clashConnection = GetClashSqlConnection();
            clashConnection.Open();
            if (gpsetName != "")
            {
                string getGpsetClashQuery = $"SELECT id, clashtype, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2 from $!tablename WHERE(gpset1 = {gpsetName} or gpset2 = {gpsetName})";
                var clashList = clashConnection.Query<ClashEntity>(getGpsetClashQuery).ToList();
                //TODO: Переписать на c# QueryClashByEl (необходимы объекты E3D)
                //var secondClashList = QueryClashByEl(gpsetName);
                var clashes = QueryClashByEl(gpsetName, "");


            }
            else
            {
                string getAllClashesQuery = $"SELECT id, clashType, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2 from {tableName}";
                var clashes = clashConnection.Query<ClashEntity>(getAllClashesQuery);
                foreach (var clash in clashes)
                {
                    switch (UpdateOneClashElementInfo(clashConnection, clash, tableName, checkMode))
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
            }
            stopwatch.Stop();
            var ellapsedSeconds = stopwatch.ElapsedMilliseconds * 0.001;

            clashConnection.Close();
            //TODO: Переписать на c# (необходимы объекты E3D)
            //WriteLogEx(@"\\tep-m.ru\data\App\PDMS\PDMS_TEP\LOG\UpdateClashElementInfo.txt", $"{ellapsedSeconds};{gpsetName};{totalCount};{deleteCount};{updateCount};{checkMode}");
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
    [PMLNetCallable]
    public double UpdateOneClashElementInfo(SqlConnection clashConnection, ClashEntity clash, string tableName, string checkMode)
    {
        clashConnection.Open();
        double retval = 0;
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
                string RealUsermod1;
                string RealUsermod2;
                var dbElem1 = DbElement.GetElement(clash.FirstElement);
                var dbElem2 = DbElement.GetElement(clash.SecondElement);

                string RealDept1 = GetDepartment(dbElem1, "");
                string RealDept2 = GetDepartment(dbElem2, "");

                string RealGpset1 = GetGroups(dbElem1);
                string RealGpset2 = GetGroups(dbElem1);

                if (checkMode == "FULL")
                {
                    RealUsermod1 = History(dbElem1, "user");
                    RealUsermod2 = History(dbElem2, "user");
                }
                else
                {
                    RealUsermod1 = clash.FirstUserMode;
                    RealUsermod2 = clash.SecondUserMode;
                }
                if (clash.FirstUserMode != RealUsermod1 || clash.SecondUserMode != RealUsermod2 || clash.FirstGpset != RealGpset1 || clash.SecondGpset != RealGpset2 || clash.FirstDept != RealDept1 || clash.SecondDept != RealDept2)
                {
                    string str = $"будет обновлена {clash.Id}";
                    var changes = new List<(bool rules, string message)>
                    {
                        (clash.FirstUserMode != RealUsermod1, $"FirstUserMode:{clash.FirstUserMode}->{RealUsermod1}"),
                        (clash.FirstDept != RealDept1,        $"FirstDept:{clash.FirstDept}->{RealDept1}"),
                        (clash.FirstGpset != RealGpset1,      $"FirstGpset:{clash.FirstGpset}->{RealGpset1}"),
                        (clash.SecondUserMode != RealUsermod2,$"SecondUserMode:{clash.SecondUserMode}->{RealUsermod2}"),
                        (clash.SecondDept != RealDept2,       $"SecondDept:{clash.SecondDept}->{RealDept2}"),
                        (clash.SecondGpset != RealGpset2,     $"SecondGpset:{clash.SecondGpset}->{RealGpset2}")
                    };

                    foreach (var change in changes.Where(e => e.rules))
                    {
                        str += change.message;
                        retval = 1;
                        string QueryUpdate = $"update {tableName} SET dept1 = {RealDept1}, gpset1 = {RealGpset1}, usermod1 = {RealUsermod1}, dept2 = {RealDept2}, gpset2 = {RealGpset2}, usermod2 = {RealUsermod1} WHERE id = {clash.Id}";
                        clashConnection.Execute(QueryUpdate, commandTimeout: 600);
                    }
                }

            }

            return retval;
        }
        catch (Exception ex)
        {
            return retval;
        }

    }

    public void WriteLogEx(string logFilePath, string logContent)
    {
        //DBElementCollection collection = new DBElementCollection(pipe);
        //List<DbElement> outlist outlist = collection.Cast<DbElement>().Where(element => element.Owner.ElementType == DbElementTypeInstance.BRANCH).ToList();
    }

    /// <summary>
    /// функция возвращает автора последнего изменения но не pdmsadmin
    /// </summary>
    [PMLNetCallable]
    public string History(DbElement dbElement, string param)
    {
        var Hist = dbElement.GetAsString(DbAttributeInstance.HIST);
        string[] HistAr = Hist.Split(' ');
        string user = "";
        string date = "";
        for (int i = 0; i <= HistAr.Length - 1; i++)
        {
            user = dbElement.EvaluateAsString(DbExpression.Parse($"SessU {HistAr[i]}")).ToLower();
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
            if (user != "balashovan" && user != "goncharenko" && user != "pdmsadmin")
            {
                break;
            }
            else
            {
                user = "admin";
            }
        }

        switch (param)
        {
            case "user":
                return user;
                break;
            case "date":
                return date;
                break;
            default:
                return "";
                break;
        }



    }

    /// <summary>
    /// функция возвращает имя комплекта или пустую строку
    /// </summary>
    [PMLNetCallable]
    public string GetGroups(DbElement dbElement)
    {
        if (!dbElement.IsValid || !dbElement.IsNull) return "";
        var ProjName = Project.CurrentProject.Name;
        int ElementDepth = dbElement.GetInteger(DbAttributeInstance.DEPTH);
        var DbElType = dbElement.GetString(DbAttributeInstance.TYPE);
        var DbElGroups = dbElement.GetString(DbAttributeInstance.GROUPS);
        var Site = dbElement.GetSite();
        var Zone = dbElement.GetZone();

        for (int i = 0; i <= ElementDepth; i++)
        {
            if (DbElType == "GPSET") return dbElement.Name();
            else if (DbElGroups != null) return DbElGroups;
            else if (DbElType == "GENPRI" || DbElType == "GENCUR" && Site.Name().Contains("_AC"))
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

        }
        return "";


    }

    /// <summary>
    /// Функция возвращает отдел по элементу
    /// </summary>
    [PMLNetCallable]
    public string GetDepartment(DbElement dbElement, string hier)
    {
        string ProjectName = Project.CurrentProject.Name;
        string DbFileName = dbElement.GetString(DbAttributeInstance.DBFI);
        string DbRef = dbElement.GetString(DbAttributeInstance.REF);
        string result = DbFileName.Split('%')[1].Substring(0, 3);
        string SiteIFC = dbElement.GetSite().ToString();

        if (SiteIFC.Contains("IFC"))
        {
            int i = SiteIFC.LastIndexOf('-');
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
                LognameByDept = new Dictionary<string, string>();
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
            int i = SiteIFC.LastIndexOf('-');
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
                List<DbElement> collection = new DBElementCollection(uW, type).Cast<DbElement>().ToList();
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
        var ProjName = Project.CurrentProject.Name;
        var ElementDepth = dbElement.GetInteger(DbAttribute.GetDbAttribute("DbDepth"));
        var DbElType = dbElement.GetString(DbAttributeInstance.TYPE);
        var Site = dbElement.GetSite();
        var Zone = dbElement.GetZone();

        for (int i = 0; i <= ElementDepth; i++)
        {
            if (DbElType == "GPSET") return dbElement.Name();

            else if (DbElType == "GENPRI" || DbElType == "GENCUR" && Site.Name().Contains("_AC"))
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


    [PMLNetCallable]
    public void DeleteById(SqlConnection clashConnection, string tableName, ClashEntity clash, string type, string comment)
    {
        var HistTableName = $"{tableName} + '_his'";
        var login = Project.CurrentProject.LoginUser;
        clashConnection.Open();

        string CreateTableHist = $"insert into {HistTableName} select {tableName} | & |.* ,getdate()  ,'{login}'  ,'{type}' ,'{comment}' from {tableName} where id = {clash.Id}";
        clashConnection.Execute(CreateTableHist);
        string DeleteIdTableHist = $"DELETE FROM {tableName} where id = {clash.Id}";
        clashConnection.Execute(DeleteIdTableHist);

        clashConnection.Close();

    }
    public bool IsNeedToDeleteClashSimple(ClashEntity clash)
    {
        return !(DbElement.GetElement(clash.FirstElement).IsValid && DbElement.GetElement(clash.SecondElement).IsValid);
    }

    private ClashEntity QueryOneSqlClash(SqlConnection clashConnection, string clashTableName, string clashType, string firstElement, string secondElement)
    {
        string firstType = clashType.Substring(0, 1);
        string secondType = clashType.Substring(1, 1);
        string invt = $"{firstType}{secondType} CLASH";
        string query;
        if (clashType == "*" || firstType == secondType)
            query = $"select * from {clashTableName} where (el1 = {firstElement} and el2 = {secondElement} or el1 = {secondElement} and el2 = {firstElement})";
        else
            query = $"select * from {clashTableName} where (el1 = {firstElement} and el2 = {secondElement} and ClashType = {clashType}  or el1 = {secondElement} and el2 = {firstElement} and ClashType = {invt})";

        var clashes = clashConnection.Query<ClashEntity>(query);

        return clashes.First();
    }

    public List<ClashEntity> QueryClashByEl(string dbElementref, string wherestring)

    {
        var dbElement = DbElement.GetElement(dbElementref);

        //собираем элементы
        //var CurEl = DbElement.GetElement(dbElement.ToString());
        var collection = new DBElementCollection(dbElement).Cast<DbElement>().ToList();
        string[] els = { };
        string tablename = "";
        var Login = Project.CurrentProject.LoginUser;
        var ProjectName = Project.CurrentProject.Name;
        var ProjectTableName = $"clashtable{ProjectName}";
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
        // ХРЕНЬ КАКАЯ-ТО , УБРАЛ
        //int s = els.Length;

        //if (s > 400000)
        //{
        //    MessageBox.Show($"более 400000 элементов {s}. действие отменено");
        //    return "";
        //}

        string Host = Environment.MachineName;

        if (Host.Contains("GPU") || Host.Contains("GRU") || Host.Contains("0047"))
        {
            var file = File.ReadAllLines("X:\\App\\PDMS\\PDMS_TEP\\ADMIN\\User_TO_VM.csv");

            foreach (string line in file)
            {
                string trimUS = line.Trim('"');
                string[] User = trimUS.Split('\t');
                if (Login.ToLower() == User[0].ToLower())
                {
                    tablename = $"{User[1]}{User[0]}";
                }

            }

        }
        else
        {
            tablename = $"{Host}{Login}";

        }
        using SqlConnection clashConnection = GetClashSqlConnection();
        clashConnection.Open();

        string SelectTableName = $"SELECT * FROM INFORMATION_SCHEMA.TABLES where TABLE_NAME = {tablename}";
        var select = clashConnection.Execute(SelectTableName);

        //!!SA = !sqlarray

        if (select == 0)
        {
            string CreateTable = $"CREATE TABLE {tablename} ( [El] NVARCHAR(40) );";
            var create = clashConnection.Execute(CreateTable);
        }

        //1 очистить tmptable

        string DeleteTable = $"delete from {tablename}";
        var delete = clashConnection.Execute(DeleteTable);

        //2 получить всех потомков и записать refno в таблицу

        // ЭТО ЖЕ НЕ НАДО?
        // var!DllPath EVAR DLLPATH
        //!LoadDll = | IMPORT '| & !DllPath & '\' & 'SQLOBJ14.1' & |' | $*ВРЕМЕННО
        // $!LoadDll
        // handle(1000, 0)
        // endhandle
        // using namespace 'SQLOBJ2'
        // !OBJ = object SQLObject()
        // !OBJ.SqlConnect(!conn)

        for (int i = 0; i <= els.Length - 1; i++)
        {
            string InsertTable = $"insert into {tablename} ( el) values ({els[i]})";
            var Insert = clashConnection.Execute(InsertTable);
        }
        clashConnection.Close();

        //получить из базы всё по этому элементу

        string GetRowTableName = $"select id 'Id', clashtype 'ClashType', El1 'FirstElement', type1 'FirstType', usermod1 'FirstUserMode', dept1 'FirstDept', gpset1 'FirstGpset', El2 'SecondElement', type2 'SecondType', usermod2 'SecondUserMode', dept2 'SecondDept', gpset2 'SecondGpset', date 'Date', x 'X', y 'Y', z 'Z', existing 'Existing', RequestToDept 'RequestToDept', RequestUser 'RequestUser', RequestDate 'RequestDate', ApproveUser 'ApproveUser', ApproveDate 'ApproveDate', ApproveReason 'ApproveReason', InWorkUser 'InWorkUser', InWorkDate 'InWorkDate' " +
            $"from {ProjectTableName} where ((el1 in (select el from {tablename}) or el2 in (select el from {tablename})) {wherestring} )";
        var clashList = clashConnection.Query<ClashEntity>(GetRowTableName).ToList();

        string TempTableName = $"SELECT count(*) FROM INFORMATION_SCHEMA.TABLES where TABLE_NAME = {tablename}";
        var TempSelect = clashConnection.Execute(SelectTableName);
        if (TempSelect > 0)
        {
            clashConnection.Execute($"drop TABLE {tablename}");
        }

        return clashList;

    }

    /// <summary>
    /// Возвращает sql соединение для clash-таблиц по наименованию проекта
    /// </summary>
    /// <returns></returns>
    private SqlConnection GetClashSqlConnection()
    {
        try
        {
            return new SqlConnection(ClashConnectionString);
        }
        catch (Exception ex)
        {
            return null;
        }
    }


    private void CheckResultToBase(ClashSet clashSet)
    {
        foreach (var clash in clashSet.Clashes.Where(e => !IsClashIgnore(e)))
            InsertOneCLash(clash);
    }

    private void InsertOneCLash(Clash clash)
    {
        try
        {

            



        }
        catch (Exception ex)
        {

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

            if (CheckSiteAndZoneNames(clash))
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
    /// Проверка по футлярам для pipe
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
            var firstJntc = firstPipe.GetDouble(DbAttributeInstance.JNTC);
            var secondJntc = secondPipe.GetDouble(DbAttributeInstance.JNTC);
            if (!firstDrrf.IsNull && firstJntc == 2 && secondJntc == 1 && firstDrrf == secondPipe)
                return true;
            else if (!secondDrrf.IsNull && secondJntc == 2 && firstJntc == 1 && secondDrrf == firstPipe)
                return true;

        }

        return false;

    }
    /// <summary>
    /// Проверка игнорируемых сайтов, сайтов/зон для экспорта IFC
    /// </summary>
    private bool CheckSiteAndZoneNames(Clash clash)
    {

        //Игнорируем коллизии внутри сайтов, содержащих ".L, /PO в имени. Сайтов, с PURPOSE == NOCL. Коллизий, внутри одного и того же сайта с IFC в имени"
        if (clash.First.TryGetOwnerByType(DbElementTypeInstance.SITE, out DbElement firstSite) && clash.Second.TryGetOwnerByType(DbElementTypeInstance.SITE, out DbElement secondSite))
        {
            string firstSiteName = firstSite.Name().ToLower();
            string secondSiteName = secondSite.Name().ToLower();
            if (
          (firstSite.GetString(DbAttributeInstance.PURP) == "NOCL"
          || secondSite.GetString(DbAttributeInstance.PURP) == "NOCL"
          || firstSiteName.Contains(".l")
          || secondSiteName.Contains(".l")
          || firstSiteName.Contains("/po")
          || secondSiteName.Contains("/po"))
          ||
          (firstSite == secondSite && firstSiteName.Contains("ifc")))
            {
                return true;
            }
        }

        if (clash.First.TryGetOwnerByType(DbElementTypeInstance.ZONE, out DbElement firstZone) && clash.Second.TryGetOwnerByType(DbElementTypeInstance.ZONE, out DbElement secondZone))
        {
            if (firstZone == secondZone && firstZone.Name().ToLower().Contains("ifc"))
                return true;
        }

        return false;
    }

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
        var frmw = pane.GetOwnerByType(DbElementTypeInstance.FRMWORK);
        if (frmw.IsNull)
            return false;
        string zoneName = zone.Name();
        if (!zoneName.Contains('_') || zoneName.Split('_').Length < 3 || zoneName.Split('_')[2] != "CL")
            return false;
        string struName = stru.Name();
        if (!struName.Contains('_') || struName.Split('_').Length < 4 || struName.Split('_')[3] != "CL")
            return false;

        string frmwName = frmw.Name();
        if (!frmwName.Contains('_') || frmwName.Split('_').Length < 5 || frmwName.Split('_')[4].Substring(0, 1) != "CC")
            return false;

        return true;

    }

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
