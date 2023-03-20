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

        public static void InitActiveAlarm()
        {
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                conn.Open();
                MySqlCommand cmd = new MySqlCommand("DELETE FROM active", conn);
                cmd.ExecuteNonQuery();
            }
        }

        public static async Task ActiveDupCheckAlarm(List<DuplicateCheckActiveAlarm> alarms, List<DuplicateCheckActiveAlarm> oldAlarms)
        {
            // oldAlarms 와 alarms을 비교함
            // alarms 에 새로운 알람이 있는지 확인
            // old 알람에서 없어진 알람이 있는지 확인

            var newAlarms = alarms.Where(alarm => !oldAlarms.Any(oldAlarm =>
            oldAlarm.State == alarm.State
            && oldAlarm.Level == alarm.Level)).ToList();

            foreach (var alarm in newAlarms)
            {
                logger.Info($"new alarm {alarm.Ip}, {alarm.UID}, {alarm.ChannelValue}, {alarm.Level}, {alarm.State}");
            }

            var deletedAlarms = oldAlarms.Where(oldAlarm => !alarms.Any(alarm =>
                oldAlarm.State == alarm.State
                && oldAlarm.Level == alarm.Level)).ToList();

            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
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
                    foreach (var deleted in deletedAlarms)
                    {
                        logger.Info($"deleted alarm {deleted.Level}, {deleted.UID}, {deleted.State}");
                        if (!string.IsNullOrEmpty(deleted.UID))
                        {
                            cmd.CommandText = string.Format(@"DELETE FROM active WHERE titan_uid = @uid AND value = @value");
                            cmd.Parameters.AddWithValue("@uid", deleted.UID);
                            cmd.Parameters.AddWithValue("@value", deleted.State);
                            await cmd.ExecuteNonQueryAsync();
                            cmd.Parameters.Clear();

                            cmd.CommandText = string.Format(@"UPDATE log SET end_at = CURRENT_TIMESTAMP() WHERE titan_uid = @uid AND value = @value ORDER BY start_at DESC LIMIT 1");
                            cmd.Parameters.AddWithValue("@uid", deleted.UID);
                            cmd.Parameters.AddWithValue("@value", deleted.State);
                            await cmd.ExecuteNonQueryAsync();
                            cmd.Parameters.Clear();
                        }
                        else
                        {
                            cmd.CommandText = string.Format(@"DELETE FROM active WHERE ip = @ip AND value = @value");
                            cmd.Parameters.AddWithValue("@ip", deleted.Ip);
                            cmd.Parameters.AddWithValue("@value", deleted.State);
                            await cmd.ExecuteNonQueryAsync();
                            cmd.Parameters.Clear();

                            cmd.CommandText = string.Format(@"UPDATE log SET end_at = CURRENT_TIMESTAMP() WHERE ip = @ip AND value = @value ORDER BY start_at DESC LIMIT 1");
                            cmd.Parameters.AddWithValue("@ip", deleted.Ip);
                            cmd.Parameters.AddWithValue("@value", deleted.State);
                            await cmd.ExecuteNonQueryAsync();
                            cmd.Parameters.Clear();
                        }
                    }

                    foreach (var alarm in newAlarms)
                    {
                        cmd.CommandText = string.Format(@"INSERT INTO active (id, ip, titan_uid, level, value, channel_value) VALUES (@id, @ip, @uid, @level, @value, @channel_value)");
                        cmd.Parameters.AddWithValue("@id", alarm.Id);
                        cmd.Parameters.AddWithValue("@ip", alarm.Ip);
                        cmd.Parameters.AddWithValue("@uid", alarm.UID);
                        cmd.Parameters.AddWithValue("@level", alarm.Level);
                        cmd.Parameters.AddWithValue("@value", alarm.State);
                        cmd.Parameters.AddWithValue("@channel_value", alarm.ChannelValue);
                        await cmd.ExecuteNonQueryAsync();
                        cmd.Parameters.Clear();

                        cmd.CommandText = string.Format(@"INSERT INTO log (ip, id, titan_uid, name, level, value, snmp_type_value, channel_value)
VALUES (@ip, @id, @uid, (SELECT name from server WHERE ip = @ip), @level, @value, 'begin', @channel_value)");
                        cmd.Parameters.AddWithValue("@id", alarm.Id);
                        cmd.Parameters.AddWithValue("@ip", alarm.Ip);
                        cmd.Parameters.AddWithValue("@uid", alarm.UID);
                        cmd.Parameters.AddWithValue("@level", alarm.Level);
                        cmd.Parameters.AddWithValue("@value", alarm.State);
                        cmd.Parameters.AddWithValue("@channel_value", alarm.ChannelValue);

                        await cmd.ExecuteNonQueryAsync();
                        cmd.Parameters.Clear();
                    }
                    trans.Commit();
                    // logger.Info($"({Ip}) Transaction Commit");
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
        }

        public static async Task ActiveAlarm(string Ip, List<TitanActiveAlarm> alarms, List<TitanActiveAlarm> oldAlarms)
        {
            // oldAlarms 와 alarms을 비교함
            // alarms 에 새로운 알람이 있는지 확인
            // old 알람에서 없어진 알람이 있는지 확인

            var newAlarms = alarms.Where(alarm => !oldAlarms.Any(oldAlarm =>
            oldAlarm.Value == alarm.Value
            && oldAlarm.Desc == alarm.Desc
            && oldAlarm.Level == alarm.Level
            && oldAlarm.ChannelName == alarm.ChannelName)).ToList();

            foreach (var alarm in newAlarms)
            {
                logger.Info($"new alarm {alarm.ChannelName}, {alarm.Value}, {alarm.Desc}");
            }

            var deletedAlarms = oldAlarms.Where(oldAlarm => !alarms.Any(alarm =>
                oldAlarm.Value == alarm.Value
                && oldAlarm.Desc == alarm.Desc
                && oldAlarm.Level == alarm.Level
                && oldAlarm.ChannelName == alarm.ChannelName)).ToList();

            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
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
                    foreach (var deleted in deletedAlarms)
                    {
                        logger.Info($"deleted alarm {deleted.ChannelName}, {deleted.Value}, {deleted.Desc}");
                        cmd.CommandText = string.Format(@"DELETE FROM active WHERE ip = @ip AND level = @level AND channel_value = @channel_value AND value = @value AND _desc = @desc");
                        cmd.Parameters.AddWithValue("@ip", Ip);
                        cmd.Parameters.AddWithValue("@level", deleted.Level);
                        cmd.Parameters.AddWithValue("@channel_value", deleted.ChannelName);
                        cmd.Parameters.AddWithValue("@value", deleted.Value);
                        cmd.Parameters.AddWithValue("@desc", deleted.Desc);
                        await cmd.ExecuteNonQueryAsync();
                        cmd.Parameters.Clear();

                        cmd.CommandText = string.Format(@"UPDATE log SET end_at = CURRENT_TIMESTAMP() WHERE ip = @ip AND level = @level AND channel_value = @channel_value AND value = @value AND _desc = @desc ORDER BY start_at DESC LIMIT 1");
                        cmd.Parameters.AddWithValue("@ip", Ip);
                        cmd.Parameters.AddWithValue("@level", deleted.Level);
                        cmd.Parameters.AddWithValue("@channel_value", deleted.ChannelName);
                        cmd.Parameters.AddWithValue("@value", deleted.Value);
                        cmd.Parameters.AddWithValue("@desc", deleted.Desc);
                        await cmd.ExecuteNonQueryAsync();
                        cmd.Parameters.Clear();
                    }

                    foreach (var alarm in newAlarms)
                    {
                        cmd.CommandText = string.Format(@"INSERT INTO active (id, ip, channel_value, level, value, _desc)
VALUES (@id, @ip, @channel_value, @level, @value, @desc)");
                        cmd.Parameters.AddWithValue("@ip", Ip);
                        cmd.Parameters.AddWithValue("@id", alarm.Id);
                        cmd.Parameters.AddWithValue("@level", alarm.Level);
                        cmd.Parameters.AddWithValue("@channel_value", alarm.ChannelName);
                        cmd.Parameters.AddWithValue("@desc", alarm.Desc);
                        cmd.Parameters.AddWithValue("@value", alarm.Value);
                        await cmd.ExecuteNonQueryAsync();
                        cmd.Parameters.Clear();

                        cmd.CommandText = string.Format(@"INSERT INTO log (ip, id, channel_value, level, value, name, _desc, snmp_type_value)
VALUES (@ip, @id, @channel_value, @level, @value, (SELECT name from server WHERE ip = @ip), @desc, 'begin')");
                        cmd.Parameters.AddWithValue("@ip", Ip);
                        cmd.Parameters.AddWithValue("@id", alarm.Id);
                        cmd.Parameters.AddWithValue("@channel_value", alarm.ChannelName);
                        cmd.Parameters.AddWithValue("@level", alarm.Level);
                        cmd.Parameters.AddWithValue("@desc", alarm.Desc);
                        cmd.Parameters.AddWithValue("@value", alarm.Value);

                        await cmd.ExecuteNonQueryAsync();
                        cmd.Parameters.Clear();
                    }
                    trans.Commit();
                    // logger.Info($"({Ip}) Transaction Commit");
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
        }

        public static async Task LoggingDatabase(Snmp trap)
        {
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                if (trap.TypeValue == "begin")
                {
                    trap.event_id = getGUID();

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
                        cmd.CommandText = string.Format(@"INSERT INTO active (id, ip, channel, channel_value, main, level, value, _desc, titan_uid)
VALUES (@id, @ip, @channel, @channel_value, @main, @level, @value, @desc, @titan_uid) ON DUPLICATE KEY UPDATE ip = @ip, channel = @channel, main = @main, level = @level, value = @value, _desc = @desc, titan_uid = @titan_uid");
                        cmd.Parameters.AddWithValue("@ip", trap.IP);
                        cmd.Parameters.AddWithValue("@id", trap.event_id);
                        cmd.Parameters.AddWithValue("@channel", trap.Channel);
                        cmd.Parameters.AddWithValue("@main", trap.Main);
                        cmd.Parameters.AddWithValue("@level", trap.LevelString);
                        cmd.Parameters.AddWithValue("@channel_value", trap.ChannelValue);
                        cmd.Parameters.AddWithValue("@desc", trap.Desc);
                        cmd.Parameters.AddWithValue("@titan_uid", trap.TitanUID);
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

                        cmd.CommandText = string.Format(@"INSERT INTO log (client_ip, ip, port, id, community, channel, channel_value, main, level, oid, value, snmp_type_value, name, _desc, titan_uid)
VALUES (@client_ip, @ip, @port, @id, @community, @channel, @channel_value, @main, @level, @oid, @value, @snmp_type_value, (SELECT name from server WHERE ip = @ip), @desc, @titan_uid) ON DUPLICATE KEY UPDATE ip = @ip, channel = @channel, channel_value = @channel_value, main = @main, level = @level, value = @value, _desc = @desc, titan_uid = @titan_uid");
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
                        cmd.Parameters.AddWithValue("@titan_uid", trap.TitanUID);
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
                            cmd.CommandText = string.Format($"SELECT * FROM active WHERE ip = @ip AND titan_uid = @titan_uid AND value = @value");
                            cmd.Parameters.AddWithValue("@titan_uid", trap.TitanUID);
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