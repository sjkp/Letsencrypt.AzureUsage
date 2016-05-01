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

        static Lazy<List<AzureRegionIp>> IpNetworks = new Lazy<List<AzureRegionIp>>(() =>
        {
            HttpClient client = new HttpClient();

            string xml = client.GetStringAsync("https://download.microsoft.com/download/0/1/8/018E208D-54F8-44CD-AA26-CD7BC9524A8C/PublicIPs_20160426.xml").Result;

            return LoadAzureIps(xml).ToList();
        });

        public static AzureInformation GetAzureInformation(string ipOrHostname)
        {
            IPAddress ip;
            if (IPAddress.TryParse(ipOrHostname, out ip))
            {
                return GetAzureInformationFromIp(ip);
            }
            else
            {
                ip = GetIpFromHost(ipOrHostname);
                if (ip != null)
                {
                    return GetAzureInformationFromIp(ip);
                }
            }
            return null;
        }

        private static AzureInformation GetAzureInformationFromIp(IPAddress ip)
        {
            var found = IpNetworks.Value.FirstOrDefault(s => IPNetwork.Contains(s.IPNetwork, ip));
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
            Parallel.For(0, 100, (i) =>
            {
                Console.WriteLine(i);
                File.WriteAllText(i + ".html", client.GetStringAsync(url + i).Result);
            });
        }

        public static IPAddress GetIpFromHost(string hostname)
        {
            IPHostEntry host;
            try
            {
                host = Dns.GetHostEntry(hostname);

            } catch(SocketException soe)
            {
                Console.WriteLine("Not founud" + hostname);
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
        
        public static void BlobToBlob([BlobTrigger("input/{name}")] TextReader input, [Blob("output/{name}")] out string output)
        {
            output = input.ReadToEnd();
        }

        /// <summary>
        /// This function is triggered when a new blob is created by "BlobToBlob"
        /// The blob name and extension will be bound from the name pattern
        /// </summary>
        public static async Task BlobTrigger(
            [BlobTrigger("output/{name}.{ext}")] Stream input,
            string name,
            string ext,
            TextWriter log)
        {
            log.WriteLine("Blob name:" + name);
            log.WriteLine("Blob extension:" + ext);

            using (StreamReader reader = new StreamReader(input))
            {
                string blobContent = await reader.ReadToEndAsync();
                log.WriteLine("Blob content: {0}", blobContent);
            }
        }

        /// <summary>
        /// Reads a "Person" object from the "persons" queue
        /// The parameter "Name" will have the same value as the property "Name" of the person object
        /// The output blob will have the name of the "Name" property of the person object
        /// </summary>
        public static async Task BlobNameFromQueueMessage(
            [QueueTrigger("persons")] Person persons,
            string Name,
            [Blob("persons/{Name}BlobNameFromQueueMessage", FileAccess.Write)] Stream output)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes("Hello " + Name);

            await output.WriteAsync(messageBytes, 0, messageBytes.Length);
        }

        /// <summary>
        /// Demonstrates how to write a queue message when a blob is created or updated
        /// </summary>
        public static void BlobToQueue(
            [BlobTrigger("test/{name}")] string input,
            string name,
            [Queue("newblob")] out string message)
        {
            message = name;
        }

        /// <summary>
        /// Same as "BlobNameFromQueueMessage" but using IBinder 
        /// </summary>
        public static void BlobIBinder([QueueTrigger("persons")] Person persons, IBinder binder)
        {
            TextWriter writer = binder.Bind<TextWriter>(new BlobAttribute("persons/" + persons.Name + "BlobIBinder"));
            writer.Write("Hello " + persons.Name);
        }

        /// <summary>
        /// Not writing anything into the output stream will not lead to blob creation
        /// </summary>
        public static void BlobCancelWrite([QueueTrigger("persons")] Person persons, [Blob("output/ShouldNotBeCreated.txt")] TextWriter output)
        {
            // Do not write anything to "output" and the blob will not be created
        }

        /// <summary>
        /// This function will always fail. It is used to demonstrate error handling.
        /// After a binding or a function fails 5 times, the trigger message is marked as poisoned
        /// </summary>
        public static void FailAlways([BlobTrigger("badcontainer/{name}")] string message, TextWriter log)
        {
            log.WriteLine("When we reach 5 retries, the message will be moved into the badqueue-poison queue");

            throw new InvalidOperationException("Simulated failure");
        }

        /// <summary>
        /// This function will be invoked when a message end up in the poison queue
        /// </summary>
        public static void PoisonErrorHandler([QueueTrigger("webjobs-blogtrigger-poison")] BlobTriggerPosionMessage message, TextWriter log)
        {
            log.Write("This blob couldn't be processed by the original function: " + message.BlobName);
        }
    }

    public class AzureInformation
    {
        public string Region { get; internal set; }
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
