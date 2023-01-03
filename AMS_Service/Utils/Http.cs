using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using AMS_Service.ExternalApi;
using log4net;

namespace AMS_Service.Utils
{
    public static class Http
    {
        private static readonly ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static async Task<HttpResponseMessage> PutAsync(string uri, JObject json)
        {
            HttpClient client = new HttpClient();
            //Basic ' + Base64("Channel:AI");
            client.Timeout = TimeSpan.FromSeconds(Service1._HttpTimeout);
            var stringContent = new StringContent(json.ToString());
            client.DefaultRequestHeaders.Add("Authorization", "Basic Q2hhbm5lbDpBSQ==");
            logger.Info(uri);
            //logger.Info(json.ToString());
            var response = await client.PutAsync($"{ChannelAI.Host}{uri}", stringContent);
            return response;
        }

        public static async Task<HttpResponseMessage> PostAsync(string uri, JObject json)
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(Service1._HttpTimeout);
            //Basic ' + Base64("Channel:AI");
            var stringContent = new StringContent(json.ToString());
            client.DefaultRequestHeaders.Add("Authorization", "Basic Q2hhbm5lbDpBSQ==");
            logger.Info(uri);
            //logger.Info(json.ToString());
            var response = await client.PostAsync($"{ChannelAI.Host}{uri}", stringContent);
            return response;
        }
    }
}