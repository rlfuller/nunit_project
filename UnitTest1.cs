using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using System.Diagnostics;
using System;
using System.Text;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace Tests
{
    
    public class Tests
    {
        // [SetUp]
        // public void Setup()
        // {
        // }{

        private IConfiguration Configuration {get; set;}
        private readonly HttpClient client = new HttpClient();

        //public string json;
        public JObject myJson;
        public int closingsCount;

        public IWebDriver driver;


        [OneTimeSetUp]
        public void SetUpTest()
        {
            //get configuration needed for rover
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");
            
            Configuration = builder.Build();

            var username = Environment.GetEnvironmentVariable("ROVER_USERNAME");
            var password = Environment.GetEnvironmentVariable("ROVER_PASSWORD");

            //get rover data
            string rover_url = String.Format(
                "{0}/sites?image_sitename={1}", Configuration["rover-api"], Configuration["site"]
            );

            string json;
            using (WebClient wb = new WebClient()) {
                string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(username + ":" + password));
                wb.Headers["Authorization"] = string.Format("Basic {0}", credentials);

                json = wb.DownloadString(rover_url);
            }

            myJson = JObject.Parse(json);

            //check if closings are enabled
            bool closingsEnabled = (bool) myJson["data"][0]["metadata"]["closings"]["enabled"];
            
            //if closings aren't enabled, end program/do not test, maybe set a flag
            if (!closingsEnabled) {
                Assert.Ignore("No tests should be run");
                return;
            }

            //get the closings counts number (will need this value for some of the tests)

            var env = Configuration["env"];
            var site = Configuration["site"];

            if ((env == "cdn") || (env == "origin") || (env == "app"))
            {
                env = "prod";
            }
            else if (env != "stage")
            {
                env = "qa";
            }
            
            string closings_data_url = String.Format(
                "http://closings.one-htv{0}-us-east-1.{0}.htvapps.net/api/v1/{1}/closings/count", env, site
            );

            using (WebClient closingsClient = new WebClient()) {
                try
                {
                    var jsonClosings = closingsClient.DownloadString(closings_data_url);

                    var closings = JObject.Parse(jsonClosings);
                    closingsCount = (int) closings["closing_count"]["count"];
                }
                catch (Exception e)
                {
                    Assert.Ignore("Could not get closings count from closings API: {0}", e.Message);
                }
            }
            //get url
            driver = new ChromeDriver();
            driver.Url = String.Format("http://{0}{1}", Configuration["host"], Configuration["path"]);
            

            //disable flex ad
            int sleep = 2000;
            System.Threading.Thread.Sleep(sleep);
            //driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
            var main_content = driver.CurrentWindowHandle;
            var frames = driver.FindElements(By.CssSelector("#ad-flex iframe"));
            if (frames.Count > 0)
            {
                if(frames[0].Displayed)
                {
                    driver.SwitchTo().Frame(frames[0]);
                    System.Threading.Thread.Sleep(sleep);
                    var logos = driver.FindElements(By.CssSelector("[class*=station-logo]"));

                    
                    foreach (var logo in logos)
                    {
                        try
                        {
                            logo.Click();
                            break;
                        }
                        catch
                        {}
                    }
                    driver.SwitchTo().Window(main_content);
                }
            }

            //run all tests


        }

        [OneTimeTearDown]
        public void endTest()
        {
            driver.Close();
            driver.Quit();
        }

        [Test]
        public void Test1()
        {   
            //System.Console.WriteLine("hello");
        //System.Console.WriteLine(json);
            // Console.Out.WriteLine("rachel jlkdfjsl");
            // TestContext.Out.WriteLine("alkdjsldfjsl this sucks");
            System.Console.WriteLine(closingsCount);
            
            Assert.Pass();
        }
    }
}