using AMS_Service.Config;
using AMS_Service.Service;
using AMS_Service.Singleton;
using log4net;
using Newtonsoft.Json;

using SnmpSharpNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace AMS_Service
{
    public partial class Service1 : ServiceBase
    {
        private int _counter = 0;

        private int _SnmpPort = 162;
        private int _PollingSec = 10;
        private JsonConfig jsonConfig;

        protected Thread m_threadSnmpGet;
        protected Thread m_threadSnmpTrap;
        protected ManualResetEvent m_shutdownEvent;
        protected TimeSpan m_get_delay;
        protected TimeSpan m_trap_delay;

        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public Service1()
        {
            InitializeComponent();
        }

        private bool LoadConfig()
        {
            // config.json 읽기
            logger.Info(Directory.GetCurrentDirectory());
            String jsonString;
            try
            {
                jsonConfig = JsonConfig.getInstance();
                if (!File.Exists(jsonConfig.configFileName))
                {
                    logger.Info(jsonConfig.configFileName + " 파일이 없습니다. 환경설정 파일을 읽지 못해 기본값으로 설정합니다.");
                    //default value
                    jsonConfig.ip = "127.0.0.1";
                    jsonConfig.port = 3306;
                    jsonConfig.id = "tnmtech";
                    jsonConfig.pw = "tnmtech";
                    jsonConfig.DatabaseName = "TNM_NMS";

                    jsonString = JsonConvert.SerializeObject(jsonConfig);
                    File.WriteAllText(jsonConfig.configFileName, jsonString);
                }

                jsonString = File.ReadAllText(jsonConfig.configFileName);
                jsonConfig = JsonConvert.DeserializeObject<JsonConfig>(jsonString);

                DatabaseManager.GetInstance().SetConnectionString(jsonConfig.ip, jsonConfig.port, jsonConfig.id, jsonConfig.pw, jsonConfig.DatabaseName);

                _SnmpPort = Snmp.GetSnmpPort();
                _PollingSec = Snmp.GetPollingSec();

                m_get_delay = new TimeSpan(0, 0, 0, _PollingSec, 0);
                m_trap_delay = new TimeSpan(0, 0, 0, 0, 500);
                logger.Info("SnmpPort : " + _SnmpPort);
                logger.Info("Polling Sec : " + _PollingSec);
            }
            catch (FileLoadException e)
            {
                logger.Error(e.ToString());
            }

            return true;
        }

        private void LogInit()
        {
            // 최초 실행시에 로그를 가져옴
            NmsInfo.GetInstance().activeLog = LogItem.GetLog();
        }

        protected override void OnStart(string[] args)
        {
            LoadConfig();
            LogInit();
            ThreadStart tsGet = new ThreadStart(this.SnmpGetService);
            ThreadStart tsTrap = new ThreadStart(this.TrapListener);
            m_shutdownEvent = new ManualResetEvent(false);
            m_threadSnmpGet = new Thread(tsGet);
            m_threadSnmpTrap = new Thread(tsTrap);
            m_threadSnmpGet.Start();
            m_threadSnmpTrap.Start();
            base.OnStart(args);
        }

        private void SnmpGetService()
        {
            logger.Info("SnmpGetService is created");

            bool signal = false;
            //최초 값
            NmsInfo.GetInstance().serverList = Server.GetServerList();
            foreach (Server t in NmsInfo.GetInstance().serverList)
            {
                logger.Info(string.Format($"최초 실행 : {t.Id}, {t.UnitName}, {t.ModelName}, {t.Status}"));
            }

            while (true)
            {
                try
                {
                    signal = m_shutdownEvent.WaitOne(m_get_delay, true);
                    if (signal)
                    {
                        break;
                    }

                    List<Server> currentServerList = Server.GetServerList();

                    /*
                    foreach (Server s in currentServerList)
                    {
                        IEnumerable<Server> results = NmsInfo.GetInstance().serverList;
                        Server c = (Server)results.Where(x => x.Id == s.Id).FirstOrDefault();
                        if (c == null)
                        {
                            NmsInfo.GetInstance().serverList.Remove(s);
                            logger.Info(string.Format($"server is removed({s.Ip}({s.UnitName}, count : {NmsInfo.GetInstance().serverList.Count})"));
                        }
                    }
                    */
                    List<Server> temp = new List<Server>();

                    foreach (Server s in NmsInfo.GetInstance().serverList)
                    {
                        Server c = (Server)currentServerList.Where(x => x.Id == s.Id).FirstOrDefault();
                        if (c == null)
                        {
                            temp.Add(s);
                        }
                    }
                    foreach (Server s in temp)
                    {
                        NmsInfo.GetInstance().serverList.Remove(s);
                        logger.Info(string.Format($"server is removed({s.Ip}, {s.Id} ({s.UnitName}, count : {NmsInfo.GetInstance().serverList.Count})"));
                    }

                    foreach (Server cs in currentServerList)
                    {
                        IEnumerable<Server> results = NmsInfo.GetInstance().serverList;
                        //Server c = (Server)results.Where(x => x.Id == s.Id).FirstOrDefault();
                        Server s = (Server)NmsInfo.GetInstance().serverList.Where(x => x.Id == cs.Id).FirstOrDefault();
                        if (s != null)
                        {
                            //logger.Info("최신상태 : " + cs.UnitName + ", " + cs.Status + " | 현재상태 : " + s.UnitName + ", " + s.Status);
                            s.PutInfo(cs);
                        }
                        else
                        {
                            NmsInfo.GetInstance().serverList.Add(cs);
                            logger.Info(string.Format($"new server added({cs.Ip}({cs.UnitName}), {s.Id}, count: {NmsInfo.GetInstance().serverList.Count})"));
                        }
                    }

                    foreach (Server server in NmsInfo.GetInstance().serverList)
                    {
                        //logger.Info(string.Format($"({server.Ip})({server.UnitName})"));
                        //string serviceOID = null;

                        logger.Info(string.Format($"({server.Ip}), {server.ModelName}, {server.IsConnect.ToString()}, {server.Status}"));

                        if (SnmpService.Get(server))
                        {
                            if (!string.IsNullOrEmpty(server.ModelName))
                            {
                                /*
                                if ("CM5000".Equals(server.ModelName))
                                {
                                    serviceOID = SnmpService._CM5000ModelName_oid;
                                }
                                else
                                {
                                    serviceOID = SnmpService._DR5000ModelName_oid;
                                }
                                */
                                // 요청 후 응답이 왔을 때
                                if (server.IsConnect != Server.EnumIsConnect.Connect)
                                {
                                    //init or disconnect
                                    if (server.IsConnect != Server.EnumIsConnect.Init)
                                    {
                                        Snmp snmp = new Snmp
                                        {
                                            IP = server.Ip,
                                            Port = "65535",
                                            Community = "public",
                                            Oid = SnmpService._MyConnectionOid,
                                            LevelString = Server.EnumStatus.Critical.ToString(),
                                            TypeValue = "end",
                                            TranslateValue = "Failed to connection"
                                        };
                                        LogItem.LoggingDatabase(snmp);
                                        logger.Info(string.Format($"{snmp.IP}, ({snmp.TypeValue}) {snmp.TranslateValue}"));
                                    }
                                    server.ConnectionErrorCount = 0;
                                    server.IsConnect = Server.EnumIsConnect.Connect;

                                    LogItem curruntStatusItem = FindCurrentStatusItem(NmsInfo.GetInstance().activeLog, server.Ip);
                                    if (curruntStatusItem != null)
                                    {
                                        server.Status = curruntStatusItem.Level;
                                        server.Message = curruntStatusItem.Value;
                                    }
                                    else
                                    {
                                        server.Status = Server.EnumStatus.Normal.ToString();
                                    }
                                }
                            }
                        }
                        else
                        {
                            //logger.Info(string.Format($" you are here this is disconnect zone ({server.Ip}) {server.IsConnect.ToString()}"));
                            if (!string.IsNullOrEmpty(server.Ip))
                            {
                                int limit = 3;
                                server.ConnectionErrorCount++;

                                if (server.IsConnect != Server.EnumIsConnect.Disconnect && server.ConnectionErrorCount > limit)
                                {
                                    //connect or init

                                    Snmp snmp = new Snmp
                                    {
                                        IP = server.Ip,
                                        Port = "65535",
                                        Community = "public",
                                        Oid = SnmpService._MyConnectionOid,
                                        LevelString = Server.EnumStatus.Critical.ToString(),
                                        TypeValue = "begin",
                                        TranslateValue = "Failed to connection"
                                    };

                                    //중복 Failed to connection check
                                    if (snmp.ConnectionCheck())
                                    {
                                        LogItem.LoggingDatabase(snmp);
                                        logger.Info(string.Format($"{snmp.IP}, ({snmp.TypeValue}) {snmp.TranslateValue}"));
                                    }
                                    else
                                    {
                                        logger.Info(string.Format($"중복 connection error log 존재함 ({snmp.IP})"));
                                    }
                                }
                                if (server.ConnectionErrorCount > limit)
                                {
                                    server.IsConnect = Server.EnumIsConnect.Disconnect;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex.ToString());
                }
                finally
                {
                    //logger.Info("SnmpGetService is Alive(" + ++_counter + ")");
                }
            }

            logger.Info(" *** SnmpGetService exit ***");
        }

        private void TrapListener()
        {
            logger.Info("TrapListener is created");

            //Titan Live alarm Oid is only one
            const string TitanLiveAlarmOid = "1.3.6.1.4.1.27338.40.5";

            // Construct a socket and bind it to the trap manager port 162
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = 1000;
            socket.SendTimeout = 1000;
            IPEndPoint ipep = new IPEndPoint(IPAddress.Any, _SnmpPort);
            EndPoint ep = (EndPoint)ipep;

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 100);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            socket.Bind(ep);
            // Disable timeout processing. Just block until packet is received

            int inlen = -1;
            logger.Info(string.Format($"Waiting for snmp trap"));
            bool signal = false;
            try
            {
                while (true)
                {
                    signal = m_shutdownEvent.WaitOne(m_trap_delay, true);
                    if (signal)
                    {
                        break;
                    }

                    byte[] indata = new byte[16 * 1024];
                    // 16KB receive buffer int inlen = 0;
                    IPEndPoint peer = new IPEndPoint(IPAddress.Any, 0);
                    EndPoint inep = (EndPoint)peer;
                    try
                    {
                        inlen = socket.ReceiveFrom(indata, ref inep);
                    }
                    catch (Exception)
                    {
                        //logger.Error(string.Format("Exception {0}", ex.Message));
                        inlen = -1;
                    }
                    if (inlen > 0)
                    {
                        // Check protocol version int
                        int ver = SnmpPacket.GetProtocolVersion(indata, inlen);
                        if (ver == (int)SnmpVersion.Ver1)
                        {
                            // Parse SNMP Version 1 TRAP packet
                            SnmpV1TrapPacket pkt = new SnmpV1TrapPacket();
                            pkt.decode(indata, inlen);
                            logger.Info(string.Format("** SNMP Version 1 TRAP received from {0}:", inep.ToString()));
                            logger.Info(string.Format("*** Trap generic: {0}", pkt.Pdu.Generic));
                            logger.Info(string.Format("*** Trap specific: {0}", pkt.Pdu.Specific));
                            logger.Info(string.Format("*** Agent address: {0}", pkt.Pdu.AgentAddress.ToString()));
                            logger.Info(string.Format("*** Timestamp: {0}", pkt.Pdu.TimeStamp.ToString()));
                            logger.Info(string.Format("*** VarBind count: {0}", pkt.Pdu.VbList.Count));
                            logger.Info("*** VarBind content:");
                            foreach (Vb v in pkt.Pdu.VbList)
                            {
                                logger.Info(string.Format("**** {0} {1}: {2}", v.Oid.ToString(), SnmpConstants.GetTypeName(v.Value.Type), v.Value.ToString()));
                            }
                            logger.Info("** End of SNMP Version 1 TRAP data.");
                        }
                        else
                        {
                            Snmp snmp = new Snmp();
                            // Parse SNMP Version 2 TRAP packet
                            SnmpV2Packet pkt = new SnmpV2Packet();
                            pkt.decode(indata, inlen);
                            logger.Info(string.Format("** SNMP Version 2 TRAP received from {0}:", inep.ToString()));
                            if ((SnmpSharpNet.PduType)pkt.Pdu.Type != PduType.V2Trap &&
                                ((SnmpSharpNet.PduType)pkt.Pdu.Type != PduType.Inform))
                            {
                                logger.Info("*** NOT an SNMPv2 trap or inform ****");
                            }
                            else
                            {
                                logger.Info(string.Format("*** Community: {0}", pkt.Community.ToString()));
                                logger.Info(string.Format("*** VarBind count: {0}", pkt.Pdu.VbList.Count));
                                logger.Info(string.Format("*** VarBind content:"));

                                foreach (Vb v in pkt.Pdu.VbList)
                                {
                                    snmp.Id = v.Oid.ToString();
                                    snmp.IP = inep.ToString().Split(':')[0];
                                    snmp.Port = inep.ToString().Split(':')[1];
                                    snmp.Syntax = SnmpConstants.GetTypeName(v.Value.Type);
                                    snmp.Community = pkt.Community.ToString();
                                    snmp.Value = v.Value.ToString();
                                    snmp.type = "trap";

                                    logger.Info("Oid : " + v.Oid.ToString());
                                    logger.Info("value : " + v.Value.ToString());

                                    if (snmp.Id.Contains(TitanLiveAlarmOid))
                                    {
                                        string TitanLiveTrapType = snmp.Id.Split('.').Last();

                                        if (TitanLiveTrapType == "9")
                                        {
                                            string levelString = v.Value.ToString();
                                            if (levelString.Equals("major"))
                                            {
                                                levelString = "warning";
                                            }
                                            else if (levelString.Equals("minor"))
                                            {
                                                levelString = "warning";
                                            }

                                            snmp.LevelString = levelString.First().ToString().ToUpper() + levelString.ToString().Substring(1);
                                            logger.Info("TitanLevelString : " + snmp.LevelString);
                                        }
                                        else if (TitanLiveTrapType == "7")
                                        {
                                            if (v.Value.ToString() == "start")
                                            {
                                                snmp.TypeValue = "begin";
                                            }
                                            else
                                            {
                                                snmp.TypeValue = v.Value.ToString();
                                            }
                                            //일부러 oid 넣지 않고 oid가 null일 경우를 titan으로 간주함
                                            //snmp.Oid = v.Oid.ToString();
                                        }
                                        else if (TitanLiveTrapType == "4")
                                        {
                                            snmp.TranslateValue = v.Value.ToString();
                                            snmp.IsTypeTrap = true;
                                        }
                                        else if (TitanLiveTrapType == "8")
                                        {
                                            snmp.TitanUID = v.Value.ToString();
                                        }
                                        else if (TitanLiveTrapType == "11")
                                        {
                                            snmp.TitanName = v.Value.ToString();
                                        }
                                    }
                                    else
                                    {
                                        string value = Snmp.GetNameFromOid(v.Oid.ToString());
                                        logger.Info("GetNameFromOid value : " + value);

                                        if (value.LastIndexOf("Level") > 0)
                                        {
                                            snmp.LevelString = Snmp.GetLevelString(Convert.ToInt32(v.Value.ToString()), v.Oid.ToString());
                                            logger.Info("LevelString : " + snmp.LevelString);
                                        }
                                        else if (value.LastIndexOf("Type") > 0)
                                        {
                                            snmp.TranslateValue = Snmp.GetTranslateValue(value);
                                            snmp.TypeValue = Enum.GetName(typeof(Snmp.TrapType), Convert.ToInt32(v.Value.ToString()));
                                            snmp.Oid = v.Oid.ToString();
                                            snmp.IsTypeTrap = true;
                                            logger.Info("TypeValue : " + snmp.TypeValue);
                                        }
                                        else if (value.LastIndexOf("Channel") > 0)
                                        {
                                            snmp.Channel = Convert.ToInt32(v.Value.ToString()) + 1; //0받으면 채널 1로
                                        }
                                        else if (value.LastIndexOf("Main") > 0)
                                        {
                                            snmp.Main = Enum.GetName(typeof(Snmp.EnumMain), Convert.ToInt32(v.Value.ToString()));
                                        }

                                        //데이터베이스 테이블을 만들기 위해 등록함(로그는 translate 테이블을 이용하자)
                                        snmp.RegisterSnmpInfo();

                                        logger.Info(String.Format("[{0}] Trap : {1} {2}: {3}", inep.ToString().Split(':')[0], v.Oid.ToString(), SnmpConstants.GetTypeName(v.Value.Type), v.Value.ToString()));
                                    }
                                }

                                //CM, DR 기록
                                if (Snmp.IsEnableTrap(snmp.Oid) || snmp.Id.Contains(TitanLiveAlarmOid))
                                {
                                    if (!String.IsNullOrEmpty(snmp.LevelString))
                                    {
                                        Server s = null;
                                        foreach (var server in NmsInfo.GetInstance().serverList)
                                        {
                                            if (server.Ip == snmp.IP)
                                            {
                                                s = server;
                                                break;
                                            }
                                        }

                                        if (s != null)
                                        {
                                            if (!snmp.LevelString.Equals("Disabled") && string.Equals(snmp.TypeValue, "begin"))
                                            {
                                                if (FindItemDuplicateTrap(NmsInfo.GetInstance().activeLog, s, snmp.Oid).Count == 0 ||
                                                    FindItemDuplicateTitanTrap(NmsInfo.GetInstance().activeLog, s, snmp.TranslateValue).Count == 0)
                                                {
                                                    if (snmp.IsTypeTrap)
                                                    {
                                                        s.ErrorCount++;
                                                    }
                                                    s.Message = snmp.TranslateValue;

                                                    LogItem log = new LogItem
                                                    {
                                                        Ip = s.Ip,
                                                        Level = snmp.LevelString,
                                                        Name = s.UnitName,
                                                        Oid = snmp.Oid,
                                                        IsConnection = true,
                                                        Value = snmp.TranslateValue,
                                                        TypeValue = "begin"
                                                    };

                                                    LoggingDisplay(log);
                                                    LogItem.LoggingDatabase(snmp);
                                                }
                                            }
                                            else if (!snmp.LevelString.Equals("Disabled") && string.Equals(snmp.TypeValue, "end"))
                                            {
                                                List<LogItem> activeItems;
                                                if (!string.IsNullOrEmpty(snmp.Oid))
                                                {
                                                    activeItems = FindItemFromOid(NmsInfo.GetInstance().activeLog, s, snmp.Oid);
                                                }
                                                else
                                                {
                                                    activeItems = FindItemFromValue(NmsInfo.GetInstance().activeLog, s, snmp.TranslateValue);
                                                }
                                                if (activeItems.Count > 0)
                                                {
                                                    foreach (var item in activeItems)
                                                    {
                                                        if (s.ErrorCount > 0 && snmp.IsTypeTrap)
                                                        {
                                                            s.ErrorCount--;
                                                        }
                                                        LogItem restoreItem = FindCurrentStatusItem(NmsInfo.GetInstance().activeLog, s.Ip);
                                                        if (restoreItem != null)
                                                        {
                                                            s.Status = restoreItem.Level;
                                                            s.Message = restoreItem.Value;
                                                            if (s.ErrorCount == 0)
                                                            {
                                                                s.Status = Server.EnumStatus.Normal.ToString();
                                                            }
                                                        }
                                                        else
                                                        {
                                                            s.Status = Server.EnumStatus.Normal.ToString();
                                                        }
                                                    }

                                                    LoggingDisplay(activeItems);
                                                    LogItem.LoggingDatabase(snmp);
                                                }
                                            }

                                            if (!string.Equals(snmp.TypeValue, "log"))
                                            {
                                                if (s.ErrorCount > 0)
                                                {
                                                    s.Status = Server.CompareState(s.Status, snmp.LevelString);
                                                }
                                                else
                                                {
                                                    s.Status = Server.EnumStatus.Normal.ToString();
                                                }
                                            }
                                        }
                                    }
                                }
                                logger.Info("** End of SNMP Version 2 TRAP data.");
                            }
                        }
                    }
                    else
                    {
                        if (inlen == 0)
                        {
                            logger.Info("Zero length packet received.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e.ToString());
            }

            logger.Info(" *** TrapListener exit ***");
        }

        private void LoggingDisplay(LogItem log)
        {
            NmsInfo.GetInstance().activeLog.Insert(0, log);
        }

        private void LoggingDisplay(List<LogItem> activeLog)
        {
            foreach (var log in activeLog)
            {
                NmsInfo.GetInstance().activeLogRemove(log);
            }
        }

        private List<LogItem> FindItemDuplicateTrap(List<LogItem> ocl, Server s, string oid)
        {
            IEnumerable<LogItem> items =
                from x in ocl
                where x.Oid == oid && x.Ip == s.Ip && x.TypeValue == "begin"
                select x;
            return items.ToList();
        }

        private LogItem FindCurrentStatusItem(List<LogItem> ocl, string Ip)
        {
            IEnumerable<LogItem> items =
                from x in ocl
                where x.Ip == Ip
                select x;
            IEnumerable<LogItem> item = items.OrderByDescending(x => x.LevelPriority).Take(1);
            if (item.Count() > 0)
            {
                return (LogItem)item.ElementAt(0);
            }
            else
            {
                return null;
            }
        }

        private List<LogItem> FindItemDuplicateTitanTrap(List<LogItem> ocl, Server s, string value)
        {
            IEnumerable<LogItem> items =
                from x in ocl
                where x.Value == value && x.Ip == s.Ip && x.TypeValue == "begin"
                select x;
            return items.ToList();
        }

        private List<LogItem> FindItemFromOid(List<LogItem> ocl, Server s, string oid)
        {
            IEnumerable<LogItem> items =
                from x in ocl
                where x.Oid == oid && x.Ip == s.Ip && string.IsNullOrEmpty(x.EndAt)
                select x;
            return items.ToList();
        }

        private List<LogItem> FindItemFromValue(List<LogItem> ocl, Server s, string value)
        {
            IEnumerable<LogItem> items =
                from x in ocl
                where x.Value == value && x.Ip == s.Ip
                select x;
            return items.ToList();
        }

        protected override void OnStop()
        {
            m_shutdownEvent.Set();

            //wait for thread to stop giving it 10 second
            m_threadSnmpTrap.Join(10000);
            m_threadSnmpGet.Join(10000);
            base.OnStop();
        }
    }
}