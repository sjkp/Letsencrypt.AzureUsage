//----------------------------------------------------------------------------------
// Microsoft Developer & Platform Evangelism
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//----------------------------------------------------------------------------------
// The example companies, organizations, products, domain names,
// e-mail addresses, logos, people, places, and events depicted
// herein are fictitious.  No association with any real company,
// organization, product, domain name, email address, logo, person,
// places, or events is intended or should be inferred.
//----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using System.Net;
using System.Xml;
using System.Net.Http;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Microsoft.WindowsAzure.Storage;
using System.Configuration;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Letsencrypt.AzureUsage
{
    public class Functions
    {
        public class SslCertInformation
        {
            public string Hostname { get; set; }
            public DateTime IssuedDate { get; set; }
            public DateTime ExpireDate { get; set; }

        }

        public static string GetAzureIpDatacenterUrl()
        {
            string baseUrl = "https://www.microsoft.com/en-us/download/confirmation.aspx?id=41653";

            HttpClient client = new HttpClient();
            var res = client.GetAsync(baseUrl).Result;

            var html = res.Content.ReadAsStringAsync().Result;

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var links = doc.DocumentNode.Descendants().Where(s => s.NodeType == HtmlAgilityPack.HtmlNodeType.Element && s.Attributes["href"] != null && s.Attributes["href"].Value.StartsWith("https://download.microsoft.com/download"));

            var url = links.Select(s => s.Attributes["href"].Value).FirstOrDefault(s => s.Contains("PublicIPs"));

            return url;

        }

        static Lazy<List<AzureRegionIp>> AzureIpNetworks = new Lazy<List<AzureRegionIp>>(() =>
        {
            HttpClient client = new HttpClient();

            string xml = client.GetStringAsync(GetAzureIpDatacenterUrl()).Result;

            return LoadAzureIps(xml).ToList();
        });

        static Lazy<List<Office365ServiceIp>> Office365Networks = new Lazy<List<Office365ServiceIp>>(() =>
        {
            HttpClient client = new HttpClient();

            var xml = client.GetStringAsync("https://support.content.office.net/en-us/static/O365IPAddresses.xml").Result;

            return LoadOffice365Ips(xml).ToList();
        }
        );

        public static IEnumerable<Office365ServiceIp> LoadOffice365Ips(string xml)
        {
            XmlDocument xdoc = new XmlDocument();
            xdoc.LoadXml(xml);


            var products = xdoc.SelectNodes("//*/product");
            foreach (XmlNode product in products)
            {
                
                foreach (XmlNode address in product.SelectNodes("*[@type=\"IPv4\"]/address"))
                {
                    
                        var subnet = address.InnerText;
                        var i = IPNetwork.Parse(subnet);
                        yield return new Office365ServiceIp
                        {
                            IPNetwork = i,
                            Product = product.Attributes["name"].Value
                        };
                    
                }
            }
        }

        public static string CheckSSLPath(IEnumerable<string> hostnames)
        {
            Dictionary<string, bool> res = new Dictionary<string, bool>();
            foreach (var hostname in hostnames)
            {
                var process = Process.Start(new ProcessStartInfo()
                {
                    FileName = @"opensslMD.exe",
                    Arguments = $"s_client -connect {hostname}:443 -servername {hostname}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    
                });
                var s = "";
                //process.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
                //{
                //    s += e.Data;
                //};


                
                var result = process.WaitForExit(2000);
                if (!process.HasExited)
                {
                    process.Kill();
                }
                s = process.StandardOutput.ReadToEnd();

                if (s.Contains("Let's Encrypt Authority X1"))
                    return s;                
            }

            return null;
            
        }       

        public static async  Task<AzureInformation> GetAzureInformation(string ipOrHostname)
        {
            IPAddress ip;
            if (IPAddress.TryParse(ipOrHostname, out ip))
            {
                return GetAzureInformationFromIp(ip);
            }
            else
            {
                ip = await GetIpFromHost(ipOrHostname);
                if (ip != null)
                {
                    return GetAzureInformationFromIp(ip);
                }
            }
            return null;
        }

        public static async Task<AzureInformation> GetOffice365Information(string ipOrHostname)
        {
            IPAddress ip;
            if (IPAddress.TryParse(ipOrHostname, out ip))
            {
                return Office365InformationFromIp(ip);
            }
            else
            {
                ip = await GetIpFromHost(ipOrHostname);
                if (ip != null)
                {
                    return Office365InformationFromIp(ip);
                }
            }
            return null;
        }

        public static AzureInformation Office365InformationFromIp(IPAddress ip)
        {
            var found = Office365Networks.Value.Where(s => IPNetwork.Contains(s.IPNetwork, ip));
            if (found.Any())
            {
                return new AzureInformation
                {
                    Office365 = string.Join(", ", found.Select(s => s.Product).ToArray())
                };
            }
            return null;
        }

        private static AzureInformation GetAzureInformationFromIp(IPAddress ip)
        {
            var found = AzureIpNetworks.Value.FirstOrDefault(s => IPNetwork.Contains(s.IPNetwork, ip));
            if (found != null)
                return new AzureInformation()
                {
                    Region = found.Region
                };
            return null;
        }

        public static IEnumerable<SslCertInformation> ParseHtmlPage(string filename)
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.Load(filename);

            var atags = doc.DocumentNode.Descendants().Where(s => s.GetAttributeValue("href", "").StartsWith("?id="));
            foreach(var a in atags)
            {
                var dates = atags.First().ParentNode.ParentNode.Descendants().Where(s => s.GetAttributeValue("style", "") == "white-space:nowrap");
                foreach(var date in dates)
                {
                    //Console.WriteLine(date.InnerText + " ");
                }
                //Console.WriteLine(a.InnerText);
                yield return new SslCertInformation()
                {
                    Hostname = a.InnerText.Substring(3),
                    IssuedDate = DateTime.Parse(dates.ElementAt(0).InnerText),
                    ExpireDate = DateTime.Parse(dates.ElementAt(1).InnerText),
                };
            }
            


        }

        public class AzureRegionIp
        {
            public string Region { get; set; }
            public IPNetwork IPNetwork { get; set; }
        }
        public static IEnumerable<AzureRegionIp> LoadAzureIps(string xml)
        {
            XmlDocument xdoc = new XmlDocument();
            xdoc.LoadXml(xml);

            var regions = xdoc.SelectNodes("//*/Region");
            foreach (XmlNode region in regions)
            {
                foreach (XmlNode node in region.ChildNodes)
                {
                    var subnet = node.Attributes["Subnet"].Value;
                    var i = IPNetwork.Parse(subnet);
                    //Console.WriteLine(i.FirstUsable.ToString() + " " + i.LastUsable.ToString());
                    //var a = IPNetwork.ListIPAddress(i);
                    yield return new AzureRegionIp
                    {
                        IPNetwork = i,
                        Region = region.Attributes["Name"].Value
                    };
                }
            }
        }

        public static void DownloadCerts()
        {
            var url = "https://crt.sh/?identity=%25&iCAID=7395&p=";

            HttpClient client = new HttpClient();
            for(int i = 1; i< 1000; i++)
            {
                Console.WriteLine(i);
                File.WriteAllText(i + ".html", client.GetStringAsync(url + i +"&n=1000").Result);
            };
        }

        public static async Task<IPAddress> GetIpFromHost(string hostname)
        {
            IPHostEntry host;
            try
            {
                host = await Dns.GetHostEntryAsync(hostname);

            } catch(SocketException soe)
            {
                //Console.WriteLine("Not founud" + hostname);
                return null;
            }

            //Console.WriteLine("GetHostEntry({0}) returns:", hostname);

            foreach (IPAddress ip in host.AddressList)
            {
                //Console.WriteLine("    {0}", ip);
            }
            return host.AddressList.FirstOrDefault();
        }

        /// <summary>
        /// Reads a blob from the container named "input" and writes it to the container named "output". The blob name ("name") is preserved
        /// </summary>
        /// 
        
        public static void ReadFiles()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.ConnectionStrings["AzureWebJobsStorage"].ConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("output");
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("azurehostnames");
            queue.CreateIfNotExists();
            var blobs = container.ListBlobs();
            int i = 0;
            foreach (var blob in blobs)
            {
                i++;
                var block = blob as CloudBlockBlob;
                using (var ms = new MemoryStream())
                {
                    block.DownloadToStream(ms);
                    var s = System.Text.Encoding.UTF8.GetString(ms.ToArray().Skip(3).ToArray());
                
                var  urls = s.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var url in urls)
                    {
                        if (!string.IsNullOrEmpty(url) && url.Length > 1)
                        {
                            Console.WriteLine(i +"/"+ blobs.Count() + url);
                            queue.AddMessage(new CloudQueueMessage(url));
                        }
                    }

                }
            }
        }
   
        public static void ReadFilesFromAzure()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.ConnectionStrings["AzureWebJobsStorage"].ConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("shouldbenotified");
            var blobs = container.ListBlobs();
            int i = 0;
            List<string> urls = new List<string>();
            Parallel.ForEach(blobs, new ParallelOptions() { MaxDegreeOfParallelism = 30 }, (blob) =>
            {
                i++;
                //var block = blob as CloudBlockBlob;
                var filename = blob.Uri.AbsolutePath.Split(new char[] { '/' }).Last();
                Console.WriteLine(i + "  " + filename);
                var arr = filename.Split(new char[] { '.' });
                urls.Add(filename + ";" + string.Join(".", arr.Skip(arr.Length - 2).ToArray()));
            });

            File.WriteAllLines("notify.txt", urls.ToArray());
        }

       
        public static void ProcessAzureHostnames(
            [QueueTrigger("azurehostnames")] string hostname,
            IBinder binder)
        {
            WebRequestHandler handler = new WebRequestHandler()
            {
            };
            handler.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            var client = new HttpClient(handler);
            HttpResponseMessage response = null;
            try {
                response = client.GetAsync("https://" + hostname).Result;
            } catch(Exception ex)
            {
                Trace.TraceError(ex.ToString());
                return;
            }
            Console.WriteLine(hostname + " " + response.Headers.Server);
            if (response.Headers.Server.Any(s => s.Product?.Name?.Contains("IIS") != null && s.Product?.Name?.Contains("IIS") == true))
            {
                //Seems like it is iis
                var res = CheckSSLPath(new[] { hostname });
                if (res != null)
                {                    
                    using (var writer = binder.Bind<TextWriter>(new BlobAttribute("shouldbenotified/" + hostname)))                    
                    {
                        var queue = binder.Bind<CloudQueue>(new QueueAttribute("shouldbenotified"));
                        writer.Write(res);
                        queue.AddMessage(new CloudQueueMessage(hostname));
                    }
                }
            }            
        }

        /// <summary>
        /// Same as "BlobNameFromQueueMessage" but using IBinder 
        /// </summary>
        public static void BlobIBinder([QueueTrigger("hostnames")] List<string> hostnames, IBinder binder)
        {
            TextWriter writer = binder.Bind<TextWriter>(new BlobAttribute("output/" + Guid.NewGuid()));


            var block = new ActionBlock<Functions.SslCertInformation>(async ip => await Program.DoWork(ip, writer));            
            hostnames.ForEach(s => block.Post(new SslCertInformation { Hostname = s }));

            block.Complete();
            block.Completion.Wait();
        }
    }

    public class Office365ServiceIp
    {
        public IPNetwork IPNetwork { get; set; }
        public string Product { get; internal set; }
    }

    public class AzureInformation
    {
        public string Region { get; internal set; }
        public string Office365 { get; set; }
    }

    public class Person
    {
        public string Name { get; set; }

        public int Age { get; set; }
    }

    public class BlobTriggerPosionMessage
    {
        public string FunctionId { get; set; }
        public string BlobType { get; set; }
        public string ContainerName { get; set; }
        public string BlobName { get; set; }
        public string ETag { get; set; }
    }
}
