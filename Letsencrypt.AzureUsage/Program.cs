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

using ComputeWebJobsSDKBlob;
using Microsoft.Azure.WebJobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Letsencrypt.AzureUsage
{
    //******************************************************************************************************
    // This will show you how to perform common scenarios using the Microsoft Azure Blob storage service using 
    // the Microsoft Azure WebJobs SDK. The scenarios covered include triggering a function when a new blob is detected
    // or updated in a blob container and bind the contents of the blob to BCL types so you can read/ write the contents.   
    // 
    // In this sample, the Program class starts the JobHost and creates the demo data. The Functions class
    // contains methods that will be invoked when blobs are added to the storage account, based on the attributes in 
    // the method headers.
    //
    // To learn more about Microsoft Azure WebJobs SDK, please see http://go.microsoft.com/fwlink/?LinkID=320976
    //
    // TODO: Open app.config and paste your Storage connection string into the AzureWebJobsDashboard and
    //      AzureWebJobsStorage connection string settings.
    //*****************************************************************************************************
    class Program
    {
        static object @lock = new object();
        public static async Task DoWork(Functions.SslCertInformation ssl, TextWriter writer)
        {
            var sw = new Stopwatch();
            sw.Start();
            var ip = await Functions.GetIpFromHost(ssl.Hostname);
            sw.Stop();
            var dnsLookup = sw.Elapsed.TotalMilliseconds;

            if (ip != null)
            {
                sw.Reset();
                sw.Start();
                var foundNetwork = networks.FirstOrDefault(n => IPNetwork.Contains(n.IPNetwork, ip));
                if (foundNetwork != null)
                {
                    Console.WriteLine(ssl.Hostname + " found in " + foundNetwork.ToString());


                    writer.WriteLine(ssl.Hostname);


                }
                sw.Stop();
            }

            Console.WriteLine(" Dns: " + dnsLookup + " ms, rest: " + sw.Elapsed.TotalMilliseconds + " ms");
        }
        static List<Functions.AzureRegionIp> networks;
        static List<Functions.SslCertInformation> ssls;
        static void Main()
        {
            //Functions.DownloadCerts();
            //var res = Functions.CheckSSLPath(System.IO.File.ReadAllLines("foundInAzure.txt"));
            //System.IO.File.WriteAllLines("sslpathinfo.txt", res.Select(s => s.Key + "," + s.Value));

            //Console.WriteLine(Functions.GetAzureIpDatacenterUrl());
            //return;

            HashSet<string> inAzure = new HashSet<string>();
            networks = Functions.LoadAzureIps(System.IO.File.ReadAllText("PublicIPs_20160418.xml")).ToList();
#if DEBUG
            //ssls = System.IO.File.ReadAllLines(@"J:\Projects\certificatetransparency\tools\out3.txt").Where(s => s.Contains("Let's Encrypt Authority X1")).Select(s => new Functions.SslCertInformation() { Hostname = s.Split(new[] { ';' }).First() }).ToList();
            //var s = Functions.CheckSSLPath(new[] { "schdo.com" });
            //CreateDemoData();
            //Functions.ReadFiles();
            Functions.ReadFilesFromAzure();

            return;
#endif


            //var files = System.IO.Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "*.html");
            //Parallel.ForEach(files, (htmlFile, fstate, x) =>
            //{
            //    var ssls = Functions.ParseHtmlPage(htmlFile);
            //    Console.WriteLine(htmlFile);
            //    Parallel.ForEach(ssls, (ssl, state, i) =>
            //    {
            //        if (!inAzure.Contains(ssl.Hostname))
            //        {
            //            inAzure.Add(ssl.Hostname);
            //        }
            //        /*var ip = Functions.GetIpFromHost(ssl.Hostname);
            //        if (ip != null)
            //        {
            //            var foundNetwork = networks.FirstOrDefault(n => IPNetwork.Contains(n.IPNetwork, ip));
            //            if (foundNetwork != null)
            //            {
            //                Console.WriteLine(ssl.Hostname + " found in " + foundNetwork.ToString());
            //                if (!inAzure.Contains(ssl.Hostname))
            //                {
            //                    inAzure.Add(ssl.Hostname);
            //                }
            //            }
            //        }*/
            //    });
            //    Console.WriteLine(inAzure.Count);
            //});

            //System.IO.File.WriteAllLines("foundInAzure-2.txt", inAzure.ToArray());


            //if (!VerifyConfiguration())
            //{
            //    Console.ReadLine();
            //    return;
            //}

            //CreateDemoData();

            var cfg = new JobHostConfiguration()
            {

            };

            cfg.Queues.BatchSize = 4;

            JobHost host = new JobHost(cfg);
            host.RunAndBlock();
        }

        private static bool VerifyConfiguration()
        {
            string webJobsDashboard = ConfigurationManager.ConnectionStrings["AzureWebJobsDashboard"].ConnectionString;
            string webJobsStorage = ConfigurationManager.ConnectionStrings["AzureWebJobsStorage"].ConnectionString;

            bool configOK = true;
            if (string.IsNullOrWhiteSpace(webJobsDashboard) || string.IsNullOrWhiteSpace(webJobsStorage))
            {
                configOK = false;
                Console.WriteLine("Please add the Azure Storage account credentials in App.config");
            }
            return configOK;
        }

        private static void CreateDemoData()
        {
            Console.WriteLine("Creating Demo data");
            Console.WriteLine("Functions will store logs in the 'azure-webjobs-hosts' container in the specified Azure storage account. The functions take in a TextWriter parameter for logging.");

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.ConnectionStrings["AzureWebJobsStorage"].ConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("output");
            container.CreateIfNotExists();

            CloudQueue queue = CreateQueue(storageAccount, "hostnames");

            CreateQueue(storageAccount, "shouldbenotified");

            queue.FetchAttributes();
            var messageCount = queue.ApproximateMessageCount;
            Console.WriteLine("Message count: " + messageCount.GetValueOrDefault());
            return;
            int bSize = 250;

            for (int i = 0; i < ssls.Count / bSize; i++)
            {
                Console.WriteLine(i + "/" + ssls.Count / bSize);
                queue.AddMessage(new CloudQueueMessage(JsonConvert.SerializeObject(ssls.Skip(i * bSize).Take(bSize).Select(s => s.Hostname))));
            }

        }

        private static CloudQueue CreateQueue(CloudStorageAccount storageAccount, string name)
        {
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(name);
            queue.CreateIfNotExists();
            return queue;
        }
    }
}
