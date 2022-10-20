using log4net;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service
{
    public class Setting
    {
        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public Setting()
        {
        }

        public static string GetAck()
        {
            string isack = null;

            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                string query = string.Format($"SELECT k,v FROM setting WHERE k = 'ack'");
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                MySqlDataReader rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    isack = rdr["v"].ToString();
                }
                rdr.Close();
            }
            return isack;
        }

        public static async void UpdateAckZero()
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
                    cmd.CommandText = "UPDATE setting set v = '0' WHERE k = 'ack'";
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