using Aveva.ClashChecker.NetCallable.Extensions;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.Core.Database;
using Aveva.Core.PMLNet;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Remoting;
using System.Security.Policy;
using System.Threading;
using System.Windows.Forms;
using static Aveva.ClashChecker.NetCallable.Exceptions;
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


    public string ClashConnectionString { get; set; } = "Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;User ID=clashuser;Password=Qgh%fS45Nm;Connection Timeout = 300";

    /// <summary>
    /// Обновляет данные по коллизиям (по отдельным комплектам/ по всем комлпекта)
    /// </summary>
    /// <param name="checkMode"></param>
    /// <param name="projectName"></param>
    /// <param name="clashDir"></param>
    /// <param name="gpsetName"></param>
    [PMLNetCallable]
    public string UpdateClashElementInfo(string checkMode, string projectName, string clashDir, string tableName, string gpsetName = "")
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            int deleteCount = 0;
            int updateCount = 0;
            int totalCount = 0;
            using SqlConnection clashConnection = GetClashSqlConnection();
            clashConnection.Open();
            if (gpsetName == "")
            {
                string getAllClashesQuery = $"SELECT id, clashType, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2 from {tableName}";
                var clashes = clashConnection.Query<ClashEntity>(getAllClashesQuery);
                foreach (var clash in clashes)
                {
                    switch (UpdateOneClashElementInfo(clash, tableName, checkMode))
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
            else
            {
                string getGpsetClashQuery = $"SELECT id, clashtype, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2 from $!tablename WHERE(gpset1 = {gpsetName} or gpset2 = {gpsetName})";
                var clashList = clashConnection.Query<ClashEntity>(getGpsetClashQuery).ToList();
                //TODO: Переписать на c# QueryClashByEl (необходимы объекты E3D)
                //var secondClashList = QueryClashByEl(gpsetName);

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

    public void WriteLogEx(string logFilePath, string logContent)
    {

    }

    [PMLNetCallable]
    public double UpdateOneClashElementInfo(ClashEntity clash, string tableName, string checkMode)
    {
        using SqlConnection clashConnection = GetClashSqlConnection();
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
                    
                    foreach ( var c in changes )
                    {
                        if (c.rules)
                        {
                            str += c.message;
                            retval = 1;
                            string QueryUpdate = $"update {tableName} SET dept1 = {RealDept1}, gpset1 = {RealGpset1}, usermod1 = {RealUsermod1}, dept2 = {RealDept2}, gpset2 = {RealGpset2}, usermod2 = {RealUsermod1} WHERE id = {clash.Id}";
                            clashConnection.Execute(QueryUpdate, commandTimeout: 600);
                        }
                    }





                  //  string str = $"будет обновлена {clash.Id}";
                  //
                  //  if (clash.FirstUserMode != RealUsermod1)
                  //  {
                  //      str += $"FirstUserMode:{clash.FirstUserMode}->{RealUsermod1}";
                  //  }
                  //
                  //  else if (clash.FirstDept != RealDept1)
                  //  {
                  //      str += $"FirstDept:{clash.FirstDept}->{RealDept1}";
                  //  }
                  //
                  //  else if (clash.FirstGpset != RealGpset1)
                  //  {
                  //      str += $"FirstGpset:{clash.FirstGpset}->{RealGpset1}";
                  //  }
                  //
                  //  else if (clash.SecondUserMode != RealUsermod2)
                  //  {
                  //      str += $"SecondUserMode:{clash.SecondUserMode}->{RealUsermod2}";
                  //  }
                  //
                  //  else if (clash.SecondDept != RealDept1)
                  //  {
                  //      str += $"SecondDept:{clash.SecondDept}->{RealDept2}";
                  //  }
                  //
                  //  else if (clash.SecondGpset != RealGpset1)
                  //  {
                  //      str += $"SecondGpset:{clash.SecondGpset}->{RealGpset2}";
                  //  }
                   

                }

            }

            return retval;
        }
        catch (Exception ex)
        {
            return retval;
        }

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
    public string History(DbElement dbElement, string param)
    {
        string aq = "";
        return aq;
    }

    [PMLNetCallable]
    public string GetGroups(DbElement dbElement)
    {
        string a = "";
        return a;
    }


    [PMLNetCallable]
    public string GetDepartment(DbElement dbElement, string hier)
    {
        string ProjectName = Project.CurrentProject.Name;
        string DbFileName = dbElement.GetString(DbAttributeInstance.DBFI);
        string DbRef = dbElement.GetString(DbAttributeInstance.REF);
        string result = DbFileName.Split('%')[1].Substring(0, 3);
        var departments = new List<DepartmentInfo>
        {
            new DepartmentInfo
            {
                Dept = "ARX",
                Tdept = "TEPRDARX",
                Mark = new[]{"_AC","_CE"}
            },
            new DepartmentInfo
            {
                Dept = "ASU",
                Tdept = "TEPRDASU",
                Mark = new[]{"_AS"}
            },
            new DepartmentInfo
            {
                Dept = "ETO",
                Tdept = "TEPRDETO",
                Mark = new[]{"_ED"}
            },
            new DepartmentInfo
            {
                Dept = "OGS",
                Tdept = "TEPRDOGS",
                Mark = new[]{"_HS"}
            },
            new DepartmentInfo
            {
                Dept = "OIV",
                Tdept = "TEPRDOIV",
                Mark = new[]{"_HV"}
            },
            new DepartmentInfo
            {
                Dept = "OMK",
                Tdept = "TEPRDOMK",
                Mark = new[]{"_SC"}
            },
            new DepartmentInfo
            {
                Dept = "OVS",
                Tdept = "TEPRDOVS",
                Mark = new[]{"_VS"}
            },
            new DepartmentInfo
            {
                Dept = "SOT",
                Tdept = "TEPRDSOT",
                Mark = new[]{"_RC"}
            },
            new DepartmentInfo
            {
                Dept = "OWP",
                Tdept = "TEPRDOWP",
                Mark = new[]{"_WT"}
            },
            new DepartmentInfo
            {
                Dept = "TMO",
                Tdept = "TEPRDTMO",
                Mark = new[]{"_TD","_WT"}
            },
            new DepartmentInfo
            {
                Dept = "VIK",
                Tdept = "TEPRDVIK",
                Mark = new[]{"_WS","_FP"}
            }
        };




        switch (result)
        {
            case "TUE":
            case "YKE":
                    string DbName = dbElement.Db.DbItem.ToString();
                    return DbName.Substring(0, 3);
                break;

            case "GCC":
                //var UlogId = new <List>
               string UlogId = dbElement.GetString(DbAttributeInstance.HULOC);
                string usermod = History(dbElement, "user").ToLower();
             //  foreach ()
             //  {
             //
             //  }
                break;
            default:
                string site = hier == "GPSET" ? dbElement.Ref.ToString() : dbElement.GetString(DbAttributeInstance.OWNER);
                //:UES_DEPART надо ли? isnullorEmpty
                if (site.Length > 0)
                {
                    string index = site.Substring(site.IndexOf('_'),2);
                }
                
                foreach (var dept in departments)
                {
                    var d =  
                }
                
                switch (result)
                {
                    case "DNS":
                    case "SVB":
                    case "WXT":

                        break;
                    default :
                        break;
                }


                break;
        }
            return result;
        }


    [PMLNetCallable]
    public bool DeleteById(SqlConnection clashConnection, string tableName, ClashEntity clash, string type, string comment)
    {
        /// надо написать метод

        return true;
    }
    public bool IsNeedToDeleteClashSimple(ClashEntity clash)
    {

        var dbElem1 = DbElement.GetElement(clash.FirstElement);
        var dbElem2 = DbElement.GetElement(clash.SecondElement);
        return (!dbElem1.IsValid || !dbElem2.IsValid);
    }

    public void QueryClashByEl()
    {
    }

    /// <summary>
    /// Возвращает имя таблицы для коллизий по проекту
    /// </summary>
    [PMLNetCallable]
    public string GetClashTableName()
    {
        try
        {

            return string.Empty;
        }
        catch (Exception ex)
        {
            return ConvertExceptionToPmlMessage(ex, "");
        }
    }

    /// <summary>
    /// Возвращает наименование отдела
    /// </summary>
    [PMLNetCallable]
    public string GetDepartment()
    {
        try
        {

            return string.Empty;
        }
        catch (Exception ex)
        {
            return ConvertExceptionToPmlMessage(ex, "");
        }
    }

    /// <summary>
    /// 
    /// </summary>
    [PMLNetCallable]
    public string History()
    {
        try
        {
            return string.Empty;
        }
        catch (Exception ex)
        {
            return ConvertExceptionToPmlMessage(ex, "");
        }
    }

    /// <summary>
    /// 
    /// </summary>
    [PMLNetCallable]
    public string GetGroups()
    {
        try
        {

            return string.Empty;
        }
        catch (Exception ex)
        {
            return ConvertExceptionToPmlMessage(ex, "");
        }

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

    [PMLNetCallable]
    public void ReplaceRefIFC(string projectName = "")
    {
        if (projectName == "")
            projectName = Project.CurrentProject.Name;

        string tableIfcName = $"tableIfc{projectName}";
        string clashRefUpdateLog = $"Clash{projectName}_RefUpdateLog";
        string clashTableName = $"clashtable{projectName}";
        using SqlConnection clashConnection = GetClashSqlConnection();
        clashConnection.Open();
        if (!clashConnection.TableExists(tableIfcName) || !clashConnection.TableExists(clashTableName))
            return;
        if (!clashConnection.TableExists(clashRefUpdateLog))
            CreateTableRefUpdateLog(projectName, clashConnection);

        int j = 3;
        for (int i = 1; i < 3; i++)
        {
            j--;
            //Идём по первым элементам в таблице коллизий
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


    private void CreateTableRefUpdateLog(string projectCode, SqlConnection sqlConnection)
    {
        string ClashRefUpdateLog = $"Clash{projectCode}_RefUpdateLog";
        sqlConnection.Execute($"CREATE TABLE [{ClashRefUpdateLog}]( [RunId] INT IDENTITY(1,1) NOT NULL, [UpdateTime] DATETIME NOT NULL, [RowId] INT NOT NULL, [OldEl1] NVARCHAR(50) NULL, [NewEl1] NVARCHAR(50) NULL, [flnm1] NVARCHAR(500) NULL);");
    }
}
