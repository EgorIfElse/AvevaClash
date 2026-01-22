using Aveva.Core.PMLNet;
using Microsoft.Data.SqlClient;
using System;
using static Aveva.ClashChecker.NetCallable.Exceptions;

namespace Aveva.ClashChecker.NetCallable;

/// <summary>
/// Класс для обработки коллизий
/// </summary>
[PMLNetCallable]
public partial class ClashChecker
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
            int deleteCount, updateCount, totalCount = 0;

            using SqlConnection clashConnection = GetClashSqlConnection();
            clashConnection.Open();

            string tableName = "";

            if (gpsetName != "")
            {
                string getAllClashesQuery = $"SELECT id, clashType, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2 from {tableName}";
             //               --получаем всю таблицу
             //	$P ТУТ

                //       !query = | select id, clashtype, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2 from $!tablename |
                //       !sqlarray = !!sqlQuery('SQL', !conn, !query)
                //	$P 1

                //       do !i from 0 to!sqlarray.size() - 1
                //	$P $!i
                //           !el1 = !sqlarray[!i][2] $*остаётся
                //           !el2 = !sqlarray[!i][7] $*надо 7
                //           !id = !sqlarray[!i][0] $*остаётся

                //           !SQLusermod1 = !sqlarray[!i][4]
                //           !SQLdept1 = !sqlarray[!i][5]
                //           !SQLgpset1 = !sqlarray[!i][6]

                //           !SQLusermod2 = !sqlarray[!i][9]
                //           !SQLdept2 = !sqlarray[!i][10]
                //           !SQLgpset2 = !sqlarray[!i][11]
                //           !LogFile.Open('APPEND')
                //           !LogFile.WriteRecord(!id & ';' & !el1 & ';' & !el2)
                //           !LogFile.Close()
                //           !ret = !!UpdateONEClashElementInfo(!mode, !id, !el1, !el2, !SQLgpset1, !SQLgpset2, !SQLusermod1, !SQLusermod2, !SQLdept1, !SQLdept2)


                //           if !ret eq 1 then
                //               !updcount = !updcount + 1

                //           elseif!ret eq -1 then
                //               !delcount = !delcount + 1

                //           endif
                //           !count = !count + 1

                //       enddo
                //   endif
                //$!tmpce
                //   !dtPOSLE = object datetime()
                //   !secPOSLE = !dtPOSLE.second() + !dtPOSLE.minute() * 60 + !dtPOSLE.hour() * 3600
                //   !res = !secPOSLE - !secDO
                //$P UpdateClashElementInfo $!res сек

                //   !!writelogEX('\\tep-m.ru\data\App\PDMS\PDMS_TEP\LOG\UpdateClashElementInfo.txt', !res & ';' & !gpsetname & ';' & !count & ';' & !delcount & ';' & !updcount & ';' & !mode)
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
    public static async Task<List<ClashEntity>> GetClashesAsync()
    {
        using var connection = new SqlConnection($"Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;Encrypt=False;User ID={sqlUserName};Password={sqlPassword};Connection Timeout = 300");
        var clashes = await connection.QueryAsync<ClashEntity>(
            "select id 'Id', clashtype 'ClashType', El1 'FirstElement', type1 'FirstType', usermod1 'FirstUserMode', dept1 'FirstDept', gpset1 'FirstGpset', El2 'SecondElement', type2 'SecondType', usermod2 'SecondUserMode', dept2 'SecondDept', gpset2 'SecondGpset' from clashtableARM");
        connection.CloseAsync();
        return [.. clashes];
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
