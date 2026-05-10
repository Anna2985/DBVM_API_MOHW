using Basic;
using HIS_DB_Lib;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using Oracle_Lib;
using SQLUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DBVM_API.Controller._API_撥補
{
    //private static readonly string conn_str = "Data Source=192.168.166.220:1521/mis;User ID=THESE;Password=these;";
    

    [Route("dbvm/[controller]")]
    [ApiController]
    public class drugStotreDistribution : ControllerBase
    {
        private static string server = "192.168.166.220";
        private static string ServiceName = "mis";
        private static string owner = "THESE";
        private static string userName = "THESE";
        private static string password = "these";
        private static int port = 1521;

        [HttpPost("update")]
        public async Task<string> update(returnData returnData)
        {
            MyTimerBasic myTimerBasic = new MyTimerBasic();
            returnData.Method = "update";
            try
            {
                ORCControl oRCControl = new ORCControl(server, ServiceName, owner, userName, password, port);

                List<drugStotreDistributionClass> drugstotreDistributions = returnData.Data.ObjToClass<List<drugStotreDistributionClass>>();
                List<UpdateItem> updateItems = new List<UpdateItem>();
                foreach (var item in drugstotreDistributions)
                {
                    string 備註 = item.備註 ?? "";

                    string 請領單號 = 備註.Length >= 13 ? 備註.Substring(0, 13) : "";
                    string 料號 = 備註.Length > 13 ? 備註.Substring(13) : "";
                    string 實撥量 = item.實撥量 ?? "0";
                    updateItems.Add(new UpdateItem
                    {
                        NO_GET = 請領單號,
                        GOOD_CODE = 料號,
                        ISSUE_AMT = 實撥量,
                        HS_KEY = item.GUID,
                        HS_ISSUE_TIME = item.撥發時間,
                        HS_ISSUE_BY = item.撥發人員ID,

                    });
                }
                List<UpdateItem> updateItems_ = updateItems
                    .GroupBy(x => x.NO_GET)
                    .Select(g => g.First())
                    .ToList();
                bool updateMisStkAplyItm = BatchUpdateMisStkAplyItm(oRCControl.conn_str, updateItems);
                bool updateMIS_STK_APLY = BatchUpdateMIS_STK_APLY(oRCControl.conn_str, updateItems_);

                if (updateMisStkAplyItm == false || updateMIS_STK_APLY == false)
                {
                    returnData.Code = -200;
                    returnData.Data = drugstotreDistributions;
                    Logger.Log("撥補回寫FAIL", drugstotreDistributions.JsonSerializationt(true));
                    returnData.TimeTaken = myTimerBasic.ToString();
                    return returnData.JsonSerializationt(true);
                }
                returnData.Result = $"回寫撥補資料成功,共<{drugstotreDistributions.Count}>筆資料";
                returnData.TimeTaken = myTimerBasic.ToString();
                returnData.Code = 200;
                returnData.Data = drugstotreDistributions;
                return returnData.JsonSerializationt(true);
            }
            catch (Exception e)
            {
                returnData.Code = -200;
                returnData.Data = null;
                returnData.Result = $"{e.Message}";
                Logger.Log($"drugstotreDistribution", $"[異常] {returnData.Result}");
                return returnData.JsonSerializationt(true);
            }
        }
        public class UpdateItem
        {
            public string NO_GET { get; set; }
            public string GOOD_CODE { get; set; }
            public string ISSUE_AMT { get; set; }
            public string HS_KEY { get; set; }
            public DateTime HS_ISSUE_TIME { get; set; }
            public string HS_ISSUE_BY { get; set; }
        }

        public static bool BatchUpdateMisStkAplyItm(string conn_str, List<UpdateItem> updateItems)
        {
            using (var conn_oracle = new OracleConnection(conn_str))
            {
                try
                {
                    conn_oracle.Open();
                }
                catch (Exception ex)
                {
                    Logger.Log($"{ex.Message}, HIS系統連接失敗");
                    Console.WriteLine($"{ex.Message}, HIS系統連接失敗!");
                    return false;
                }

                using (var tran = conn_oracle.BeginTransaction())
                {
                    try
                    {
                        string commandText = @"
                    UPDATE THESE.MIS_STK_APLY_ITM
                    SET ISSUE_AMT = :ISSUE_AMT,
                        HS_KEY = :HS_KEY
                    WHERE NO_GET = :NO_GET
                      AND GOOD_CODE = :GOOD_CODE";

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            cmd.Parameters.Add("ISSUE_AMT", OracleDbType.Int32);
                            cmd.Parameters.Add("HS_KEY", OracleDbType.Varchar2);
                            cmd.Parameters.Add("NO_GET", OracleDbType.Varchar2);
                            cmd.Parameters.Add("GOOD_CODE", OracleDbType.Varchar2);

                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                cmd.Parameters["ISSUE_AMT"].Value = item.ISSUE_AMT;
                                cmd.Parameters["HS_KEY"].Value = item.HS_KEY;
                                cmd.Parameters["NO_GET"].Value = item.NO_GET;
                                cmd.Parameters["GOOD_CODE"].Value = item.GOOD_CODE;

                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"批次更新成功，總影響筆數: {totalRows}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tran.Rollback();
                        Logger.Log($"批次更新失敗: {ex.Message}");
                        Console.WriteLine($"批次更新失敗: {ex.Message}");
                        return false;
                    }
                }
            }
        }
        public static bool BatchUpdateMIS_STK_APLY(string conn_str, List<UpdateItem> updateItems)
        {
            using (var conn_oracle = new OracleConnection(conn_str))
            {
                try
                {
                    conn_oracle.Open();
                }
                catch (Exception ex)
                {
                    Logger.Log($"{ex.Message}, HIS系統連接失敗");
                    Console.WriteLine($"{ex.Message}, HIS系統連接失敗!");
                    return false;
                }

                using (var tran = conn_oracle.BeginTransaction())
                {
                    try
                    {
                        string commandText = @"
                        UPDATE THESE.MIS_STK_APLY
                        SET HS_ISSUE_TIME = :HS_ISSUE_TIME,
                            HS_ISSUE_BY = :HS_ISSUE_BY
                        WHERE NO_GET = :NO_GET ";

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            cmd.Parameters.Add("HS_ISSUE_TIME", OracleDbType.TimeStamp);
                            cmd.Parameters.Add("HS_ISSUE_BY", OracleDbType.Varchar2);
                            cmd.Parameters.Add("NO_GET", OracleDbType.Varchar2);

                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                cmd.Parameters["HS_ISSUE_TIME"].Value = item.HS_ISSUE_TIME;
                                cmd.Parameters["HS_ISSUE_BY"].Value = item.HS_ISSUE_BY;
                                cmd.Parameters["NO_GET"].Value = item.NO_GET;

                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"批次更新成功，總影響筆數: {totalRows}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tran.Rollback();
                        Logger.Log($"批次更新失敗: {ex.Message}");
                        Console.WriteLine($"批次更新失敗: {ex.Message}");
                        return false;
                    }
                }
            }
        }
    }
}
