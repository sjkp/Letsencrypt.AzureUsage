using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Letsencrypt.AzureUsage.Test
{
    [TestClass]
    public class TestFunctions
    {
        [DeploymentItem("O365IPAddresses.xml")]
        [TestMethod]
        public void TestGetOffice365ServicIps()
        {
            var res = Functions.LoadOffice365Ips(System.IO.File.ReadAllText("O365IPAddresses.xml")).ToList();

            Assert.IsNotNull(res);
            Assert.AreNotEqual(0, res.Count);
        }
    }
}
