using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service.Config
{
    internal class JsonConfig
    {
        public String ip { get; set; }
        public int port { get; set; }
        public String id { get; set; }
        public String pw { get; set; }
        public String DatabaseName { get; set; }

        public string ChannelAIHost { get; set; }

        public bool EnableChannelAI { get; set; } = false;

        public int timeout { get; set; } = 3;

        public string ha_role { get; set; } = "M";

        public String configFileName = "config.json";

        public static JsonConfig instance;

        public static JsonConfig getInstance()
        {
            if (instance == null)
            {
                instance = new JsonConfig();
            }
            return instance;
        }
    }
}