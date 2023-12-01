using NUnit.Framework;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System;
using System.Net.Http;
using System.Text;

using System.Threading.Tasks;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using RestSharp;

namespace CSharpPlaywrightBrowserStack
{
    [TestFixture]
    [Category("sample-test")]
    public class SampleTest : PageTest
    {
        public SampleTest() : base() { }

        [Test]
        public async Task SearchBstackDemo()
        {
            PercyHelper percy = new PercyHelper();
            //Navigate to the bstackdemo url
        
            _ = await Page.GotoAsync("https://google.com/");
            await percy.CapturePercyScreenshotAsync(Page,"ss1");

            // Add the first item to cart
            await Page.Locator("[id=\"\\31 \"]").GetByText("Add to Cart").ClickAsync();
            IReadOnlyList<string> phone = await Page.Locator("[id=\"\\31 \"]").Locator(".shelf-item__title").AllInnerTextsAsync();
            Console.WriteLine("Phone =>" + phone[0]);


            // Get the items from Cart
            IReadOnlyList<string> quantity = await Page.Locator(".bag__quantity").AllInnerTextsAsync();
            Console.WriteLine("Bag quantity =>" + quantity[0]);

            // Verify if there is a shopping cart
            StringAssert.Contains("1", await Page.Locator(".bag__quantity").InnerTextAsync());


            //Get the handle for cart item
            ILocator cartItem = Page.Locator(".shelf-item__details").Locator(".title");

            // Verify if the cart has the right item
            StringAssert.Contains(await cartItem.InnerTextAsync(), string.Join(" ", phone));
            IReadOnlyList<string> cartItemText = await cartItem.AllInnerTextsAsync();
            Console.WriteLine("Cart item => " + cartItemText[0]);

            Assert.That(phone[0], Is.EqualTo(cartItemText[0]));
        }
    }



    public class PercyHelper
    {
        string _percyBaseURL;
        public PercyHelper(string percyBaseUrl = "http://localhost:5338")
        {
            _percyBaseURL = percyBaseUrl;
        }

        /// <summary>
        /// Method ot check if Percy is running or not
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsPercyEnabledAsync()
        {
           
            bool isPercyEnabled = true;
            try
            {
                var options = new RestClientOptions(_percyBaseURL)
                {
                    MaxTimeout = -1,
                };
                var client = new RestClient(options);
                var request = new RestRequest("/percy/healthcheck", Method.Get);
                RestResponse response = await client.ExecuteAsync(request);
                  Console.WriteLine("Percy is enabled");
                return response.IsSuccessful;
            }
            catch (Exception)
            {
                Console.WriteLine("Percy is not running, disabling snapshots");
            }
            return isPercyEnabled;
        }

        /// <summary>
        /// Gettign teh PErcy DOM JS
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetPercyDOMAsync()
        {
            if (await IsPercyEnabledAsync())
            {
                 Console.WriteLine("Percy is enable in enabledaysnc");
                var options = new RestClientOptions(_percyBaseURL)
                {
                    MaxTimeout = -1,
                };
                var client = new RestClient(options);
                var request = new RestRequest("/percy/dom.js", Method.Get);
                RestResponse response = await client.ExecuteAsync(request);
                Console.WriteLine("Return the response");

                return response.Content;
            }
              Console.WriteLine("I return empty");
            return string.Empty;
        }

        /// <summary>
        /// Method to capture and post percy screenshot
        /// </summary>
        /// <param name="page">Playwright Page object</param>
        /// <param name="name">Name for the Percy Screenshot</param>
        /// <returns></returns>
        public async Task CapturePercyScreenshotAsync(IPage page, string name)
        {
            if (!await IsPercyEnabledAsync())
                return;
             Console.WriteLine("Percy before eval domasync");    
            await page.EvaluateAsync(await GetPercyDOMAsync());
            try
            {
                Console.WriteLine("Percy after eval domasync"); 
                var dom_snapshot = await page.EvaluateAsync<String>("return PercyDOM.serialize()");
                 Console.WriteLine("Percy after eval dom serialize"); 

                        using (HttpClient client = new HttpClient())
                        {
                     var postData = new
              {
                domSnapshot = dom_snapshot,
                url = page.Url,
                name = name
            };
                    var json = JsonConvert.SerializeObject(postData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = client.PostAsync("http://localhost:5338/percy/snapshot", content).Result;
            response.EnsureSuccessStatusCode();
                        }

            }
            catch (Exception e)
            {
                Console.WriteLine($"Not able to capture the screenshot due to error {e.Message}");
            }

        }
    }
}
