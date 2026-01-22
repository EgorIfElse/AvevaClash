using Aveva.ClashChecker.NetCallable.Models;
using Aveva.Core.PMLNet;
using Aveva.Core.Database;
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
    public string UpdateClashElementInfo(string checkMode, string projectName, string clashDir, string tableName,string gpsetName = "")
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

            return true;
        }
        catch(Exception ex)
        {
            return false;
        }
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
}
