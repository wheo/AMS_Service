using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service
{
    public class OldMessages
    {
        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public OldMessages()
        {
            if (messageList == null)
            {
                messageList = new List<TitanMessageList>();
                logger.Info("messageList instance is creaated");
            }
        }

        public List<TitanMessageList> messageList { get; set; }
    }

    public class TitanMessageList
    {
        public string ip { get; set; }
        public string responseContent { get; set; }
    }

    public class TitanMessage
    {
        public string name { get; set; }
        public List<State> state { get; set; }
    }

    public class State
    {
        public string Name { get; set; }
        public string Description { get; set; }

        private string _Level;

        public string Level
        {
            get { return _Level; }
            set
            {
                _Level = char.ToUpper(value[0]) + value.Substring(1);
            }
        }
    }
}