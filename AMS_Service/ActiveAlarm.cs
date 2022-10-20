using log4net;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service
{
    internal class ActiveAlarm
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
        public string Level { get; set; }
        public string Value { get; set; }

        public string IsAck { get; set; }

        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public ActiveAlarm()
        {
        }

        public static List<ActiveAlarm> GetActiveAlarm()
        {
            DataTable dt = new DataTable();
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                string sql = $"SELECT id, ip, level, value, isack FROM active";
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(sql, conn);
                MySqlDataAdapter adpt = new MySqlDataAdapter(sql, conn);
                adpt.Fill(dt);
            }

            return dt.AsEnumerable().Select(row => new ActiveAlarm
            {
                Id = row.Field<string>("id"),
                Ip = row.Field<string>("ip"),
                Level = row.Field<string>("level"),
                Value = row.Field<string>("value"),
                IsAck = row.Field<string>("isack")
            }).ToList();
        }

        public static List<ActiveAlarm> GetActiveAlarmStillNotAck()
        {
            DataTable dt = new DataTable();
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                string sql = $"SELECT id, ip, level, value, isack FROM active WHERE isack = 'N'";
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(sql, conn);
                MySqlDataAdapter adpt = new MySqlDataAdapter(sql, conn);
                adpt.Fill(dt);
            }

            return dt.AsEnumerable().Select(row => new ActiveAlarm
            {
                Id = row.Field<string>("id"),
                Ip = row.Field<string>("ip"),
                Level = row.Field<string>("level"),
                Value = row.Field<string>("value"),
                IsAck = row.Field<string>("isack")
            }).ToList();
        }

        public static async void UpdateAckAlarm(ActiveAlarm alarm)
        {
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                await conn.OpenAsync();
                MySqlCommand cmd = conn.CreateCommand();
                MySqlTransaction trans = conn.BeginTransaction();

                cmd.Connection = conn;
                cmd.Transaction = trans;

                try
                {
                    cmd.CommandText = "UPDATE active set isack = 'Y' WHERE id = @id";
                    cmd.Parameters.AddWithValue("@id", alarm.Id);
                    await cmd.ExecuteNonQueryAsync();
                    trans.Commit();
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());

                    try
                    {
                        trans.Rollback();
                    }
                    catch (MySqlException ex)
                    {
                        if (trans.Connection != null)
                        {
                            logger.Error("An exception of type " + ex.GetType() + " was encountered while attempting to roll back the transaction.");
                        }
                    }
                    logger.Error("An exception of type " + e.GetType() + " was encountered while inserting the data.");
                    logger.Error("Neither record was written to database.");
                }
            }
        }
    }
}