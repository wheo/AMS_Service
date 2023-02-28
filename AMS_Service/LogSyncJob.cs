using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service
{
    internal class LogSyncJob
    {
        public bool isReboot { get; set; } = false;

        public string target_ip { get; set; }

        public string TitanUID { get; set; }
    }
}