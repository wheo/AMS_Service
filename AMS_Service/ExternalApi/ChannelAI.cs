using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AMS_Service.ExternalApi
{
    internal static class ChannelAI
    {
        public static bool IsEnable { get; set; }
        public static string Host { get; set; }

        public static string Ha_role { get; set; }

        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static string GetTimestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        public static string GetSystemName()
        {
            return "AMS";
        }

        public static void AckAlarmEvent(ActiveAlarm alarm)
        {
            string event_state = null;
            string response = null;
            if (!string.IsNullOrEmpty(alarm.Id))
            {
                event_state = "Acknowledged";
                response = PutEvent(alarm.Id, Server.GetServerID(alarm.Ip), event_state, alarm.Value);
                logger.Info($"response : {response}");
            }
        }

        public static void CombinePostEvent(Snmp snmp, Server server)
        {
            logger.Info($"event_id : {snmp.event_id}");
            logger.Info($"type : {snmp.type}");

            if (!string.IsNullOrEmpty(snmp.event_id))
            {
                string device_type = null;
                string event_state = null;
                string response = null;
                if (server.ModelName == "CM5000")
                {
                    device_type = "Encoder";
                }
                else if (server.ModelName == "DR5000")
                {
                    device_type = "Decoder";
                }
                else if (server.ModelName == "Titan")
                {
                    device_type = "Encoder";
                }

                if (snmp.TypeValue == "begin")
                {
                    event_state = "Outstanding";
                    response = PostEvent(snmp.event_id, device_type, server.Id, server.UnitName, "162"
                        , "Channel"
                        , server.Ip
                        , server.UnitName
                        , snmp.Event_type
                        , snmp.Api_msg
                        , snmp.TranslateValue
                        , event_state
                        , snmp.LevelString
                        , snmp.TranslateValue);
                }
                else if (snmp.TypeValue == "end")
                {
                    event_state = "Terminated";
                    response = PutEvent(snmp.event_id, server.Id, event_state, snmp.TranslateValue);
                }

                logger.Info($"response : {response}");
            }
        }

        private static string PostEvent(
            string event_id
            , string device_type, string device_id, string device_name, string port_name
            , string target_type, string target_id, string target_name
            , string event_type, string probable_cause, string specific_problem, string event_state
            , string severity
            , string message)
        {
            // datetime milsec
            string system_name = GetSystemName();
            string timestamp = GetTimestamp();
            JObject o = new JObject();
            JObject source_device = new JObject();
            JObject event_target = new JObject();
            o.Add("system_name", system_name);
            o.Add("event_id", event_id);

            source_device.Add("device_type", device_type);
            source_device.Add("device_id", device_id);
            source_device.Add("device_name", device_name);
            source_device.Add("port_name", port_name);
            o.Add("source_device", source_device);

            event_target.Add("target_type", target_type);
            event_target.Add("target_id", target_id);
            event_target.Add("target_name", target_name);

            o.Add("event_target", event_target);

            o.Add("event_type", event_type);
            o.Add("probable_cause", probable_cause);
            o.Add("specific_problem", specific_problem);
            o.Add("event_state", event_state);
            o.Add("severity", severity);
            o.Add("timestamp", timestamp);
            o.Add("message", message);
            if (!string.IsNullOrEmpty(probable_cause) && !string.IsNullOrEmpty(specific_problem))
            {
                var response = Utils.Http.PostAsync("/v1/event", o);
                logger.Info("-------------- post event --------------");
                logger.Info($"{o.ToString(Formatting.Indented)}");

                return response.Result.Content.ReadAsStringAsync().Result;
            }
            else
            {
                logger.Info($"probable_cause and specific_problem is null");
                return null;
            }
        }

        private static string PutEvent(string event_id, string device_id, string event_state, string reason)
        {
            string system_name = GetSystemName();
            string timestamp = GetTimestamp();
            JObject o = new JObject();
            o.Add("system_name", system_name);
            o.Add("event_id", event_id);
            o.Add("device_id", device_id);
            o.Add("event_state", event_state);
            o.Add("reason", $"{reason} Alarm is {event_state.ToLower()}");
            o.Add("timestamp", timestamp);
            JObject user = new JObject();
            o.Add("user", user);
            user.Add("user_id", "system");
            user.Add("user_company", "goup");
            user.Add("user_name", "system");

            var response = Utils.Http.PutAsync("/v1/event", o);
            logger.Info("-------------- put event --------------");
            logger.Info($"{o.ToString(Formatting.Indented)}");

            return response.Result.Content.ReadAsStringAsync().Result;
        }

        public static string Performances(string device_id
            , int cpu_util, int mem_util, int disk_util)
        {
            string system_name = GetSystemName();
            string timestamp = GetTimestamp();
            JObject o = new JObject(); ;
            o.Add("system_name", system_name);
            o.Add("timestamp", timestamp);
            JObject performances = new JObject();
            performances.Add("cpu_util", cpu_util);
            performances.Add("mem_util", mem_util);
            performances.Add("disk_util", disk_util);
            o.Add("performances", performances);

            var response = Utils.Http.PostAsync("/v1/performances", o);
            return response.Result.Content.ReadAsStringAsync().Result;
        }

        public static string Device(List<Server> ss)
        {
            string system_name = GetSystemName();
            string timestamp = GetTimestamp();
            string device_type = "ENCODER";
            string range = "FULL";
            string mnfc_name = "ATEME";
            string vendor_name = "GOUP";
            string ha_role = Ha_role == "M" ? "101" : "102"; // 101 메인, 102 백업

            JObject o = new JObject();
            JArray a = new JArray();
            o.Add("system_name", system_name);
            // o.Add("timestamp", timestamp);
            o.Add("range", range);
            o.Add("device_type", device_type);

            foreach (Server s in ss)
            {
                JObject devices = new JObject();
                devices.Add("device_id", s.Id);
                devices.Add("host_name", s.UnitName);
                devices.Add("mgmt_ip", s.Ip);
                devices.Add("firmware_ver", string.IsNullOrEmpty(s.Version) == true ? "" : s.Version);
                devices.Add("mnfc_name", mnfc_name);
                devices.Add("vendor_name", vendor_name);
                devices.Add("ha_role", ha_role);
                a.Add(devices);
            }
            o.Add("devices", a);

            logger.Info("-------------- device event --------------");
            logger.Info($"{o.ToString(Formatting.Indented)}");

            var response = Utils.Http.PostAsync("/v1/devices", o);
            return response.Result.Content.ReadAsStringAsync().Result;
        }

        public static string DeviceState(JArray devices)
        {
            string system_name = GetSystemName();
            string timestamp = GetTimestamp();
            string hostname = "";

            JObject o = new JObject();
            o.Add("system_name", system_name);
            o.Add("hostname", hostname);
            o.Add("timestamp", timestamp);
            o.Add("devices", devices);
            /*
             * 로그를 매초 보내면 사이즈가 커서 보내는 로그를 저장하지 않음
            logger.Info("-------------- device state --------------");
            logger.Info($"{o.ToString(Formatting.Indented)}");
            */

            var response = Utils.Http.PostAsync("/v1/devices/state", o);
            return response.Result.Content.ReadAsStringAsync().Result;
        }
    }
}