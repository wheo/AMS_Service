using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using SnmpSharpNet;
using System.Diagnostics;
using System.Net.Sockets;
using log4net;
using System.Threading;
using AMS_Service;

namespace AMS_Service.Service
{
    public class SnmpService
    {
        public static bool _shouldStop = false;

        public static string _MyConnectionOid = "1.3.6.1.4.1.27338";

        public static string _SysUptime = "1.3.6.1.2.1.1.3.0";

        public static string _CM5000UnitName_oid = "1.3.6.1.4.1.27338.4.2.1.0";
        public static string _DR5000UnitName_oid = "1.3.6.1.4.1.27338.5.2.1.0";

        public static string _DR5000ModelName_oid = "1.3.6.1.4.1.27338.5.2.2.0";
        public static string _CM5000ModelName_oid = "1.3.6.1.4.1.27338.4.2.2.0";
        public static string _TITANLiveModelName_oid = "1.3.6.1.2.1.1.5.0";

        public static string _DR5000ServicePid_oid = "1.3.6.1.4.1.27338.5.3.2.3.2.1.0";
        public static string _DR5000VideoOutputId_oid = "1.3.6.1.4.1.27338.5.3.2.3.3.1.0";

        public static string _DR5000CurrentVersion_oid = "1.3.6.1.4.1.27338.5.6.1.0";
        public static string _CM5000CurrentVersion_oid = "1.3.6.1.4.1.27338.4.4.1.0";

        public static int _timeout = 100;
        public static int _retry = 3;

        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public SnmpService()
        {
            //Constructor
        }

        public static void SetTimeout(int timeout)
        {
            _timeout = timeout;
        }

        public static void SetRetry(int retry)
        {
            _retry = retry;
        }

        public static bool Set(Server s)
        {
            // Prepare target
            if (string.IsNullOrEmpty(s.Ip))
                return false;
            UdpTarget target = new UdpTarget((IPAddress)new IpAddress(s.Ip));
            // Create a SET PDU
            Pdu pdu = new Pdu(PduType.Set);
            /*
            // Set sysLocation.0 to a new string
            pdu.VbList.Add(new Oid("1.3.6.1.2.1.1.6.0"), new OctetString("Some other value"));
            // Set a value to integer
            pdu.VbList.Add(new Oid("1.3.6.1.2.1.67.1.1.1.1.5.0"), new Integer32(500));
            */
            // Set a value to unsigned integer
            /*
            pdu.VbList.Add(new Oid("1.3.6.1.4.1.27338.5.3.2.3.2.1.0"), new Integer32(5000)); // primary service id
            pdu.VbList.Add(new Oid("1.3.6.1.4.1.27338.5.3.2.3.3.1.0"), new Integer32(5005)); // video output pid
            */

            if (string.IsNullOrEmpty(s.ModelName))
            {
                return false;
            }

            if ("cm5000".Equals(s.ModelName.ToLower()))
            {
                pdu.VbList.Add(new Oid(_CM5000UnitName_oid), new OctetString(s.UnitName));
            }
            else if ("dr5000".Equals(s.ModelName.ToLower()))
            {
                pdu.VbList.Add(new Oid(_DR5000ServicePid_oid), new Integer32(s.ServicePid)); // primary service id
                pdu.VbList.Add(new Oid(_DR5000VideoOutputId_oid), new Integer32(s.VideoOutputId)); // video output pid
                pdu.VbList.Add(new Oid(_DR5000UnitName_oid), new OctetString(s.UnitName));
            }

            // Set Agent security parameters
            AgentParameters aparam = new AgentParameters(SnmpVersion.Ver2, new OctetString("private"));
            // Response packet
            SnmpV2Packet response;
            try
            {
                // Send request and wait for response
                response = target.Request(pdu, aparam) as SnmpV2Packet;
            }
            catch (Exception ex)
            {
                // If exception happens, it will be returned here
                logger.Info(String.Format($"[{s.Ip}] Request failed with exception: {ex.Message}"));
                target.Close();
                return false;
            }
            // Make sure we received a response
            if (response == null)
            {
                logger.Info(string.Format($"[{s.Ip}] Error in sending SNMP request."));
                return false;
            }
            else
            {
                // Check if we received an SNMP error from the agent
                if (response.Pdu.ErrorStatus != 0)
                {
                    logger.Info(String.Format($"[{s.Ip}] SNMP agent returned ErrorStatus {response.Pdu.ErrorStatus} on index {response.Pdu.ErrorIndex}"));
                }
                else
                {
                    // Everything is ok. Agent will return the new value for the OID we changed
                    logger.Info(String.Format($"[{s.Ip}]Agent response {response.Pdu[0].Oid.ToString()}: {response.Pdu[0].Value.ToString()}"));
                }
            }
            return true;
        }

        public static bool GetInfomation(Server s)
        {
            if (string.IsNullOrEmpty(s.Ip))
            {
                return false;
            }
            OctetString community = new OctetString("public");
            // Define agent parameters class
            AgentParameters param = new AgentParameters(community);
            // Set SNMP version to 1 (or 2)
            param.Version = SnmpVersion.Ver2;

            // Construct the agent address object
            // IPAddress class is easy to use here because it will try to resolve constructor parameter if it doesn't parse to an IP address
            IpAddress agent = new IpAddress(s.Ip);
            // Construct target
            UdpTarget target = new UdpTarget((IPAddress)agent, 161, _timeout, _retry);
            // Pdu class used for all requests
            Pdu pdu = new Pdu(PduType.Get);
            /*
            pdu.VbList.Add("1.3.6.1.2.1.1.1.0"); //sysDescr
            pdu.VbList.Add("1.3.6.1.2.1.1.2.0"); //sysObjectID
            pdu.VbList.Add("1.3.6.1.2.1.1.3.0"); //sysUpTime
            pdu.VbList.Add("1.3.6.1.2.1.1.4.0"); //sysContact
            pdu.VbList.Add("1.3.6.1.2.1.1.5.0"); //sysName
            */

            pdu.VbList.Add(_SysUptime);

            if (string.IsNullOrEmpty(s.ModelName))
            {
                return false;
            }
            // _oid : 장비 이름
            if (s.ModelName.ToLower() == "cm5000")
            {
                pdu.VbList.Add(_CM5000CurrentVersion_oid);
                pdu.VbList.Add(_CM5000UnitName_oid);
                pdu.VbList.Add(_CM5000ModelName_oid);
            }
            else if (s.ModelName.ToLower() == "dr5000")
            {
                pdu.VbList.Add(_DR5000CurrentVersion_oid);
                pdu.VbList.Add(_DR5000UnitName_oid);
                pdu.VbList.Add(_DR5000ModelName_oid);
                pdu.VbList.Add(_DR5000ServicePid_oid);
                pdu.VbList.Add(_DR5000VideoOutputId_oid);
            }

            try
            {
                // Make SNMP request
                SnmpV2Packet result = (SnmpV2Packet)target.Request(pdu, param);
                // If request is null then agent didn't reply or we couldn't parse the reply.
                if (result != null)
                {
                    // ErrorStatus other then 0 is an error returned by the Agent - see SnmpConstants for error definitions
                    if (result.Pdu.ErrorStatus != 0)
                    {
                        // agent reported an error with request
                        Debug.WriteLine("Error in SNMP reply. Error {0} index {1}",
                            result.Pdu.ErrorStatus,
                            result.Pdu.ErrorIndex);
                    }
                    else
                    {
                        // Reply variables are returned in the same order as they were added to the VbList
                        for (int i = 0; i < result.Pdu.VbCount; i++)
                        {
                            Debug.WriteLine("[{3}] sysDescr({0}) ({1}): {2}",
                                result.Pdu.VbList[i].Oid.ToString(),
                                SnmpConstants.GetTypeName(result.Pdu.VbList[0].Value.Type),
                                result.Pdu.VbList[i].Value.ToString(), s.Ip);

                            if (result.Pdu.VbList[i].Value.ToString().Equals("SNMP No-Such-Object"))
                            {
                                continue;
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_SysUptime))
                            {
                                s.Uptime = result.Pdu.VbList[i].Value.ToString();
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_DR5000UnitName_oid))
                            {
                                s.UnitName = result.Pdu.VbList[i].Value.ToString();
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_DR5000ModelName_oid))
                            {
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_DR5000CurrentVersion_oid))
                            {
                                s.Version = result.Pdu.VbList[i].Value.ToString();
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_DR5000ServicePid_oid))
                            {
                                s.ServicePid = Convert.ToInt32(result.Pdu.VbList[i].Value.ToString());
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_DR5000VideoOutputId_oid))
                            {
                                s.VideoOutputId = Convert.ToInt32(result.Pdu.VbList[i].Value.ToString());
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_CM5000CurrentVersion_oid))
                            {
                                s.Version = result.Pdu.VbList[i].Value.ToString();
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_CM5000UnitName_oid))
                            {
                                s.UnitName = result.Pdu.VbList[i].Value.ToString();
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_CM5000ModelName_oid))
                            {
                            }
                            else
                            {
                                //s.Version = result.Pdu.VbList[i].Value.ToString();
                                continue;
                            }
                        }
                        target.Close();
                        return true;
                    }
                }
                else
                {
                    target.Close();
                    logger.Info(string.Format($"({s.Ip}) No response received from SNMP agent."));
                    return false;
                }
            }
            catch (Exception ex)
            {
                target.Close();
                logger.Error(String.Format("Exception ({0})", s.Ip));
                logger.Error(ex.ToString());
            }
            return false;
        }

        public static bool GetModelName(Server s)
        {
            return GetInfo(s, 0);
        }

        public static bool Get(Server s)
        {
            return GetInfo(s, 1);
        }

        public static bool GetInfo(Server s, int type)
        {
            if (string.IsNullOrEmpty(s.Ip))
            {
                return false;
            }
            // SNMP community name
            OctetString community = new OctetString("public");
            // Define agent parameters class
            AgentParameters param = new AgentParameters(community);
            // Set SNMP version to 1 (or 2)
            param.Version = SnmpVersion.Ver2;

            // Construct the agent address object
            // IPAddress class is easy to use here because it will try to resolve constructor parameter if it doesn't parse to an IP address
            IpAddress agent = new IpAddress(s.Ip);
            // Construct target
            UdpTarget target = new UdpTarget((IPAddress)agent, 161, _timeout, _retry);
            // Pdu class used for all requests
            Pdu pdu = new Pdu(PduType.Get);
            /*
            pdu.VbList.Add("1.3.6.1.2.1.1.1.0"); //sysDescr
            pdu.VbList.Add("1.3.6.1.2.1.1.2.0"); //sysObjectID
            pdu.VbList.Add("1.3.6.1.2.1.1.3.0"); //sysUpTime
            pdu.VbList.Add("1.3.6.1.2.1.1.4.0"); //sysContact
            pdu.VbList.Add("1.3.6.1.2.1.1.5.0"); //sysName
            */
            // _oid : 장비 이름

            pdu.VbList.Add(_SysUptime);
            pdu.VbList.Add(_DR5000ModelName_oid);
            pdu.VbList.Add(_CM5000ModelName_oid);
            pdu.VbList.Add(_TITANLiveModelName_oid);

            if (type == 1)
            {
                pdu.VbList.Add(_DR5000UnitName_oid);
                pdu.VbList.Add(_CM5000UnitName_oid);
            }

            try
            {
                // Make SNMP request
                SnmpV2Packet result = (SnmpV2Packet)target.Request(pdu, param);
                // If request is null then agent didn't reply or we couldn't parse the reply.
                if (result != null)
                {
                    // ErrorStatus other then 0 is an error returned by the Agent - see SnmpConstants for error definitions
                    if (result.Pdu.ErrorStatus != 0)
                    {
                        // agent reported an error with request
                        Debug.WriteLine("Error in SNMP reply. Error {0} index {1}",
                            result.Pdu.ErrorStatus,
                            result.Pdu.ErrorIndex);
                    }
                    else
                    {
                        // Reply variables are returned in the same order as they were added to the VbList
                        for (int i = 0; i < result.Pdu.VbCount; i++)
                        {
                            Debug.WriteLine("[{3}] sysDescr({0}) ({1}): {2}",
                                result.Pdu.VbList[i].Oid.ToString(),
                                SnmpConstants.GetTypeName(result.Pdu.VbList[0].Value.Type),
                                result.Pdu.VbList[i].Value.ToString(), s.Ip);

                            if (result.Pdu.VbList[i].Value.ToString().Equals("SNMP No-Such-Object") ||
                                string.IsNullOrEmpty(result.Pdu.VbList[i].Value.ToString()))
                            {
                                continue;
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_SysUptime))
                            {
                                s.Uptime = result.Pdu.VbList[i].Value.ToString();
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_CM5000ModelName_oid) ||
                                result.Pdu.VbList[i].Oid.Equals(_DR5000ModelName_oid) ||
                                result.Pdu.VbList[i].Oid.Equals(_TITANLiveModelName_oid))
                            {
                                s.ModelName = result.Pdu.VbList[i].Value.ToString();
                            }
                            else if (result.Pdu.VbList[i].Oid.Equals(_CM5000UnitName_oid) ||
                                result.Pdu.VbList[i].Oid.Equals(_DR5000UnitName_oid))
                            {
                                s.UnitName = result.Pdu.VbList[i].Value.ToString();
                            }

                            //logger.Info(String.Format($"[{Ip}] sysDescr({result.Pdu.VbList[i].Oid.ToString()}) ({ SnmpConstants.GetTypeName(result.Pdu.VbList[0].Value.Type)}): { result.Pdu.VbList[i].Value.ToString()}"));
                        }
                        target.Close();
                        return true;
                    }
                }
                else
                {
                    target.Close();
                    logger.Info("No response received from SNMP agent.");
                    return false;
                }
            }
            catch (Exception)
            {
                target.Close();
                logger.Error(String.Format("Ip : {0}", s.Ip));
                //Debug.WriteLine(e.ToString());
            }
            return false;
        }
    }
}