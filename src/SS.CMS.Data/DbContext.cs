﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using SqlKata.Compilers;
using SS.CMS.Data.DatabaseImpl;
using SS.CMS.Data.Utils;

namespace SS.CMS.Data
{
    public class DbContext
    {
        public DatabaseType DatabaseType { get; }

        public string ConnectionString { get; }

        public DbContext(DatabaseType databaseType, string connectionString)
        {
            if (databaseType == DatabaseType.MySql)
            {
                connectionString = connectionString.TrimEnd(';');
                if (!Utilities.ContainsIgnoreCase(connectionString, "SslMode="))
                {
                    connectionString += ";SslMode=Preferred;";
                }
                if (!Utilities.ContainsIgnoreCase(connectionString, "CharSet="))
                {
                    connectionString += ";CharSet=utf8;";
                }
            }
            else if (databaseType == DatabaseType.Oracle)
            {
                connectionString = connectionString.TrimEnd(';');
                if (!Utilities.ContainsIgnoreCase(connectionString, "pooling="))
                {
                    connectionString += ";pooling=false;";
                }
            }

            DatabaseType = databaseType;
            ConnectionString = connectionString;
        }



        public bool IsTableExists(string tableName)
        {
            bool exists;

            if (DatabaseType == DatabaseType.Oracle)
            {
                tableName = tableName.ToUpper();
            }
            else if (DatabaseType == DatabaseType.MySql || DatabaseType == DatabaseType.PostgreSql)
            {
                tableName = tableName.ToLower();
            }

            try
            {
                if (DatabaseType == DatabaseType.Oracle)
                {
                    var userName = Utilities.GetConnectionStringUserName(ConnectionString);
                    var sql = $"SELECT COUNT(*) FROM ALL_OBJECTS WHERE OBJECT_TYPE = 'TABLE' AND OWNER = '{userName.ToUpper()}' and OBJECT_NAME = '{tableName}'";

                    using (var connection = GetConnection())
                    {
                        exists = connection.ExecuteScalar<int>(sql) == 1;
                    }

                }
                else if (DatabaseType == DatabaseType.SQLite)
                {
                    var sql = $"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";

                    using (var connection = GetConnection())
                    {
                        exists = connection.ExecuteScalar<int>(sql) == 1;
                    }
                }
                else // ANSI SQL way.  Works in PostgreSQL, MSSQL, MySQL.  
                {
                    var sql = $"select case when exists((select * from information_schema.tables where table_name = '{tableName}')) then 1 else 0 end";

                    using (var connection = GetConnection())
                    {
                        exists = connection.ExecuteScalar<int>(sql) == 1;
                    }
                }
            }
            catch
            {
                try
                {
                    var sql = $"select 1 from {tableName} where 1 = 0";

                    using (var connection = GetConnection())
                    {
                        exists = connection.ExecuteScalar<int>(sql) == 1;
                    }
                }
                catch
                {
                    exists = false;
                }
            }

            return exists;
        }

        public async Task<bool> IsTableExistsAsync(string tableName)
        {
            bool exists;

            if (DatabaseType == DatabaseType.Oracle)
            {
                tableName = tableName.ToUpper();
            }
            else if (DatabaseType == DatabaseType.MySql || DatabaseType == DatabaseType.PostgreSql)
            {
                tableName = tableName.ToLower();
            }

            try
            {
                if (DatabaseType == DatabaseType.Oracle)
                {
                    var userName = Utilities.GetConnectionStringUserName(ConnectionString);
                    var sql = $"SELECT COUNT(*) FROM ALL_OBJECTS WHERE OBJECT_TYPE = 'TABLE' AND OWNER = '{userName.ToUpper()}' and OBJECT_NAME = '{tableName}'";

                    using (var connection = GetConnection())
                    {
                        exists = await connection.ExecuteScalarAsync<int>(sql) == 1;
                    }
                }
                else if (DatabaseType == DatabaseType.SQLite)
                {
                    var sql = $"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";

                    using (var connection = GetConnection())
                    {
                        exists = await connection.ExecuteScalarAsync<int>(sql) == 1;
                    }
                }
                else // ANSI SQL way.  Works in PostgreSQL, MSSQL, MySQL.  
                {
                    var sql = $"select case when exists((select * from information_schema.tables where table_name = '{tableName}')) then 1 else 0 end";

                    using (var connection = GetConnection())
                    {
                        exists = await connection.ExecuteScalarAsync<int>(sql) == 1;
                    }
                }
            }
            catch
            {
                try
                {
                    var sql = $"select 1 from {tableName} where 1 = 0";

                    using (var connection = GetConnection())
                    {
                        exists = await connection.ExecuteScalarAsync<int>(sql) == 1;
                    }
                }
                catch
                {
                    exists = false;
                }
            }

            return exists;
        }

        public string AddIdentityColumnIdIfNotExists(string tableName, List<TableColumn> columns)
        {
            var identityColumnName = string.Empty;
            foreach (var column in columns)
            {
                if (column.IsIdentity || Utilities.EqualsIgnoreCase(column.AttributeName, "id"))
                {
                    identityColumnName = column.AttributeName;
                    break;
                }
            }

            if (string.IsNullOrEmpty(identityColumnName))
            {
                identityColumnName = nameof(Entity.Id);
                var sqlString =
                    GetAddColumnsSqlString(tableName, $"{identityColumnName} {GetAutoIncrementDataType(true)}");

                using (var connection = GetConnection())
                {
                    connection.Execute(sqlString);
                }

                columns.Insert(0, new TableColumn
                {
                    AttributeName = identityColumnName,
                    DataType = DataType.Integer,
                    IsPrimaryKey = false,
                    IsIdentity = true
                });
            }

            return identityColumnName;
        }

        public void AlterTable(string tableName, IList<TableColumn> tableColumns, IList<string> dropColumnNames = null)
        {
            var list = new List<string>();

            var columnNameList = GetColumnNames(tableName);
            foreach (var tableColumn in tableColumns)
            {
                if (!Utilities.ContainsIgnoreCase(columnNameList, tableColumn.AttributeName))
                {
                    list.Add(GetAddColumnsSqlString(tableName, GetColumnSqlString(tableColumn)));
                }
            }

            if (dropColumnNames != null)
            {
                foreach (var columnName in columnNameList)
                {
                    if (Utilities.ContainsIgnoreCase(dropColumnNames, columnName))
                    {
                        list.Add(GetDropColumnsSqlString(tableName, columnName));
                    }
                }
            }

            if (list.Count <= 0) return;

            foreach (var sqlString in list)
            {
                using (var connection = GetConnection())
                {
                    connection.Execute(sqlString);
                }
            }
        }

        public async Task AlterTableAsync(string tableName, IList<TableColumn> tableColumns, IList<string> dropColumnNames = null)
        {
            var list = new List<string>();

            var columnNameList = GetColumnNames(tableName);
            foreach (var tableColumn in tableColumns)
            {
                if (!Utilities.ContainsIgnoreCase(columnNameList, tableColumn.AttributeName))
                {
                    list.Add(GetAddColumnsSqlString(tableName, GetColumnSqlString(tableColumn)));
                }
            }

            if (dropColumnNames != null)
            {
                foreach (var columnName in columnNameList)
                {
                    if (Utilities.ContainsIgnoreCase(dropColumnNames, columnName))
                    {
                        list.Add(GetDropColumnsSqlString(tableName, columnName));
                    }
                }
            }

            if (list.Count <= 0) return;

            foreach (var sqlString in list)
            {
                using (var connection = GetConnection())
                {
                    await connection.ExecuteAsync(sqlString);
                }
            }
        }

        public void CreateTable(string tableName, List<TableColumn> tableColumns)
        {
            var sqlBuilder = new StringBuilder();

            sqlBuilder.Append($@"CREATE TABLE {GetQuotedIdentifier(tableName)} (").AppendLine();

            var primaryKeyColumns = new List<TableColumn>();
            TableColumn identityColumn = null;

            foreach (var tableColumn in tableColumns)
            {
                if (Utilities.EqualsIgnoreCase(tableColumn.AttributeName, nameof(Entity.Id)))
                {
                    tableColumn.DataType = DataType.Integer;
                    tableColumn.IsIdentity = true;
                    tableColumn.IsPrimaryKey = true;
                }
                else if (Utilities.EqualsIgnoreCase(tableColumn.AttributeName, nameof(Entity.Guid)))
                {
                    tableColumn.DataType = DataType.VarChar;
                    tableColumn.DataLength = 50;
                }
                else if (Utilities.EqualsIgnoreCase(tableColumn.AttributeName, nameof(Entity.LastModifiedDate)))
                {
                    tableColumn.DataType = DataType.DateTime;
                }
            }

            foreach (var tableColumn in tableColumns)
            {
                if (string.IsNullOrEmpty(tableColumn.AttributeName)) continue;

                if (tableColumn.IsIdentity)
                {
                    identityColumn = tableColumn;
                }

                if (tableColumn.IsPrimaryKey)
                {
                    primaryKeyColumns.Add(tableColumn);
                }

                if (tableColumn.DataType == DataType.VarChar && tableColumn.DataLength == 0)
                {
                    tableColumn.DataLength = DbUtils.VarCharDefaultLength;
                }

                var columnSql = GetColumnSqlString(tableColumn);
                if (!string.IsNullOrEmpty(columnSql))
                {
                    sqlBuilder.Append(columnSql).Append(",");
                }
            }

            if (DatabaseType != DatabaseType.SQLite)
            {
                if (identityColumn != null)
                {
                    var primaryKeySql = GetPrimaryKeySqlString(tableName, identityColumn.AttributeName);
                    if (!string.IsNullOrEmpty(primaryKeySql))
                    {
                        sqlBuilder.Append(primaryKeySql).Append(",");
                    }
                }
                else if (primaryKeyColumns.Count > 0)
                {
                    foreach (var tableColumn in primaryKeyColumns)
                    {
                        var primaryKeySql = GetPrimaryKeySqlString(tableName, tableColumn.AttributeName);
                        if (!string.IsNullOrEmpty(primaryKeySql))
                        {
                            sqlBuilder.Append(primaryKeySql).Append(",");
                        }
                    }
                }
            }

            sqlBuilder.Length--;

            sqlBuilder.AppendLine().Append(DatabaseType == DatabaseType.MySql
                ? ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"
                : ")");

            using (var connection = GetConnection())
            {
                connection.Execute(sqlBuilder.ToString());
            }
        }

        public async Task CreateTableAsync(string tableName, List<TableColumn> tableColumns)
        {
            var sqlBuilder = new StringBuilder();

            sqlBuilder.Append($@"CREATE TABLE {GetQuotedIdentifier(tableName)} (").AppendLine();

            var primaryKeyColumns = new List<TableColumn>();
            TableColumn identityColumn = null;

            foreach (var tableColumn in tableColumns)
            {
                if (Utilities.EqualsIgnoreCase(tableColumn.AttributeName, nameof(Entity.Id)))
                {
                    tableColumn.DataType = DataType.Integer;
                    tableColumn.IsIdentity = true;
                    tableColumn.IsPrimaryKey = true;
                }
                else if (Utilities.EqualsIgnoreCase(tableColumn.AttributeName, nameof(Entity.Guid)))
                {
                    tableColumn.DataType = DataType.VarChar;
                    tableColumn.DataLength = 50;
                }
                else if (Utilities.EqualsIgnoreCase(tableColumn.AttributeName, nameof(Entity.LastModifiedDate)))
                {
                    tableColumn.DataType = DataType.DateTime;
                }
            }

            foreach (var tableColumn in tableColumns)
            {
                if (string.IsNullOrEmpty(tableColumn.AttributeName)) continue;

                if (tableColumn.IsIdentity)
                {
                    identityColumn = tableColumn;
                }

                if (tableColumn.IsPrimaryKey)
                {
                    primaryKeyColumns.Add(tableColumn);
                }

                if (tableColumn.DataType == DataType.VarChar && tableColumn.DataLength == 0)
                {
                    tableColumn.DataLength = DbUtils.VarCharDefaultLength;
                }

                var columnSql = GetColumnSqlString(tableColumn);
                if (!string.IsNullOrEmpty(columnSql))
                {
                    sqlBuilder.Append(columnSql).Append(",");
                }
            }

            if (DatabaseType != DatabaseType.SQLite)
            {
                if (identityColumn != null)
                {
                    var primaryKeySql = GetPrimaryKeySqlString(tableName, identityColumn.AttributeName);
                    if (!string.IsNullOrEmpty(primaryKeySql))
                    {
                        sqlBuilder.Append(primaryKeySql).Append(",");
                    }
                }
                else if (primaryKeyColumns.Count > 0)
                {
                    foreach (var tableColumn in primaryKeyColumns)
                    {
                        var primaryKeySql = GetPrimaryKeySqlString(tableName, tableColumn.AttributeName);
                        if (!string.IsNullOrEmpty(primaryKeySql))
                        {
                            sqlBuilder.Append(primaryKeySql).Append(",");
                        }
                    }
                }
            }

            sqlBuilder.Length--;

            sqlBuilder.AppendLine().Append(DatabaseType == DatabaseType.MySql
                ? ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"
                : ")");

            using (var connection = GetConnection())
            {
                await connection.ExecuteAsync(sqlBuilder.ToString());
            }
        }

        public void CreateIndex(string tableName, string indexName, params string[] columns)
        {
            var sqlString = new StringBuilder($@"CREATE INDEX {GetQuotedIdentifier(indexName)} ON {GetQuotedIdentifier(tableName)}(");

            foreach (var column in columns)
            {
                var columnName = column;
                var columnOrder = "ASC";
                var i = column.IndexOf(" ", StringComparison.Ordinal);
                if (i != -1)
                {
                    columnName = column.Substring(0, i);
                    columnOrder = column.Substring(i + 1);
                }
                sqlString.Append($"{GetQuotedIdentifier(columnName)} {columnOrder}, ");
            }

            sqlString.Length--;
            sqlString.Append(")");

            using (var connection = GetConnection())
            {
                connection.Execute(sqlString.ToString());
            }
        }

        public async Task CreateIndexAsync(string tableName, string indexName, params string[] columns)
        {
            var sqlString = new StringBuilder($@"CREATE INDEX {GetQuotedIdentifier(indexName)} ON {GetQuotedIdentifier(tableName)}(");

            foreach (var column in columns)
            {
                var columnName = column;
                var columnOrder = "ASC";
                var i = column.IndexOf(" ", StringComparison.Ordinal);
                if (i != -1)
                {
                    columnName = column.Substring(0, i);
                    columnOrder = column.Substring(i + 1);
                }
                sqlString.Append($"{GetQuotedIdentifier(columnName)} {columnOrder}, ");
            }

            sqlString.Length--;
            sqlString.Append(")");

            using (var connection = GetConnection())
            {
                await connection.ExecuteAsync(sqlString.ToString());
            }
        }

        public List<string> GetColumnNames(string tableName)
        {
            var allTableColumnInfoList = GetTableColumns(tableName);
            return allTableColumnInfoList.Select(tableColumnInfo => tableColumnInfo.AttributeName).ToList();
        }

        public List<TableColumn> GetTableColumns<T>() where T : Entity
        {
            return ReflectionUtils.GetTableColumns(typeof(T));
        }

        public void DropTable(string tableName)
        {
            using (var connection = GetConnection())
            {
                connection.Execute($"DROP TABLE {GetQuotedIdentifier(tableName)}");
            }
        }

        public async Task DropTableAsync(string tableName)
        {
            using (var connection = GetConnection())
            {
                await connection.ExecuteAsync($"DROP TABLE {GetQuotedIdentifier(tableName)}");
            }
        }

        public DbConnection GetConnection()
        {
            if (string.IsNullOrWhiteSpace(ConnectionString)) return null;

            DbConnection conn = null;
            if (DatabaseType == DatabaseType.MySql)
            {
                conn = MySqlImpl.Instance.GetConnection(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                conn = SqlServerImpl.Instance.GetConnection(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                conn = PostgreSqlImpl.Instance.GetConnection(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                conn = OracleImpl.Instance.GetConnection(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                conn = SQLiteImpl.Instance.GetConnection(ConnectionString);
            }

            return conn;
        }

        public Compiler GetCompiler()
        {
            Compiler compiler = null;

            if (DatabaseType == DatabaseType.MySql)
            {
                compiler = MySqlImpl.Instance.GetCompiler(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                compiler = SqlServerImpl.Instance.GetCompiler(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                compiler = PostgreSqlImpl.Instance.GetCompiler(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                compiler = OracleImpl.Instance.GetCompiler(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                compiler = SQLiteImpl.Instance.GetCompiler(ConnectionString);
            }

            return compiler;
        }

        public List<TableColumn> GetTableColumns(string tableName)
        {
            List<TableColumn> list = null;

            if (DatabaseType == DatabaseType.MySql)
            {
                list = MySqlImpl.Instance.GetTableColumns(ConnectionString, tableName);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                list = SqlServerImpl.Instance.GetTableColumns(ConnectionString, tableName);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                list = PostgreSqlImpl.Instance.GetTableColumns(ConnectionString, tableName);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                list = OracleImpl.Instance.GetTableColumns(ConnectionString, tableName);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                list = SQLiteImpl.Instance.GetTableColumns(ConnectionString, tableName);
            }

            return list;
        }

        public List<string> GetTableNames()
        {
            List<string> tableNames = null;

            if (DatabaseType == DatabaseType.MySql)
            {
                tableNames = MySqlImpl.Instance.GetTableNames(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                tableNames = SqlServerImpl.Instance.GetTableNames(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                tableNames = PostgreSqlImpl.Instance.GetTableNames(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                tableNames = OracleImpl.Instance.GetTableNames(ConnectionString);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                tableNames = SQLiteImpl.Instance.GetTableNames(ConnectionString);
            }

            return tableNames;
        }

        public string ColumnIncrement(string columnName, int plusNum = 1)
        {
            var retVal = string.Empty;

            if (DatabaseType == DatabaseType.MySql)
            {
                retVal = MySqlImpl.Instance.ColumnIncrement(columnName, plusNum);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                retVal = SqlServerImpl.Instance.ColumnIncrement(columnName, plusNum);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                retVal = PostgreSqlImpl.Instance.ColumnIncrement(columnName, plusNum);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                retVal = OracleImpl.Instance.ColumnIncrement(columnName, plusNum);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                retVal = SQLiteImpl.Instance.ColumnIncrement(columnName, plusNum);
            }

            return retVal;
        }

        public string ColumnDecrement(string columnName, int minusNum = 1)
        {
            var retVal = string.Empty;

            if (DatabaseType == DatabaseType.MySql)
            {
                retVal = MySqlImpl.Instance.ColumnDecrement(columnName, minusNum);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                retVal = SqlServerImpl.Instance.ColumnDecrement(columnName, minusNum);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                retVal = PostgreSqlImpl.Instance.ColumnDecrement(columnName, minusNum);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                retVal = OracleImpl.Instance.ColumnDecrement(columnName, minusNum);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                retVal = SQLiteImpl.Instance.ColumnDecrement(columnName, minusNum);
            }

            return retVal;
        }

        public string GetAutoIncrementDataType(bool alterTable = false)
        {
            var retVal = string.Empty;

            if (DatabaseType == DatabaseType.MySql)
            {
                retVal = MySqlImpl.Instance.GetAutoIncrementDataType(alterTable);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                retVal = SqlServerImpl.Instance.GetAutoIncrementDataType(alterTable);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                retVal = PostgreSqlImpl.Instance.GetAutoIncrementDataType(alterTable);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                retVal = OracleImpl.Instance.GetAutoIncrementDataType(alterTable);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                retVal = SQLiteImpl.Instance.GetAutoIncrementDataType(alterTable);
            }

            return retVal;
        }

        public string GetColumnSqlString(TableColumn tableColumn)
        {
            var retVal = string.Empty;

            if (DatabaseType == DatabaseType.MySql)
            {
                retVal = MySqlImpl.Instance.GetColumnSqlString(tableColumn);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                retVal = SqlServerImpl.Instance.GetColumnSqlString(tableColumn);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                retVal = PostgreSqlImpl.Instance.GetColumnSqlString(tableColumn);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                retVal = OracleImpl.Instance.GetColumnSqlString(tableColumn);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                retVal = SQLiteImpl.Instance.GetColumnSqlString(tableColumn);
            }

            return retVal;
        }

        public string GetPrimaryKeySqlString(string tableName, string attributeName)
        {
            var retVal = string.Empty;

            if (DatabaseType == DatabaseType.MySql)
            {
                retVal = MySqlImpl.Instance.GetPrimaryKeySqlString(tableName, attributeName);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                retVal = SqlServerImpl.Instance.GetPrimaryKeySqlString(tableName, attributeName);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                retVal = PostgreSqlImpl.Instance.GetPrimaryKeySqlString(tableName, attributeName);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                retVal = OracleImpl.Instance.GetPrimaryKeySqlString(tableName, attributeName);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                retVal = SQLiteImpl.Instance.GetPrimaryKeySqlString(tableName, attributeName);
            }

            return retVal;
        }

        public string GetQuotedIdentifier(string identifier)
        {
            var retVal = string.Empty;

            if (DatabaseType == DatabaseType.MySql)
            {
                retVal = MySqlImpl.Instance.GetQuotedIdentifier(identifier);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                retVal = SqlServerImpl.Instance.GetQuotedIdentifier(identifier);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                retVal = PostgreSqlImpl.Instance.GetQuotedIdentifier(identifier);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                retVal = OracleImpl.Instance.GetQuotedIdentifier(identifier);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                retVal = SQLiteImpl.Instance.GetQuotedIdentifier(identifier);
            }

            return retVal;
        }

        public string GetAddColumnsSqlString(string tableName, string columnsSqlString)
        {
            var retVal = string.Empty;

            if (DatabaseType == DatabaseType.MySql)
            {
                retVal = MySqlImpl.Instance.GetAddColumnsSqlString(tableName, columnsSqlString);
            }
            else if (DatabaseType == DatabaseType.SqlServer)
            {
                retVal = SqlServerImpl.Instance.GetAddColumnsSqlString(tableName, columnsSqlString);
            }
            else if (DatabaseType == DatabaseType.PostgreSql)
            {
                retVal = PostgreSqlImpl.Instance.GetAddColumnsSqlString(tableName, columnsSqlString);
            }
            else if (DatabaseType == DatabaseType.Oracle)
            {
                retVal = OracleImpl.Instance.GetAddColumnsSqlString(tableName, columnsSqlString);
            }
            else if (DatabaseType == DatabaseType.SQLite)
            {
                retVal = SQLiteImpl.Instance.GetAddColumnsSqlString(tableName, columnsSqlString);
            }

            return retVal;
        }

        public string GetDropColumnsSqlString(string tableName, string columnName)
        {
            return $"ALTER TABLE {GetQuotedIdentifier(tableName)} DROP COLUMN {GetQuotedIdentifier(columnName)}";
        }
    }
}
