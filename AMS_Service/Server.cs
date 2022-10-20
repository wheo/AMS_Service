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
    public class Server : INotifyPropertyChanged
    {
        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        [JsonIgnore]
        private string _Status;

        [JsonIgnore]
        private string _Type;

        [JsonIgnore]
        private string _Color;

        [JsonIgnore]
        private string _Uptime;

        [JsonIgnore]
        public string Uptime
        {
            get { return _Uptime; }

            set
            {
                if (_Uptime != value && !string.IsNullOrEmpty(value))
                {
                    _Uptime = value;
                    UptimeFormat = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Uptime"));
                }
            }
        }

        [JsonIgnore]
        private string _UptimeFormat;

        [JsonIgnore]
        public string UptimeFormat
        {
            get { return _UptimeFormat; }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    string[] uptime;
                    uptime = value.Split(' ');
                    int day = Convert.ToInt32(uptime[0].Substring(0, uptime[0].Length - 1));
                    int hour = Convert.ToInt32(uptime[1].Substring(0, uptime[1].Length - 1));
                    int min = Convert.ToInt32(uptime[2].Substring(0, uptime[2].Length - 1));
                    int sec = Convert.ToInt32(uptime[3].Substring(0, uptime[3].Length - 1));
                    _UptimeFormat = String.Format($"{day}일 {hour}시 {min}분 {sec}초");
                }
            }
        }

        public string Id { get; set; }
        public string Ip { get; set; }

        [JsonIgnore]
        private string _UnitName { get; set; }

        public string UnitName
        {
            get
            { return _UnitName; }
            set
            {
                if (_UnitName != value)
                {
                    _UnitName = value;
                }
            }
        }

        public string Gid { get; set; }

        public string GroupName { get; set; }

        [JsonIgnore]
        public string Version { get; set; }

        [JsonIgnore]
        private int _ServicePid { get; set; }

        [JsonIgnore]
        public int ServicePid
        {
            get
            {
                return _ServicePid;
            }
            set
            {
                if (_ServicePid != value)
                {
                    _ServicePid = value;
                }
            }
        }

        [JsonIgnore]
        public int _VideoOutputId { get; set; }

        [JsonIgnore]
        public int VideoOutputId
        {
            get
            {
                return _VideoOutputId;
            }
            set
            {
                if (_VideoOutputId != value)
                {
                    _VideoOutputId = value;
                }
            }
        }

        public string ModelName
        {
            get { return _Type; }
            set
            {
                if (_Type != value)
                {
                    _Type = value;
                }
            }
        }

        [JsonIgnore]
        public string Color

        {
            get { return _Color; }
            set
            {
                if (_Color != value)
                {
                    _Color = value;
                }
            }
        }

        [JsonIgnore]
        public string UpdateState
        {
            set
            {
                //this.Status = value;
                this.Status = GetServerStatusFromActive(value);
                OnPropertyChanged(new PropertyChangedEventArgs("Status"));
                //logger.Info($"{this.Ip} => ({Status}) api updated");
            }
        }

        private string GetServerStatusFromActive(string level)
        {
            if (!string.IsNullOrEmpty(this.Ip))
            {
                using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
                {
                    string query = string.Format($"SELECT level from active WHERE ip = '{this.Ip}'");
                    conn.Open();
                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    MySqlDataReader rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        //logger.Info($"({this.Ip}) level : {level}, active : {rdr["level"].ToString()}");
                        level = CompareState(level, rdr["level"].ToString());
                        //logger.Info($"result : {level}");
                    }
                    rdr.Close();
                }
            }
            //logger.Info($"finally level : {level}");
            return level;
        }

        [JsonIgnore]
        public string Status
        {
            get { return _Status; }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    //logger.Info("this id : " + this.Id + ", _s : " + _Status + ", v : " + value);
                    if (!value.Equals(this.Status))
                    {
                        if (value.ToLower().Equals("normal"))
                        {
                            this.Color = "#00FF7F";
                        }
                        else if (value.ToLower().Equals("critical"))
                        {
                            this.Color = "#EE0000";
                        }
                        else if (value.ToLower().Equals("warning"))
                        {
                            this.Color = "#FF8000";
                        }
                        else if (value.ToLower().Equals("information"))
                        {
                            this.Color = "#0000FF";
                        }
                        else
                        {
                            this.Color = "#CCECFF";
                        }
                        if (!string.IsNullOrEmpty(this.Status))
                        {
                            //logger.Info($"({this.Ip}) {this.ModelName} ({this.Status}) => ({value}) changed");
                            _Status = value;
                        }
                    }
                }
                else
                {
                    _Status = "Normal";
                }
            }
        }

        public enum EnumStatus
        {
            Normal = 0,
            Disabled = 2,
            Information = 3,
            Warning = 4,
            Critical = 5
        }

        public EnumIsConnect IsConnect { get; set; }

        public enum EnumIsConnect
        {
            Connect = 0,
            Disconnect = 1
        }

        [JsonIgnore]
        public string IsMute { get; set; }

        [JsonIgnore]
        public string Reboot { get; set; }

        public Server()
        {
            _Status = "Normal";
            IsConnect = Server.EnumIsConnect.Connect;
        }

        public static string GetServerID(string ip)
        {
            string id = null;
            if (!string.IsNullOrEmpty(ip))
            {
                using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
                {
                    string query = string.Format($"SELECT id from server WHERE ip = '{ip}'");
                    conn.Open();
                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    MySqlDataReader rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        id = rdr["id"].ToString();
                    }
                    rdr.Close();
                }
            }
            return id;
        }

        public static string GetServerName(string ip)
        {
            string name = null;
            try
            {
                if (!string.IsNullOrEmpty(ip))
                {
                    using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
                    {
                        string query = string.Format($"SELECT name from server WHERE ip = '{ip}'");
                        conn.Open();
                        MySqlCommand cmd = new MySqlCommand(query, conn);
                        MySqlDataReader rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            name = rdr["name"].ToString();
                        }
                        rdr.Close();
                    }
                }
            }
            catch
            {
                name = "";
            }
            return name;
        }

        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        public void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, e);
                if (e.PropertyName.Equals("Status"))
                {
                    UpdateServerStatus();
                }
                else if (e.PropertyName.Equals("Uptime"))
                {
                    UpdateServerUptime();
                }
            }
        }

        public static List<Server> GetServerList()
        {
            DataTable dt = new DataTable();
            string query = @"SELECT S.id, S.gid, S.name, S.ip, S.type, IFNULL(S.uptime,'') as uptime, S.status, S.ismute, S.reboot
, IF(S.status = 'Critical', 'Red', IF(S.status = 'Warning', '#FF8000', IF(S.status = 'Information', 'Blue', 'Green'))) AS color
, G.name as grp_name
, A.path FROM server S
LEFT JOIN asset A ON S.status = A.id
LEFT JOIN grp G ON G.id = S.gid
ORDER BY S.name ASC";

            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                MySqlDataAdapter adpt = new MySqlDataAdapter(query, conn);
                adpt.Fill(dt);
            }

            return dt.AsEnumerable().Select(row => new Server
            {
                Id = row.Field<string>("id"),
                Gid = row.Field<string>("gid"),
                UnitName = row.Field<string>("name"),
                Ip = row.Field<string>("ip"),
                GroupName = row.Field<string>("grp_name"),
                ModelName = row.Field<string>("type"),
                Uptime = row.Field<string>("uptime"),
                Status = row.Field<string>("status"),
                IsMute = row.Field<string>("ismute"),
                Reboot = row.Field<string>("reboot")
            }).ToList();
        }

        public string CompareState(string a, string b)
        {
            a = char.ToUpper(a[0]) + a.Substring(1);
            b = char.ToUpper(b[0]) + b.Substring(1);
            EnumStatus e1 = (EnumStatus)Enum.Parse(typeof(EnumStatus), a);
            EnumStatus e2 = (EnumStatus)Enum.Parse(typeof(EnumStatus), b);
            string str = null;

            if (e1 > e2)
            {
                str = Enum.GetName(typeof(EnumStatus), e1);
            }
            else
            {
                str = Enum.GetName(typeof(EnumStatus), e2);
            }
            return str;
        }

        public async void UpdateServerStatus()
        {
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                if (string.IsNullOrEmpty(this.ModelName))
                    this.ModelName = "CM5000";

                try
                {
                    await conn.OpenAsync();
                    string sql = "UPDATE server set status = @status WHERE id = @id";

                    using (MySqlCommand cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", this.Id);
                        cmd.Parameters.AddWithValue("@status", this.Status);
                        await cmd.ExecuteNonQueryAsync();

                        //logger.Info($"UpdateServerStatus ({this.Status}), {this.Ip}, {this.Uptime}, {this.UptimeFormat}");
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
        }

        public async void UpdateServerUptime()
        {
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                if (string.IsNullOrEmpty(this.ModelName))
                    this.ModelName = "CM5000";

                try
                {
                    await conn.OpenAsync();
                    string sql = "UPDATE server set uptime = @uptime, uptime_format = @uptime_format WHERE id = @id";

                    using (MySqlCommand cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", this.Id);
                        cmd.Parameters.AddWithValue("@uptime", this.Uptime);
                        cmd.Parameters.AddWithValue("@uptime_format", this.UptimeFormat);
                        await cmd.ExecuteNonQueryAsync();

                        //logger.Info($"UpdateServerStatus ({this.Status}), {this.Ip}, {this.Uptime}, {this.UptimeFormat}");
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
        }

        public static int UpdateServerInformation(Server server)
        {
            int ret = 0;
            string query = "UPDATE server set version = @version WHERE id = @id";
            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                conn.Open();
                MySqlCommand cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", server.Id);
                cmd.Parameters.AddWithValue("@version", server.Version);
                //cmd.Prepare();
                ret = cmd.ExecuteNonQuery();
            }
            return ret;
        }

        public static bool ImportServer(ObservableCollection<Server> servers)
        {
            /* 1.transation
             * 2. Delete server table
             * 3. insert new server info
             * 4. commit
             */

            using (MySqlConnection conn = new MySqlConnection(DatabaseManager.GetInstance().ConnectionString))
            {
                int ret = 0;
                conn.Open();
                MySqlTransaction trans = conn.BeginTransaction();
                try
                {
                    string query = $"DELETE FROM server";
                    MySqlCommand cmd = conn.CreateCommand();
                    cmd.CommandText = query;
                    //cmd.Prepare();
                    ret = cmd.ExecuteNonQuery();

                    query = "INSERT INTO server (id, ip, name, gid) VALUES (@id, @ip, @name, @gid)";
                    cmd.CommandText = query;
                    foreach (Server s in servers)
                    {
                        if (!string.IsNullOrEmpty(s.Id))
                        {
                            cmd.Parameters.AddWithValue("@id", s.Id);
                            cmd.Parameters.AddWithValue("@name", s.UnitName);
                            cmd.Parameters.AddWithValue("@ip", s.Ip);
                            cmd.Parameters.AddWithValue("@gid", s.Gid);
                            //cmd.Parameters.AddWithValue("@location", s.Location);
                            //cmd.Prepare();
                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();
                        }
                    }
                    trans.Commit();
                }
                catch (Exception ex)
                {
                    logger.Error(ex.ToString());
                    trans.Rollback();
                    return false;
                }
            }

            return true;
        }
    }
}