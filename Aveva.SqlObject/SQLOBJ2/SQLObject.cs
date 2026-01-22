using System;
using System.Collections;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Aveva.Core.PMLNet;

namespace SQLOBJ2;

/// <summary>
/// Класс для взаимодвействия с Sql 
/// </summary>
[PMLNetCallable]
public class SQLObject
{

    public SqlConnection mSqlConnection;

    [PMLNetCallable]
    public SQLObject()
    {
    }

    [PMLNetCallable]
    public void Assign(SQLObject that)
    {
    }

    [PMLNetCallable]
    public Hashtable Test()
    {
        Hashtable hashtable = [];
        Hashtable hashtable2 = [];
        Hashtable hashtable3 = [];
        Hashtable hashtable4 = [];
        hashtable2.Add(0, "Первый");
        hashtable3.Add(0, "Четвертый");
        hashtable4.Add(0, "Седьмой");
        hashtable2.Add(1, "Второй");
        hashtable3.Add(1, "Пятый");
        hashtable4.Add(1, "Восьмой");
        hashtable2.Add(2, "Третий");
        hashtable3.Add(2, "Шестой");
        hashtable4.Add(2, "Девятый");
        NetArray netArray = new NetArray();
        hashtable.Add(0, hashtable2);
        hashtable.Add(1, hashtable3);
        hashtable.Add(2, hashtable4);
        netArray.Append(hashtable);
        return netArray.Val;
    }

    [PMLNetCallable]
    public Hashtable SqlQueryA(string connectionString, Hashtable HT)
    {
        string[] array = new string[HT.Count + 1];
        string text = "";
        try
        {
            foreach (DictionaryEntry item in HT)
            {
                array[Convert.ToInt16(item.Key.ToString())] = item.Value.ToString();
            }
            for (int i = 1; i <= HT.Count; i++)
            {
                text += array[i];
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
        return SqlQuery(connectionString, text);
    }

    [PMLNetCallable]
    public Hashtable SqlQuery(string connectionString, string Query)
    {
        try
        {
            SqlConnection sqlConnection = new(connectionString);
            SqlCommand sqlCommand = new()
            {
                CommandTimeout = 300,
                CommandText = Query,
                Connection = sqlConnection
            };
            sqlConnection.Open();
            SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
            ArrayList arrayList = [];
            try
            {
                int num = 0;
                while (sqlDataReader.Read())
                {
                    Hashtable hashtable = [];
                    for (int i = 0; i < sqlDataReader.FieldCount; i++)
                    {
                        hashtable.Add(i, Convert.ToString(sqlDataReader[i]));
                    }
                    arrayList.Add(hashtable);
                    num++;
                }
            }
            finally
            {
                sqlDataReader.Close();
                sqlConnection.Close();
            }
            Hashtable hashtable2 = [];
            for (int i = 0; i < arrayList.Count; i++)
            {
                hashtable2.Add(i, arrayList[i]);
            }
            NetArray netArray = new NetArray();
            netArray.Append(hashtable2);
            return netArray.Val;
        }
        catch (Exception ex)
        {
            var hashTable = new Hashtable
            {
                { 1, $"{ex.Message}\n{ex.StackTrace}" }
            };
            return hashTable;
        }
    }

    [PMLNetCallable]
    public string SqlQueryWithCsvOutput(string connectionString, string query, string filePath)
    {
        try
        {
            char cvsSeparatorChar = ';';
            SqlConnection sqlConnection = new(connectionString);
            SqlCommand sqlCommand = new()
            {
                CommandTimeout = 600,
                CommandText = query,
                Connection = sqlConnection
            };
            sqlConnection.Open();
            using var reader = sqlCommand.ExecuteReader(CommandBehavior.SequentialAccess);

            try
            {
                File.Delete(filePath);
            }
            catch(Exception ex)
            {

            }

            using var writer = new StreamWriter(filePath, false, Encoding.UTF8, 65536); //Запись результата SQL запроса в CSV через стрим (нельзя выходить за 3гб оперативы, поток расходует память вместе с AVEVA)
            var headers = new StringBuilder();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (i > 0) headers.Append(cvsSeparatorChar);
                headers.Append(EscapeCsv(reader.GetName(i)));
            }
            writer.WriteLine(headers);
            var rowBuilder = new StringBuilder(1024);
            //Средний расход ОЗУ за обработку чанка на данный момент - 19МБ
            const int chunkSize = 1048576; //Размер чанка - 1 метр
            while (reader.Read())
            {
                rowBuilder.Clear();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (i > 0) rowBuilder.Append(cvsSeparatorChar);

                    if (reader.IsDBNull(i))
                        continue;
                    var fieldType = reader.GetFieldType(i);
                    if (fieldType == typeof(string))
                    {
                        using var textReader = reader.GetTextReader(i);
                        rowBuilder.Append(textReader.ReadToEnd());
                    }
                    else
                    {
                        // Для остальных типов данных
                        var value = reader.GetValue(i);
                        rowBuilder.Append(EscapeCsv(value?.ToString()));
                    }
                }
                writer.WriteLine(rowBuilder);
                // Собираем мусор каждый 1мб 
                //TODO: поиграться с соотношением быстродействия / расхода памяти
                if (writer.BaseStream.Position % chunkSize == 0)
                {
                    writer.Flush();
                    GC.Collect(2, GCCollectionMode.Optimized, false, true);
                }
            }
            return "success";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

    }

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(",") || value.Contains("\"") ||
            value.Contains("\r") || value.Contains("\n"))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    [PMLNetCallable]
    public void SqlConnect(string connectionString)
    {
        mSqlConnection = new SqlConnection(connectionString);
        mSqlConnection.Open();
    }

    [PMLNetCallable]
    public void SqlDisconnect()
    {
        mSqlConnection.Close();
        mSqlConnection.Dispose();
    }

 
    [PMLNetCallable]
    public Hashtable SqlQQuery(string Query)
    {
        SqlCommand sqlCommand = new SqlCommand();
        sqlCommand.CommandText = Query;
        sqlCommand.Connection = mSqlConnection;
        SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
        ArrayList arrayList = new ArrayList();
        try
        {
            int num = 0;
            while (sqlDataReader.Read())
            {
                Hashtable hashtable = new Hashtable();
                for (int i = 0; i < sqlDataReader.FieldCount; i++)
                {
                    hashtable.Add(i, Convert.ToString(sqlDataReader[i]));
                }
                arrayList.Add(hashtable);
                num++;
            }
        }
        catch
        {
            mSqlConnection.Close();
        }
        finally
        {
            sqlDataReader.Close();
            sqlDataReader.Dispose();
            sqlCommand.Dispose();
        }
        Hashtable hashtable2 = new Hashtable();
        for (int i = 0; i < arrayList.Count; i++)
        {
            hashtable2.Add(i, arrayList[i]);
        }
        NetArray netArray = new NetArray();
        netArray.Append(hashtable2);
        return netArray.Val;
    }

}
