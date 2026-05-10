using Basic;
using HIS_DB_Lib;
using Microsoft.VisualBasic;
using Oracle.ManagedDataAccess.Client;
using System;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;

namespace ConsoleApp_AddDrugStotreDistribution
{
     class Program
     {
        private static readonly string conn_str = "Data Source=192.168.166.220:1521/mis;User ID=THESE;Password=these;";
        private static string API = "http://127.0.0.1:4433";
        private static List<medClass> medClasses = medClass.get_med_cloud(API);
        private static Dictionary<string, List<medClass>> dic_medclass = medClasses.CoverToDictionaryByCode();
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
        private static DateTime dt = new DateTime(2001, 1, 1, 0, 0, 0);
        private static System.Threading.Mutex mutex;

        static void Main(string[] args)
        {
            Console.Title = "ConsoleApp_AddDrugStotreDistribution";

            mutex = new System.Threading.Mutex(true, Console.Title);
            if (mutex.WaitOne(0, false) == false) return;
            while (true)
            {
                List<drugStotreDistributionClass> drugStotreDistributionClasses = new List<drugStotreDistributionClass>();
                List<drugStotreDistributionClass> drugStotreDistributionClasses_delete = new List<drugStotreDistributionClass>();
                string today = DateTime.Now.ToString("yyyyMMdd");
                using (var conn_oracle = new OracleConnection(conn_str))
                {
                    //===============================
                    // 1. 連線至 HIS
                    //===============================
                    try
                    {
                        MyTimerBasic t1 = new MyTimerBasic();
                        conn_oracle.Open();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($" {ex.Message}, HIS系統連接失敗");
                        Console.WriteLine($"{ex.Message},HIS系統連接失敗!");
                    }

                    //===============================
                    // 2. 解析條碼 → CommandText
                    //===============================
                    string commandText = $@"
                    SELECT 
                        A.*, 
                        B.*
                    FROM THESE.MIS_STK_APLY_ITM A
                    LEFT JOIN THESE.MIS_STK_APLY B
                        ON A.NO_GET = B.NO_GET
                    WHERE 
                        A.ISSUE_AMT IS NULL 
                        and B.DATE_G = '{today}'";
                    //===============================
                    // 3. 執行查詢（強化版，不洩漏 cursor）
                    //===============================

                    using (var cmd = new OracleCommand(commandText, conn_oracle))
                    using (var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection))
                    {
                        MyTimerBasic t_query = new MyTimerBasic();

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
                                Console.WriteLine($"HIS系統資料讀取異常 (Read)：{ex.Message}");
                            }

                            if (!hasRow) break;

                            //--- 單筆資料解析（不可拋例外）
                            try
                            {
                                drugStotreDistributionClass drugStotreDistribution = new drugStotreDistributionClass();
                                string 料號 = SafeGet(reader, "GOOD_CODE");
                                medClass medclasses = dic_medclass.GetByCode(料號).FirstOrDefault();
                                if (medclasses == null) continue;
                                string 目的庫別 = SafeGet(reader, "DPT_CODE_O") == "5030B" ? "藥庫" : "";
                                string 來源庫別 = SafeGet(reader, "DPT_CODE_I") == "5030A" ? "藥局" : "";
                                string 目的庫庫存 = SafeGet(reader, "STK_AMT_O");
                                string 來源庫庫存 = SafeGet(reader, "STK_AMT_I");
                                string 作廢 = SafeGet(reader, "VOIDED");
                                string 數量 = SafeGet(reader, "SUG_AMT");
                                string 包裝單位 = SafeGet(reader, "MIN_PACK_NAME");
                                drugStotreDistribution.目的庫別 = 目的庫別;
                                drugStotreDistribution.來源庫別 = 來源庫別;
                                drugStotreDistribution.目的庫庫存 = 目的庫庫存;
                                drugStotreDistribution.來源庫庫存 = 來源庫庫存;
                                drugStotreDistribution.撥發量 = 數量;
                                drugStotreDistribution.報表名稱 = "系統建單";
                                drugStotreDistribution.狀態 = "等待過帳";
                                drugStotreDistribution.包裝單位 = 包裝單位;
                                drugStotreDistribution.藥碼 = medclasses.藥品碼;
                                drugStotreDistribution.藥名 = medclasses.藥品名稱;
                                string 單號 = SafeGet(reader, "NO_GET");
                                drugStotreDistribution.備註 = $"{單號}{料號}";
                                if (作廢 == "Y")
                                {
                                    drugStotreDistributionClasses_delete.Add(drugStotreDistribution);
                                }
                                else
                                {
                                    drugStotreDistributionClasses.Add(drugStotreDistribution);
                                }


                            }
                            catch (Exception ex)
                            {
                                Console.Write($"MIS系統資料解析異常 (Row)：{ex.Message}");
                            }
                        }
                    }
                }
                if (drugStotreDistributionClasses.Count > 0)
                {
                    List<drugStotreDistributionClass> drugStotreDistribution_SQL = drugStotreDistributionClass.get_by_addedTime(API, dt, DateTime.Now);

                    List<drugStotreDistributionClass> add = new List<drugStotreDistributionClass>();
                    List<drugStotreDistributionClass> delete = new List<drugStotreDistributionClass>();


                    foreach (var item in drugStotreDistributionClasses)
                    {
                        drugStotreDistributionClass drugStotre_buff = drugStotreDistribution_SQL.Where(x => x.備註 == item.備註).FirstOrDefault();
                        if (drugStotre_buff != null) continue;
                        add.Add(item);
                    }
                    foreach (var item in drugStotreDistributionClasses_delete)
                    {
                        drugStotreDistributionClass drugStotre_buff = drugStotreDistribution_SQL.Where(x => x.備註 == item.備註).FirstOrDefault();
                        if (drugStotre_buff != null) delete.Add(item);
                    }
                    if (add.Count > 0)
                    {
                        drugStotreDistributionClass.add(API, add);
                    }
                    if (delete.Count > 0)
                    {
                        drugStotreDistributionClass.delete_by_guid(API, delete);
                    }
                }
                else
                {
                    Console.WriteLine("無今日建議請領資料");
                }
                Thread.Sleep(120000);
            }
        } 
     } 
    public static class MedClassExtensions
    {
        public static Dictionary<string, List<medClass>> CoverToDictionaryByCode(this List<medClass> medClasses)
        {
            Dictionary<string, List<medClass>> dictionary = new Dictionary<string, List<medClass>>();
            foreach (var item in medClasses)
            {
                if (dictionary.TryGetValue(item.料號, out List<medClass> list))
                {
                    list.Add(item);
                }
                else
                {
                    dictionary[item.料號] = new List<medClass> { item };
                }
            }
            return dictionary;
        }
        public static List<medClass> GetByCode(this Dictionary<string, List<medClass>> dictionary, string code)
        {
            if (dictionary.TryGetValue(code, out List<medClass> medclass))
            {
                return medclass;
            }
            else
            {
                return new List<medClass>();
            }
        }
    }
}
