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

        public static async Task LoggingDatabase(Snmp trap)
        {
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                if (trap.TypeValue == "begin")
                {
                    if (string.IsNullOrEmpty(trap.event_id))
                    {
                        if (!string.IsNullOrEmpty(trap.TitanUID))
                        {
                            trap.event_id = trap.TitanUID;
                        }
                        else
                        {
                            trap.event_id = getGUID();
                        }
                    }

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
                        cmd.CommandText = string.Format(@"INSERT INTO active (id, ip, channel, channel_value, main, level, value, _desc)
VALUES (@id, @ip, @channel, @channel_value, @main, @level, @value, @desc) ON DUPLICATE KEY UPDATE ip = @ip, channel = @channel, main = @main, level = @level, value = @value, _desc = @desc");
                        cmd.Parameters.AddWithValue("@ip", trap.IP);
                        cmd.Parameters.AddWithValue("@id", trap.event_id);
                        cmd.Parameters.AddWithValue("@channel", trap.Channel);
                        cmd.Parameters.AddWithValue("@main", trap.Main);
                        cmd.Parameters.AddWithValue("@level", trap.LevelString);
                        cmd.Parameters.AddWithValue("@channel_value", trap.ChannelValue);
                        cmd.Parameters.AddWithValue("@desc", trap.Desc);
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

                        cmd.CommandText = string.Format(@"INSERT INTO log (client_ip, ip, port, id, community, channel, channel_value, main, level, oid, value, snmp_type_value, name, _desc)
VALUES (@client_ip, @ip, @port, @id, @community, @channel, @channel_value, @main, @level, @oid, @value, @snmp_type_value, (SELECT name from server WHERE ip = @ip), @desc) ON DUPLICATE KEY UPDATE ip = @ip, channel = @channel, channel_value = @channel_value, main = @main, level = @level, value = @value, _desc = @desc");
                        cmd.Parameters.AddWithValue("@client_ip", trap._LocalIP);
                        cmd.Parameters.AddWithValue("@ip", trap.IP);
                        cmd.Parameters.AddWithValue("@id", trap.event_id);
                        cmd.Parameters.AddWithValue("@port", trap.Port);
                        cmd.Parameters.AddWithValue("@community", trap.Community);
                        cmd.Parameters.AddWithValue("@channel", trap.Channel);
                        cmd.Parameters.AddWithValue("@channel_value", trap.ChannelValue);
                        cmd.Parameters.AddWithValue("@main", trap.Main);
                        cmd.Parameters.AddWithValue("@level", trap.LevelString);
                        cmd.Parameters.AddWithValue("@oid", trap.Oid);
                        cmd.Parameters.AddWithValue("@desc", trap.Desc);
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
                    //logger.Debug($"({trap.TypeValue}) {trap.TranslateValue}, {trap.TrapString}");

                    MySqlCommand cmd = conn.CreateCommand();
                    MySqlTransaction trans;

                    trans = conn.BeginTransaction();

                    // Must assign both transaction object and connection
                    // to Command object for a pending local transaction

                    cmd.Connection = conn;
                    cmd.Transaction = trans;

                    try
                    {
                        if (!string.IsNullOrEmpty(trap.TitanUID))
                        {
                            trap.event_id = trap.TitanUID;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(trap.ChannelValue))
                            {
                                cmd.CommandText = string.Format($"SELECT * FROM active WHERE ip = @ip AND channel_value = @channel_value AND value = @value");
                                cmd.Parameters.AddWithValue("@channel_value", trap.ChannelValue);
                            }
                            else
                            {
                                cmd.CommandText = string.Format(@"SELECT * FROM active WHERE ip = @ip AND channel = @channel AND main = @main AND value = @value");
                                cmd.Parameters.AddWithValue("@main", trap.Main);
                                cmd.Parameters.AddWithValue("@channel", trap.Channel);
                            }
                            cmd.Parameters.AddWithValue("@ip", trap.IP);

                            if (String.IsNullOrEmpty(trap.TrapString))
                            {
                                cmd.Parameters.AddWithValue("@value", trap.TranslateValue);
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("@value", trap.TrapString);
                            }

                            MySqlDataReader rdr = cmd.ExecuteReader();
                            while (rdr.Read())
                            {
                                trap.event_id = rdr["id"].ToString();
                            }
                            rdr.Close();

                            cmd.Parameters.Clear();
                        }

                        logger.Debug($"({trap.TypeValue}) event id : {trap.event_id}, titanUID : {trap.TitanUID}");

                        cmd.CommandText = string.Format(@"DELETE FROM active WHERE id = @id");
                        cmd.Parameters.AddWithValue("@id", trap.event_id);

                        await cmd.ExecuteNonQueryAsync();

                        cmd.Parameters.Clear();

                        cmd.CommandText = string.Format(@"UPDATE log set end_at = current_timestamp() WHERE id = @id");
                        cmd.Parameters.AddWithValue("@id", trap.event_id);

                        logger.Info($"log end_at : {trap.event_id}");

                        await cmd.ExecuteNonQueryAsync();
                        trans.Commit();
                        // logger.Info($"{cmd.CommandText}, {trap.IP}, {trap.Channel}, {trap.Main}, {trap.Oid}, {trap.TranslateValue}, {trap.TrapString}");
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
                    if (string.IsNullOrEmpty(trap.event_id))
                    {
                        if (!string.IsNullOrEmpty(trap.TitanUID))
                        {
                            trap.event_id = trap.TitanUID;
                        }
                        else
                        {
                            trap.event_id = getGUID();
                        }
                    }

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
                        cmd.CommandText = string.Format(@"INSERT INTO cuetone (id, ip, name, level, channel, main, value, _desc)
VALUES (@id, @ip, @name, @level, @channel, @main, @value, @desc)");
                        cmd.Parameters.AddWithValue("@id", trap.event_id);
                        cmd.Parameters.AddWithValue("@ip", trap.IP);
                        cmd.Parameters.AddWithValue("@name", Server.GetServerName(trap.IP));
                        cmd.Parameters.AddWithValue("@level", trap.LevelString);
                        cmd.Parameters.AddWithValue("@channel", trap.Channel);
                        cmd.Parameters.AddWithValue("@main", trap.Main);
                        cmd.Parameters.AddWithValue("@desc", trap.Desc);
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
        }
    }
}