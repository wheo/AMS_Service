﻿using System;
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
        public static string Host { get; set; }

        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static string GetTimestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        public static string GetSystemName()
        {
            return "AMS";
        }

        public static void CombinePostEvent(Snmp snmp, Server server)
        {
            logger.Info("event_id : " + snmp.event_id);
            logger.Info("type : " + snmp.type);
            if (snmp.TypeValue == "begin")
            {
                if (!string.IsNullOrEmpty(snmp.event_id))
                {
                    string device_type = null;
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

                    string response = PostEvent(snmp.event_id, device_type, server.Id, server.UnitName, "162"
                        , "Service", "SERVICE_ID_01", "SERVICE_NAME_01"
                        , "Equipment Alarm", snmp.Api_msg, "A2", "Outstanding"
                        , snmp.Level.ToString()
                        , snmp.TranslateValue);

                    logger.Info("response : " + response);
                }
            }
            else if (snmp.TypeValue == "end")
            {
                //
            }
        }

        public static string PostEvent(
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
            var response = Utils.Http.PostAsync("/v1/event", o);
            logger.Info("-------------- event --------------");

            return response.Result.Content.ReadAsStringAsync().Result;
        }

        public static string PutEvent()
        {
            return null;
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

        public static string Device(string device_type
            , string device_id
            , string host_name
            , string mgmt_ip
            , string firmware_ver
            , string mnfc_name
            , string vendor_name
            , string ha_role)
        {
            string system_name = GetSystemName();
            string timestamp = GetTimestamp();
            string range = "FULL";

            JObject o = new JObject();
            o.Add("system_name", system_name);
            o.Add("timestamp", timestamp);
            o.Add("range", range);
            o.Add("device_type", device_type);
            JObject devices = new JObject();
            devices.Add("device_id", device_id);
            devices.Add("host_name", host_name);
            devices.Add("mgmt_ip", mgmt_ip);
            devices.Add("firmware_ver", firmware_ver);
            devices.Add("mnfc_name", mnfc_name);
            devices.Add("vendor_name", vendor_name);
            devices.Add("ha_role", ha_role);

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

            var response = Utils.Http.PostAsync("/v1/devices/state", o);
            return response.Result.Content.ReadAsStringAsync().Result;
        }
    }
}