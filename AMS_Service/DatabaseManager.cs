using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service
{
    internal class DatabaseManager
    {
        private DatabaseManager()
        {
            conn = null;
        }

        public String server { get; set; }
        public int port { get; set; }
        public String user { get; set; }
        public String pw { get; set; }
        public String databaseName { get; set; }

        private MySqlConnection conn;
        public static DatabaseManager instance;

        public string ConnectionString { get; set; }

        public void SetConnectionString(string server, int port, string user, string pw, string databaseName)
        {
            this.server = server;
            this.port = port;
            this.user = user;
            this.pw = pw;
            this.databaseName = databaseName;

            ConnectionString = String.Format("server={0};port={1};uid={2};pwd={3};database={4};charset=utf8mb4;SslMode=none",
                    server,
                    port,
                    user,
                    pw,
                    databaseName);
        }

        public static DatabaseManager GetInstance()
        {
            if (instance == null)
            {
                instance = new DatabaseManager();
            }
            return instance;
        }

        public void Close()
        {
            if (conn != null)
            {
                conn.Close();
            }
        }

        public void Dispose()
        {
            if (conn != null)
            {
                conn.Dispose();
            }
        }
    }
}