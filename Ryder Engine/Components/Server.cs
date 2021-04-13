﻿using Newtonsoft.Json;
using Ryder_Engine.Components.MonitorModules;
using Ryder_Engine.Components.Tools;
using Ryder_Engine.Forms;
using Ryder_Engine.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Ryder_Engine.Components
{
    class Server
    {
        public static HttpListener listener;
        public static HttpClient client;
        public static string url = "http://+:9519/";

        private List<string> listeners = new List<string>();
        private bool _stop = false;
        private SystemMonitor systemMonitor;
        private PowerPlanManager powerPlanManager;
        // netsh http add urlacl url=http://+:9519/ user=administrator listen=yes
        public Server(SystemMonitor systemMonitor, PowerPlanManager powerPlanManager)
        {
            this.systemMonitor = systemMonitor;
            this.powerPlanManager = powerPlanManager;

            listener = new HttpListener();
            listener.Prefixes.Add(url);
            client = new HttpClient();
        }

        public void start()
        {
            _stop = false;
            listener.Start();
            ListenAsync();
        }

        public void stop()
        {
            _stop = true;
            listener.Stop();
        }

        public void sendDataToListeners()
        {
            if (listeners.Count > 0)
            {
                string data = JsonConvert.SerializeObject(systemMonitor.getStatus());
                foreach (var ip in listeners)
                {
                    try
                    {
                        HttpContent content = new StringContent(data, Encoding.UTF8, "application/json");
                        client.PostAsync(ip + "/status", content);
                    }
                    catch (Exception e) { }
                }
            }
        }

        public async void ListenAsync()
        {
            while (!_stop)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = await listener.GetContextAsync();
                }
                catch (HttpListenerException ex)
                {
                    Debug.Print("Error");
                }

                if (ctx == null) continue;

                // Process Request
                HttpListenerRequest request = ctx.Request;
                string txt;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    txt = reader.ReadToEnd();
                }
                dynamic json_request = JsonConvert.DeserializeObject(txt);
                string txt_request = json_request["request"];

                HttpListenerResponse response = ctx.Response;
                response.ContentType = "application/json";
                string rsp_txt = "";
                byte[] rsp_buff;
                switch (txt_request)
                {
                    case "status":
                        {
                            var status = systemMonitor.getStatus();
                            rsp_txt = JsonConvert.SerializeObject(status);
                            break;
                        }
                    case "subscribe":
                        {
                            string ip = getIPfromContext(ctx);
                            if (!listeners.Contains(ip))
                            {
                                listeners.Add(ip);
                                Debug.WriteLine(ip);
                                rsp_txt = JsonConvert.SerializeObject(ip + " registered");
                            } else
                            {
                                rsp_txt = JsonConvert.SerializeObject(ip + " is already registered");
                            }
                            new Thread(() =>
                            {
                                HttpContent content = new StringContent(JsonConvert.SerializeObject(systemMonitor.foregroundProcessMonitor.foregroundProcessName), Encoding.UTF8, "application/json");
                                Debug.WriteLine(systemMonitor.foregroundProcessMonitor.foregroundProcessName);
                                client.PostAsync(ip + "/foregroundProcessName", content);
                            }).Start();
                            break;
                        }
                    case "foregroundProcessIcon":
                        {
                            string ip = getIPfromContext(ctx);
                            new Thread(() => {
                                // Retrieve process name and icon
                                Process process = systemMonitor.foregroundProcessMonitor.foregroundProcess;
                                string name = null;
                                string icon = convertExeIconToBase64(process);
                                try
                                {
                                    name = process != null ? process.ProcessName : null;
                                }
                                catch { }
                                // Attempt to send data back to requester
                                try
                                {
                                    HttpContent content = new StringContent(JsonConvert.SerializeObject(new string[] { name, icon }), Encoding.UTF8, "application/json");
                                    client.PostAsync(ip + "/foregroundProcessIcon", content);
                                }
                                catch { }
                            }).Start();
                            rsp_txt = JsonConvert.SerializeObject("OK");
                            break;
                        }
                    case "steamLoginUP":
                        {
                            string ip = getIPfromContext(ctx);
                            new Thread(() => {
                                Steam_Login steamLoginForm = new Steam_Login(ip);
                                steamLoginForm.sendSteamLogin = this.sendSteamLoginUsernameAndPassword;
                                Application.Run(steamLoginForm);
                            }).Start();
                            rsp_txt = JsonConvert.SerializeObject("OK");
                            Debug.WriteLine("Steam login data request");
                            break;
                        }
                    case "steamLogin2FA":
                        {
                            string ip = getIPfromContext(ctx);
                            new Thread(() =>
                            {
                                Steam_2FA steam2faForm = new Steam_2FA(ip);
                                steam2faForm.sendSteam2FA = this.sendSteam2FA;
                                Application.Run(steam2faForm);
                            }).Start();
                            rsp_txt = JsonConvert.SerializeObject("OK");
                            Debug.WriteLine("Steam 2FA data request");
                            break;
                        }
                    case "powerPlan":
                        {
                            string plan = json_request["name"];
                            Debug.WriteLine("Power Plan switch: " + plan);
                            powerPlanManager.applyPowerPlan(plan);
                            rsp_txt = JsonConvert.SerializeObject("OK");
                            break;
                        }
                    case "audioProfile":
                        {
                            string playbackDevice = json_request["devices"]["playbackDevice"];
                            string playbackCommunicationDevice = json_request["devices"]["playbackDeviceCommunication"];
                            string recordingDevice = json_request["devices"]["recordingDevice"];
                            AudioManager.switchDeviceTo(playbackDevice, 1);
                            AudioManager.switchDeviceTo(playbackCommunicationDevice, 2);
                            AudioManager.switchDeviceTo(recordingDevice, 1);
                            AudioManager.switchDeviceTo(recordingDevice, 2);
                            rsp_txt = JsonConvert.SerializeObject("OK");
                            break;
                        }
                    default:
                        {
                            rsp_txt = JsonConvert.SerializeObject("Unknown");
                            break;
                        }
                }
                rsp_buff = System.Text.Encoding.UTF8.GetBytes(rsp_txt);

                try
                {
                    response.Headers.Add(HttpResponseHeader.CacheControl, "private, no-store");
                    response.ContentLength64 = rsp_buff.Length;
                    response.OutputStream.Write(rsp_buff, 0, rsp_buff.Length);
                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.OutputStream.Close();
                    response.Close();
                }
                catch (Exception e) { }
            }
        }

        public void sendForegroundProcessToListener(object sender, string name)
        {
            foreach (var ip in listeners)
            {
                try
                {
                    HttpContent content = new StringContent(JsonConvert.SerializeObject(name), Encoding.UTF8, "application/json");
                    client.PostAsync(ip + "/foregroundProcessName", content);
                }
                catch (Exception e) { }
            }
        }

        public void sendNotificationToListener(object sender, NotificationMonitor.Notification notification)
        {
            foreach (var ip in listeners)
            {
                try
                {
                    HttpContent content = new StringContent(JsonConvert.SerializeObject(notification), Encoding.UTF8, "application/json");
                    client.PostAsync(ip+"/notification", content);
                } catch (Exception e) { }
            }
        }

        public void sendSteamLoginUsernameAndPassword(object sender, string[] formData)
        {
            try
            {
                string[] data = { formData[0], formData[1] };
                HttpContent content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                client.PostAsync(formData[2] + "/steamLogin", content);
            }
            catch (Exception e) { }
        }

        public void sendSteam2FA(object sender, string[] formData)
        {
            try
            {
                string data = formData[0];
                HttpContent content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                client.PostAsync(formData[1] + "/steam2fa", content);
            }
            catch (Exception e) { }
        }

        private string getIPfromContext(HttpListenerContext ctx)
        {
            string ip = ctx.Request.RemoteEndPoint.ToString();
            ip = ip.Substring(0, ip.LastIndexOf(":"));
            ip = "http://" + ip + ":9520";
            return ip;
        }

        private string convertExeIconToBase64(Process p)
        {
            try
            {
                if (p != null)
                {
                    string filename = p.MainModule.FileName;
                    IntPtr hIcon = IconExtractor.GetJumboIcon(IconExtractor.GetIconIndex(filename));
                    // Extract Icon
                    string result;
                    ImageConverter converter = new ImageConverter();
                    using (Bitmap ico = ((Icon)Icon.FromHandle(hIcon).Clone()).ToBitmap())
                    {
                        Bitmap bitmap = IconExtractor.ClipToCircle(ico);
                        // save to file (or show in a picture box)
                        result = Convert.ToBase64String((byte[])converter.ConvertTo(bitmap, typeof(byte[])));
                        bitmap.Dispose();
                    }
                    IconExtractor.Shell32.DestroyIcon(hIcon); // Cleanup
                    return result;
                }
            }
            catch (Exception e) { }
            return null;
        }
    }
}