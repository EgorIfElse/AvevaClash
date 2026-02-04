using SQLOBJ2;
using Microsoft.Data.SqlClient;
using Dapper;
namespace ConsoleTesting;

public class Program
{

    private const string sqlUserName = "clashuser";
    private const string sqlPassword = "Qgh%fS45Nm";

    public static void Main(string[] args)
    {
        using var connection = new SqlConnection($"Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;Encrypt=False;User ID={sqlUserName};Password={sqlPassword};Connection Timeout = 300");

        connection.Open();
        var reader = connection.ExecuteScalar("select top 1 COUNT(*) from clashtableARM");
        //while(c)
        //while (reader.Read())
        //{
        //    var res = reader.GetValue(0);
        //foreach (var item in reader)
        //{
            Console.WriteLine(reader);
        //}

        //}

        connection.Close();
    }


    //"select id 'Id', clashtype 'ClashType', El1 'FirstElement', type1 'FirstType', usermod1 'FirstUserMode', dept1 'FirstDept', gpset1 'FirstGpset', El2 'SecondElement', type2 'SecondType', usermod2 'SecondUserMode', dept2 'SecondDept', gpset2 'SecondGpset' from clashtableARM");

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
