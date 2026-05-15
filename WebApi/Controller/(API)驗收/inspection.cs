using Basic;
using HIS_DB_Lib;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using Oracle_Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DBVM_API.Controller._API_驗收
{
    [Route("dbvm/[controller]")]
    [ApiController]
    public class inspection : ControllerBase
    {
        private static string API = "http://127.0.0.1:4433";
        private static string server = "192.168.166.220";
        private static string ServiceName = "mis";
        private static string owner = "THESE";
        private static string userName = "THESE";
        private static string password = "these";
        private static int port = 1521;
        private static DateTime dt = new DateTime(2001, 1, 1, 0, 0, 0);

        //[HttpPost("update")]
        //public async Task<string> update(returnData returnData)
        //{
        //    MyTimerBasic myTimerBasic = new MyTimerBasic();
        //    returnData.Method = "update";
        //    try
        //    {
        //        if (returnData.Data == null)
        //        {
        //            returnData.Code = -200;
        //            returnData.Result = $"傳入資料異常!";
        //            return returnData.JsonSerializationt();
        //        }
        //        List<inspectionClass.sub_content> sub_contents = returnData.Data.ObjToListClass<inspectionClass.sub_content>();
        //        if (sub_contents == null)
        //        {
        //            sub_contents = new List<inspectionClass.sub_content>() { returnData.Data.ObjToClass<inspectionClass.sub_content>() };
        //            if (sub_contents == null)
        //            {
        //                returnData.Code = -200;
        //                returnData.Result = $"傳入資料異常!";
        //                return returnData.JsonSerializationt();
        //            }
        //        }
        //        ORCControl oRCControl = new ORCControl(server, ServiceName, owner, userName, password, port);
        //        returnData returnData_content = inspectionClass.content_get_all("http://127.0.0.1:4433");
        //        if (returnData_content == null || returnData_content.Code != 200)
        //        {
        //            throw new Exception("資料取得失敗");
        //        }
        //        List<inspectionClass.content> contents = returnData_content.Data.ObjToClass<List<inspectionClass.content>>(); //目前資料庫的content
        //        List<string> GUID_content = sub_contents.Select(x => x.Master_GUID).ToList(); //這次傳入的資料的Master_GUID集合
        //        contents = contents.Where(x => GUID_content.Contains(x.GUID)).ToList(); //過濾出這次傳入的資料對應的content






        //        //returnData.Result = $"回寫申領資料成功,共<{input_materialRequisitions.Count}>筆資料，{returnData_update.Result}";
        //        //returnData.TimeTaken = myTimerBasic.ToString();
        //        //returnData.Code = 200;
        //        //returnData.Data = input_materialRequisitions;
        //        return returnData.JsonSerializationt(true);
        //    }
        //    catch (Exception e)
        //    {
        //        returnData.Code = -200;
        //        returnData.Data = null;
        //        returnData.Result = $"{e.Message}";
        //        Logger.Log($"drugstotreDistribution", $"[異常] {returnData.Result}");
        //        return returnData.JsonSerializationt(true);
        //    }
        //}
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

                List<inspectionClass.sub_content> sub_contents =
                    returnData.Data.ObjToListClass<inspectionClass.sub_content>();

                if (sub_contents == null)
                {
                    sub_contents = new List<inspectionClass.sub_content>()
                    {
                        returnData.Data.ObjToClass<inspectionClass.sub_content>()
                    };

                    if (sub_contents == null || sub_contents.Count == 0)
                    {
                        returnData.Code = -200;
                        returnData.Result = $"傳入資料異常!";
                        return returnData.JsonSerializationt();
                    }
                }

                ORCControl oRCControl = new ORCControl(server, ServiceName, owner, userName, password, port);

                returnData returnData_content = inspectionClass.content_get_all("http://127.0.0.1:4433");

                if (returnData_content == null || returnData_content.Code != 200)
                {
                    throw new Exception("資料取得失敗");
                }

                List<inspectionClass.content> contents =
                    returnData_content.Data.ObjToClass<List<inspectionClass.content>>();

                List<string> GUID_content = sub_contents
                    .Select(x => x.Master_GUID)
                    .Distinct()
                    .ToList();

                contents = contents
                    .Where(x => GUID_content.Contains(x.GUID))
                    .ToList();

                if (contents.Count == 0)
                {
                    throw new Exception("找不到對應的驗收主檔資料");
                }

                List<UpdateItem_acpt> addItems = new List<UpdateItem_acpt>();
                List<UpdateItem_acpt> updateItems = new List<UpdateItem_acpt>();

                foreach (var sub in sub_contents)
                {
                    var content = contents.FirstOrDefault(x => x.GUID == sub.Master_GUID);

                    if (content == null)
                    {
                        Logger.Log("驗收回寫", $"找不到對應主檔 GUID:{sub.Master_GUID}");
                        continue;
                    }

                    if (content.請購單號.StringIsEmpty())
                    {
                        Logger.Log("驗收回寫", $"請購單號為空 GUID:{content.GUID}");
                        continue;
                    }

                    string[] 請購單號_array = content.請購單號.Split("-").ToArray();

                    if (請購單號_array.Length != 2)
                    {
                        Logger.Log("驗收回寫", $"請購單號格式錯誤:{content.請購單號}");
                        continue;
                    }

                    string 請購單號 = 請購單號_array[0];
                    string 請購序號 = 請購單號_array[1];

                    UpdateItem_acpt item = new UpdateItem_acpt()
                    {
                        // 主檔
                        NO_ACPT = content.驗收單號,
                        //VNDR_NAME = content.供應商,
                        ACPT_DATE = sub.操作時間,
                        INT_IN_TIME = DateTime.Now.ToDateTimeString(),

                        // 明細
                        NO_REQ = 請購單號,
                        CREC = 請購序號.StringToInt32(),
                        GOOD_CODE = sub.料號,
                        AMT_ACPT = sub.實收數量,
                        BATCH = sub.批號,
                        DATE_V = sub.效期.StringToDateTime().ToString("yyyyMMdd"),
                        HS_KEY = sub.GUID,
                        //MIS_ACPT_TIME = DateTime.Now
                    };

                    // 已回寫過 -> 更新
                    if (content.API回寫註記 == "True")
                    {
                        updateItems.Add(item);
                    }
                    else
                    {
                        addItems.Add(item);
                    }
                }

                if (addItems.Count == 0 && updateItems.Count == 0)
                {
                    throw new Exception("無可回寫的驗收資料");
                }

                // 主檔去重
                List<UpdateItem_acpt> addMasterItems = addItems
                    .GroupBy(x => x.NO_ACPT)
                    .Select(g => g.First())
                    .ToList();

                List<UpdateItem_acpt> updateMasterItems = updateItems
                    .GroupBy(x => x.NO_ACPT)
                    .Select(g => g.First())
                    .ToList();

                // =========================
                // 新增
                // =========================
                if (addItems.Count > 0)
                {
                    //bool addMaster = BatchAddHS_PUR_ACPT(
                    //    oRCControl.conn_str,
                    //    addMasterItems
                    //);

                    //if (!addMaster)
                    //{
                    //    throw new Exception("HS_PUR_ACPT 主檔新增失敗");
                    //}

                    bool addDetail = BatchAddHS_PUR_ACPT_ITM(
                        oRCControl.conn_str,
                        addItems
                    );

                    if (!addDetail)
                    {
                        throw new Exception("HS_PUR_ACPT_ITM 明細新增失敗");
                    }

                    Logger.Log("驗收回寫", "--add--");
                    Logger.Log("驗收回寫", addItems.JsonSerializationt(true));
                }

                // =========================
                // 更新
                // =========================
                if (updateItems.Count > 0)
                {
                    bool updateMaster = BatchUpdateHS_PUR_ACPT(
                        oRCControl.conn_str,
                        updateMasterItems
                    );

                    if (!updateMaster)
                    {
                        throw new Exception("HS_PUR_ACPT 主檔更新失敗");
                    }

                    bool updateDetail = BatchUpdateHS_PUR_ACPT_ITM(
                        oRCControl.conn_str,
                        updateItems
                    );

                    if (!updateDetail)
                    {
                        throw new Exception("HS_PUR_ACPT_ITM 明細更新失敗");
                    }

                    Logger.Log("驗收回寫", "--update--");
                    Logger.Log("驗收回寫", updateItems.JsonSerializationt(true));
                }

                Logger.Log("驗收回寫", $"新增:{addItems.Count} 更新:{updateItems.Count}");

                // =========================
                // 更新本地資料 API回寫註記
                // =========================

                foreach (var content in contents)
                {
                    content.API回寫註記 = "True";
                }

                returnData returnData_update =
                    inspectionClass.content_update("http://127.0.0.1:4433", contents);

                Logger.Log("驗收回寫", "--主表更新--");
                Logger.Log("驗收回寫", contents.JsonSerializationt(true));

                returnData.Result =
                    $"回寫驗收資料成功，新增<{addItems.Count}>筆，更新<{updateItems.Count}>筆";

                returnData.TimeTaken = myTimerBasic.ToString();
                returnData.Code = 200;

                returnData.Data = new
                {
                    addItems,
                    updateItems
                };

                return returnData.JsonSerializationt(true);
            }
            catch (Exception e)
            {
                returnData.Code = -200;
                returnData.Data = null;
                returnData.Result = $"{e.Message}";

                Logger.Log($"inspection.update", $"[異常] {returnData.Result}");

                return returnData.JsonSerializationt(true);
            }
        }
        public class UpdateItem_acpt
        {
            public string NO_ACPT { get; set; }      // 驗收單號
            public string VNDR_NAME { get; set; }    // 廠商名稱
            public string ACPT_DATE { get; set; }  // 驗收日期
            public string INT_IN_TIME { get; set; }// 鴻森系統.介接轉入時間

            public string NO_REQ { get; set; }       // 請購單號
            public int CREC { get; set; }            // 請購單品項序號
            public string GOOD_CODE { get; set; }    // 請購藥品代碼
            public string AMT_ACPT { get; set; }     // 驗收數量
            public string BATCH { get; set; }        // 批號
            public string DATE_V { get; set; }       // 效期 yyyyMMdd
            public string HS_KEY { get; set; }       // 鴻森系統KEY值
        }

        public static bool BatchAddHS_PUR_ACPT(string conn_str, List<UpdateItem_acpt> updateItems)
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
                INSERT INTO THESE.HS_PUR_ACPT
                (
                    NO_ACPT,
                    ACPT_DATE,
                    INT_IN_TIME
                )
                VALUES
                (
                    :NO_ACPT,
                    :ACPT_DATE,
                    :INT_IN_TIME
                )";

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            cmd.Parameters.Add("NO_ACPT", OracleDbType.Varchar2);
                            //cmd.Parameters.Add("VNDR_NAME", OracleDbType.NVarchar2);
                            cmd.Parameters.Add("ACPT_DATE", OracleDbType.Date);
                            cmd.Parameters.Add("INT_IN_TIME", OracleDbType.TimeStamp);

                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                cmd.Parameters["NO_ACPT"].Value = item.NO_ACPT;
                                cmd.Parameters["ACPT_DATE"].Value = DateTime.Parse(item.ACPT_DATE);
                                cmd.Parameters["INT_IN_TIME"].Value = DateTime.Parse(item.INT_IN_TIME);

                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"HS_PUR_ACPT批次新增成功，總影響筆數: {totalRows}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tran.Rollback();
                        Logger.Log($"HS_PUR_ACPT批次新增失敗: {ex.Message}");
                        Console.WriteLine($"HS_PUR_ACPT批次新增失敗: {ex.Message}");
                        return false;
                    }
                }
            }
        }
        public static bool BatchUpdateHS_PUR_ACPT(string conn_str, List<UpdateItem_acpt> updateItems)
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
                UPDATE THESE.HS_PUR_ACPT
                SET VNDR_NAME = :VNDR_NAME,
                    ACPT_DATE = :ACPT_DATE,
                    INT_IN_TIME = :INT_IN_TIME
                WHERE NO_ACPT = :NO_ACPT";

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            //cmd.Parameters.Add("VNDR_NAME", OracleDbType.NVarchar2);
                            cmd.Parameters.Add("ACPT_DATE", OracleDbType.Date);
                            cmd.Parameters.Add("INT_IN_TIME", OracleDbType.TimeStamp);
                            cmd.Parameters.Add("NO_ACPT", OracleDbType.Varchar2);

                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                //cmd.Parameters["VNDR_NAME"].Value = item.VNDR_NAME;
                                cmd.Parameters["ACPT_DATE"].Value = item.ACPT_DATE;
                                cmd.Parameters["INT_IN_TIME"].Value = item.INT_IN_TIME;
                                cmd.Parameters["NO_ACPT"].Value = item.NO_ACPT;

                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"HS_PUR_ACPT批次更新成功，總影響筆數: {totalRows}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tran.Rollback();
                        Logger.Log($"HS_PUR_ACPT批次更新失敗: {ex.Message}");
                        Console.WriteLine($"HS_PUR_ACPT批次更新失敗: {ex.Message}");
                        return false;
                    }
                }
            }
        }

        public static bool BatchAddHS_PUR_ACPT_ITM(string conn_str, List<UpdateItem_acpt> updateItems)
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
                INSERT INTO THESE.HS_PUR_ACPT_ITM
                (
                    NO_ACPT,
                    NO_REQ,
                    CREC,
                    GOOD_CODE,
                    AMT_ACPT,
                    BATCH,
                    DATE_V,
                    HS_KEY
                )
                VALUES
                (
                    :NO_ACPT,
                    :NO_REQ,
                    :CREC,
                    :GOOD_CODE,
                    :AMT_ACPT,
                    :BATCH,
                    :DATE_V,
                    :HS_KEY
                )";

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            cmd.Parameters.Add("NO_ACPT", OracleDbType.Varchar2);
                            cmd.Parameters.Add("NO_REQ", OracleDbType.Varchar2);
                            cmd.Parameters.Add("CREC", OracleDbType.Decimal);
                            cmd.Parameters.Add("GOOD_CODE", OracleDbType.Varchar2);
                            cmd.Parameters.Add("AMT_ACPT", OracleDbType.Decimal);
                            cmd.Parameters.Add("BATCH", OracleDbType.Varchar2);
                            cmd.Parameters.Add("DATE_V", OracleDbType.Char);
                            cmd.Parameters.Add("HS_KEY", OracleDbType.Varchar2);

                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                cmd.Parameters["NO_ACPT"].Value = item.NO_ACPT;
                                cmd.Parameters["NO_REQ"].Value = item.NO_REQ;
                                cmd.Parameters["CREC"].Value = item.CREC;
                                cmd.Parameters["GOOD_CODE"].Value = item.GOOD_CODE;
                                cmd.Parameters["AMT_ACPT"].Value = item.AMT_ACPT.StringToDouble();
                                cmd.Parameters["BATCH"].Value = item.BATCH;
                                cmd.Parameters["DATE_V"].Value = item.DATE_V;
                                cmd.Parameters["HS_KEY"].Value = item.HS_KEY;
                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"HS_PUR_ACPT_ITM批次新增成功，總影響筆數: {totalRows}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tran.Rollback();
                        Logger.Log($"HS_PUR_ACPT_ITM批次新增失敗: {ex.Message}");
                        Console.WriteLine($"HS_PUR_ACPT_ITM批次新增失敗: {ex.Message}");
                        return false;
                    }
                }
            }
        }
        public static bool BatchUpdateHS_PUR_ACPT_ITM(string conn_str, List<UpdateItem_acpt> updateItems)
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
                UPDATE THESE.HS_PUR_ACPT_ITM
                SET GOOD_CODE = :GOOD_CODE,
                    AMT_ACPT = :AMT_ACPT,
                    BATCH = :BATCH,
                    DATE_V = :DATE_V,
                    HS_KEY = :HS_KEY
                WHERE NO_ACPT = :NO_ACPT
                  AND NO_REQ = :NO_REQ
                  AND CREC = :CREC";

                        using (var cmd = new OracleCommand(commandText, conn_oracle))
                        {
                            cmd.Transaction = tran;

                            cmd.Parameters.Add("GOOD_CODE", OracleDbType.Varchar2);
                            cmd.Parameters.Add("AMT_ACPT", OracleDbType.Decimal);
                            cmd.Parameters.Add("BATCH", OracleDbType.Varchar2);
                            cmd.Parameters.Add("DATE_V", OracleDbType.Char);
                            cmd.Parameters.Add("HS_KEY", OracleDbType.Varchar2);
                            cmd.Parameters.Add("MIS_ACPT_TIME", OracleDbType.TimeStamp);

                            cmd.Parameters.Add("NO_ACPT", OracleDbType.Varchar2);
                            cmd.Parameters.Add("NO_REQ", OracleDbType.Varchar2);
                            cmd.Parameters.Add("CREC", OracleDbType.Decimal);

                            int totalRows = 0;

                            foreach (var item in updateItems)
                            {
                                cmd.Parameters["GOOD_CODE"].Value = item.GOOD_CODE;
                                cmd.Parameters["AMT_ACPT"].Value = item.AMT_ACPT.StringToDouble();
                                cmd.Parameters["BATCH"].Value = item.BATCH;
                                cmd.Parameters["DATE_V"].Value = item.DATE_V;
                                cmd.Parameters["HS_KEY"].Value = item.HS_KEY;
                                cmd.Parameters["NO_ACPT"].Value = item.NO_ACPT;
                                cmd.Parameters["NO_REQ"].Value = item.NO_REQ;
                                cmd.Parameters["CREC"].Value = item.CREC;

                                totalRows += cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            Console.WriteLine($"HS_PUR_ACPT_ITM批次更新成功，總影響筆數: {totalRows}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tran.Rollback();
                        Logger.Log($"HS_PUR_ACPT_ITM批次更新失敗: {ex.Message}");
                        Console.WriteLine($"HS_PUR_ACPT_ITM批次更新失敗: {ex.Message}");
                        return false;
                    }
                }
            }
        }
    }
}
