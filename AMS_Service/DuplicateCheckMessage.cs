using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service
{
    public class DuplicateCheckOldMessage
    {
        public DuplicateCheckOldMessage()
        {
            if (oldMessages == null)
            {
                oldMessages = new List<DuplicateCheckMessage>();
            }
        }

        public List<DuplicateCheckMessage> oldMessages { get; set; }
    }

    public class DuplicateCheckMessage
    {
        public string state { get; set; }
        public string level { get; set; }

        public string ip { get; set; }
        public string channel_value { get; set; }

        public string uid { get; set; }
    }
}