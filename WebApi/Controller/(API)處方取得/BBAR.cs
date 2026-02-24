using Basic;
using Google.Protobuf.WellKnownTypes;
using HIS_DB_Lib;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using SQLUI;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using H_Pannel_lib;

namespace DB2VM
{

    [Route("dbvm/[controller]")]
    [ApiController]
    public class BBARController : ControllerBase
    {

        public enum enum_急診藥袋
        {
            本次領藥號,
            看診日期,
            病歷號,
            序號,
            頻率,
            途徑,
            總量,
            前次領藥號,
            本次醫令序號,
        }
        private static readonly string conn_str = "Data Source=192.168.166.220:1521/sisdcp;User ID=hson_kutech;Password=6w1xPDQnsnw3kO;";

        static string MySQL_server = $"{ConfigurationManager.AppSettings["MySQL_server"]}";
        static string MySQL_database = $"{ConfigurationManager.AppSettings["MySQL_database"]}";
        static string MySQL_userid = $"{ConfigurationManager.AppSettings["MySQL_user"]}";
        static string MySQL_password = $"{ConfigurationManager.AppSettings["MySQL_password"]}";
        static string MySQL_port = $"{ConfigurationManager.AppSettings["MySQL_port"]}";

        private SQLControl sQLControl_醫囑資料 = new SQLControl(MySQL_server, MySQL_database, "order_list", MySQL_userid, MySQL_password, (uint)MySQL_port.StringToInt32(), MySql.Data.MySqlClient.MySqlSslMode.None);
        private string API_Server = "http://192.168.12.164:4433";
        [HttpGet]
        public string Get(string? BarCode)
        {
            MyTimerBasic myTimer_total = new MyTimerBasic();
            string HIS連線時間 = "";
            string HISData時間 = "";
            string DB寫入時間 = "";

            try
            {
                using (var conn_oracle = new OracleConnection(conn_str))
                {
                    //===============================
                    // 1. 連線至 HIS
                    //===============================
                    try
                    {
                        MyTimerBasic t1 = new MyTimerBasic();
                        conn_oracle.Open();
                        HIS連線時間 = t1.ToString();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"{BarCode} - {ex.Message}, HIS系統連接失敗");
                        return $"{ex.Message},HIS系統連接失敗!";
                    }

                    //===============================
                    // 2. 解析條碼 → CommandText
                    //===============================
                    string commandText = BuildCommandText(BarCode, out bool flag_術中醫令, out string 領藥號, out string 看診日期);

                    if (commandText.StringIsEmpty())
                    {
                        returnData rd = new returnData()
                        {
                            Code = -200,
                            Result = "BarCode 格式無法解析!"
                        };
                        return rd.JsonSerializationt(true);
                    }

                    //===============================
                    // 3. 執行查詢（強化版，不洩漏 cursor）
                    //===============================
                    List<OrderClass> orderClasses = new List<OrderClass>();

                    using (var cmd = new OracleCommand(commandText, conn_oracle))
                    using (var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection))
                    {
                        MyTimerBasic t_query = new MyTimerBasic();
                        HISData時間 = t_query.ToString();

                        while (true)
                        {
                            bool hasRow = false;

                            //--- 防止 Read() 拋例外造成 Cursor 卡在 HIS
                            try
                            {
                                hasRow = reader.Read();
                            }
                            catch (Exception ex)
                            {
                                return $"HIS系統資料讀取異常 (Read)：{ex.Message}";
                            }

                            if (!hasRow) break;

                            //--- 單筆資料解析（不可拋例外）
                            try
                            {
                                OrderClass orderClass = new OrderClass();

                                //====== 藥袋類型 ======
                                string type = SafeGet(reader, "PAC_TYPE");
                                orderClass.藥袋類型 = type switch
                                {
                                    "E" => "急診",
                                    "S" => "住院ST",
                                    "B" => "住院首日量",
                                    "O" => "門診",
                                    "M" => "出院帶藥",
                                    _ => ""
                                };

                                //====== 基本欄位 ======
                                orderClass.藥袋條碼 = BarCode;
                                orderClass.住院序號 = SafeGet(reader, "PAC_SEQ");
                                orderClass.藥品碼 = SafeGet(reader, "PAC_DIACODE");
                                orderClass.藥品名稱 = SafeGet(reader, "PAC_DIANAME");
                                orderClass.病人姓名 = SafeGet(reader, "PAC_PATNAME");
                                orderClass.病歷號 = SafeGet(reader, "PAC_PATID");
                                orderClass.領藥號 = SafeGet(reader, "PAC_DRUGNO");
                                orderClass.科別 = SafeGet(reader, "PAC_SECTNAME");
                                orderClass.醫師代碼 = SafeGet(reader, "PAC_DOCNAME");
                                orderClass.頻次 = SafeGet(reader, "PAC_FEQNO");
                                orderClass.天數 = SafeGet(reader, "PAC_DAYS");
                                orderClass.單次劑量 = SafeGet(reader, "PAC_QTYPERTIME");
                                orderClass.劑量單位 = SafeGet(reader, "PAC_UNIT");
                                orderClass.費用別 = SafeGet(reader, "PAC_PAYCD") == "Y" ? "自費" : "健保";
                                orderClass.批序 = SafeGet(reader, "PAC_ORDERSEQ");

                                //====== 就醫時間 ======
                                string visit = SafeGet(reader, "PAC_VISITDT");
                                if (visit.Length == 8)
                                    orderClass.就醫時間 = $"{visit[..4]}-{visit[4..6]}-{visit[6..8]}";

                                //====== 開方日期 ======
                                string 時間 = SafeGet(reader, "PAC_PROCDTTM");
                                if (時間.Length == 14)
                                {
                                    orderClass.開方日期 =
                                        $"{時間[..4]}/{時間[4..6]}/{時間[6..8]} " +
                                        $"{時間[8..10]}:{時間[10..12]}:{時間[12..14]}";
                                }

                                //====== 交易量（負值） ======
                                double sumQTY = SafeDouble(reader, "PAC_SUMQTY");
                                orderClass.交易量 = (-sumQTY).ToString();

                                //====== PRI_KEY ======
                                string key = $"{orderClass.頻次}{orderClass.天數}{orderClass.單次劑量}{orderClass.劑量單位}";
                                orderClass.PRI_KEY = $"{時間}-{orderClass.病歷號}-{orderClass.藥品碼}{orderClass.交易量}-{key}";

                                orderClasses.Add(orderClass);
                            }
                            catch (Exception ex)
                            {
                                return $"HIS系統資料解析異常 (Row)：{ex.Message}";
                            }
                        }
                    }

                    //===============================
                    // 4. 無資料處理
                    //===============================
                    if (orderClasses.Count == 0)
                    {
                        returnData rd = new returnData()
                        {
                            Code = -200,
                            TimeTaken = myTimer_total.ToString(),
                            Result = "無此藥袋資料!"
                        };
                        return rd.JsonSerializationt(true);
                    }

                    //===============================
                    // 5. 寫入資料庫
                    //===============================
                    MyTimerBasic t_db = new MyTimerBasic();
                    Logger.Log(orderClasses.JsonSerializationt(true));

                    var returnData_order = OrderClass.update_order_list_new("http://127.0.0.1:4433", orderClasses);
                    DB寫入時間 = t_db.ToString();

                    returnData_order.Value = BarCode;
                    returnData_order.TimeTaken += $"{myTimer_total}";
                    returnData_order.Result += $"，HIS連線時間:{HIS連線時間}，取得HIS資料:{HISData時間}，DB寫入時間:{DB寫入時間}";

                    string json = returnData_order.JsonSerializationt(true);
                    //Logger.Log(json);
                    conn_oracle.Close();
                    return json;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception :{ex.Message} [{BarCode}]");
                if(ex.Message.Contains("ORA-01000"))
                {
                    OracleConnection.ClearAllPools();
                    Logger.Log($" OracleConnection.ClearAllPools() ,清除所有DB連線");
                }
                return $"Exception : {ex.Message}";
            }
        }
        private static string SafeGet(OracleDataReader r, string col)
        {
            try
            {
                return r[col]?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static double SafeDouble(OracleDataReader r, string col)
        {
            try
            {
                double.TryParse(r[col]?.ToString(), out double v);
                return v;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 根據 BarCode 自動建構 SQL CommandText
        /// </summary>
        private string BuildCommandText(string? BarCode,
                                       out bool flag_術中醫令,
                                       out string 領藥號,
                                       out string 看診日期)
        {
            flag_術中醫令 = false;
            領藥號 = "";
            看診日期 = "";

            if (BarCode.StringIsEmpty())
                return "";

            string[] arr = BarCode.Split(';');

            //=====================================================
            // 1️⃣ 條碼為 5 段 → 術中醫令
            //=====================================================
            if (arr.Length == 5)
            {
                string PAC_SEQ = arr[0];
                string PAC_VISITDT = arr[1];
                string PAC_DIACODE = arr[2];
                string PAC_ORDERSEQ = arr[3];

                flag_術中醫令 = true;
                return 術中醫令(PAC_SEQ, PAC_VISITDT, PAC_DIACODE, PAC_ORDERSEQ);
            }

            //=====================================================
            // 2️⃣ 條碼為 1 段 → 一般病歷號查詢 3 日區間
            //=====================================================
            if (arr.Length == 1)
            {
                string 病歷號 = arr[0].PadLeft(10, '0');
                string day1 = DateTime.Now.ToString("yyyyMMdd");
                //string day2 = DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
                //string day3 = DateTime.Now.AddDays(-2).ToString("yyyyMMdd");

                return
                    $"select * from phaadcal " +
                    //$"where (PAC_VISITDT='{day1}' or PAC_VISITDT='{day2}' or PAC_VISITDT='{day3}') " +
                    $"where PAC_VISITDT='{day1}' " +
                    $"and PAC_PATID='{病歷號}'";
            }
            if (arr.Length == 2)
            {
                string 病歷號 = arr[1].PadLeft(10, '0');
                string day = arr[0];

                return
                    $"select * from phaadcal " +
                    $"where PAC_VISITDT='{day}' " +
                    $"and PAC_PATID='{病歷號}'";
            }
            //=====================================================
            // 3️⃣ 條碼為 4 段 → 住院 ST / 首日量 or 管制藥 or 術中醫令
            //=====================================================
            if (arr.Length == 4)
            {
                // 住院首日 or ST 藥袋（第一段長度 = 25）
                if (arr[0].Length == 25)
                {
                    string 住院序號 = arr[0].Substring(0, 10);
                    string 醫令時間 = arr[0].Substring(10, 14);
                    string 醫令類型 = arr[0].Substring(24, 1);
                    醫令類型 = (醫令類型 == "0") ? "S" : "B";

                    return STAT(住院序號, 醫令時間, 醫令類型);
                }
                // 住院UD
                if (arr[2].Length == 10)
                {
                    string PAC_DRUGNO = arr[0]; // 領藥號
                    string PAC_VISITDT = arr[1]; //看診日
                    string year_tw = PAC_VISITDT.Substring(0, 3);
                    PAC_VISITDT = $"{year_tw.StringToInt32() + 1911}{PAC_VISITDT.Substring(3, 4)}";
                    string PAC_PATID = arr[2]; //病歷號
                    string PAC_SEQ = arr[3]; //住院號


                    return Control(PAC_DRUGNO, PAC_VISITDT, PAC_PATID, PAC_SEQ);
                }

                // 一般術中醫令
                {
                    string PAC_SEQ = arr[0];
                    string PAC_VISITDT = arr[1];
                    string PAC_DIACODE = arr[2];
                    string PAC_ORDERSEQ = arr[3];

                    flag_術中醫令 = true;
                    return 術中醫令(PAC_SEQ, PAC_VISITDT, PAC_DIACODE, PAC_ORDERSEQ);
                }
            }

            //=====================================================
            // 4️⃣ 單字串長度 = 25 → 住院序號 + 醫令時間 + 類型
            //=====================================================
            if (BarCode.Length == 25)
            {
                string 住院序號 = BarCode.Substring(0, 10);
                string 醫令時間 = BarCode.Substring(10, 14);
                string 醫令類型 = BarCode.Substring(24, 1);
                醫令類型 = (醫令類型 == "0") ? "S" : "B";

                return STAT(住院序號, 醫令時間, 醫令類型);
            }

            //=====================================================
            // 5️⃣ 條碼 ≥ 9 段 → 急診 / 出院帶藥 / 慢箋
            //=====================================================
            if (arr.Length >= 9)
            {
                // 例如：
                // 出院帶藥: 7104;20251111;0000199092;1140000618;BID;PO;7;0;...
                // 慢箋:    177;20251121;0000161190;1;QD;PO;28;0;...

                看診日期 = arr[(int)enum_急診藥袋.看診日期];
                領藥號 = arr[(int)enum_急診藥袋.本次領藥號];
                string 病歷號 = arr[(int)enum_急診藥袋.病歷號];

                return
                    $"select * from phaadcal " +
                    $"where PAC_VISITDT='{看診日期}' " +
                    $"and PAC_PATID='{病歷號}' " +
                    $"and PAC_DRUGNO='{領藥號}'";
            }

            //=====================================================
            // 無法解析格式
            //=====================================================
            return "";
        }


        [HttpGet(("order_controll_drug"))]
        public string order_controll_drug(string? BarCode)
        {
            MyTimerBasic timerTotal = new MyTimerBasic();
            string HIS連線時間 = "";
            string HISData時間 = "";
            string DB寫入時間 = "";
            bool flag_術中醫令 = false;
            string 領藥號 = "";
            string 看診日期 = "";

            try
            {
                using (var conn_oracle = new OracleConnection(conn_str))
                {
                    //===============================
                    // 1. 連線 HIS
                    //===============================
                    try
                    {
                        MyTimerBasic t = new MyTimerBasic();
                        conn_oracle.Open();
                        HIS連線時間 = t.ToString();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"{BarCode} - {ex.Message}, HIS系統連線失敗");
                        return $"{ex.Message},HIS系統連線失敗";
                    }

                    //===============================
                    // 2. 解析條碼 → 產生 SQL
                    //===============================
                    string commandText = BuildCommandText_order_controll(BarCode,
                                                                         out flag_術中醫令,
                                                                         out 領藥號,
                                                                         out 看診日期);

                    if (commandText.StringIsEmpty())
                    {
                        returnData emptyData = new returnData()
                        {
                            Code = -200,
                            Result = "無法解析條碼格式!"
                        };
                        return emptyData.JsonSerializationt(true);
                    }

                    //===============================
                    // 3. 執行查詢
                    //===============================
                    List<OrderClass> orderClasses = new List<OrderClass>();

                    using (var cmd = new OracleCommand(commandText, conn_oracle))
                    using (var reader = cmd.ExecuteReader())
                    {
                        MyTimerBasic t2 = new MyTimerBasic();
                        HISData時間 = t2.ToString();

                        try
                        {
                            while (reader.Read())
                            {
                                OrderClass o = new OrderClass();

                                string type = reader["PAC_TYPE"].ToString().Trim();
                                o.藥袋類型 = type switch
                                {
                                    "E" => "急診",
                                    "S" => "住院ST",
                                    "B" => "住院首日量",
                                    "O" => "門診",
                                    "M" => "出院帶藥",
                                    _ => ""
                                };

                                o.藥袋條碼 = BarCode;
                                o.住院序號 = reader["PAC_SEQ"].ToString().Trim();
                                o.藥品碼 = reader["PAC_DIACODE"].ToString().Trim();
                                o.藥品名稱 = reader["PAC_DIANAME"].ToString().Trim();
                                o.病人姓名 = reader["PAC_PATNAME"].ToString().Trim();
                                o.病歷號 = reader["PAC_PATID"].ToString().Trim();
                                o.領藥號 = reader["PAC_DRUGNO"].ToString().Trim();

                                string visit = reader["PAC_VISITDT"].ToString().Trim();
                                o.就醫時間 = $"{visit.Substring(0, 4)}-{visit.Substring(4, 2)}-{visit.Substring(6, 2)}";

                                o.科別 = reader["PAC_SECTNAME"].ToString().Trim();
                                o.醫師代碼 = reader["PAC_DOCNAME"].ToString().Trim();
                                o.頻次 = reader["PAC_FEQNO"].ToString().Trim();
                                o.天數 = reader["PAC_DAYS"].ToString().Trim();
                                o.單次劑量 = reader["PAC_QTYPERTIME"].ToString().Trim();
                                o.劑量單位 = reader["PAC_UNIT"].ToString().Trim();
                                o.費用別 = reader["PAC_PAYCD"].ToString().Trim() == "Y" ? "自費" : "健保";
                                o.批序 = reader["PAC_ORDERSEQ"].ToString().Trim();

                                // 開方日期
                                string dttm = reader["PAC_PROCDTTM"].ToString().Trim();
                                if (dttm.Length == 14)
                                {
                                    o.開方日期 =
                                        $"{dttm.Substring(0, 4)}/{dttm.Substring(4, 2)}/{dttm.Substring(6, 2)} " +
                                        $"{dttm.Substring(8, 2)}:{dttm.Substring(10, 2)}:{dttm.Substring(12, 2)}";
                                }

                                // 交易量（負數）
                                double qty = reader["PAC_SUMQTY"].ToString().Trim().StringToDouble();
                                o.交易量 = (qty * -1).ToString();

                                // PRI_KEY
                                string key = $"{o.頻次}{o.天數}{o.單次劑量}{o.劑量單位}";
                                o.PRI_KEY = $"{dttm}-{o.病歷號}-{o.藥品碼}{o.交易量}-{key}";

                                orderClasses.Add(o);
                            }
                        }
                        catch (Exception e1)
                        {
                            Logger.Log($"{BarCode} - HIS資料讀取錯誤 {e1.Message}");
                            return "HIS系統回傳資料異常";
                        }
                    }

                    //===============================
                    // 4. 查無資料
                    //===============================
                    if (orderClasses.Count == 0)
                    {
                        returnData rd = new returnData()
                        {
                            Code = -200,
                            TimeTaken = timerTotal.ToString(),
                            Result = "無此藥袋資料!"
                        };
                        return rd.JsonSerializationt(true);
                    }

                    //===============================
                    // 5. 批序去重
                    //===============================
                    orderClasses = orderClasses
                        .GroupBy(o => o.批序)
                        .Select(g => g.OrderByDescending(x => x.領藥號).First())
                        .ToList();

                    //===============================
                    // 6. 過濾無裝置的藥碼
                    //===============================
                    List<string> code = orderClasses.Select(x => x.藥品碼).Distinct().ToList();
                    if (code.Count == 1) code[0] = code[0] + ",";

                    List<medClass> medClasses =
                        medClass.get_dps_medClass_by_code("http://127.0.0.1:4433", "ADC32抽", code);

                    List<string> validCodes =
                        medClasses.Where(x => x.DeviceBasics.Count > 0)
                                  .Select(x => x.藥品碼)
                                  .Distinct()
                                  .ToList();

                    orderClasses = orderClasses.Where(x => validCodes.Contains(x.藥品碼)).ToList();

                    //===============================
                    // 7. 寫入 DB
                    //===============================
                    MyTimerBasic t3 = new MyTimerBasic();

                    var result =
                        OrderClass.update_order_list_new("http://127.0.0.1:4433", orderClasses);

                    DB寫入時間 = t3.ToString();

                    result.Value = BarCode;
                    result.TimeTaken += $"{timerTotal}";
                    result.Result += $"，HIS連線時間:{HIS連線時間}，取得HIS資料:{HISData時間}，DB寫入時間:{DB寫入時間}";

                    string json = result.JsonSerializationt(true);
                    Logger.Log(json);
                    conn_oracle.Close();
                    return json;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"{BarCode} - Exception: {ex.Message}");
                if (ex.Message.Contains("ORA-01000"))
                {
                    OracleConnection.ClearAllPools();
                    Logger.Log($" OracleConnection.ClearAllPools() ,清除所有DB連線");
                }
                return $"Exception: {ex.Message}";
            }
        }
        /// <summary>
        /// 控制藥品條碼解析：建立 SQL CommandText
        /// </summary>
        private string BuildCommandText_order_controll(string? BarCode,
                                                       out bool flag_術中醫令,
                                                       out string 領藥號,
                                                       out string 看診日期)
        {
            flag_術中醫令 = false;
            領藥號 = "";
            看診日期 = "";

            if (BarCode.StringIsEmpty())
                return "";

            string[] arr = BarCode.Split(';');

            //=====================================================
            // 1️⃣ 五段 → 術中醫令
            //=====================================================
            if (arr.Length == 5)
            {
                string PAC_SEQ = arr[0];
                string PAC_VISITDT = arr[1];
                string PAC_DIACODE = arr[2];
                string PAC_ORDERSEQ = arr[3];

                flag_術中醫令 = true;
                return 術中醫令(PAC_SEQ, PAC_VISITDT, PAC_DIACODE, PAC_ORDERSEQ);
            }

            //=====================================================
            // 2️⃣ 一段 → 病歷號查近三天
            //=====================================================
            if (arr.Length == 1)
            {
                string 病歷號 = arr[0].PadLeft(10, '0');
                string day1 = DateTime.Now.ToString("yyyyMMdd");
                string day2 = DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
                string day3 = DateTime.Now.AddDays(-2).ToString("yyyyMMdd");

                return
                    $"select * from phaadcal " +
                    $"where (PAC_VISITDT='{day1}' or PAC_VISITDT='{day2}' or PAC_VISITDT='{day3}') " +
                    $"and PAC_PATID='{病歷號}'";
            }

            //=====================================================
            // 3️⃣ 兩段 → 日期 + 病歷號 (此 API 特別規則)
            //=====================================================
            if (arr.Length == 2)
            {
                string 日期 = arr[0];
                string 病歷號 = arr[1].PadLeft(10, '0');

                看診日期 = 日期;

                return
                    $"select * from phaadcal " +
                    $"where PAC_VISITDT='{日期}' and PAC_PATID='{病歷號}'";
            }

            //=====================================================
            // 4️⃣ 四段 → 住院首日/ST 或 管制藥 或 術中
            //=====================================================
            if (arr.Length == 4)
            {
                // 住院 25 字首日量/ST
                if (arr[0].Length == 25)
                {
                    string 住院序號 = arr[0].Substring(0, 10);
                    string 醫令時間 = arr[0].Substring(10, 14);
                    string 類型代碼 = arr[0].Substring(24, 1);
                    類型代碼 = (類型代碼 == "0") ? "S" : "B";

                    return STAT(住院序號, 醫令時間, 類型代碼);
                }

                // 管制藥（病歷號為 10 碼）
                if (arr[2].Length == 10)
                {
                    string PAC_DRUGNO = arr[0]; // 領藥號
                    string PAC_VISITDT = arr[1]; //看診日
                    string year_tw = PAC_VISITDT.Substring(0, 3);
                    PAC_VISITDT = $"{year_tw.StringToInt32() + 1911}{PAC_VISITDT.Substring(3, 4)}";
                    string PAC_PATID = arr[2]; //病歷號
                    string PAC_SEQ = arr[3]; //住院號


                    return Control(PAC_DRUGNO, PAC_VISITDT, PAC_PATID, PAC_SEQ);
                }

                // 一般術中醫令
                {
                    string PAC_SEQ = arr[0];
                    string PAC_VISITDT = arr[1];
                    string PAC_DIACODE = arr[2];
                    string PAC_ORDERSEQ = arr[3];

                    flag_術中醫令 = true;
                    return 術中醫令(PAC_SEQ, PAC_VISITDT, PAC_DIACODE, PAC_ORDERSEQ);
                }
            }

            //=====================================================
            // 5️⃣ 單字串長度＝25 → 住院 ST/首日
            //=====================================================
            if (BarCode.Length == 25)
            {
                string 住院序號 = BarCode.Substring(0, 10);
                string 醫令時間 = BarCode.Substring(10, 14);
                string 類型 = BarCode.Substring(24, 1);
                類型 = (類型 == "0") ? "S" : "B";

                return STAT(住院序號, 醫令時間, 類型);
            }

            //=====================================================
            // 6️⃣ 九段以上 → 急診 / 出院帶藥 / 慢箋
            //=====================================================
            if (arr.Length >= 9)
            {
                看診日期 = arr[(int)enum_急診藥袋.看診日期];
                string 本次領藥號 = arr[(int)enum_急診藥袋.本次領藥號];
                string 病歷號 = arr[(int)enum_急診藥袋.病歷號];
                string 序號 = arr[(int)enum_急診藥袋.序號];

                領藥號 = 本次領藥號;

                // 此版本你用的是：PAC_SEQ + DRUGNO + PATID + VISITDT
                return
                    $"select * from phaadcal " +
                    $"where PAC_VISITDT='{看診日期}' " +
                    $"and PAC_PATID='{病歷號}' " +
                    $"and PAC_SEQ='{序號}' " +
                    $"and PAC_DRUGNO='{本次領藥號}'";
            }

            //=====================================================
            // 7️⃣ 其他格式 → 無法匹配
            //=====================================================
            return "";
        }



        [Route("order_update")]
        [HttpGet]
        public string GET_order_update(DateTime datetime)
        {
            string conn_str = "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.24.211)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=SISDCP)));User ID=tphphaadc;Password=tph@phaadc2860;";
            OracleConnection conn_oracle;
            OracleDataReader reader;
            OracleCommand cmd;
            try
            {
                try
                {
                    conn_oracle = new OracleConnection(conn_str);
                    conn_oracle.Open();
                }
                catch
                {
                    return "HIS系統連結失敗!";
                }

                returnData returnData = new returnData();
                MyTimerBasic myTimerBasic = new MyTimerBasic();
                string commandText = "";
                List<OrderClass> orderClasses = new List<OrderClass>();
                List<object[]> list_value_Add = new List<object[]>();
                List<object[]> list_value_replace = new List<object[]>();
                string[] strArray_Barcode = new string[0];


                commandText = "";
                commandText += "select ";
                commandText += "min(PAC_VISITDT) PAC_VISITDT,";
                commandText += "sum(PAC_SUMQTY) PAC_SUMQTY,";
                commandText += "PAC_ORDERSEQ,"; //醫令序號
                commandText += "PAC_SEQ,"; //序號
                commandText += "PAC_DIACODE,"; //藥品院內碼
                commandText += "PAC_DIANAME,"; //藥品商品名稱
                commandText += "PAC_PATNAME,"; //病歷姓名
                commandText += "PAC_PATID,"; //病歷號
                commandText += "PAC_UNIT,"; //小單位
                commandText += "PAC_QTYPERTIME,"; //次劑量
                commandText += "PAC_FEQNO,"; //頻率
                commandText += "PAC_PATHNO,"; //途徑
                commandText += "PAC_DAYS,"; //使用天數
                commandText += "PAC_TYPE,"; // 醫令類型
                commandText += "PAC_DRUGNO,"; //領藥號
                commandText += "PAC_SECTNAME,"; //科別
                commandText += "PAC_DOCNAME,"; //醫師代碼
                commandText += "PAC_PROCDTTM,"; //醫令開立時間
                commandText += "PAC_PAYCD,"; //費用別
                commandText += "PAC_ICDX1,"; //診斷碼1
                commandText += "PAC_ICDX2,"; //診斷碼2
                commandText += "PAC_ICDX3,"; //診斷碼3
                commandText += "PAC_SEX,"; //性別 
                //commandText += "PAC_AGE,"; //年齡 
                commandText += "PAC_DRUGGIST "; //藥師代碼
                commandText += $"from phaadcal where SUBSTR(PAC_PROCDTTM, 1, 8) = '{datetime.Year}{datetime.Month.ToString("00")}{datetime.Day.ToString("00")}' ";
                commandText += "GROUP BY ";
                commandText += "PAC_ORDERSEQ,"; //醫令序號
                commandText += "PAC_SEQ,"; //序號
                commandText += "PAC_DIACODE,"; //藥品院內碼
                commandText += "PAC_DIANAME,"; //藥品商品名稱
                commandText += "PAC_PATNAME,"; //病歷姓名
                commandText += "PAC_PATID,"; //病歷號
                commandText += "PAC_UNIT,"; //小單位
                commandText += "PAC_QTYPERTIME,"; //次劑量
                commandText += "PAC_FEQNO,"; //頻率
                commandText += "PAC_PATHNO,"; //途徑
                commandText += "PAC_DAYS,"; //使用天數
                commandText += "PAC_TYPE,"; // 醫令類型
                commandText += "PAC_DRUGNO,"; //領藥號
                commandText += "PAC_SECTNAME,"; //科別
                commandText += "PAC_DOCNAME,"; //醫師代碼
                commandText += "PAC_PROCDTTM,"; //醫令開立時間
                commandText += "PAC_PAYCD,"; //費用別
                commandText += "PAC_ICDX1,"; //診斷碼1
                commandText += "PAC_ICDX2,"; //診斷碼2
                commandText += "PAC_ICDX3,"; //診斷碼3
                commandText += "PAC_SEX,"; //性別 
                //commandText += "PAC_AGE,"; //年齡 
                commandText += "PAC_DRUGGIST "; //藥師代碼
                commandText += " ORDER BY PAC_PROCDTTM DESC ";
                commandText += " FETCH FIRST 300 ROWS ONLY ";
                cmd = new OracleCommand(commandText, conn_oracle);
                try
                {
                    reader = cmd.ExecuteReader();


                    try
                    {
                        while (reader.Read())
                        {
                            try
                            {
                                OrderClass orderClass = new OrderClass();

                                string Time = reader["PAC_PROCDTTM"].ToString().Trim();
                                if (Time.Length == 14)
                                {
                                    string Year = Time.Substring(0, 4);
                                    string Month = Time.Substring(4, 2);
                                    string Day = Time.Substring(6, 2);
                                    string Hour = Time.Substring(8, 2);
                                    string Min = Time.Substring(10, 2);
                                    string Sec = Time.Substring(12, 2);
                                    if ($"{Year}/{Month}/{Day}" != $"{datetime.Year}/{datetime.Month.ToString("00")}/{datetime.Day.ToString("00")}")
                                    {
                                        continue;
                                    }
                                    orderClass.開方日期 = $"{Year}/{Month}/{Day} {Hour}:{Min}:{Sec}";
                                }

                                string type = reader["PAC_TYPE"].ToString().Trim();
                                string PAC_ORDERSEQ = reader["PAC_ORDERSEQ"].ToString().Trim();

                                if (type == "E") orderClass.藥袋類型 = "PHER";
                                if (type == "S") orderClass.藥袋類型 = "STAT";
                                if (type == "B") orderClass.藥袋類型 = "首日量";
                                if (type == "O") orderClass.藥袋類型 = "OPD";
                                if (type == "M") orderClass.藥袋類型 = "出院帶藥";

                                //orderClass. = reader["PAC_SEQ"].ToString().Trim();
                                orderClass.PRI_KEY = $"{reader["PAC_ORDERSEQ"].ToString().Trim()}-{reader["PAC_DRUGNO"].ToString().Trim()}";
                                orderClass.藥袋條碼 = $"{reader["PAC_VISITDT"].ToString().Trim()}{reader["PAC_PATID"].ToString().Trim()}{reader["PAC_SEQ"].ToString().Trim()}";
                                orderClass.藥品碼 = reader["PAC_DIACODE"].ToString().Trim();
                                orderClass.藥品名稱 = reader["PAC_DIANAME"].ToString().Trim();
                                orderClass.病人姓名 = reader["PAC_PATNAME"].ToString().Trim();
                                orderClass.病歷號 = reader["PAC_PATID"].ToString().Trim();
                                orderClass.領藥號 = reader["PAC_DRUGNO"].ToString().Trim();
                                orderClass.產出時間 = DateTime.Now.ToDateTimeString_6();
                                string PAC_QTYPERTIME = reader["PAC_QTYPERTIME"].ToString().Trim();
                                string PAC_SUMQTY = reader["PAC_SUMQTY"].ToString().Trim();
                                double sumQTY = PAC_SUMQTY.StringToDouble();
                                //sumQTY = Math.Ceiling(sumQTY);
                                orderClass.交易量 = (sumQTY * -1).ToString();

                                orderClasses.Add(orderClass);
                            }
                            catch
                            {

                            }




                        }
                    }
                    catch
                    {
                        return "HIS系統回傳資料異常!";
                    }
                }
                catch
                {
                    return "HIS系統命令下達失敗!";
                }
                conn_oracle.Close();
                conn_oracle.Dispose();



                List<List<OrderClass>> list_orderclasses = GroupOrders(orderClasses);
                for (int i = 0; i < list_orderclasses.Count; i++)
                {
                    double Truncate;
                    List<OrderClass> temp_orderclasses = list_orderclasses[i];
                    double 總量 = 0.0D;
                    if (temp_orderclasses[0].藥品碼 == "OMOD")
                    {

                    }
                    for (int k = 0; k < temp_orderclasses.Count; k++)
                    {
                        總量 += temp_orderclasses[k].交易量.StringToDouble();
                    }
                    Truncate = 總量 - Math.Truncate(總量);
                    if (Truncate != 0) 總量 = (int)總量 - 1;
                    for (int k = 0; k < temp_orderclasses.Count; k++)
                    {
                        double 交易量 = temp_orderclasses[k].交易量.StringToDouble();
                        Truncate = 交易量 - Math.Truncate(交易量);
                        if (Truncate != 0) 交易量 = (int)交易量 - 1;
                        if (總量 - 交易量 <= 0)
                        {
                            temp_orderclasses[k].交易量 = 交易量.ToString();
                        }
                        else
                        {
                            temp_orderclasses[k].交易量 = 總量.ToString();
                        }
                        總量 = 總量 - 交易量;
                    }
                }
                string 病歷號 = orderClasses[0].病歷號;
                DateTime datetime_st = new DateTime(datetime.Year, datetime.Month, datetime.Day, 00, 00, 00);
                DateTime datetime_end = new DateTime(datetime.Year, datetime.Month, datetime.Day, 23, 59, 59);
                List<object[]> list_value = this.sQLControl_醫囑資料.GetRowsByBetween(null, enum_醫囑資料.開方日期.GetEnumName(), datetime_st.ToDateTimeString(), datetime_end.ToDateTimeString());

                List<object[]> list_value_buf = new List<object[]>();
                for (int i = 0; i < orderClasses.Count; i++)
                {
                    list_value_buf = list_value.GetRows((int)enum_醫囑資料.PRI_KEY, orderClasses[i].PRI_KEY);

                    if (list_value_buf.Count == 0)
                    {
                        object[] value = new object[new enum_醫囑資料().GetLength()];
                        value[(int)enum_醫囑資料.GUID] = Guid.NewGuid().ToString();
                        orderClasses[i].GUID = value[(int)enum_醫囑資料.GUID].ObjectToString();
                        orderClasses[i].狀態 = value[(int)enum_醫囑資料.狀態].ObjectToString();
                        value[(int)enum_醫囑資料.PRI_KEY] = orderClasses[i].PRI_KEY;
                        value[(int)enum_醫囑資料.藥局代碼] = orderClasses[i].藥局代碼;
                        value[(int)enum_醫囑資料.領藥號] = orderClasses[i].領藥號;
                        value[(int)enum_醫囑資料.藥品碼] = orderClasses[i].藥品碼;
                        value[(int)enum_醫囑資料.藥品名稱] = orderClasses[i].藥品名稱;
                        value[(int)enum_醫囑資料.病歷號] = orderClasses[i].病歷號;
                        value[(int)enum_醫囑資料.藥袋條碼] = orderClasses[i].藥袋條碼;
                        value[(int)enum_醫囑資料.病人姓名] = orderClasses[i].病人姓名;
                        value[(int)enum_醫囑資料.交易量] = orderClasses[i].交易量;
                        value[(int)enum_醫囑資料.開方日期] = orderClasses[i].開方日期;
                        value[(int)enum_醫囑資料.產出時間] = DateTime.Now.ToDateTimeString_6();
                        value[(int)enum_醫囑資料.過帳時間] = DateTime.MinValue.ToDateTimeString_6();
                        value[(int)enum_醫囑資料.狀態] = "未過帳";
                        list_value_Add.Add(value);
                    }

                }
                Task task = Task.Run(() =>
                {
                    if (list_value_Add.Count > 0)
                    {
                        this.sQLControl_醫囑資料.AddRows(null, list_value_Add);
                    }
                    if (list_value_replace.Count > 0)
                    {
                        this.sQLControl_醫囑資料.UpdateByDefulteExtra(null, list_value_replace);
                    }
                });
                task.Wait();
                returnData.Code = 200;
                returnData.Data = "";
                returnData.TimeTaken = myTimerBasic.ToString();
                returnData.Result = $"取得醫囑完成! 共<{orderClasses.Count}>筆 ,新增<{list_value_Add.Count}>筆,修改<{list_value_replace.Count}>筆";

                return returnData.JsonSerializationt(true);
            }
            catch (Exception e)
            {
                return $"醫令串接異常,msg:{e.Message}";
            }
        }
        [Route("get_by_BagNum_date")]
        [HttpPost]
        public string get_by_BagNum_date([FromBody] returnData returnData)
        {
            if (returnData.ValueAry == null)
            {
                returnData.Code = -200;
                returnData.Result = $"returnData.ValueAry 無傳入資料";
                return returnData.JsonSerializationt(true);
            }
            if (returnData.ValueAry.Count != 2)
            {
                returnData.Code = -200;
                returnData.Result = $"returnData.ValueAry 內容應為[領藥號,日期]";
                return returnData.JsonSerializationt(true);
            }
            bool flag_術中醫令 = false;
            OracleConnection conn_oracle;
            OracleDataReader reader;
            OracleCommand cmd;
            try
            {

                try
                {
                    conn_oracle = new OracleConnection(conn_str);
                    conn_oracle.Open();
                }
                catch
                {
                    return "HIS系統連結失敗!";
                }
                string PAC_ORDERSEQ = "";
                //returnData returnData = new returnData();
                MyTimerBasic myTimerBasic = new MyTimerBasic();
                string commandText = "";
                List<OrderClass> orderClasses = new List<OrderClass>();
                List<object[]> list_value_Add = new List<object[]>();
                List<object[]> list_value_replace = new List<object[]>();
                string[] strArray_Barcode = new string[0];

                string 本次領藥號 = returnData.ValueAry[0];
                string 開方時間 = returnData.ValueAry[1];
                DateTime datetime = DateTime.Parse(開方時間);
                string formattedDate = datetime.ToString("yyyyMMdd");


                //commandText = "";
                //commandText += "select ";
                //commandText += "min(PAC_VISITDT) PAC_VISITDT,";
                //commandText += "sum(PAC_SUMQTY) PAC_SUMQTY,";
                //commandText += "PAC_ORDERSEQ,";
                //commandText += "PAC_SEQ,";
                //commandText += "PAC_DIACODE,";
                //commandText += "PAC_DIANAME,";
                //commandText += "PAC_PATNAME,";
                //commandText += "PAC_PATID,";
                //commandText += "PAC_UNIT,";
                //commandText += "PAC_QTYPERTIME,";
                //commandText += "PAC_FEQNO,";
                //commandText += "PAC_PATHNO,";
                //commandText += "PAC_DAYS,";
                //commandText += "PAC_TYPE,";
                //commandText += "PAC_DRUGNO,";
                //commandText += "PAC_SECTNAME,";
                //commandText += "PAC_DOCNAME,";
                //commandText += "PAC_PROCDTTM ,";
                //commandText += "PAC_VISITDT ,";
                //commandText += "PAC_DRUGGIST ";
                ////commandText += $"from phaadcal where PAC_DRUGNO={本次領藥號}  and SUBSTR(PAC_PROCDTTM, 1, 8) = '{datetime.Year}{datetime.Month.ToString("00")}{datetime.Day.ToString("00")}' ";
                //commandText += $"from phaadcal where PAC_DRUGNO={本次領藥號}";

                //commandText += "GROUP BY ";

                //commandText += "PAC_ORDERSEQ,";
                //commandText += "PAC_SEQ,";
                //commandText += "PAC_DIACODE,";
                //commandText += "PAC_DIANAME,";
                //commandText += "PAC_PATNAME,";
                //commandText += "PAC_PATID,";
                //commandText += "PAC_UNIT,";
                //commandText += "PAC_QTYPERTIME,";
                //commandText += "PAC_FEQNO,";
                //commandText += "PAC_PATHNO,";
                //commandText += "PAC_DAYS,";
                //commandText += "PAC_TYPE,";
                //commandText += "PAC_DRUGNO,";
                //commandText += "PAC_SECTNAME,";
                //commandText += "PAC_DOCNAME,";
                //commandText += "PAC_PROCDTTM ,";
                //commandText += "PAC_VISITDT ,";
                //commandText += "PAC_DRUGGIST ";
                string date_str = $"{datetime.Year}{datetime.Month.ToString("00")}{datetime.Day.ToString("00")}";
                commandText = "";
                commandText += "select ";
                commandText += "min(PAC_VISITDT) PAC_VISITDT,";
                commandText += "sum(PAC_SUMQTY) PAC_SUMQTY,";
                commandText += "PAC_ORDERSEQ,";
                commandText += "PAC_SEQ,";
                commandText += "PAC_DIACODE,";
                commandText += "PAC_DIANAME,";
                commandText += "PAC_PATNAME,";
                commandText += "PAC_PATID,";
                commandText += "PAC_UNIT,";
                commandText += "PAC_QTYPERTIME,";
                commandText += "PAC_FEQNO,";
                commandText += "PAC_PATHNO,";
                commandText += "PAC_DAYS,";
                commandText += "PAC_TYPE,";
                commandText += "PAC_DRUGNO,";
                commandText += "PAC_SECTNAME,";
                commandText += "PAC_DOCNAME,";
                commandText += "PAC_PROCDTTM ,";
                commandText += "PAC_VISITDT ,";
                commandText += "PAC_DRUGGIST ";
                commandText += $"from phaadcal where PAC_DRUGNO={本次領藥號} and PAC_VISITDT = {date_str}   ";
                commandText += "GROUP BY ";

                commandText += "PAC_ORDERSEQ,";
                commandText += "PAC_SEQ,";
                commandText += "PAC_DIACODE,";
                commandText += "PAC_DIANAME,";
                commandText += "PAC_PATNAME,";
                commandText += "PAC_PATID,";
                commandText += "PAC_UNIT,";
                commandText += "PAC_QTYPERTIME,";
                commandText += "PAC_FEQNO,";
                commandText += "PAC_PATHNO,";
                commandText += "PAC_DAYS,";
                commandText += "PAC_TYPE,";
                commandText += "PAC_DRUGNO,";
                commandText += "PAC_SECTNAME,";
                commandText += "PAC_DOCNAME,";
                commandText += "PAC_PROCDTTM ,";
                commandText += "PAC_VISITDT ,";
                commandText += "PAC_DRUGGIST ";


                cmd = new OracleCommand(commandText, conn_oracle);
                List<object[]> list_temp = new List<object[]>();
                string queryTime = "";
                try
                {
                    MyTimerBasic myTimerBasic_query = new MyTimerBasic();

                    reader = cmd.ExecuteReader();
                    queryTime = myTimerBasic_query.ToString();
                    List<string> list_colname = new List<string>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string colname = reader.GetName(i);
                        list_colname.Add(colname);
                    }

                    try
                    {
                        List<personPageClass> personPageClasses = personPageClass.get_all(API_Server);

                        while (reader.Read())
                        {
                            List<object> value = new List<object>();
                            for (int i = 0; i < list_colname.Count; i++)
                            {
                                value.Add(reader[list_colname[i]]);

                            }
                            list_temp.Add(value.ToArray());
                            OrderClass orderClass = new OrderClass();
                            string type = reader["PAC_TYPE"].ToString().Trim();
                            if (type == "E") orderClass.藥局代碼 = "PHER";
                            if (type == "S") orderClass.藥局代碼 = "STAT";
                            if (type == "B") orderClass.藥局代碼 = "首日量";
                            if (type == "O") orderClass.藥局代碼 = "OPD";
                            if (type == "M") orderClass.藥局代碼 = "出院帶藥";

                            orderClass.PRI_KEY = $"{reader["PAC_ORDERSEQ"].ToString().Trim()}-{reader["PAC_DRUGNO"].ToString().Trim()}";
                            orderClass.藥袋條碼 = $"{reader["PAC_VISITDT"].ToString().Trim()}{reader["PAC_PATID"].ToString().Trim()}{reader["PAC_SEQ"].ToString().Trim()}";
                            orderClass.住院序號 = reader["PAC_SEQ"].ToString().Trim();
                            orderClass.藥品碼 = reader["PAC_DIACODE"].ToString().Trim();
                            orderClass.藥品名稱 = reader["PAC_DIANAME"].ToString().Trim();
                            orderClass.病人姓名 = reader["PAC_PATNAME"].ToString().Trim();
                            orderClass.病歷號 = reader["PAC_PATID"].ToString().Trim();
                            orderClass.領藥號 = reader["PAC_DRUGNO"].ToString().Trim();

                            orderClass.就醫時間 = reader["PAC_VISITDT"].ToString().Trim();
                            orderClass.就醫時間 = $"{orderClass.就醫時間.Substring(0, 4)}-{orderClass.就醫時間.Substring(4, 2)}-{orderClass.就醫時間.Substring(6, 2)}";
                            orderClass.科別 = reader["PAC_SECTNAME"].ToString().Trim();
                            orderClass.醫師代碼 = reader["PAC_DOCNAME"].ToString().Trim();


                            if (strArray_Barcode.Length >= 9)
                            {
                                string PAC_DRUGGIST = reader["PAC_DRUGGIST"].ToString().Trim();
                                orderClass.藥師ID = PAC_DRUGGIST.StringToInt32().ToString();
                                List<personPageClass> personPageClasses_buf = (from temp in personPageClasses
                                                                               where temp.ID == orderClass.藥師ID
                                                                               select temp).ToList();
                                if (personPageClasses_buf.Count > 0)
                                {
                                    orderClass.藥師姓名 = personPageClasses_buf[0].姓名;
                                }
                            }


                            string PAC_QTYPERTIME = reader["PAC_QTYPERTIME"].ToString().Trim();
                            string PAC_SUMQTY = reader["PAC_SUMQTY"].ToString().Trim();
                            double sumQTY = PAC_SUMQTY.StringToDouble();
                            //sumQTY = Math.Ceiling(sumQTY);
                            orderClass.交易量 = (sumQTY * -1).ToString();
                            string Time = reader["PAC_PROCDTTM"].ToString().Trim();
                            if (Time.Length == 14)
                            {
                                string Year = Time.Substring(0, 4);
                                string Month = Time.Substring(4, 2);
                                string Day = Time.Substring(6, 2);
                                string Hour = Time.Substring(8, 2);
                                string Min = Time.Substring(10, 2);
                                string Sec = Time.Substring(12, 2);
                                orderClass.開方日期 = $"{Year}/{Month}/{Day} {Hour}:{Min}:{Sec}";
                            }
                            orderClasses.Add(orderClass);

                        }
                    }
                    catch
                    {
                        return "HIS系統回傳資料異常!";
                    }
                }
                catch
                {
                    return "HIS系統命令下達失敗!";
                }
                conn_oracle.Close();
                conn_oracle.Dispose();

                if (orderClasses.Count == 0)
                {
                    returnData.Code = -200;
                    returnData.TimeTaken = myTimerBasic.ToString();
                    returnData.Result = $"無此藥袋資料!";

                    return returnData.JsonSerializationt(true);
                }

                List<List<OrderClass>> list_orderclasses = GroupOrders(orderClasses);
                for (int i = 0; i < list_orderclasses.Count; i++)
                {
                    double Truncate;
                    List<OrderClass> temp_orderclasses = list_orderclasses[i];
                    double 總量 = 0.0D;
                    for (int k = 0; k < temp_orderclasses.Count; k++)
                    {

                        總量 += temp_orderclasses[k].交易量.StringToDouble();

                    }
                    Truncate = 總量 - Math.Truncate(總量);
                    if (Truncate != 0) 總量 = (int)總量 - 1;
                    bool 總量已到達 = false;
                    for (int k = 0; k < temp_orderclasses.Count; k++)
                    {


                        double 交易量 = temp_orderclasses[k].交易量.StringToDouble();
                        Truncate = 交易量 - Math.Truncate(交易量);
                        if (Truncate != 0) 交易量 = (int)交易量 - 1;
                        if (總量已到達)
                        {
                            temp_orderclasses[k].交易量 = "0";
                            continue;
                        }
                        if (總量 - 交易量 <= 0)
                        {
                            temp_orderclasses[k].交易量 = 交易量.ToString();
                        }
                        else
                        {
                            temp_orderclasses[k].交易量 = 總量.ToString();
                            總量已到達 = true;
                        }
                        總量 = 總量 - 交易量;
                    }
                }
                List<KeyValuePair<string, int>> groupedByDate = orderClasses
                    .GroupBy(order => order.就醫時間)
                    .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
                    .ToList();

                // 輸出分類結果
                //Logger.Log("BBAR", $"條碼: {BarCode}");

                foreach (KeyValuePair<string, int> item in groupedByDate)
                {
                    Logger.Log("BBAR", $"日期: {item.Key}, 筆數: {item.Value}");
                }
                string 病歷號 = orderClasses[0].病歷號;
                string Today = DateTime.Now.ToString("yyyy-MM-dd");
                string tenDaysAgo = DateTime.Now.AddDays(-0).ToString("yyyy-MM-dd");
                //orderClasses = orderClasses.Where(temp => string.Compare(temp.就醫時間, tenDaysAgo) >= 0 && string.Compare(temp.就醫時間, Today) <= 0).ToList();
                orderClasses = orderClasses.Where(temp => string.Compare(temp.就醫時間, tenDaysAgo) >= 0).ToList();


                List<object[]> list_value = this.sQLControl_醫囑資料.GetRowsByDefult(null, enum_醫囑資料.病歷號.GetEnumName(), 病歷號);
                List<object[]> list_value_buf = new List<object[]>();
                for (int i = 0; i < orderClasses.Count; i++)
                {
                    list_value_buf = list_value.GetRows((int)enum_醫囑資料.PRI_KEY, orderClasses[i].PRI_KEY);
                    if (list_value_buf.Count == 0)
                    {
                        object[] value = new object[new enum_醫囑資料().GetLength()];
                        value[(int)enum_醫囑資料.GUID] = Guid.NewGuid().ToString();
                        orderClasses[i].GUID = value[(int)enum_醫囑資料.GUID].ObjectToString();
                        orderClasses[i].狀態 = value[(int)enum_醫囑資料.狀態].ObjectToString();
                        value[(int)enum_醫囑資料.PRI_KEY] = orderClasses[i].PRI_KEY;
                        value[(int)enum_醫囑資料.藥局代碼] = orderClasses[i].藥局代碼;
                        value[(int)enum_醫囑資料.領藥號] = orderClasses[i].領藥號;
                        value[(int)enum_醫囑資料.藥品碼] = orderClasses[i].藥品碼;
                        value[(int)enum_醫囑資料.藥品名稱] = orderClasses[i].藥品名稱;
                        value[(int)enum_醫囑資料.病歷號] = orderClasses[i].病歷號;
                        value[(int)enum_醫囑資料.藥袋條碼] = orderClasses[i].藥袋條碼;
                        value[(int)enum_醫囑資料.病人姓名] = orderClasses[i].病人姓名;
                        value[(int)enum_醫囑資料.交易量] = orderClasses[i].交易量;
                        value[(int)enum_醫囑資料.住院序號] = orderClasses[i].住院序號;
                        value[(int)enum_醫囑資料.醫師代碼] = orderClasses[i].醫師代碼;
                        value[(int)enum_醫囑資料.科別] = orderClasses[i].科別;
                        value[(int)enum_醫囑資料.就醫時間] = orderClasses[i].就醫時間;
                        value[(int)enum_醫囑資料.藥師姓名] = orderClasses[i].藥師姓名;
                        value[(int)enum_醫囑資料.產出時間] = DateTime.Now.ToDateTimeString_6();
                        value[(int)enum_醫囑資料.過帳時間] = DateTime.MinValue.ToDateTimeString_6();
                        value[(int)enum_醫囑資料.狀態] = "未過帳";
                        list_value_Add.Add(value);
                    }
                    else
                    {
                        object[] value = list_value_buf[0];
                        orderClasses[i].GUID = value[(int)enum_醫囑資料.GUID].ObjectToString();
                        orderClasses[i].狀態 = value[(int)enum_醫囑資料.狀態].ObjectToString();
                        object[] value_src = orderClasses[i].ClassToSQL<OrderClass, enum_醫囑資料>();
                        bool flag_replace = false;
                        string src_開方日期 = value_src[(int)enum_醫囑資料.開方日期].ToDateTimeString();
                        if (src_開方日期.StringIsEmpty()) src_開方日期 = value_src[(int)enum_醫囑資料.開方日期].ObjectToString();
                        string dst_開方日期 = value[(int)enum_醫囑資料.開方日期].ToDateTimeString();
                        if (dst_開方日期.StringIsEmpty()) dst_開方日期 = value[(int)enum_醫囑資料.開方日期].ObjectToString();
                        if (src_開方日期 != dst_開方日期)
                        {
                            flag_replace = true;
                        }

                        if (flag_replace)
                        {

                            value[(int)enum_醫囑資料.PRI_KEY] = orderClasses[i].PRI_KEY;
                            value[(int)enum_醫囑資料.藥局代碼] = orderClasses[i].藥局代碼;
                            value[(int)enum_醫囑資料.領藥號] = orderClasses[i].領藥號;
                            value[(int)enum_醫囑資料.住院序號] = orderClasses[i].住院序號;
                            value[(int)enum_醫囑資料.藥品碼] = orderClasses[i].藥品碼;
                            value[(int)enum_醫囑資料.藥品名稱] = orderClasses[i].藥品名稱;
                            value[(int)enum_醫囑資料.病歷號] = orderClasses[i].病歷號;
                            value[(int)enum_醫囑資料.藥袋條碼] = orderClasses[i].藥袋條碼;
                            value[(int)enum_醫囑資料.病人姓名] = orderClasses[i].病人姓名;
                            value[(int)enum_醫囑資料.交易量] = orderClasses[i].交易量;
                            value[(int)enum_醫囑資料.醫師代碼] = orderClasses[i].醫師代碼;
                            value[(int)enum_醫囑資料.科別] = orderClasses[i].科別;
                            value[(int)enum_醫囑資料.就醫時間] = orderClasses[i].就醫時間;
                            value[(int)enum_醫囑資料.開方日期] = orderClasses[i].開方日期;
                            value[(int)enum_醫囑資料.藥師姓名] = orderClasses[i].藥師姓名;

                            value[(int)enum_醫囑資料.狀態] = "未過帳";
                            orderClasses[i].狀態 = "未過帳";
                            list_value_replace.Add(value);
                        }



                    }
                }
                Task task = Task.Run(() =>
                {
                    if (list_value_Add.Count > 0)
                    {
                        this.sQLControl_醫囑資料.AddRows(null, list_value_Add);
                    }
                    if (list_value_replace.Count > 0)
                    {
                        this.sQLControl_醫囑資料.UpdateByDefulteExtra(null, list_value_replace);
                    }
                });

                returnData.Code = 200;
                returnData.Data = orderClasses;
                returnData.TimeTaken = myTimerBasic.ToString();
                returnData.Result = $"取得醫囑完成! 共<{orderClasses.Count}>筆 ,新增<{list_value_Add.Count}>筆,修改<{list_value_replace.Count}>筆 , 從DB取得時間:{queryTime}";
                string json_result = returnData.JsonSerializationt(true);
                Logger.Log("BBAR", json_result);
                return json_result;
            }
            catch
            {
                return "醫令串接異常";
            }

        }
        [HttpGet("get_DBdata")]
        public string get_DBdata(string? commandText)
        {
            returnData returnData = new returnData();
            MyTimerBasic myTimerBasic = new MyTimerBasic();
            try
            {
                OracleConnection conn_oracle;
                OracleDataReader reader;
                OracleCommand cmd;
                try
                {
                    conn_oracle = new OracleConnection(conn_str);
                    conn_oracle.Open();
                    Logger.Log("conn_oracle", $"與HIS建立連線");
                }
                catch
                {
                    return "HIS系統連線失敗";
                }
                cmd = new OracleCommand(commandText, conn_oracle);
                Logger.Log("conn_oracle", $"與HIS擷取資料開始");
                reader = cmd.ExecuteReader();
                Logger.Log("conn_oracle", $"與HIS擷取資料結束");
                returnData.Code = 200;
                returnData.TimeTaken = $"{myTimerBasic}";
                returnData.Result = "成功取得資料";
                return returnData.JsonSerializationt(true);
            }
            catch (Exception ex)
            {
                returnData.Code = -200;
                returnData.Result = ex.Message;
                return returnData.JsonSerializationt(true);

            }
        }

        [HttpGet("pragnant")]
        public string pragnant(string? ID)
        {
            returnData returnData = new returnData();
            OracleConnection conn_oracle = new OracleConnection(conn_str);
            try
            {
                conn_oracle = new OracleConnection(conn_str);
                conn_oracle.Open();
            }
            catch
            {
                returnData.Code = -200;
                returnData.Result = "HIS系統連結失敗!";
                return returnData.JsonSerializationt(true);
            }
            finally
            {
                conn_oracle.Close();
                conn_oracle.Dispose();
            }
            return pragnant(ID, conn_oracle);
        }
        private string pragnant(string? ID, OracleConnection conn_oracle)
        {
            OracleDataReader reader;
            OracleCommand cmd;
            returnData returnData = new returnData();
            List<object[]> list_value = new List<object[]>();

            try
            {
                try
                {
                    conn_oracle = new OracleConnection(conn_str);
                    conn_oracle.Open();
                }
                catch
                {
                    return "HIS系統連結失敗!";
                }
                MyTimerBasic myTimerBasic = new MyTimerBasic();
                string commandText = "";

                commandText += "select ";
                commandText += "* ";
                commandText += $"from PHAADCPRGY where PRG_PATID ='{ID}' ";

                cmd = new OracleCommand(commandText, conn_oracle);
                OracleDataAdapter adapter = new OracleDataAdapter(cmd);
                try
                {
                    reader = cmd.ExecuteReader();
                    try
                    {
                        while (reader.Read())
                        {
                            object[] value = new object[new enum_懷孕檢測報告().GetLength()];
                            value[(int)enum_懷孕檢測報告.病歷號] = reader["PRG_PATID"].ToString().Trim();
                            value[(int)enum_懷孕檢測報告.院內碼] = reader["PRG_DIACODE"].ToString().Trim();
                            value[(int)enum_懷孕檢測報告.健保碼] = reader["PRG_INSCODE"].ToString().Trim();
                            value[(int)enum_懷孕檢測報告.檢驗項目名稱] = reader["PRG_EGNAME"].ToString().Trim();
                            value[(int)enum_懷孕檢測報告.報告值] = reader["PRG_STATE"].ToString().Trim();
                            value[(int)enum_懷孕檢測報告.報告日期] = reader["PRG_REPDTTM"].ToString().Trim();
                            list_value.Add(value);
                        }
                    }
                    catch
                    {
                        return "HIS系統回傳資料異常!";
                    }
                }
                catch (Exception ex)
                {
                    return "HIS系統命令下達失敗! \n {ex} \n {commandText}!";
                }
                conn_oracle.Close();
                conn_oracle.Dispose();
                List<pragnantClass> pragnantClasses = list_value.SQLToClass<pragnantClass, enum_懷孕檢測報告>();
                returnData.Code = 200;
                returnData.Data = pragnantClasses;
                returnData.TimeTaken = myTimerBasic.ToString();
                returnData.Result = $"取得懷孕資料! 共<{pragnantClasses.Count}>筆 ";
                return returnData.JsonSerializationt(true);
            }
            catch (Exception ex)
            {
                return $"Exception : {ex.Message} ";
            }

        }
        [HttpGet("ICD")]
        public string ICD(List<OrderClass> orderClasses)
        {
            OracleConnection conn_oracle;
            OracleDataReader reader;
            OracleCommand cmd;
            returnData returnData = new returnData();
            List<object[]> list_value = new List<object[]>();
            List<string> list_ICD = new List<string>();

            try
            {
                try
                {
                    conn_oracle = new OracleConnection(conn_str);
                    conn_oracle.Open();
                }
                catch
                {
                    return "HIS系統連結失敗!";
                }
                MyTimerBasic myTimerBasic = new MyTimerBasic();
                string commandText = "";
                string 就醫時間 = orderClasses[0].就醫時間.Replace("-", "");
                string 病歷號 = orderClasses[0].病歷號;
                string 住院序號 = orderClasses[0].住院序號;
                string ICD = string.Empty;
                commandText += "select ";
                commandText += "* ";
                commandText += $"from PHAOPDSOA where SOA_VISITDT ='{就醫時間}' and SOA_PATID ='{病歷號}' and  SOA_SEQ = '{住院序號}'";

                cmd = new OracleCommand(commandText, conn_oracle);
                OracleDataAdapter adapter = new OracleDataAdapter(cmd);
                try
                {
                    reader = cmd.ExecuteReader();
                    try
                    {
                        while (reader.Read())
                        {
                            ICD = reader["SOA_CONTENT"].ToString().Trim();
                            ICD = ICD.Split("_")[0];
                            list_ICD.Add(ICD);
                        }
                    }
                    catch
                    {
                        return "HIS系統回傳資料異常!";
                    }
                }
                catch (Exception ex)
                {
                    return "HIS系統命令下達失敗! \n {ex} \n {commandText}!";
                }
                conn_oracle.Close();
                conn_oracle.Dispose();
                returnData.Code = 200;
                returnData.Data = list_ICD;
                returnData.TimeTaken = myTimerBasic.ToString();
                returnData.Result = $"取得疾病資料! 共<{list_ICD.Count}>筆 ";
                return returnData.JsonSerializationt(true);
            }
            catch (Exception ex)
            {
                return $"Exception : {ex.Message} ";
            }

        }

        [HttpPost("PHAADCLABDATA")]
        public string PHAADCLABDATA([FromBody] List<OrderClass> orderClasses)
        {
            returnData returnData = new returnData();
            OracleConnection conn_oracle = new OracleConnection(conn_str);
            try
            {
                conn_oracle = new OracleConnection(conn_str);
                conn_oracle.Open();
            }
            catch
            {
                returnData.Code = -200;
                returnData.Result = "HIS系統連結失敗!";
                return returnData.JsonSerializationt(true);
            }
            finally
            {
                conn_oracle.Close();
                conn_oracle.Dispose();
            }
            return PHAADCLABDATA(orderClasses, conn_oracle);
        }
        private string PHAADCLABDATA(List<OrderClass> orderClasses, OracleConnection conn_oracle)
        {
            OracleDataReader reader;
            OracleCommand cmd;
            returnData returnData = new returnData();

            try
            {

                MyTimerBasic myTimerBasic = new MyTimerBasic();
                string commandText = "";
                string 病歷號 = orderClasses[0].病歷號;
                commandText += "SELECT t1.* ";
                commandText += "FROM PHAADCLABDATA t1, ";
                commandText += "     ( ";
                commandText += "         SELECT PRG_DIACODE, MAX(PRG_REPDTTM) AS MaxTime ";
                commandText += $"         FROM PHAADCLABDATA WHERE PRG_PATID = '{病歷號}' ";
                commandText += "         GROUP BY PRG_DIACODE ";
                commandText += "     ) t2 ";
                commandText += "WHERE t1.PRG_DIACODE = t2.PRG_DIACODE ";
                commandText += "  AND t1.PRG_REPDTTM = t2.MaxTime ";
                commandText += $"  AND t1.PRG_PATID = '{病歷號}'";

                cmd = new OracleCommand(commandText, conn_oracle);
                List<labResultClass> labResultClasses = new List<labResultClass>();
                OracleDataAdapter adapter = new OracleDataAdapter(cmd);
                try
                {
                    reader = cmd.ExecuteReader();
                    try
                    {
                        while (reader.Read())
                        {
                            labResultClass labResultClass = new labResultClass();
                            labResultClass.病歷號 = reader["PRG_PATID"].ToString().Trim();
                            labResultClass.檢驗項目代碼 = reader["PRG_DIACODE"].ToString().Trim();
                            labResultClass.檢驗醫令代碼 = reader["PRG_INSCODE"].ToString().Trim();
                            labResultClass.檢驗項目 = reader["PRG_EGNAME"].ToString().Trim();
                            labResultClass.檢驗結果 = reader["PRG_STATE"].ToString().Trim();
                            string 檢驗時間 = reader["PRG_REPDTTM"].ToString().Trim();
                            labResultClass.檢驗時間 = $"{檢驗時間.Substring(0, 4)}-{檢驗時間.Substring(4, 2)}-{檢驗時間.Substring(6, 2)} {檢驗時間.Substring(8, 2)}:{檢驗時間.Substring(10, 2)}:{檢驗時間.Substring(12, 2)}";
                            labResultClasses.Add(labResultClass);
                        }
                    }
                    catch (Exception ex)
                    {
                        return $"HIS系統回傳資料異常! {ex.Message}";
                    }
                }
                catch (Exception ex)
                {
                    return "HIS系統命令下達失敗! \n {ex} \n {commandText}!";
                }

                conn_oracle.Close();
                conn_oracle.Dispose();
                labResultClasses = labResultClasses
                    .GroupBy(g => g.檢驗項目代碼)
                    .SelectMany(group =>
                    {
                        var list = group.ToList();
                        if (list.Count == 2 && list.All(x => x.檢驗項目 == "Creatinine-eGFR"))
                        {
                            double val1 = list[0].檢驗結果.StringToDouble();
                            double val2 = list[1].檢驗結果.StringToDouble();
                            if (val1 > val2)
                            {
                                list[0].檢驗項目 = "Creatinine";
                                list[1].檢驗項目 = "eGFR";
                            }
                            else
                            {
                                list[1].檢驗項目 = "Creatinine";
                                list[0].檢驗項目 = "eGFR";
                            }
                        }
                        return list;
                    })
                    .ToList();
                returnData = labResultClass.add(API_Server, labResultClasses);
                if (returnData == null || returnData.Code != 200)
                {
                    Logger.Log("labResultClass_add", $"{returnData.JsonSerializationt(true)}");
                    Console.WriteLine($"檢測數據加入失敗");
                    return returnData.JsonSerializationt(true);
                }
                returnData.Code = 200;
                returnData.Data = labResultClasses;
                returnData.TimeTaken = myTimerBasic.ToString();
                Console.WriteLine($"取得檢驗資料! 共<{labResultClasses.Count}>筆 ");
                returnData.Result = $"取得檢驗資料! 共<{labResultClasses.Count}>筆 ";
                return returnData.JsonSerializationt(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
                return $"Exception : {ex.Message} ";
            }

        }
        [HttpGet("test")]
        public string test(List<OrderClass> orderClasses)
        {
            List<List<OrderClass>> list_orderclasses = GroupOrders(orderClasses);
            for (int i = 0; i < list_orderclasses.Count; i++)
            {
                double Truncate;
                List<OrderClass> temp_orderclasses = list_orderclasses[i];
                double 總量 = 0.0D;
                for (int k = 0; k < temp_orderclasses.Count; k++)
                {

                    總量 += temp_orderclasses[k].交易量.StringToDouble();

                }
                Truncate = 總量 - Math.Truncate(總量);
                bool flag = false;
                if (Truncate != 0)
                {
                    flag = true;
                    總量 = (int)總量 - 1;
                }
                bool 總量已到達 = false;
                for (int k = 0; k < temp_orderclasses.Count; k++)
                {
                    double 交易量 = temp_orderclasses[k].交易量.StringToDouble();
                    Truncate = 交易量 - Math.Truncate(交易量);
                    //if (Truncate != 0 && flag) 交易量 = (double)交易量 - 1;
                    if (Truncate != 0 && flag) 交易量 = Math.Floor(交易量);

                    if (總量已到達)
                    {
                        temp_orderclasses[k].交易量 = "0";
                        continue;
                    }
                    if (總量 - 交易量 <= 0)
                    {
                        temp_orderclasses[k].交易量 = 交易量.ToString();
                    }
                    else
                    {
                        temp_orderclasses[k].交易量 = 總量.ToString();
                        總量已到達 = true;
                    }
                    總量 = 總量 - 交易量;
                }
            }
            return orderClasses.JsonSerializationt(true);
        }
        public static List<List<OrderClass>> GroupOrders(List<OrderClass> orders)
        {
            List<List<OrderClass>> groupedOrders = orders
                .GroupBy(o => new { o.藥品碼, o.病歷號, o.開方日期 })
                .Select(group => group.ToList())
                .ToList();

            return groupedOrders;
        }
        private string PAC_PATID(string PAC_PATID)
        {
            string commandText = string.Empty;
            commandText += "select ";
            commandText += "PAC_VISITDT,";
            commandText += "PAC_SUMQTY PAC_SUMQTY,";
            commandText += "PAC_ORDERSEQ,"; //醫令序號
            commandText += "PAC_SEQ,"; //序號
            commandText += "PAC_DIACODE,"; //藥品院內碼
            commandText += "PAC_DIANAME,"; //藥品商品名稱
            commandText += "PAC_PATNAME,"; //病歷姓名
            commandText += "PAC_PATID,"; //病歷號
            commandText += "PAC_UNIT,"; //小單位
            commandText += "PAC_QTYPERTIME,"; //次劑量
            commandText += "PAC_FEQNO,"; //頻率
            commandText += "PAC_PATHNO,"; //途徑
            commandText += "PAC_DAYS,"; //使用天數
            commandText += "PAC_TYPE,"; // 醫令類型
            commandText += "PAC_DRUGNO,"; //領藥號
            commandText += "PAC_SECTNAME,"; //科別
            commandText += "PAC_DOCNAME,"; //醫師代碼
            commandText += "PAC_PROCDTTM,"; //醫令開立時間
            commandText += "PAC_PAYCD,"; //費用別
            commandText += "PAC_SEX,"; //性別 
            commandText += "PAC_AGE,"; //年齡 
            commandText += "PAC_DRUGGIST "; //藥師代碼

            commandText += $"from PHAADCAL where PAC_PATID='{PAC_PATID}' ";

            return commandText;

        }
        private string STAT(string 住院序號, string 醫令時間, string 醫令類型)
        {
            string commandText = string.Empty;
            commandText += "select * ";
            //commandText += "PAC_VISITDT,";
            //commandText += "PAC_SUMQTY PAC_SUMQTY,";
            //commandText += "PAC_ORDERSEQ,"; //醫令序號
            //commandText += "PAC_SEQ,"; //序號
            //commandText += "PAC_DIACODE,"; //藥品院內碼
            //commandText += "PAC_DIANAME,"; //藥品商品名稱
            //commandText += "PAC_PATNAME,"; //病歷姓名
            //commandText += "PAC_PATID,"; //病歷號
            //commandText += "PAC_UNIT,"; //小單位
            //commandText += "PAC_QTYPERTIME,"; //次劑量
            //commandText += "PAC_FEQNO,"; //頻率
            //commandText += "PAC_PATHNO,"; //途徑
            //commandText += "PAC_DAYS,"; //使用天數
            //commandText += "PAC_TYPE,"; // 醫令類型
            //commandText += "PAC_DRUGNO,"; //領藥號
            //commandText += "PAC_SECTNAME,"; //科別
            //commandText += "PAC_DOCNAME,"; //醫師代碼
            //commandText += "PAC_PROCDTTM,"; //醫令開立時間
            //commandText += "PAC_PAYCD,"; //費用別
            //commandText += "PAC_SEX,"; //性別 
            //commandText += "PAC_AGE,"; //年齡 
            //commandText += "PAC_DRUGGIST "; //藥師代碼

            commandText += $"from  phaadcal  where PAC_SEQ='{住院序號}' and PAC_PROCDTTM='{醫令時間}' AND PAC_TYPE='{醫令類型}' ";

            return commandText;

        }
        private string Control(string 領藥號,string 看診日期, string 病歷號, string 序號)
        {
            string commandText = string.Empty;
            commandText += "select ";
            commandText += "PAC_VISITDT,";
            commandText += "PAC_SUMQTY PAC_SUMQTY,";
            commandText += "PAC_ORDERSEQ,"; //醫令序號
            commandText += "PAC_SEQ,"; //序號
            commandText += "PAC_DIACODE,"; //藥品院內碼
            commandText += "PAC_DIANAME,"; //藥品商品名稱
            commandText += "PAC_PATNAME,"; //病歷姓名
            commandText += "PAC_PATID,"; //病歷號
            commandText += "PAC_UNIT,"; //小單位
            commandText += "PAC_QTYPERTIME,"; //次劑量
            commandText += "PAC_FEQNO,"; //頻率
            commandText += "PAC_PATHNO,"; //途徑
            commandText += "PAC_DAYS,"; //使用天數
            commandText += "PAC_TYPE,"; // 醫令類型
            commandText += "PAC_DRUGNO,"; //領藥號
            commandText += "PAC_SECTNAME,"; //科別
            commandText += "PAC_DOCNAME,"; //醫師代碼
            commandText += "PAC_PROCDTTM,"; //醫令開立時間
            commandText += "PAC_PAYCD,"; //費用別
            commandText += "PAC_SEX,"; //性別 
            commandText += "PAC_AGE,"; //年齡 
            commandText += "PAC_DRUGGIST "; //藥師代碼

            commandText += $"from phaadcal where PAC_DRUGNO ='{領藥號}' and PAC_VISITDT = '{看診日期}' AND PAC_PATID='{病歷號}' AND PAC_SEQ='{序號}'";


           

            return commandText;

        }
        private string OPD(string 領藥號, string 看診日期, string 病歷號, string 序號)
        {
            string commandText = string.Empty;
            commandText += "select ";
            commandText += "PAC_VISITDT,";
            commandText += "PAC_SUMQTY PAC_SUMQTY,";
            commandText += "PAC_ORDERSEQ,"; //醫令序號
            commandText += "PAC_SEQ,"; //序號
            commandText += "PAC_DIACODE,"; //藥品院內碼
            commandText += "PAC_DIANAME,"; //藥品商品名稱
            commandText += "PAC_PATNAME,"; //病歷姓名
            commandText += "PAC_PATID,"; //病歷號
            commandText += "PAC_UNIT,"; //小單位
            commandText += "PAC_QTYPERTIME,"; //次劑量
            commandText += "PAC_FEQNO,"; //頻率
            commandText += "PAC_PATHNO,"; //途徑
            commandText += "PAC_DAYS,"; //使用天數
            commandText += "PAC_TYPE,"; // 醫令類型
            commandText += "PAC_DRUGNO,"; //領藥號
            commandText += "PAC_SECTNAME,"; //科別
            commandText += "PAC_DOCNAME,"; //醫師代碼
            commandText += "PAC_PROCDTTM,"; //醫令開立時間
            commandText += "PAC_PAYCD,"; //費用別
            commandText += "PAC_SEX,"; //性別 
            commandText += "PAC_AGE,"; //年齡 
            commandText += "PAC_DRUGGIST "; //藥師代碼

            commandText += $"from  phaadcal  where PAC_VISITDT ='{看診日期}' and PAC_PATID='{病歷號}' AND PAC_SEQ='{序號}' and PAC_DRUGNO = '{領藥號}'";


            return commandText;

        }
        private string 術中醫令(string 序號, string 看診日期, string 藥碼, string 醫令序號)
        {
            string commandText = string.Empty;
            commandText += "select ";
            commandText += "PAC_VISITDT,";
            commandText += "PAC_SUMQTY PAC_SUMQTY,";
            commandText += "PAC_ORDERSEQ,"; //醫令序號
            commandText += "PAC_SEQ,"; //序號
            commandText += "PAC_DIACODE,"; //藥品院內碼
            commandText += "PAC_DIANAME,"; //藥品商品名稱
            commandText += "PAC_PATNAME,"; //病歷姓名
            commandText += "PAC_PATID,"; //病歷號
            commandText += "PAC_UNIT,"; //小單位
            commandText += "PAC_QTYPERTIME,"; //次劑量
            commandText += "PAC_FEQNO,"; //頻率
            commandText += "PAC_PATHNO,"; //途徑
            commandText += "PAC_DAYS,"; //使用天數
            commandText += "PAC_TYPE,"; // 醫令類型
            commandText += "PAC_DRUGNO,"; //領藥號
            commandText += "PAC_SECTNAME,"; //科別
            commandText += "PAC_DOCNAME,"; //醫師代碼
            commandText += "PAC_PROCDTTM,"; //醫令開立時間
            commandText += "PAC_PAYCD,"; //費用別
            commandText += "PAC_SEX,"; //性別 
            commandText += "PAC_AGE,"; //年齡 
            commandText += "PAC_DRUGGIST "; //藥師代碼

            commandText += $"from phaadcal where PAC_SEQ='{序號}' and PAC_VISITDT='{看診日期}' AND PAC_DIACODE='{藥碼}' AND PAC_ORDERSEQ='{醫令序號}' ";

            return commandText;

        }
        public void add(List<OrderClass> orderClasses)
        {
            if (orderClasses.Count == 0) return;
            List<suspiciousRxLogClass> suspiciousRxLoges = suspiciousRxLogClass.get_by_barcode(API_Server, orderClasses[0].藥袋條碼);
            List<string> list_ICD = new List<string> { "I11.0" };
            //suspiciousRxLogClass suspiciousRxLogClass = new suspiciousRxLogClass();
            if (suspiciousRxLoges.Count == 0)
            {
                List<string> disease_list = list_ICD;

                List<diseaseClass> diseaseClasses = diseaseClass.get_by_ICD(API_Server, disease_list);



                suspiciousRxLogClass suspiciousRxLogClass = new suspiciousRxLogClass()
                {
                    GUID = Guid.NewGuid().ToString(),
                    藥袋條碼 = orderClasses[0].藥袋條碼,
                    加入時間 = DateTime.Now.ToDateTimeString(),
                    病歷號 = orderClasses[0].病歷號,
                    科別 = orderClasses[0].科別,
                    醫生姓名 = orderClasses[0].醫師代碼,
                    開方時間 = orderClasses[0].開方日期,
                    藥袋類型 = orderClasses[0].藥袋類型,
                    //錯誤類別 = string.Join(",", suspiciousRxLog.error_type),
                    //簡述事件 = suspiciousRxLog.response,
                    狀態 = enum_suspiciousRxLog_status.未辨識.GetEnumName(),
                    //調劑人員 = orderClasses[0].藥師姓名,
                    調劑時間 = DateTime.Now.ToDateTimeString(),
                    //提報等級 = enum_suspiciousRxLog_ReportLevel.Normal.GetEnumName(),
                    提報時間 = DateTime.MinValue.ToDateTimeString(),
                    處理時間 = DateTime.MinValue.ToDateTimeString(),
                    性別 = "男",
                    年齡 = "",
                    diseaseClasses = diseaseClasses
                };
                suspiciousRxLogClass.add(API_Server, suspiciousRxLogClass);
            }
        }

        private enum enum_懷孕檢測報告
        {
            病歷號,
            院內碼,
            健保碼,
            檢驗項目名稱,
            報告值,
            報告日期,
        }
        private class pragnantClass
        {
            [JsonPropertyName("PRG_PATID")]
            public string 病歷號 { get; set; }
            [JsonPropertyName("PRG_DIACODE")]
            public string 院內碼 { get; set; }
            [JsonPropertyName("PRG_INSCODE")]
            public string 健保碼 { get; set; }
            [JsonPropertyName("PRG_EGNAME")]
            public string 檢驗項目名稱 { get; set; }
            [JsonPropertyName("PRG_STATE")]
            public string 報告值 { get; set; }
            [JsonPropertyName("PRG_REPDTTM")]
            public string 報告日期 { get; set; }
        }

    }

}
