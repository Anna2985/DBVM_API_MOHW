using Basic;
using Google.Protobuf.WellKnownTypes;
using HIS_DB_Lib;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using Oracle_Lib;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;


// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DBVM_API.Controller._API_申領
{
    [Route("dbvm/[controller]")]
    [ApiController]
    public class materialRequisition : ControllerBase
    {
        private static string API = "http://127.0.0.1:4433";
        private static string server = "192.168.166.220";
        private static string ServiceName = "mis";
        private static string owner = "THESE";
        private static string userName = "THESE";
        private static string password = "these";
        private static int port = 1521;
        private static DateTime dt = new DateTime(2001, 1, 1, 0, 0, 0);

        [HttpPost("update")]
        public async Task<string> update(returnData returnData)
        {
            MyTimerBasic myTimerBasic = new MyTimerBasic();
            returnData.Method = "update";
            try
            {
                if (returnData.Data == null)
                {
                    returnData.Code = -200;
                    returnData.Result = $"傳入資料異常!";
                    return returnData.JsonSerializationt();
                }
                List<materialRequisitionClass> input_materialRequisitions = returnData.Data.ObjToClass<List<materialRequisitionClass>>();
                if (input_materialRequisitions == null)
                {
                    input_materialRequisitions = new List<materialRequisitionClass>() { returnData.Data.ObjToClass<materialRequisitionClass>() };
                    if (input_materialRequisitions == null)
                    {
                        returnData.Code = -200;
                        returnData.Result = $"傳入資料異常!";
                        return returnData.JsonSerializationt();
                    }
                }
                ORCControl oRCControl = new ORCControl(server, ServiceName, owner, userName, password, port);
                List<materialRequisitionClass> materialRequisitionClasses = materialRequisitionClass.get_by_requestTime(API, dt, DateTime.Now);
                string today = DateTime.Now.ToString("yyyyMMdd");
                string prefix = $"A1{today}";
                List<materialRequisitionClass> todayOrders = materialRequisitionClasses.Where(x => x.備註.StringIsEmpty() == false && x.備註.StartsWith(prefix)).ToList();
                int maxSerial = todayOrders
                    .Select(x =>
                    {
                        string orderNo = x.備註.Substring(10,4);
                        string serialText = orderNo.Substring(orderNo.Length - 4, 4);
                        return int.TryParse(serialText, out int serial) ? serial : 0;
                    }).DefaultIfEmpty(0).Max();

                // 這次新建立的單號 -> 已有哪些藥碼
                Dictionary<string, HashSet<string>> newOrderDrugMap = new Dictionary<string, HashSet<string>>();

                int currentSerial = maxSerial;
                List<materialRequisitionClass> add = new List<materialRequisitionClass>();
                List<materialRequisitionClass> update = new List<materialRequisitionClass>();
                List<medClass> medClasses = medClass.get_med_cloud(API);

                foreach (var item in input_materialRequisitions)
                {
                    // 有備註就跳過（代表已經有請領單號）
                    if (item.備註.StringIsEmpty() == false)
                    {
                        update.Add(item);
                        continue;
                    }
                        

                    string drugCode = item.藥碼 ?? "";

                    // 找目前這次新開的單，有沒有可以放的（沒有重複藥碼）
                    string matchedOrderNo = newOrderDrugMap
                        .OrderBy(x => x.Key)
                        .Where(x => x.Value.Contains(drugCode) == false)
                        .Select(x => x.Key)
                        .FirstOrDefault();

                    // 如果沒有可以放的，就開新單
                    if (string.IsNullOrEmpty(matchedOrderNo))
                    {
                        currentSerial++;
                        matchedOrderNo = $"{prefix}{currentSerial:D4}";
                        newOrderDrugMap[matchedOrderNo] = new HashSet<string>();
                    }

                    // 指派備註（請領單號 + 藥碼）
                    item.備註 = $"{matchedOrderNo}{drugCode}";
                    add.Add(item);
                    // 記錄這張單已有這個藥碼
                    if (!string.IsNullOrEmpty(drugCode))
                    {
                        newOrderDrugMap[matchedOrderNo].Add(drugCode);
                    }
                }

                List<UpdateItem_mr> updateItem_Mrs_add = new List<UpdateItem_mr>();
                List<UpdateItem_mr> updateItem_Mrs_update = new List<UpdateItem_mr>();

                DateTime now = DateTime.Now;
                foreach (var item in add)
                {
                    string 備註 = item.備註 ?? "";
                    string 請領單號 = 備註.Length >= 13 ? 備註.Substring(0, 14) : "";
                    string 料號 = item.料號;
                    if (料號.StringIsEmpty())
                    {
                        medClass med = medClasses.FirstOrDefault(x => x.藥品碼 == item.藥碼);
                        料號 = med != null ? med.料號 : "";
                    }
                    string 實撥量 = item.實撥量 ?? "0";
                    if (item.狀態 == "等待過帳") 實撥量 = "0";
                    updateItem_Mrs_add.Add(new UpdateItem_mr
                    {
                        NO_APLY = 請領單號,
                        GOOD_CODE = 料號,
                        ISSUE_AMT = 實撥量,
                        HS_KEY = item.GUID,
                        INT_IN_TIME = now,
                        ISSUE_TIME = item.核撥時間.StringToDateTime(),
                        ISSUE_BY = item.核撥人員ID,
                        DPT_CODE_O = "5030B",
                        DPT_CODE_I = item.申領單位 == "藥局" ? "5030A" : ""
                    });
                }
                foreach (var item in update)
                {
                    string 備註 = item.備註 ?? "";
                    string 請領單號 = 備註.Length >= 14 ? 備註.Substring(0, 14) : "";
                    string 料號 = 備註.Length > 13 ? 備註.Substring(13) : "";
                    string 實撥量 = item.實撥量 ?? "0";
                    if (item.狀態 == "等待過帳") 實撥量 = "0";
                    updateItem_Mrs_update.Add(new UpdateItem_mr
                    {
                        NO_APLY = 請領單號,
                        GOOD_CODE = 料號,
                        ISSUE_AMT = 實撥量,
                        HS_KEY = item.GUID,
                        INT_IN_TIME = now,
                        ISSUE_TIME = item.核撥時間.StringToDateTime(),
                        ISSUE_BY = item.核撥人員ID,
                        DPT_CODE_O = "5030B",
                        DPT_CODE_I = item.申領單位 == "藥局" ? "5030A" : ""
                    });
                }
                
                List<UpdateItem_mr> updateItem_Mrs_add_ = updateItem_Mrs_add
                    .GroupBy(x => x.NO_APLY)
                    .Select(g => g.First())
                    .ToList();
                List<UpdateItem_mr> updateItem_Mrs_update_ = updateItem_Mrs_update
                   .GroupBy(x => x.NO_APLY)
                   .Select(g => g.First())
                   .ToList();
                if (updateItem_Mrs_add.Count > 0)
                {
                    bool addHS_STK_APLY = BatchAddHS_STK_APLY(oRCControl.conn_str, updateItem_Mrs_add_);
                    bool addHS_STK_APLY_ITM = BatchAddHS_STK_APLY_ITM(oRCControl.conn_str, updateItem_Mrs_add);
                    Logger.Log("申領回寫", "--add--");

                    Logger.Log("申領回寫", updateItem_Mrs_add.JsonSerializationt(true));

                }
                if (updateItem_Mrs_update.Count > 0)
                {
                    bool updateHS_STK_APLY = BatchUpdateHS_STK_APLY(oRCControl.conn_str, updateItem_Mrs_update_);
                    bool updateHS_STK_APLY_ITM = BatchAddHS_STK_APLY(oRCControl.conn_str, updateItem_Mrs_update);
                    Logger.Log("申領回寫", "--update--");
                    Logger.Log("申領回寫", updateItem_Mrs_update.JsonSerializationt(true));

                }
                Logger.Log("申領回寫", "--主表更新--");
                Logger.Log("申領回寫", input_materialRequisitions.JsonSerializationt(true));

                returnData returnData_update = materialRequisitionClass.update_by_guid(API, input_materialRequisitions);

                returnData.Result = $"回寫申領資料成功,共<{input_materialRequisitions.Count}>筆資料，{returnData_update.Result}";
                returnData.TimeTaken = myTimerBasic.ToString();
                returnData.Code = 200;
                returnData.Data = input_materialRequisitions;
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
        public class UpdateItem_mr
        {
            public string NO_APLY { get; set; } //申領單號
            public string GOOD_CODE { get; set; }//申領藥品(代碼)
            public string ISSUE_AMT { get; set; } //核撥數量(in 最小包裝)
            public string HS_KEY { get; set; } //鴻森系統KEY值
            public DateTime INT_IN_TIME { get; set; } //鴻森系統.介接轉入時間
            public DateTime ISSUE_TIME { get; set; } //核撥時間
            public string ISSUE_BY { get; set; } //核撥人員(員工編號)
            public string DPT_CODE_O { get; set; } //來源庫別(發料庫代碼 : MUST = 藥庫)
            public string DPT_CODE_I { get; set; } //目的庫別(請領庫代碼)
        }

        public static bool BatchAddHS_STK_APLY(string conn_str, List<UpdateItem_mr> updateItems)
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
                        INSERT INTO THESE.HS_STK_APLY
                        (
                            NO_APLY,
                            DPT_CODE_O,
                            DPT_CODE_I, 
                            INT_IN_TIME,     
                            ISSUE_TIME,
                            ISSUE_BY
                        )
                        VALUES
                        (
                            :NO_APLY,
                            :DPT_CODE_O,
                            :DPT_CODE_I,
                            :INT_IN_TIME,
                            :ISSUE_TIME,
                            :ISSUE_BY
                        )";

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            cmd.Parameters.Add("NO_APLY", OracleDbType.Varchar2);
                            cmd.Parameters.Add("DPT_CODE_O", OracleDbType.Varchar2);
                            cmd.Parameters.Add("DPT_CODE_I", OracleDbType.Varchar2);
                            cmd.Parameters.Add("INT_IN_TIME", OracleDbType.TimeStamp);
                            cmd.Parameters.Add("ISSUE_TIME", OracleDbType.TimeStamp);
                            cmd.Parameters.Add("ISSUE_BY", OracleDbType.Varchar2);


                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                cmd.Parameters["NO_APLY"].Value = item.NO_APLY;
                                cmd.Parameters["DPT_CODE_O"].Value = item.DPT_CODE_O;
                                cmd.Parameters["DPT_CODE_I"].Value = item.DPT_CODE_I;
                                cmd.Parameters["INT_IN_TIME"].Value = item.INT_IN_TIME;
                                cmd.Parameters["ISSUE_TIME"].Value = item.ISSUE_TIME;
                                cmd.Parameters["ISSUE_BY"].Value = item.ISSUE_BY;
                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"批次新增成功，總影響筆數: {totalRows}");
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
        public static bool BatchUpdateHS_STK_APLY(string conn_str, List<UpdateItem_mr> updateItems)
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
                        UPDATE THESE.HS_STK_APLY
                        SET INT_IN_TIME = :INT_IN_TIME,
                            ISSUE_TIME = :ISSUE_TIME,
                            ISSUE_BY = :ISSUE_BY
                        WHERE NO_APLY = :NO_APLY ";

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            cmd.Parameters.Add("NO_APLY", OracleDbType.Varchar2);
                            cmd.Parameters.Add("INT_IN_TIME", OracleDbType.TimeStamp);
                            cmd.Parameters.Add("ISSUE_TIME", OracleDbType.TimeStamp);
                            cmd.Parameters.Add("ISSUE_BY", OracleDbType.Varchar2);


                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                cmd.Parameters["NO_APLY"].Value = item.NO_APLY;
                                cmd.Parameters["INT_IN_TIME"].Value = item.INT_IN_TIME;
                                cmd.Parameters["ISSUE_TIME"].Value = item.ISSUE_TIME;
                                cmd.Parameters["ISSUE_BY"].Value = item.ISSUE_BY;
                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"HS_STK_APLY批次更新成功，總影響筆數: {totalRows}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tran.Rollback();
                        Logger.Log($"HS_STK_APLY批次更新失敗: {ex.Message}");
                        Console.WriteLine($"批次更新失敗: {ex.Message}");
                        return false;
                    }
                }
            }
        }

        public static bool BatchAddHS_STK_APLY_ITM(string conn_str, List<UpdateItem_mr> updateItems) //add
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
                        INSERT INTO THESE.HS_STK_APLY_ITM
                        (
                            NO_APLY,
                            GOOD_CODE,
                            ISSUE_AMT,     
                            HS_KEY
                        )
                        VALUES
                        (
                            :NO_APLY,
                            :GOOD_CODE,
                            :ISSUE_AMT,
                            :HS_KEY
                        )";

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            cmd.Parameters.Add("NO_APLY", OracleDbType.Varchar2);
                            cmd.Parameters.Add("GOOD_CODE", OracleDbType.Varchar2);
                            cmd.Parameters.Add("ISSUE_AMT", OracleDbType.Decimal);
                            cmd.Parameters.Add("HS_KEY", OracleDbType.Varchar2);


                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                cmd.Parameters["NO_APLY"].Value = item.NO_APLY;
                                cmd.Parameters["GOOD_CODE"].Value = item.GOOD_CODE;
                                cmd.Parameters["ISSUE_AMT"].Value = item.ISSUE_AMT.StringToDouble();
                                cmd.Parameters["HS_KEY"].Value = item.HS_KEY;
                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"HS_STK_APLY_ITM批次新增成功，總影響筆數: {totalRows}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tran.Rollback();
                        Logger.Log($"HS_STK_APLY_ITM批次新增失敗: {ex.Message}");
                        Console.WriteLine($"HS_STK_APLY_ITM批次新增失敗: {ex.Message}");
                        return false;
                    }
                }
            }
        } 
        public static bool BatchUpdateHS_STK_APLY_ITM(string conn_str, List<UpdateItem_mr> updateItems)
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
                        UPDATE THESE.HS_STK_APLY_ITM
                        SET ISSUE_AMT = :ISSUE_AMT
                        WHERE NO_APLY = :NO_APLY 
                        and GOOD_CODE = :GOOD_CODE "; 

                        

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            cmd.Parameters.Add("NO_APLY", OracleDbType.Varchar2);
                            cmd.Parameters.Add("GOOD_CODE", OracleDbType.Varchar2);
                            cmd.Parameters.Add("ISSUE_AMT", OracleDbType.Decimal);

                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                cmd.Parameters["NO_APLY"].Value = item.NO_APLY;
                                cmd.Parameters["GOOD_CODE"].Value = item.GOOD_CODE;
                                cmd.Parameters["ISSUE_AMT"].Value = item.ISSUE_AMT.StringToDouble();
                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"HS_STK_APLY_ITM批次更新成功，總影響筆數: {totalRows}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tran.Rollback();
                        Logger.Log($"HS_STK_APLY_ITM更新失敗: {ex.Message}");
                        Console.WriteLine($"HS_STK_APLY_ITM批次更新失敗: {ex.Message}");
                        return false;
                    }
                }
            }
        }



        


    }
}
