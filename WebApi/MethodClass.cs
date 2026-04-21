using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using System.Text;

namespace DBVM_API
{
    public class MethodClass
    {
        private static readonly string conn_str = "Data Source=10.200.55.55:1521/thishq;User ID=hson_test;Password=hson@test6643;";

        public static string[] GetAllColumn_Name(string db, string tableName)
        {
            List<string> columnNames = new List<string>();

            string sql = @$"SELECT COLUMN_NAME FROM ALL_TAB_COLUMNS WHERE OWNER = '{db}'
                  AND TABLE_NAME = '{tableName}'
                ORDER BY COLUMN_ID";

            using (var conn = new OracleConnection(conn_str))  // ← 你自己的連線字串
            {
                conn.Open();

                using (var cmd = new OracleCommand(sql, conn))
                {
                    cmd.BindByName = true;



                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            columnNames.Add(reader.GetString(0));
                        }
                    }
                }

                conn.Close();
            }

            return columnNames.ToArray();
        }
        public static async Task<int> AddRowsAsync(string db, string tableName, List<object[]> values)
        {
            if (values == null || values.Count == 0) return 0;

            // 取得欄位名稱（例如 PURRESD 有 20 欄）
            string[] allColumnNames = GetAllColumn_Name(db, tableName);
            if (values[0].Length == 0 || values[0].Length > allColumnNames.Length) return 0;

            int affected = 0;

            // 完整表名 = SCHEMA.TABLE
            string fullTableName = $"{db}.{tableName}";

            using (var conn = new OracleConnection(conn_str))
            {
                await conn.OpenAsync();

                using (var tx = conn.BeginTransaction())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.BindByName = true;

                    // --- 組 INSERT SQL ---
                    var sbCols = new StringBuilder();
                    var sbVals = new StringBuilder();

                    for (int k = 0; k < values[0].Length; k++)
                    {
                        string colName = allColumnNames[k];

                        if (k > 0)
                        {
                            sbCols.Append(",");
                            sbVals.Append(",");
                        }

                        sbCols.Append(colName);
                        sbVals.Append($":{colName}");

                        // 建立參數骨架
                        var p = cmd.CreateParameter();
                        p.ParameterName = colName;    // 無冒號
                        p.Value = DBNull.Value;
                        cmd.Parameters.Add(p);
                    }

                    // 不要加分號
                    cmd.CommandText = $"INSERT INTO {fullTableName} ({sbCols}) VALUES ({sbVals})";

                    try
                    {
                        // --- 實際塞資料 ---
                        foreach (var row in values)
                        {
                            for (int k = 0; k < row.Length; k++)
                            {
                                string colName = allColumnNames[k];
                                cmd.Parameters[colName].Value = row[k] ?? DBNull.Value;
                            }

                            affected += await cmd.ExecuteNonQueryAsync();
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }

            return affected;
        }
    }
}
