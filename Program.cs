using AngleSharp;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace FB.ExpiredDomainsParser
{
    class Program
    {
        const string CookiesFileName = "cookies.txt";
        const int GoodDomainLikes = 1000;
        static async Task Main(string[] args)
        {
            CopyrightHelper.Show();
            ServicePointManager.ServerCertificateValidationCallback +=
                    (sender, certificate, chain, sslPolicyErrors) => true;
            string proxystr = null, token = null;
            if (!File.Exists(CookiesFileName))
            {
                Console.WriteLine("File with Facebook cookies (cookies.txt) not found!\nCreate one, put cookies there and restart!");
                return;
            }
            var cookies = File.ReadAllText(CookiesFileName);
            if (args.Length == 2)
            {
                proxystr = args[0];
                token = args[1];
            }

            if (string.IsNullOrEmpty(proxystr))
            {
                Console.WriteLine("Proxy format - ip:port:login:password");
                Console.WriteLine("Proxy must be HTTP (not SOCKS)");
                Console.Write("Write your proxy (press Enter, if not needed):");
                proxystr = Console.ReadLine();
            }
            if (string.IsNullOrEmpty(token))
            {
                Console.Write("Write your Facebook access token:");
                token = Console.ReadLine();
            }

            WebProxy proxy;
            HttpClient httpClient = new HttpClient();
            var rco = new RestClientOptions("https://graph.facebook.com/v15.0") { MaxTimeout = -1 };

            var r = new Random();
            if (!string.IsNullOrEmpty(proxystr))
            {
                var split = proxystr.Split(':');
                proxy = new WebProxy(split[0], int.Parse(split[1]))
                {
                    Credentials = new NetworkCredential()
                    {
                        UserName = split[2],
                        Password = split[3]
                    }
                };
                var hch = new HttpClientHandler() { UseProxy = true, Proxy = proxy };
                httpClient = new HttpClient(hch);

                var tmp = r.Next(20, 99);
                httpClient.DefaultRequestHeaders.Add("User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.{tmp} (KHTML like Gecko) Chrome/78.0.3904.108 Safari/537.{tmp}");
                rco.Proxy = proxy;
            }
            var client = new RestClient(rco);

            List<string> domains = new List<string>();
            var zones = new int[] { 2, 3, 4, 5, 7, 12, 19, 59, 69, 76, 1, 87, 94, 89, 119, 129, 154, 167, 247, 249, 674, 1129, 1065, 595, 660 };
            foreach (var z in zones)
            {
                Console.WriteLine($"Getting domain names for zone {z}...");
                var res = await httpClient.GetAsync($"https://www.expireddomains.net/deleted-domains?ftlds[]={z}");
                int i = 25;
                int dCount = 1;
                do
                {
                    try
                    {
                        var resContent = await res.Content.ReadAsStringAsync();
                        Console.WriteLine($"Got domains list #{dCount}.");
                        var config = Configuration.Default;
                        var context = BrowsingContext.New(config);
                        var doc = await context.OpenAsync(req => req.Content(resContent));
                        var newDomains = doc.All
                            .Where(el => el.ClassList.Contains("field_domain")).Select(el => el.TextContent.ToLower()).ToList();
                        Console.WriteLine($"Found {newDomains.Count} domains.");
                        domains.AddRange(newDomains);
                        if (newDomains.Count == 0)
                        {
                            Console.WriteLine("The list ended, not going further.");
                            break;
                        }
                        i += 25;
                        dCount++;
                        Console.WriteLine("Waiting...");
                        await Task.Delay(r.Next(1000, 3000));
                        res = await httpClient.GetAsync($"https://www.expireddomains.net/deleted-domains/?start={i}&ftlds[]={z}#listing");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Got error: {e}");
                    }
                } while (i <= 300);
            }

            var found = new List<string>();
            var prefixes = new[] { "http://", "https://" };
            foreach (var d in domains)
            {
                if (d.Contains("...")) continue;
                foreach (var p in prefixes)
                {
                    var domain = $"{p}{d}";
                    Console.Write($"Getting domain likes for {domain}...");
                    var request = new RestRequest();
                    LoadCookiesIntoRequest(request, cookies);
                    request.AddQueryParameter("id", domain);
                    request.AddQueryParameter("scrape", "true");
                    request.AddQueryParameter("fields", "engagement");
                    request.AddQueryParameter("access_token", token);

                    var response = await client.GetAsync(request);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        Console.WriteLine($"\nCouldn't get domain likes for {domain}");
                        Console.WriteLine(response.ErrorMessage);
                        continue;
                    }
                    var obj = JObject.Parse(response.Content);
                    var count = int.Parse(obj["engagement"]["reaction_count"].ToString());
                    count += int.Parse(obj["engagement"]["comment_count"].ToString());
                    count += int.Parse(obj["engagement"]["share_count"].ToString());
                    Console.WriteLine($" {count}");

                    if (count >= GoodDomainLikes)
                    {
                        Console.WriteLine($"Checking {domain}...");
                        request = new RestRequest();
                        LoadCookiesIntoRequest(request, cookies);
                        request.AddQueryParameter("id", domain);
                        request.AddQueryParameter("scrape", "true");
                        request.AddQueryParameter("fields", "engagement");
                        request.AddQueryParameter("access_token", token);
                        response = await client.GetAsync(request);
                        obj = JObject.Parse(response.Content);
                        count = int.Parse(obj["engagement"]["reaction_count"].ToString());
                        count += int.Parse(obj["engagement"]["comment_count"].ToString());
                        count += int.Parse(obj["engagement"]["share_count"].ToString());
                        if (count >= GoodDomainLikes)
                        {
                            found.Add($"{domain} : {count}");
                            Console.WriteLine($"{domain} : {count}");
                        }
                        else
                            Console.WriteLine("No, that's not good!");
                    }
                }
            }

            if (found.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Found good domains!");
                found.ForEach(Console.WriteLine);
            }
            else Console.WriteLine("Didn't find anything good((");
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        internal static void LoadCookiesIntoRequest(RestRequest req, string cookies)
        {
            var res = new List<string>();
            try
            {
                var cookieArray = JArray.Parse(cookies);
                if (cookieArray != null)
                {
                    res = cookieArray
                        .Where(c => c["domain"].ToString() == ".facebook.com")
                        .Select(c => $"{c["name"]}={c["value"]};").ToList();
                }
                if (res.Count > 0)
                {
                    req.AddHeader("cookie", string.Join(" ", res).TrimEnd(';'));
                }

            }
            catch { }
        }
    }
}
