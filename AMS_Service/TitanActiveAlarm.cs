using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service
{
    internal class TitanActiveAlarm
    {
        public string Id { get; set; }
        public string TitanUID { get; set; }
        public string ChannelName { get; set; }
        public string Level { get; set; }

        private string _Value;

        public string Value
        {
            get { return _Value; }
            set
            {
                Id = Guid.NewGuid().ToString();
                _Value = value;
            }
        }

        public string Desc { get; set; }
    }
}