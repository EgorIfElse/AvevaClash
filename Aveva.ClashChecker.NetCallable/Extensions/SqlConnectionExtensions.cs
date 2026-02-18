using System;
using System.Linq;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Aveva.ClashChecker.NetCallable.Extensions;

public static class SqlConnectionExtensions
{

    public static bool TableExists(this SqlConnection sqlConnection, string tableName)
    {
        return sqlConnection.Query<int>($"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'").First() > 0;
    }

}
