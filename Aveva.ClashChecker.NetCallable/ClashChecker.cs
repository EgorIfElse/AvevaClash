using Aveva.Core.PMLNet;
using Microsoft.Data.SqlClient;
using static Aveva.ClashChecker.NetCallable.Exceptions;

namespace Aveva.ClashChecker.NetCallable;

/// <summary>
/// Класс для обработки коллизий
/// </summary>
[PMLNetCallable]
public class ClashChecker
{

    public static string ClashConnectionString { get; set; } = "Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;User ID=clashuser;Password=Qgh%fS45Nm;Connection Timeout = 300";

    /// <summary>
    /// Обновляет данные по коллизиям (по отдельным комплектам/ по всем комлпекта)
    /// </summary>
    /// <param name="checkMode"></param>
    /// <param name="projectName"></param>
    /// <param name="clashDir"></param>
    /// <param name="gpsetName"></param>
    /// <returns></returns>
    [PMLNetCallable]
    public string UpdateClashElementInfo(string checkMode, string projectName, string clashDir, string gpsetName = "")
    {
        try
        {
            SqlConnection clashConnection = GetClashSqlConnection();

            if (gpsetName != "")
            {

            }
            else
            {

            }
            return string.Empty;
        }
        catch (Exception ex)
        {
            return ConvertExceptionToPmlMessage(ex, "");
        }
    }

    public void UpdateOneClashElementInfo()
    {

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
    /// 
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
    private static SqlConnection GetClashSqlConnection()
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
