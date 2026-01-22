using SQLOBJ2;
using Microsoft.Data.SqlClient;
using Dapper;
namespace ConsoleTesting;

public class Program
{

    private const string sqlUserName = "clashuser";
    private const string sqlPassword = "Qgh%fS45Nm";

    public static async Task Main(string[] args)
    {

        //string sqlQuery = "select id 'Id', clashtype 'ClashType', El1 'FirstElement', type1 'FirstType', usermod1 'FirstUserMode', dept1 'FirstDept', gpset1 'FirstGpset', El2 'SecondElement', type2 'SecondType', usermod2 'SecondUserMode', dept2 'SecondDept', gpset2 'SecondGpset' from clashtableARM";
        //string sqlConnection = $"Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;User ID={sqlUserName};Password={sqlPassword};Connection Timeout = 300";
        //string csvFilePath = "D:\\temp.csv";
        var temp = await GetClashesAsync();
        //SQLObject sQLObject = new();
        //var result = sQLObject.SqlQueryWithCsvOutput(sqlConnection, sqlQuery, csvFilePath);
        //Console.WriteLine(result);


    }

    public static async Task<List<ClashEntity>> GetClashesAsync()
    {
        using var connection = new SqlConnection($"Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;Encrypt=False;User ID={sqlUserName};Password={sqlPassword};Connection Timeout = 300");
        var clashes = await connection.QueryAsync<ClashEntity>(
            "select id 'Id', clashtype 'ClashType', El1 'FirstElement', type1 'FirstType', usermod1 'FirstUserMode', dept1 'FirstDept', gpset1 'FirstGpset', El2 'SecondElement', type2 'SecondType', usermod2 'SecondUserMode', dept2 'SecondDept', gpset2 'SecondGpset' from clashtableARM");
        connection.CloseAsync();
        return [.. clashes];
    }


    public record ClashEntity
    {
        public int Id { get; set; }
        public string ClashType { get; set; } = "";
        public string FirstElement { get; set; } = "";
        public string FirstType { get; set; } = "";
        public string FirstUserMode { get; set; } = "";
        public string FirstDept { get; set; } = "";
        public string FirstGpset { get; set; } = "";
        public string SecondElement { get; set; } = "";
        public string SecondType { get; set; } = "";
        public string SecondUserMode { get; set; } = "";
        public string SecondDept { get; set; } = "";
        public string SecondGpset { get; set; } = "";
    }



}
