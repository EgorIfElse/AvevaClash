using Aveva.ClashChecker.NetCallable.Extensions;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.Core.Database;
using Aveva.Core.PMLNet;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Diagnostics;
using System.Linq;
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
                    if (UpdateOneClashElementInfo(clash, tableName))
                        updateCount++;
                    else
                        deleteCount++;
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
    public bool UpdateOneClashElementInfo(ClashEntity clash, string tableName)
    {
        try
        {
            if (IsNeedToDeleteClashSimple(clash))
            {

            }


            return true;
        }
        catch (Exception ex)
        {
            return false;
        }
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
    public void ReplaceRefIFC(string projectName = "", string projectCode = "")
    {
        if (projectCode == "")
            projectCode = Project.CurrentProject.Number;
        if (projectName == "")
            projectName = Project.CurrentProject.Name;

        string tableIfcName = $"tableIfc{projectName}";
        string clashRefUpdateLog = $"Clash{projectCode}_RefUpdateLog";
        string clashTableName = $"clashtable{projectCode}";
        using SqlConnection clashConnection = GetClashSqlConnection();
        clashConnection.Open();
        if (!clashConnection.TableExists(tableIfcName) || !clashConnection.TableExists(clashTableName))
            return;
        if (!clashConnection.TableExists(clashRefUpdateLog))
        {
            //CreateTableRefUpdateLog()
        }

        int j = 3;
        for (int i = 1; i < 3; i++) {
            j--;
            //Идём по первым элементам в таблице коллизий
            string updateFirstQuery = $"WITH ClashElemsE{i} AS(SELECT DISTINCT El{i} AS OldE{i}, flnm{i} AS flnm{i} FROM {clashTableName} WHERE type{i} = 'GENPRI'), " +
                $"ClashWithMemE{i} AS(SELECT OldE{i}, flnm{i}, LEFT(flnm{i}, CHARINDEX(' of ', flnm{i} + ' of ') - 1) AS mempos{i} FROM ClashElemsE{i}), " +
                $"OldWithUuidE{i} AS(SELECT c.OldE{i}, c.flnm{i}, i.UUIDowner FROM ClashWithMemE{i} c JOIN {tableIfcName} i ON i.ELEM = c.OldE{i} AND i.flnm = c.mempos{i}), " +
                $"LatestPerUUIDE{i} AS(SELECT UUIDowner, ELEM AS NewE{i}, ROW_NUMBER() OVER (PARTITION BY UUIDowner ORDER BY [DATE] DESC ) AS rn FROM {tableIfcName} ), " +
                $"MapOldNewE{i} AS(SELECT o.OldE{i}, o.flnm{i}, l.NewE{i} FROM OldWithUuidE{i} o JOIN LatestPerUUIDE{i} l ON l.UUIDowner = o.UUIDowner AND l.rn = 1) " +
                $"UPDATE c SET c.El{i} = m.NewE{i} OUTPUT deleted.id, deleted.El{i}, inserted.El{i}, inserted.flnm{i}, GETDATE() INTO" +
                $"{clashRefUpdateLog}(RowId, OldEl{j}, NewEl{j}, flnm{j}, UpdateTime) FROM {clashTableName} c JOIN MapOldNewE{i} m ON c.El{i} = m.OldE{i} AND c.flnm{i} = m.flnm{i} WHERE c.type{i} = 'GENPRI' AND m.NewE{i}<> c.El{i} " +
                "SELECT @@ROWCOUNT AS UpdatedRows;";
            clashConnection.Execute(updateFirstQuery, commandTimeout: 600);

        }


        //идём по вторым элементам в таблице коллизий
        string updateSecondQuery = $"WITH ClashElemsE2 AS(SELECT DISTINCT El2 AS OldE2, flnm2 AS flnm2 FROM {clashTableName} WHERE type2 = 'GENPRI'), " +
            $"ClashWithMemE2 AS(SELECT OldE2, flnm2, LEFT(flnm2, CHARINDEX(' of ', flnm2 + ' of ') - 1) AS mempos2 FROM ClashElemsE2), " +
            $"OldWithUuidE2 AS(SELECT c.OldE2, c.flnm2, i.UUIDowner FROM ClashWithMemE2 c JOIN {tableIfcName} i ON i.ELEM = c.OldE2 AND i.flnm = c.mempos2), " +
            $"LatestPerUUIDE2 AS (SELECT UUIDowner, ELEM AS NewE2, ROW_NUMBER() OVER (PARTITION BY UUIDowner ORDER BY [DATE] DESC ) AS rn FROM {tableIfcName} ), " +
            $"MapOldNewE2 AS(SELECT o.OldE2, o.flnm2, l.NewE2 FROM OldWithUuidE2 o JOIN LatestPerUUIDE2 l ON l.UUIDowner = o.UUIDowner AND l.rn = 1)" +
            $" UPDATE c SET c.El2 = m.NewE2 OUTPUT deleted.id, deleted.El2, inserted.El2, inserted.flnm2, GETDATE() INTO " +
            $"{clashRefUpdateLog}(RowId, OldEl1, NewEl1, flnm1, UpdateTime) FROM {clashTableName} c JOIN MapOldNewE2 m ON c.El2 = m.OldE2 AND c.flnm2 = m.flnm2 WHERE c.type2 = 'GENPRI' AND m.NewE2<> c.El2 " +
            $"SELECT @@ROWCOUNT AS UpdatedRows;";
        //   !sqlarray = !!sqlQuery('SQL', !conn, !query)
        clashConnection.Close();
    }
}
