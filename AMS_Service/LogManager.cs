using AMS_Service.Utils;
using log4net;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service

{
    internal class LogManager
    {
        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public string StartAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public string _EndAt { get; set; }
        public string Ip { get; set; }
        public string Name { get; set; }
        public string Oid { get; set; }
        public bool IsConnection { get; set; } = true;
        public string _Level { get; set; }
        public int LevelPriority { get; set; }
        public string TypeValue { get; set; }

        public string EndAt
        {
            get
            {
                return _EndAt;
            }
            set
            {
                _EndAt = value;
                OnPropertyChanged(new PropertyChangedEventArgs("EndAt"));
            }
        }

        public string Color { get; set; }
        public string Value { get; set; }

        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        public void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, e);
            }
        }

        public LogManager()
        {
        }

        public static string getGUID()
        {
            Guid g = Guid.NewGuid();
            return g.ToString();
        }

        public static string getEventID(string ip, string oid)
        {
            string id = null;
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                string query = string.Format($"SELECT id FROM log WHERE ip = '{ip}' AND oid = '{oid}' AND snmp_type_value = 'begin' ORDER BY start_at DESC LIMIT 1");
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                MySqlDataReader rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    id = rdr["id"].ToString();
                }
                rdr.Close();
            }
            return id;
        }

        public static async Task<string> LoggingDatabase(Snmp trap)
        {
            string id = null;
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                if (trap.TypeValue == "begin")
                {
                    id = getGUID();
                    await conn.OpenAsync();

                    MySqlCommand cmd = conn.CreateCommand();
                    MySqlTransaction trans;

                    trans = conn.BeginTransaction();

                    // Must assign both transaction object and connection
                    // to Command object for a pending local transaction

                    cmd.Connection = conn;
                    cmd.Transaction = trans;

                    try
                    {
                        cmd.CommandText = string.Format(@"INSERT INTO active (id, ip, level, value)
VALUES (@id, @ip, @level, @value) ON DUPLICATE KEY UPDATE ip = @ip, level = @level, value = @value");
                        cmd.Parameters.AddWithValue("@ip", trap.IP);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@level", trap.LevelString);
                        if (String.IsNullOrEmpty(trap.TrapString))
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TranslateValue);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TrapString);
                        }
                        await cmd.ExecuteNonQueryAsync();

                        cmd.Parameters.Clear();

                        cmd.CommandText = string.Format(@"INSERT INTO log (client_ip, ip, port, id, community, level, oid, value, snmp_type_value, name)
VALUES (@client_ip, @ip, @port, @id, @community, @level, @oid, @value, @snmp_type_value, (SELECT name from server WHERE ip = @ip)) ON DUPLICATE KEY UPDATE ip = @ip, level = @level, value = @value");
                        cmd.Parameters.AddWithValue("@client_ip", trap._LocalIP);
                        cmd.Parameters.AddWithValue("@ip", trap.IP);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@port", trap.Port);
                        cmd.Parameters.AddWithValue("@community", trap.Community);
                        cmd.Parameters.AddWithValue("@level", trap.LevelString);
                        cmd.Parameters.AddWithValue("@oid", trap.Oid);
                        if (String.IsNullOrEmpty(trap.TrapString))
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TranslateValue);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TrapString);
                        }
                        cmd.Parameters.AddWithValue("@snmp_type_value", trap.TypeValue);

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
                else if (trap.TypeValue == "end")
                {
                    await conn.OpenAsync();

                    MySqlCommand cmd = conn.CreateCommand();
                    MySqlTransaction trans;

                    trans = conn.BeginTransaction();

                    // Must assign both transaction object and connection
                    // to Command object for a pending local transaction

                    cmd.Connection = conn;
                    cmd.Transaction = trans;

                    try
                    {
                        cmd.CommandText = string.Format(@"DELETE FROM active WHERE ip = @ip AND value = @value");
                        cmd.Parameters.AddWithValue("@ip", trap.IP);
                        if (String.IsNullOrEmpty(trap.TrapString))
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TranslateValue);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TrapString);
                        }
                        await cmd.ExecuteNonQueryAsync();

                        cmd.Parameters.Clear();

                        id = getEventID(trap.IP, trap.Oid);

                        if (!string.IsNullOrEmpty(trap.Oid))
                        {
                            cmd.CommandText = string.Format(@"UPDATE log set end_at = current_timestamp() WHERE ip = @ip AND oid = @oid AND snmp_type_value = 'begin' ORDER BY start_at DESC LIMIT 1");
                        }
                        else
                        {
                            cmd.CommandText = string.Format(@"UPDATE log set end_at = current_timestamp() WHERE ip = @ip AND value = @value AND snmp_type_value = 'begin' ORDER BY start_at DESC LIMIT 1");
                        }

                        cmd.Parameters.AddWithValue("@ip", trap.IP);
                        cmd.Parameters.AddWithValue("@oid", trap.Oid);

                        if (String.IsNullOrEmpty(trap.TrapString))
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TranslateValue);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TrapString);
                        }

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
                        logger.Error("An exception of type " + e.GetType() + " was encountered while delete and update the data.");
                        logger.Error("Neither record was written to database.");
                    }
                }
                else if (trap.TypeValue == "log")
                {
                    id = getGUID();
                    await conn.OpenAsync();

                    MySqlCommand cmd = conn.CreateCommand();
                    MySqlTransaction trans;

                    trans = conn.BeginTransaction();

                    // Must assign both transaction object and connection
                    // to Command object for a pending local transaction

                    cmd.Connection = conn;
                    cmd.Transaction = trans;

                    try
                    {
                        cmd.CommandText = string.Format(@"INSERT INTO cuetone (id, ip, name, value)
VALUES (@id, @ip, @name, @value)");
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@ip", trap.IP);
                        cmd.Parameters.AddWithValue("@name", Server.GetServerName(trap.IP));
                        if (String.IsNullOrEmpty(trap.TrapString))
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TranslateValue);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@value", trap.TrapString);
                        }
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

            return id;
        }
    }
}