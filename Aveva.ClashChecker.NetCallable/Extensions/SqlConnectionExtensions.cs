using System;
using Microsoft.Data.SqlClient;

namespace Aveva.ClashChecker.NetCallable.Extensions;

public static class SqlConnectionExtensions
{

    public static bool TableExists(this SqlConnection sqlConnection, string tableName)
    {
        var query = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName";
        using var command = sqlConnection.CreateCommand();
        command.CommandText = query;
        command.Parameters.AddWithValue("@tableName", tableName);

        int rowCount = Convert.ToInt32(command.ExecuteScalar());
        return rowCount > 0;
    }

}
