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
using System.Linq;

namespace Tests
{
    public class Tests
    {

        //private IConfiguration Configuration {get; set;}
        private IConfiguration _configuration;
        public IConfiguration Configuration {
            get
            {
                if (_configuration != null)
                    return _configuration;

                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json");
                
                return _configuration = builder.Build();
            }
        }

        public JObject roverData;
        public int closingsCount;
        public IWebDriver driver;

        private int initialFlexAdTimeout = 2000;

        private void setUpRoverData()
        {
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

            roverData = JObject.Parse(json);
        }

        private void setUpClosingsCount()
        {
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
        }

        private void disableFlexAd(int timeout = 0)
        {
            
            System.Threading.Thread.Sleep(timeout);
            //driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
            var main_content = driver.CurrentWindowHandle;
            var frames = driver.FindElements(By.CssSelector("#ad-flex iframe"));
            if (frames.Count > 0)
            {
                if(frames[0].Displayed)
                {
                    driver.SwitchTo().Frame(frames[0]);
                    System.Threading.Thread.Sleep(timeout);
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
        }

        [OneTimeSetUp]
        public void SetUpTest()
        {
            //get configuration needed for rover
            // var builder = new ConfigurationBuilder()
            //     .SetBasePath(Directory.GetCurrentDirectory())
            //     .AddJsonFile("appsettings.json");
            
            // Configuration = builder.Build();

            //Rover Data
            setUpRoverData();

            //check if closings are enabled
            bool closingsEnabled = (bool) roverData["data"][0]["metadata"]["closings"]["enabled"];
            
            //if closings aren't enabled, end program/do not test, maybe set a flag
            if (!closingsEnabled) {
                Assert.Ignore("No tests should be run");
                return;
            }

            //get the closings counts number (will need this value for some of the tests)
            setUpClosingsCount();
            
            //get url
            driver = new ChromeDriver();
            driver.Url = String.Format("http://{0}{1}", Configuration["host"], Configuration["path"]);
            

            //disable flex ad
            disableFlexAd(initialFlexAdTimeout);

            //run all tests

        }

        [OneTimeTearDown]
        public void endTest()
        {
            driver.Close();
            driver.Quit();
        }

        [SetUp]
        public void Setup()
        {
            disableFlexAd();
        }

        [Test]
        public void TestClosingCountAlertBar()
        {
            if (closingsCount < 1) {
                Assert.Ignore("Closing Count must be greater than 0 for this test.");
                return;
            }

            var alertsBarEl = driver.FindElement(By.CssSelector(".alerts--container"));
            var anchorEls = alertsBarEl.FindElements(By.CssSelector("a"));
            
            var anchorEl = (from anchor in anchorEls
                             where anchor.GetAttribute("textContent").Contains("Closings:")
                             select anchor).FirstOrDefault();

            Assert.IsNotNull(anchorEl, "Closing Alert not found in alerts bar.");

            Assert.Multiple(
                () => { 
                    TestAlertBarClosingsText(anchorEl);
                    TestAlertBarClosingsNumber(anchorEl);
                    Assert.Fail("argh!");
                    Assert.True(false,"NOt really true");
                }
            );

        }

        public void TestAlertBarClosingsText(IWebElement el)
        {

            IWebElement closingsLabelEl = el.FindElement(By.CssSelector(".alerts--label"));
            string closingsLabel = closingsLabelEl.GetAttribute("textContent");

            IWebElement closingsHeadlineEl = el.FindElement(By.CssSelector(".alerts--headline"));
            string closingsHeadline = closingsHeadlineEl.GetAttribute("textContent");

            Tuple<string, int, string> closingsSyntax;
            if (this.closingsCount == 1){
                closingsSyntax = new Tuple<string, int, string>("is", 1, "");
            }
            else
                closingsSyntax = new Tuple<string, int, string>("are", closingsCount, "s");
            
            string comparisionText = String.Format("Closings: There {0} currently {1} active closing{2} or delay{2}",
                                            closingsSyntax.Item1, closingsSyntax.Item2, closingsSyntax.Item3);

            string closingsText = String.Format("{0} {1}", closingsLabel, closingsHeadline);

            Assert.AreEqual(comparisionText, closingsText, "There are active closings, but the closings text is not correct." + 
                                                    "Page is displaying {0}.", closingsText);
        }

        public void TestAlertBarClosingsNumber(IWebElement el)
        {

            IWebElement closingsHeadlineEl = el.FindElement(By.CssSelector(".alerts--headline"));
            string closingsHeadline = closingsHeadlineEl.GetAttribute("textContent");

            //extract the closings number from the closings Headline

            // Split on one or more non-digit characters.
            int? alertsBarClosings = null;
            string[] numbers = System.Text.RegularExpressions.Regex.Split(closingsHeadline, @"\D+");
            foreach (string value in numbers)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    alertsBarClosings = int.Parse(value);
                }
            }
            Assert.AreEqual(alertsBarClosings, closingsCount, "Closings count is not correct. Api shows {0} and " + 
                "alerts bar shows {1}.", closingsCount, alertsBarClosings
                                            );
        }
    }
}