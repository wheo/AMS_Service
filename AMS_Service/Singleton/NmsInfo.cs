using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_Service.Singleton
{
    internal class NmsInfo
    {
        private NmsInfo()
        {
        }

        public List<Server> serverList;

        [JsonIgnore]
        public List<LogItem> activeLog;

        public void activeLogRemove(LogItem item)
        {
            this.activeLog.Remove(item);
        }

        public class LimitedSizeObservableCollection<T> : ObservableCollection<T>
        {
            public int Capacity { get; }

            public LimitedSizeObservableCollection(List<T> list, int capacity)
            {
                Capacity = capacity;
                CopyFrom(list);
            }

            private void CopyFrom(IEnumerable<T> collection)
            {
                IList<T> items = Items;
                if (collection != null && items != null)
                {
                    using (IEnumerator<T> enumerator = collection.GetEnumerator())
                    {
                        while (enumerator.MoveNext())
                        {
                            items.Add(enumerator.Current);
                        }
                    }
                }
            }

            public new void Add(T item)
            {
                if (Count >= Capacity)
                {
                    this.RemoveAt(0);
                }
                base.Add(item);
            }
        }

        public ObservableCollection<Server> RealServerList()
        {
            // oc deep copy
            ObservableCollection<Server> oc = new ObservableCollection<Server>(serverList);

            for (int i = 0; i < oc.Count; i++)
            {
                if (string.IsNullOrEmpty(oc[i].Id))
                {
                    oc.Remove(oc[i]);
                    --i;
                }
            }
            return oc;
        }

        private static NmsInfo instance = null;

        public static NmsInfo GetInstance()
        {
            if (instance == null)
            {
                instance = new NmsInfo();
            }
            return instance;
        }
    }
}