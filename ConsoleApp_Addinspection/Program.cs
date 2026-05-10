using Basic;
using HIS_DB_Lib;
using Microsoft.VisualBasic;
using Oracle.ManagedDataAccess.Client;
using Oracle_Lib;
using System;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;


namespace ConsoleApp_Addinspection
{
    internal class Program
    {
        private static string server = "192.168.166.220";
        private static string ServiceName = "mis";
        private static string owner = "THESE";
        private static string userName = "THESE";
        private static string password = "these";
        private static int port = 1521;
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
        static void Main(string[] args)
        {
            returnData returnData_content = inspectionClass.content_get_all("http://127.0.0.1:4433");
            if (returnData_content == null || returnData_content.Code != 200)
            {
                throw new Exception("資料取得失敗");
            }
            List<inspectionClass.content> contents = returnData_content.Data.ObjToClass<List<inspectionClass.content>>(); //目前資料庫的content

            ORCControl oRCControl = new ORCControl(server, ServiceName, owner, userName, password, port);

            using (var conn_oracle = new OracleConnection(oRCControl.conn_str))
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
                string commandText = @"
                    SELECT A.*
                    FROM THESE.MIS_PUR_APLY_ITM A
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM THESE.MIS_PUR_APLY B
                        WHERE B.NO_REQ = A.NO_REQ
                          AND B.VOIDED = 'Y'
                    )";



                //===============================
                // 3. 執行查詢（強化版，不洩漏 cursor）
                //===============================
                inspectionClass.creat creat = new inspectionClass.creat();
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
                            inspectionClass.content content = new inspectionClass.content();
                            string 料號 = SafeGet(reader, "GOOD_CODE");
                            medClass medclasses = dic_medclass.GetByCode(料號).FirstOrDefault();
                            if (medclasses == null) continue;
                            string 請購單號 = SafeGet(reader, "NO_REQ");
                            string 請購序號 = SafeGet(reader, "CREC");       
                            string 請購單號_ = $"{請購單號}-{請購序號}";

                            inspectionClass.content content_buff = contents
                            .Where(temp => temp.請購單號 == 請購單號_)
                            .FirstOrDefault();
                            if (content_buff != null) continue; //已存在資料庫，跳過
                            string 數量 = SafeGet(reader, "AMT_APLY");

                            content.藥品碼 = medclasses.藥品碼;
                            content.藥品名稱 = medclasses.藥品名稱;
                            content.中文名稱 = medclasses.中文名稱;
                            content.廠牌 = medclasses.廠牌;
                            content.料號 = medclasses.料號;
                            content.包裝單位 = medclasses.包裝單位;
                            content.請購單號 = $"{請購單號}-{請購序號}";
                            content.應收數量 = 數量;
                            creat.Contents.Add(content);

                        }
                        catch (Exception ex)
                        {
                            Console.Write($"MIS系統資料解析異常 (Row)：{ex.Message}");
                        }
                    }


                }
                if (creat.Contents.Count() == 0)
                {
                    Console.WriteLine($"{DateTime.Now.ToDateTimeString()}無可新增資料");
                }
                else
                {
                    returnData returnData = inspectionClass.creat_add_returnData(API, creat);
                    Logger.Log(returnData.JsonSerializationt(true));
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
