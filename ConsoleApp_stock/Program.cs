using Basic;
using H_Pannel_lib;
using HIS_DB_Lib;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Oracle.ManagedDataAccess.Client;


namespace ConsoleApp_stock
{
    class Program
    {
        private static readonly string conn_str = "Data Source=192.168.166.220:1521/mis;User ID=THESE;Password=these;";
        private static string API = "http://127.0.0.1:4433";
        private static List<medClass> medClasses = medClass.get_med_cloud(API);
        private static Dictionary<string, List<medClass>> dic_medclass = medClasses.CoverToDictionaryByCode();
           
        private static List<sys_serverSettingClass> settingClasses = sys_serverSettingClass.get_name(API).Where(x => x.設備名稱 == "藥庫" || x.設備名稱 == "藥局").ToList();

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

        static void Main(string[] args)
        {
            List<stockClass> stockClasses = new List<stockClass>();
            
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
                    Console.WriteLine($"{ex.Message},HIS系統連接失敗!") ;
                }

                //===============================
                // 2. 解析條碼 → CommandText
                //===============================
                string commandText = $"select * from THESE.MIS_STK_AMT ";
      

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
                            stockClass stock = new stockClass();
                            string 料號 = SafeGet(reader, "GOOD_CODE");
                            medClass medclasses = dic_medclass.GetByCode(料號).FirstOrDefault();
                            if (medclasses == null) continue;
                            string 效期 = SafeGet(reader, "DATE_V");
                            string 批號 = SafeGet(reader, "BATCH");
                            string 數量 = SafeGet(reader, "AMT_I");

                            if (效期.Length == 8)
                            {
                                stock.效期 = new List<string> { $"{效期[..4]}/{效期[4..6]}/{效期[6..8]}" };                                                                   
                            }

                            stock.藥碼 = medclasses.藥品碼;
                            stock.料號 = medclasses.藥品碼;
                            stock.藥名 = medclasses.藥品名稱;
                            stock.批號 = new List<string> { 批號};
                            stock.數量 = new List<string> { 數量};
                            stock.serverName = SafeGet(reader, "DPT_NAME");

                            stockClasses.Add(stock);
                        }
                        catch (Exception ex)
                        {
                            Console.Write($"MIS系統資料解析異常 (Row)：{ex.Message}") ;
                        }
                    }
                }                                  
            }
            



            foreach (var item in settingClasses)
            {
                string serverName = item.設備名稱;
                string serverType = item.類別;
                List<stockClass> stock_buff = stockClasses
                    .Where(x => x.serverName == serverName)
                    .GroupBy(x => x.料號)
                    .Select(g => new stockClass
                    {
                        藥碼 = g.First().藥碼,
                        料號 = g.Key,
                        藥名 = g.First().藥名,
                        serverName = g.First().serverName,

                        效期 = g.SelectMany(x => x.效期).ToList(),
                        批號 = g.SelectMany(x => x.批號).ToList(),
                        數量 = g.SelectMany(x => x.數量).ToList(),
                    })
                    .ToList();

                List<stockClass> add = new List<stockClass>();
                List<stockClass> update = new List<stockClass>();

                returnData returnData_stock = stockClass.get_stock(API, serverName, serverType);
                List<stockClass> stockClasses_sql = returnData_stock.Data.ObjToClass<List<stockClass>>();
                Dictionary<string, List<stockClass>> dic_stock = stockClasses_sql.ToDictByCode();

                for (int i = 0; i < stock_buff.Count; i++)
                {
                    string 藥碼 = stock_buff[i].藥碼;
                    stockClass stocks = dic_stock.GetByCode(藥碼).FirstOrDefault();
                    if (stocks == null)
                    {

                        add.Add(stock_buff[i]);
                    }
                    else
                    {
                        stocks.料號 = stock_buff[i].料號;
                        stocks.藥名 = stock_buff[i].藥名;
                        stocks.效期 = stock_buff[i].效期;
                        stocks.批號 = stock_buff[i].批號;
                        stocks.數量 = stock_buff[i].數量;
                        update.Add(stocks);
                    }
                }
                if (add.Count > 0)
                {
                    returnData returnData_add = stockClass.add(API, serverName, serverType, add);
                    if (returnData_add == null) Logger.Log(add.JsonSerializationt(true));
                    else Logger.Log($"{returnData_add.JsonSerializationt(true)}");

                }
                if (update.Count > 0)
                {
                    returnData returnData_update = stockClass.update(API, serverName, serverType, update);
                    if (returnData_update == null) Logger.Log(update.JsonSerializationt(true));
                    else Logger.Log($"{returnData_update.JsonSerializationt(true)}");

                }
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
