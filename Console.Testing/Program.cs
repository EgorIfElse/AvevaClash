using System;
using SQLOBJ2;
namespace ConsoleTesting;

public class Program
{

    private const string sqlUserName = "clashuser";
    private const string sqlPassword = "Qgh%fS45Nm";

    public static void Main(string[] args)
    {

        string sqlQuery = "select id, clashtype, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2 from clashtableARM";
        string sqlConnection = $"Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;User ID={sqlUserName};Password={sqlPassword};Connection Timeout = 300";
        string csvFilePath = "D:\\temp.csv";

        SQLObject sQLObject = new();
        var result = sQLObject.SqlQueryWithCsvOutput(sqlConnection, sqlQuery, csvFilePath);
        Console.WriteLine(result);
    }
}
