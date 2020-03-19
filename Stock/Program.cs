using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Stock
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var client = new HttpClient();
            var list = new List<Stock>();
            for (var p = 1; ; p++)
            {
                var html = await client.GetStringAsync($"http://datainterface.eastmoney.com/EM_DataCenter/JS.aspx?type=SHSZZS&sty=SHSZZS&st=0&sr=-1&p={p}&ps=50&js={{pages:(pc),data:[(x)]}}&code=000300&rt=52816722");
                if (string.IsNullOrEmpty(html))
                {
                    return;
                }
                var dic = JsonConvert.DeserializeObject<JObject>(html);
                var items = dic.Value<JArray>("data").ToObject<string[]>();
                foreach (var item in items)
                {
                    var arr = item.Split(',');
                    var stock = new Stock() { Code = arr[0], Name = arr[1], Industry = arr[2], StockPrice = decimal.Parse(arr[9]), AssetPrice = decimal.Parse(arr[5]) };
                    if (stock.AssetPrice > stock.StockPrice)
                    {
                        list.Add(stock);
                    }
                }
                if (dic.Value<int>("pages") <= p)
                {
                    break;
                }
                await Task.Delay(1000);
            }

            foreach (var item in list)
            {
                string html;
                if (item.Code.StartsWith("600") || item.Code.StartsWith("601") || item.Code.StartsWith("603"))
                {
                    html = await client.GetStringAsync($"https://xueqiu.com/S/SH{item.Code}");

                }
                else if (item.Code.StartsWith("000") || item.Code.StartsWith("300"))
                {
                    html = await client.GetStringAsync($"https://xueqiu.com/S/SZ{item.Code}");
                }
                else
                {
                    Console.WriteLine("{0},{1}", item.Name, item.Code);
                    continue;
                }
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                var node = htmlDoc.DocumentNode.SelectSingleNode("//tr[6]//td[2]//span[1]");
                Console.WriteLine(node.InnerText);
            }
            Console.WriteLine("Hello World!");
        }
    }
}
