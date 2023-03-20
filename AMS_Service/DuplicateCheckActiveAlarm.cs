using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service
{
    public class DuplicateCheckActiveAlarm
    {
        public string Id { get; set; }
        public string Ip { get; set; }

        public string Level { get; set; }

        public string ChannelValue { get; set; }
        public string UID { get; set; }

        private string _State;

        public string State
        {
            get { return _State; }
            set
            {
                Id = Guid.NewGuid().ToString();
                _State = value;
            }
        }
    }
}