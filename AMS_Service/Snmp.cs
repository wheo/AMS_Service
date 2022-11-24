using AMS_Service.Service;
using AMS_Service.Utils;
using log4net;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service
{
    internal class Snmp
    {
        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public string event_id { get; set; }

        public string type { get; set; }
        public string IP { get; set; }

        public string Syntax { get; set; }
        public string Port { get; set; }
        public string Community { get; set; }
        public string Value { get; set; }
        public string LevelString { get; set; }
        public string Color { get; set; }
        public string TypeValue { get; set; }
        public string Oid { get; set; }

        public string LogOid { get; set; }
        public bool Enable { get; set; }
        public string Desc { get; set; }
        public int Channel { get; set; } = 0; // Default Channel is 0

        public string ChannelValue { get; set; }
        public int Index { get; set; }
        public int Main { get; set; } = 0; // Default is 0, 0 is Main, 1 is PIP
        public string TranslateValue { get; set; }

        public string Api_msg { get; set; }
        public string Event_type { get; set; }
        public bool IsTypeTrap { get; set; } = false;
        public string TrapString { get; set; }
        public string TitanUID { get; set; }
        public string TitanName { get; set; }
        public TrapType Type { get; set; }
        public EnumLevel Level { get; set; }

        public string _LocalIP { get; set; }

        public Snmp()
        {
            _LocalIP = Util.GetLocalIpAddress();
        }

        public enum EnumLevel
        {
            Disabled = 1,
            Information = 2,
            Warning = 3,
            Critical = 4
        }

        public enum TrapType
        {
            begin = 1,
            end = 2,
            log = 3
        }

        public enum EnumMain
        {
            Main = 0,
            Pip = 1
        }

        public static int GetSnmpPort()
        {
            int value = 0;
            string query = String.Format($"SELECT v FROM setting WHERE k = 'snmp_port'");
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                MySqlDataReader rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    value = Convert.ToInt32(rdr["v"].ToString());
                }
                rdr.Close();
            }
            return value;
        }

        public static int GetPollingSec()
        {
            int value = 0;
            string query = String.Format($"SELECT v FROM setting WHERE k = 'polling_sec'");
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                MySqlDataReader rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    value = Convert.ToInt32(rdr["v"].ToString());
                }
                rdr.Close();
            }
            return value;
        }

        public static bool IsEnableTrap(string compareID)
        {
            logger.Info(string.Format($"compareID : {compareID}"));
            if (string.IsNullOrEmpty(compareID))
            {
                logger.Info($"compareID is null, invisible trap");
                return false;
            }
            string value = null;
            string query = String.Format(@"SELECT S.id, T.translate FROM translate T
INNER JOIN snmp S ON S.name = T.name
WHERE T.is_enable = 'N'
AND T.is_visible = 'Y'");
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                MySqlDataReader rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    value = rdr["id"].ToString();
                    int idx = compareID.LastIndexOf('.');
                    string searchID = compareID.Substring(0, idx) + ".3"; //Type은 .3으로 끝남
                    if (value.Equals(searchID))
                    {
                        logger.Info($"disable oid : {value}, compareID : {searchID} is equal, invisible trap");
                        return false;
                    }
                }
                rdr.Close();
            }

            return true;
        }

        public static string GetLevelString(int level, string oid)
        {
            string levelstring = GetAlternateLevelString(oid.Substring(0, oid.Length - 1));
            if (!string.IsNullOrEmpty(levelstring))
            {
                return levelstring;
            }
            else
            {
                return Enum.GetName(typeof(EnumLevel), level);
            }
        }

        public static string GetAlternateLevelString(string oid)
        {
            string value = null;
            string query = String.Format($"SELECT S.id, T.name, T.level FROM snmp S INNER JOIN translate T ON T.name = S.name WHERE S.id like '{oid}%'");
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                MySqlDataReader rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    value = rdr["level"].ToString();
                }
                rdr.Close();
            }
            return value;
        }

        public static string GetNameFromOid(string oid)
        {
            string value = null;
            string query = String.Format($"SELECT name FROM snmp WHERE id = '{oid}'");
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                MySqlDataReader rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    value = rdr["name"].ToString();
                }
                rdr.Close();
            }
            if (value == null)
            {
                value = oid;
            }
            return value;
        }

        public static void GetTranslateValue(string name, out string translate, out string api_msg, out string event_type)
        {
            translate = "";
            api_msg = "";
            event_type = "";
            string query = String.Format($"SELECT translate, api_msg, event_type FROM translate WHERE name = '{name}'");
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                MySqlDataReader rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    translate = rdr["translate"].ToString();
                    api_msg = rdr["api_msg"].ToString();
                    event_type = rdr["event_type"].ToString();
                }
                rdr.Close();
            }
            if (translate == null)
            {
                translate = "";
            }
            if (api_msg == null)
            {
                api_msg = "";
            }
            if (event_type == null)
            {
                event_type = "";
            }
        }

        public async void RegisterSnmpInfo()
        {
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                string query = String.Format(@"INSERT INTO snmp (id, ip, syntax, community, type) VALUES (@id, @ip, @syntax, @community, @type) ON DUPLICATE KEY UPDATE edit_time = CURRENT_TIMESTAMP(), ip = @ip, syntax = @syntax, community = @community, type = @type");
                await conn.OpenAsync();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", this.Oid);
                cmd.Parameters.AddWithValue("@ip", this.IP);
                cmd.Parameters.AddWithValue("@syntax", this.Syntax);
                cmd.Parameters.AddWithValue("@community", this.Community);
                cmd.Parameters.AddWithValue("@type", this.type);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}