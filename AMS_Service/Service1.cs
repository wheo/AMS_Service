﻿using AMS_Service.Config;
using AMS_Service.ExternalApi;
using AMS_Service.Service;
using AMS_Service.Singleton;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        // private int _counter = 0;

        private int _SnmpPort = 162;
        private int _PollingSec = 10; // sec
        private int _DeviceReport = 86400; // sec
        private int _SnmpGetTimeout = 300; // millisec
        private int _SnmpRetryCount = 3;
        private int _SnmpTrapWaitingTime = 10;
        private string _Ha_role = "M";
        public static int _HttpTimeout = 10;

        public static string _Api_ip;
        public static int _Api_port;

        private JsonConfig jsonConfig;

        protected Thread m_threadSnmpGet;
        protected Thread m_threadSnmpTrap;
        protected Thread m_threadWorker;

        protected Thread m_threadDuplicateCheckWorker;

        protected ManualResetEvent m_shutdownEvent;
        protected TimeSpan m_get_delay;
        protected TimeSpan m_worker_delay;
        protected TimeSpan m_trap_delay;

        private CancellationTokenSource _cts;
        private OldMessages _oldMessages;

        // private static object _lock = new object(); // 아래 SemaphoreSlim으로 대체
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public Service1()
        {
            InitializeComponent();
        }

        private bool LoadConfig()
        {
            // config.json 읽기
            System.IO.Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
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
                    jsonConfig.id = "root";
                    jsonConfig.pw = "tnmtech";
                    jsonConfig.DatabaseName = "TNM_NMS";

                    jsonConfig.api_ip = "127.0.0.1";
                    jsonConfig.api_port = 80;

                    jsonConfig.ChannelAIHost = "http://61.78.151.69:8081";
                    jsonConfig.EnableChannelAI = false;
                    jsonConfig.ha_role = "M";

                    jsonString = JsonConvert.SerializeObject(jsonConfig);
                    File.WriteAllText(jsonConfig.configFileName, jsonString);
                }

                jsonString = File.ReadAllText(jsonConfig.configFileName);
                jsonConfig = JsonConvert.DeserializeObject<JsonConfig>(jsonString);

                DatabaseManager.GetInstance().SetConnectionString(jsonConfig.ip, jsonConfig.port, jsonConfig.id, jsonConfig.pw, jsonConfig.DatabaseName);

                ChannelAI.Host = jsonConfig.ChannelAIHost;
                ChannelAI.IsEnable = jsonConfig.EnableChannelAI;
                ChannelAI.Ha_role = jsonConfig.ha_role;
                ChannelAI.timeout = jsonConfig.timeout;
                _Api_ip = jsonConfig.api_ip;
                _Api_port = jsonConfig.api_port;

                logger.Info($"ChannelAI.Host : {ChannelAI.Host}");
                logger.Info($"Enable ChannelAI : {ChannelAI.IsEnable}");
                logger.Info($"ha_role : {_Ha_role}");
                logger.Info($"http timeout : {ChannelAI.timeout}");

                logger.Info($"API IP : {_Api_ip}");
                logger.Info($"API Port : {_Api_port}");

                logger.Info($"{jsonConfig.ip}, {jsonConfig.port}, {jsonConfig.id}, {jsonConfig.pw}, {jsonConfig.DatabaseName}");

                _SnmpPort = Snmp.GetSnmpPort();
                _PollingSec = Snmp.GetPollingSec();
                _HttpTimeout = ChannelAI.timeout;

                m_get_delay = new TimeSpan(0, 0, 0, _PollingSec, 0);
                m_trap_delay = new TimeSpan(0, 0, 0, 0, _SnmpTrapWaitingTime);
                m_worker_delay = new TimeSpan(0, 0, 0, 1, 0); // worker 1 sec

                logger.Info($"SnmpPort : {_SnmpPort}");
                logger.Info($"Polling Sec : {_PollingSec}");
                logger.Info($"Snmp Get Timeout  : {_SnmpGetTimeout}");
                logger.Info($"Snmp Get Retry Count : {_SnmpRetryCount}");
                logger.Info($"Snmp Trap Waiting Time : {_SnmpTrapWaitingTime}");
            }
            catch (FileLoadException e)
            {
                logger.Error(e.ToString());
            }

            return true;
        }

        protected override void OnStart(string[] args)
        {
            LoadConfig();
            m_shutdownEvent = new ManualResetEvent(false);

            ThreadStart tsGet = new ThreadStart(this.SnmpGetServiceAsync);
            //ThreadStart tsTrap = new ThreadStart(this.TrapListenerAsync);
            ThreadStart tsApiWorker = new ThreadStart(this.ApiWorker);

            ThreadStart tsApiDuplicateCheckWorker = new ThreadStart(this.ApiDuplicateCheckWorker);

            m_threadSnmpGet = new Thread(tsGet);
            //m_threadSnmpTrap = new Thread(tsTrap);
            m_threadWorker = new Thread(tsApiWorker);

            m_threadDuplicateCheckWorker = new Thread(tsApiDuplicateCheckWorker);

            m_threadSnmpGet.Start();
            //m_threadSnmpTrap.Start();
            m_threadWorker.Start();

            m_threadDuplicateCheckWorker.Start();

            _cts = new CancellationTokenSource();
            _oldMessages = new OldMessages();

            base.OnStart(args);
        }

        private List<DuplicateCheckActiveAlarm> oldDupCheckAlarms = new List<DuplicateCheckActiveAlarm>();

        private async void ApiDuplicateCheckWorker()
        {
            logger.Info("Api Duplicate Check Worker Start...");

            bool signal = false;

            while (true)
            {
                try
                {
                    signal = m_shutdownEvent.WaitOne(m_worker_delay, true);
                    if (signal)
                    {
                        logger.Info($"signal TERM in while");
                        break;
                    }

                    string uri = $"http://{_Api_ip}:{_Api_port}/api/v2/servicesmngt/services/state/errorcheck";
                    var response = Utils.Http.GetAsync(uri);
                    string content = await response.Result.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        List<DuplicateCheckMessage> duplicateMsgs = JsonConvert.DeserializeObject<List<DuplicateCheckMessage>>(content);
                        List<DuplicateCheckActiveAlarm> alarms = new List<DuplicateCheckActiveAlarm>();

                        foreach (var msg in duplicateMsgs)
                        {
                            // logger.Info($"{msg.level}, {msg.state}, {msg.name}");

                            DuplicateCheckActiveAlarm alarm = new DuplicateCheckActiveAlarm
                            {
                                Level = msg.level,
                                State = msg.state,
                                Ip = msg.ip,
                                ChannelValue = msg.channel_value
                            };
                            alarms.Add(alarm);
                        }

                        // 데이터베이스 안정성을 위해 세마포어 공유
                        await _semaphore.WaitAsync();
                        try
                        {
                            await LogManager.ActiveDupCheckAlarm(alarms, oldDupCheckAlarms);
                            oldDupCheckAlarms = alarms;
                        }
                        catch (Exception e)
                        {
                            logger.Error(e.ToString());
                        }
                        finally
                        {
                            _semaphore.Release();
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
            logger.Info(" *** Api Duplicate Check Worker exit ***");
        }

        private async void ApiWorker()
        {
            logger.Info("Api Worker Start...");

            bool signal = false;

            // 최초 실행시 모든 액티브 알람 삭제
            // 여기
            // ****
            LogManager.InitActiveAlarm();

            while (true)
            {
                try
                {
                    signal = m_shutdownEvent.WaitOne(m_worker_delay, true);
                    if (signal)
                    {
                        logger.Info($"signal TERM in while");
                        break;
                    }
                    List<Server> servers = null;

                    servers = Server.GetServerList();

                    List<Task<bool>> taskList = new List<Task<bool>>();

                    foreach (Server server in servers)
                    {
                        taskList.Add(Task.Run(() => ApiTask(server)));
                    }
                    // logger.Info("**** Task Any 시작 ***");
                    while (taskList.Any())
                    {
                        Task<bool> taskCompleted = await Task.WhenAny(taskList);
                        // 상태 체크
                        // logger.Info($"{taskCompleted.Result}");
                        taskList.Remove(taskCompleted);
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
            logger.Info(" *** Api Worker exit ***");
        }

        private List<OldAlarms> oldAlarms = new List<OldAlarms>();

        private async Task<bool> ApiTask(Server server)
        {
            if (oldAlarms.Where(x => x.Ip == server.Ip).Count() == 0)
            {
                OldAlarms o = new OldAlarms();
                o.Ip = server.Ip;
                oldAlarms.Add(o);
                logger.Info($"{o.Ip} added");
            }
            string uri = $"http://{_Api_ip}:{_Api_port}/api/v2/log/sync/{server.Ip}";
            // logger.Info(uri);
            var response = Utils.Http.GetAsync(uri);
            string content = await response.Result.Content.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(content) && content.Length > 7)
            {
                List<TitanMessage> titanmsgs = JsonConvert.DeserializeObject<List<TitanMessage>>(content);
                List<TitanActiveAlarm> alarms = new List<TitanActiveAlarm>();

                foreach (var msg in titanmsgs)
                {
                    foreach (var state in msg.state)
                    {
                        if (string.IsNullOrEmpty(state.Level))
                        {
                            if (state.Name.Contains("Service State Is Invalid") || state.Name.Contains("Service State Is Stopped"))
                            {
                                state.Level = "Critical";
                            }
                            else
                            {
                                state.Level = "Information";
                            }
                        }

                        // logger.Info($"*** API Info *** {server.Ip}, {msg.name}, {state.Level}, {state.Name}, {state.Description}");
                        TitanActiveAlarm alarm = new TitanActiveAlarm
                        {
                            ChannelName = msg.name,
                            Level = state.Level,
                            Value = state.Name,
                            Desc = state.Description
                        };
                        alarms.Add(alarm);
                    }
                }

                // 동기화
                await _semaphore.WaitAsync();
                try
                {
                    var oldAlarm = oldAlarms.Where(x => x.Ip == server.Ip).FirstOrDefault();
                    await LogManager.ActiveAlarm(server.Ip, alarms, oldAlarm.Alarms);
                    oldAlarm.Alarms = alarms;
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            else
            {
                logger.Info("Titan Api Msg is empty");
            }
            return true;
        }

        private async Task<JObject> ServerTask(Server server, Server oldServer)
        {
            Snmp snmp = new Snmp
            {
                IP = server.Ip,
                Port = "65535",
                Community = "public",
                Oid = SnmpService._MyConnectionOid,
                LevelString = Server.EnumStatus.Normal.ToString(),
                TypeValue = "begin",
                TranslateValue = ""
            };
            // logger.Info($"[{server.Ip}] Get 시작");
            if (SnmpService.Get(server))
            {
                if (!string.IsNullOrEmpty(server.ModelName))
                {
                    if (oldServer.IsConnect == Server.EnumIsConnect.Disconnect)
                    {
                        logger.Info($"{snmp.IP} is Reconnected");
                        snmp.IP = server.Ip;
                        snmp.Port = "65535";
                        snmp.Community = "public";
                        snmp.Oid = SnmpService._MyConnectionOid;
                        snmp.LevelString = Server.EnumStatus.Critical.ToString();
                        snmp.TypeValue = "end";
                        snmp.TranslateValue = "Failed to connection";
                        snmp.Main = 0;
                        snmp.Channel = 0;
                        await LogManager.LoggingDatabase(snmp);
                    }
                }
                server.IsConnect = Server.EnumIsConnect.Connect;
                server.UpdateState = Server.EnumStatus.Normal.ToString();
            }
            else // snmp get time out
            {
                if (!string.IsNullOrEmpty(server.Ip))
                {
                    if (oldServer.IsConnect == Server.EnumIsConnect.Connect)
                    {
                        snmp.IP = server.Ip;
                        snmp.Port = "65535";
                        snmp.Community = "public";
                        snmp.Oid = SnmpService._MyConnectionOid;
                        snmp.LevelString = Server.EnumStatus.Critical.ToString();
                        snmp.TypeValue = "begin";
                        snmp.TranslateValue = "Failed to connection";
                        snmp.Main = 0;
                        snmp.Channel = 0;
                        await LogManager.LoggingDatabase(snmp);
                    }
                }
                server.IsConnect = Server.EnumIsConnect.Disconnect;
                server.UpdateState = Server.EnumStatus.Critical.ToString();
            }

            JObject o = new JObject();
            if (ChannelAI.IsEnable)
            {
                try
                {
                    o.Add("device_id", server.Id);
                    o.Add("status", server.Status);
                    //10d 7h 51m 0s 520ms to second
                    string[] uptime;
                    if (!string.IsNullOrEmpty(server.Uptime))
                    {
                        uptime = server.Uptime.Split(' ');
                        int day = Convert.ToInt32(uptime[0].Substring(0, uptime[0].Length - 1));
                        int hour = Convert.ToInt32(uptime[1].Substring(0, uptime[1].Length - 1));
                        int min = Convert.ToInt32(uptime[2].Substring(0, uptime[2].Length - 1));
                        int sec = Convert.ToInt32(uptime[3].Substring(0, uptime[3].Length - 1));
                        Int64 uptime_sec = day * 86400 + hour * 3600 + min * 60 + sec;
                        o.Add("sys_uptime", uptime_sec);
                    }
                    else
                    {
                        o.Add("sys_uptime", 0);
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
            // logger.Info($"[{server.Ip}] Get 끝");
            return o;
        }

        private async void SnmpGetServiceAsync()
        {
            logger.Info("SnmpGetService is created");

            bool signal = false;
            List<ActiveAlarm> firstActiveAlarms = null;
            List<Server> firstServers = null;
            try
            {
                firstActiveAlarms = ActiveAlarm.GetActiveAlarm();
                firstServers = Server.GetServerList();

                foreach (ActiveAlarm alarm in firstActiveAlarms)
                {
                    Server s = InitCheck(firstServers, alarm);
                    if (s != null)
                    {
                        s.IsConnect = Server.EnumIsConnect.Disconnect;
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error($"{e.ToString()}");
                firstServers = Server.GetServerList();
            }
            logger.Info($"*** Service is loaded ***");
            foreach (Server s in firstServers)
            {
                logger.Info($"{s.Ip}, {s.UnitName}, InitConnect : {s.IsConnect}");
            }
            logger.Info($"*************************************");

            List<Server> oldServers = null;

            while (true)
            {
                try
                {
                    signal = m_shutdownEvent.WaitOne(m_get_delay, true);
                    if (signal)
                    {
                        logger.Info($"signal TERM in while");
                        break;
                    }

                    List<Server> newServers = null;

                    //현재 서버 상태를 받아옴
                    if (oldServers == null)
                    {
                        newServers = firstServers;
                        oldServers = firstServers;
                    }
                    else
                    {
                        newServers = Server.GetServerList();
                    }

                    _DeviceReport += _PollingSec;
                    if (_DeviceReport >= 3600)
                    {
                        _DeviceReport = 0;
                        if (ChannelAI.IsEnable)
                        {
                            try
                            {
                                // 채널 AI status
                                string response = ExternalApi.ChannelAI.Device(newServers);
                                logger.Info("api response : " + response);
                            }
                            catch (Exception e)
                            {
                                logger.Error(e.ToString());
                            }
                        }
                    }

                    JArray j = new JArray();

                    List<Task<JObject>> taskList = new List<Task<JObject>>();

                    foreach (Server server in newServers)
                    {
                        Server oldServer = null;

                        oldServer = GetOldServer(oldServers, server);
                        if (oldServer == null)
                        {
                            oldServer = server;
                        }
                        //logger.Info($"*** old server ({oldServer.Ip}), {oldServer.ModelName}, {oldServer.Status}, isConnect : {oldServer.IsConnect}");

                        //logger.Info($"*** new server ({server.Ip}), {server.ModelName}, {server.Status}, isConnect : {server.IsConnect}");
                        SnmpService.SetTimeout(_SnmpGetTimeout);
                        SnmpService.SetRetry(_SnmpRetryCount);
                        taskList.Add(Task.Run(() => ServerTask(server, oldServer)));
                    }
                    // logger.Info("**** Task Any 시작 ***");
                    while (taskList.Any())
                    {
                        Task<JObject> taskCompleted = await Task.WhenAny(taskList);
                        taskList.Remove(taskCompleted);
                        j.Add(await taskCompleted);
                    }

                    if (ChannelAI.IsEnable)
                    {
                        try
                        {
                            // 채널 AI status
                            string response = ExternalApi.ChannelAI.DeviceState(j);
                            logger.Info("api response : " + response);
                        }
                        catch (Exception e)
                        {
                            logger.Error(e.ToString());
                        }
                    }

                    // ack check
                    if (Setting.GetAck() == "1")
                    {
                        List<ActiveAlarm> ackAlarm = ActiveAlarm.GetActiveAlarmStillNotAck();
                        foreach (ActiveAlarm alarm in ackAlarm)
                        {
                            ActiveAlarm.UpdateAckAlarm(alarm);

                            // update and channelAI api call
                            if (ChannelAI.IsEnable)
                            {
                                try
                                {
                                    ChannelAI.AckAlarmEvent(alarm);
                                }
                                catch (Exception e)
                                {
                                    logger.Error(e.ToString());
                                }
                            }
                        }
                        Setting.UpdateAckZero();
                    }

                    oldServers = newServers;
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

        private Server GetOldServer(List<Server> oldServers, Server s)
        {
            IEnumerable<Server> items = from x in oldServers
                                        where x.Id == s.Id
                                        select x;
            if (items.Count() == 0)
            {
                return null;
            }
            else
            {
                return items.ElementAt(0);
            }
        }

        private Server InitCheck(List<Server> servers, ActiveAlarm a)
        {
            IEnumerable<Server> items = from x in servers
                                        where x.Ip == a.Ip && a.Value.ToLower() == "failed to connection"
                                        select x;
            if (items.Count() == 0)
            {
                return null;
            }
            else
            {
                return items.ElementAt(0);
            }
        }

        #region trap deprecated

        private async void TrapListenerAsync()
        {
            logger.Info("TrapListener is created");

            //Titan Live alarm Oid is only one
            const string TitanLiveAlarmOid = "1.3.6.1.4.1.27338.40.5";
            const string SencoreMRD4400Oid = "1.3.6.1.4.1.9986.3.14.1.8.4";

            // Construct a socket and bind it to the trap manager port 162
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = 1000;
            socket.SendTimeout = 1000;
            IPEndPoint ipep = new IPEndPoint(IPAddress.Any, _SnmpPort);
            EndPoint ep = (EndPoint)ipep;

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 1000);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            socket.Bind(ep);
            // Disable timeout processing. Just block until packet is received

            int inlen = -1;
            logger.Info("Waiting for snmp trap");
            bool signal = false;

            while (true)
            {
                try
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
                            logger.Info($"** SNMP Version 1 TRAP received from {inep.ToString()}:");
                            logger.Info($"*** Trap generic: {pkt.Pdu.Generic}");
                            logger.Info($"*** Trap specific: {pkt.Pdu.Specific}");
                            logger.Info($"*** Agent address: {pkt.Pdu.AgentAddress.ToString()}");
                            logger.Info($"*** Timestamp: {pkt.Pdu.TimeStamp.ToString()}");
                            logger.Info($"*** VarBind count: {pkt.Pdu.VbList.Count}");
                            logger.Info("*** VarBind content:");
                            foreach (Vb v in pkt.Pdu.VbList)
                            {
                                logger.Info($"**** {v.Oid.ToString()} {SnmpConstants.GetTypeName(v.Value.Type)}: {v.Value.ToString()}");
                            }
                            logger.Info("** End of SNMP Version 1 TRAP data.");
                        }
                        else
                        {
                            Snmp snmp = new Snmp();
                            // Parse SNMP Version 2 TRAP packet
                            SnmpV2Packet pkt = new SnmpV2Packet();
                            pkt.decode(indata, inlen);
                            logger.Info($"** SNMP Version 2 TRAP received from {inep.ToString()}");
                            if ((SnmpSharpNet.PduType)pkt.Pdu.Type != PduType.V2Trap &&
                                ((SnmpSharpNet.PduType)pkt.Pdu.Type != PduType.Inform))
                            {
                                logger.Info("*** NOT an SNMPv2 trap or inform ****");
                            }
                            else
                            {
                                logger.Info($"*** Community: {pkt.Community.ToString()}");
                                logger.Info($"*** VarBind count: {pkt.Pdu.VbList.Count}");
                                logger.Info($"*** VarBind content:");

                                foreach (Vb v in pkt.Pdu.VbList)
                                {
                                    snmp.Oid = v.Oid.ToString();
                                    snmp.IP = inep.ToString().Split(':')[0];
                                    snmp.Port = inep.ToString().Split(':')[1];
                                    snmp.Syntax = SnmpConstants.GetTypeName(v.Value.Type);
                                    snmp.Community = pkt.Community.ToString();
                                    snmp.Value = v.Value.ToString();
                                    snmp.type = "trap";

                                    logger.Info("Oid : " + v.Oid.ToString());
                                    logger.Info("value : " + v.Value.ToString());

                                    if (snmp.Oid.Contains(TitanLiveAlarmOid))
                                    {
                                        string TitanLiveTrapType = snmp.Oid.Split('.').Last();

                                        if (TitanLiveTrapType == "9")
                                        {
                                            string levelString = snmp.Value;
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
                                            if (snmp.Value == "start")
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
                                            snmp.LogOid = snmp.Oid;
                                            snmp.IsTypeTrap = true;
                                        }
                                        else if (TitanLiveTrapType == "5")
                                        {
                                            snmp.Desc = v.Value.ToString();
                                        }
                                        else if (TitanLiveTrapType == "8")
                                        {
                                            snmp.TitanUID = v.Value.ToString();
                                        }
                                        else if (TitanLiveTrapType == "11")
                                        {
                                            snmp.TitanName = v.Value.ToString();
                                            snmp.ChannelValue = v.Value.ToString();
                                        }
                                    }
                                    else if (snmp.Oid.Contains(SencoreMRD4400Oid))
                                    {
                                        // logger.Info($"Sencore MRD4400 trap");

                                        snmp.LevelString = "Warning"; // MRD4400 default level

                                        string[] oidSplit = snmp.Oid.Split('.');
                                        string TrapType = oidSplit[oidSplit.Length - 2];

                                        if (TrapType == "1")
                                        {
                                            //logger.Info($"trapSysConditionOid : {snmp.Value}");
                                            snmp.LogOid = snmp.Value;
                                        }
                                        else if (TrapType == "2")
                                        {
                                            //logger.Info($"trapSysConditionValue : {snmp.Value}");
                                            if (snmp.Value == "1")
                                            {
                                                snmp.TypeValue = "end";
                                            }
                                            else if (snmp.Value == "2")
                                            {
                                                snmp.TypeValue = "begin";
                                            }
                                        }
#if false
//Trapstring 넣지 않으면 TranslateValue를 logging 함
                                        else if (TrapType == "5")
                                        {
                                            snmp.TrapString = snmp.Value;
                                        }
#endif
                                        else if (TrapType == "7")
                                        {
                                            snmp.TranslateValue += snmp.Value;
                                        }
                                        else if (TrapType == "8")
                                        {
                                            snmp.TranslateValue += " - " + snmp.Value;
                                            snmp.IsTypeTrap = true;
                                        }
                                    }
                                    else // 타이탄 라이브가 아닐 때
                                    {
                                        string value = Snmp.GetNameFromOid(v.Oid.ToString());
                                        logger.Info($"GetNameFromOid value : {value}");

                                        if (value.LastIndexOf("Level") > 0)
                                        {
                                            snmp.LevelString = Snmp.GetLevelString(Convert.ToInt32(v.Value.ToString()), v.Oid.ToString());
                                            logger.Info($"LevelString : {snmp.LevelString}");
                                        }
                                        else if (value.LastIndexOf("Type") > 0)
                                        {
                                            string TranslateValue = "";
                                            string Api_msg = "";
                                            string EventType = "";
                                            Snmp.GetTranslateValue(value, out TranslateValue, out Api_msg, out EventType);
                                            snmp.TranslateValue = TranslateValue;
                                            snmp.Api_msg = Api_msg;
                                            snmp.Event_type = EventType;
                                            snmp.TypeValue = Enum.GetName(typeof(Snmp.TrapType), Convert.ToInt32(v.Value.ToString()));
                                            //snmp.Oid = v.Oid.ToString();
                                            snmp.LogOid = v.Oid.ToString();
                                            snmp.IsTypeTrap = true;
                                            logger.Info($"TypeValue : {snmp.TypeValue}");
                                        }
                                        else if (value.LastIndexOf("Channel") > 0)
                                        {
                                            snmp.Channel = Convert.ToInt32(v.Value.ToString());
                                        }
                                        else if (value.LastIndexOf("Main") > 0)
                                        {
                                            snmp.Main = Convert.ToInt32(v.Value.ToString());
                                        }

                                        //데이터베이스 테이블을 만들기 위해 등록함(로그는 translate 테이블을 이용하자)
                                        snmp.RegisterSnmpInfo();

                                        //logger.Info($"[{inep.ToString().Split(':')[0]}] Trap : {v.Oid.ToString()} {SnmpConstants.GetTypeName(v.Value.Type)}: {v.Value.ToString()}");
                                    }
                                }

                                /*
                                 * LGHV 요구사항에 따른 예외 (추후 삭제 될 수 있음)
                                 */

                                /*
                                if (snmp.Oid.Contains(TitanLiveAlarmOid) && snmp.TranslateValue == "Service State Is Stopped")
                                {
                                    //Service State Is Stopped의 경우 trap type을 null에서 begin 으로 임시 변경
                                    snmp.TypeValue = "begin";
                                }
                                else if (snmp.Oid.Contains(TitanLiveAlarmOid) && snmp.TranslateValue == "Service State Is Encoding")
                                {
                                    //Service State Is Encoding의 경우 trap type을 null에서 end 으로 임시 변경
                                    snmp.TypeValue = "end";
                                    snmp.TranslateValue = "Service State Is Stopped";
                                }
                                */

                                //CM, DR 기록
                                if (Snmp.IsEnableTrap(snmp.Oid) || snmp.Oid.Contains(TitanLiveAlarmOid) || snmp.Oid.Contains(SencoreMRD4400Oid))
                                {
                                    if (!String.IsNullOrEmpty(snmp.LevelString) & !"Disabled".Equals(snmp.LevelString))
                                    {
                                        logger.Info($"trap info : ({snmp.IP})[{snmp.Channel}, {snmp.Main}, {snmp.ChannelValue}], ({snmp.TypeValue}), ({snmp.LevelString}), {snmp.TranslateValue}");

                                        ThreadPool.SetMinThreads(50, 100);
                                        // 타이탄일 경우 api 알람 동기화
                                        if (snmp.Oid.Contains(TitanLiveAlarmOid))
                                        {
                                            //
                                        }
                                        else
                                        {
                                            await LogManager.LoggingDatabase(snmp);
                                        }

                                        // 현재 Server State를 결정하는 지점
                                        if (!string.Equals(snmp.TypeValue, "log"))
                                        {
                                            Server s = new Server
                                            {
                                                Ip = snmp.IP,
                                                Id = Server.GetServerID(snmp.IP),
                                                UnitName = Server.GetServerName(snmp.IP),
                                                UpdateState = snmp.LevelString
                                            };

                                            if (ChannelAI.IsEnable)
                                            {
                                                try
                                                {
                                                    ExternalApi.ChannelAI.CombinePostEvent(snmp, s);
                                                }
                                                catch (Exception e)
                                                {
                                                    logger.Error(e.ToString());
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            logger.Info("** End of SNMP Version 2 TRAP data.");
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
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
            logger.Info(" *** TrapListener exit ***");
        }

        #endregion trap deprecated

        protected override void OnStop()
        {
            m_shutdownEvent.Set();

            //wait for thread to stop giving it 10 second
            m_threadDuplicateCheckWorker.Join(1000);
            m_threadWorker.Join(10000);
            // m_threadSnmpTrap.Join(10000);
            m_threadSnmpGet.Join(10000);
            base.OnStop();
        }
    }
}