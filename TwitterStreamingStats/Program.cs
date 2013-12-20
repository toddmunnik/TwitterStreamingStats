using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitterStreamingStats
{
    class Program
    {
        private static BlockingCollection<string> _queuedTweets;
        private static int totalNumber = 0;
        private static int previousTotalNumber = 0;
        //top hashtags
        //percent that contain url
        //percent that contain a photo
        //top domain of urls in tweets

        static void Main(string[] args)
        {
            try
            {
                StreamTwitter();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        static void StreamTwitter()
        {
            string OAuthToken = ConfigurationManager.AppSettings["accesstoken"];
            string OAuthTokenSecret = ConfigurationManager.AppSettings["accesstokensecret"];
            string OAuthConsumerKey = ConfigurationManager.AppSettings["consumerkey"];
            string OAuthConsumerSecret = ConfigurationManager.AppSettings["consumersecret"];

            // Other OAuth connection/authentication variables

            string OAuthVersion = "1.0";
            string OAuthSignatureMethod = "HMAC-SHA1";
            string OAuthNonce = Convert.ToBase64String(new ASCIIEncoding().GetBytes(DateTime.Now.Ticks.ToString()));
            TimeSpan timeSpan = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            string OAuthTimestamp = Convert.ToInt64(timeSpan.TotalSeconds).ToString();
            string ResourceUrl = "https://stream.twitter.com/1.1/statuses/filter.json";

            // Generate OAuth signature. Note that Twitter is very particular about the format of this string. Even reordering the variables
            // will cause authentication errors.

            var baseFormat = "oauth_consumer_key={0}&oauth_nonce={1}&oauth_signature_method={2}" +
            "&oauth_timestamp={3}&oauth_token={4}&oauth_version={5}&track=twitter";

            var baseString = string.Format(baseFormat,
            OAuthConsumerKey,
            OAuthNonce,
            OAuthSignatureMethod,
            OAuthTimestamp,
            OAuthToken,
            OAuthVersion
            );

            baseString = string.Concat("GET&", Uri.EscapeDataString(ResourceUrl), "&", Uri.EscapeDataString(baseString));

            // Generate an OAuth signature using the baseString

            var compositeKey = string.Concat(Uri.EscapeDataString(OAuthConsumerSecret), "&", Uri.EscapeDataString(OAuthTokenSecret));
            string OAuthSignature;
            using (HMACSHA1 hasher = new HMACSHA1(ASCIIEncoding.ASCII.GetBytes(compositeKey)))
            {
                OAuthSignature = Convert.ToBase64String(hasher.ComputeHash(ASCIIEncoding.ASCII.GetBytes(baseString)));
            }

            // Now build the Authentication header. Again, Twitter is very particular about the format. Do not reorder variables.

            var HeaderFormat = "OAuth " +
            "oauth_consumer_key=\"{0}\", " +
            "oauth_nonce=\"{1}\", " +
            "oauth_signature=\"{2}\", " +
            "oauth_signature_method=\"{3}\", " +
            "oauth_timestamp=\"{4}\", " +
            "oauth_token=\"{5}\", " +
            "oauth_version=\"{6}\"";

            var authHeader = string.Format(HeaderFormat,
            Uri.EscapeDataString(OAuthConsumerKey),
            Uri.EscapeDataString(OAuthNonce),
            Uri.EscapeDataString(OAuthSignature),
            Uri.EscapeDataString(OAuthSignatureMethod),
            Uri.EscapeDataString(OAuthTimestamp),
            Uri.EscapeDataString(OAuthToken),
            Uri.EscapeDataString(OAuthVersion)
            );

            // Now build the actual request

            ServicePointManager.Expect100Continue = false;
            var postBody = "track=twitter";
            ResourceUrl += "?" + postBody;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ResourceUrl);
            request.Headers.Add("Authorization", authHeader);
            request.Method = "GET";
            request.ContentType = "application/x-www-form-urlencoded";
            request.KeepAlive = true;
            //ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3;

            // Retrieve the response data
            WebResponse response = request.GetResponse();

            StreamReader streamReader = new StreamReader(response.GetResponseStream());

            //fire up the concurrent queue
            _queuedTweets = new BlockingCollection<string>();
            Task.Run(() => ProcessQueue());

            //start displaying results
            Task.Run(() => DisplayResults());

            while (!streamReader.EndOfStream)
            {
                _queuedTweets.Add(streamReader.ReadLine());
            }

        }

        static void ProcessQueue()
        {
            string value;

            while (!_queuedTweets.IsCompleted)
            {
                _queuedTweets.TryTake(out value, Timeout.Infinite);
                totalNumber += 1;

            }
        }

        static void DisplayResults()
        {
            int displayResultsInterval = int.Parse(ConfigurationManager.AppSettings["displayresultsinterval"]);

            //total number processed
            Console.WriteLine(string.Format("Number of Tweets Processed: {0}", totalNumber));

            if (previousTotalNumber > 0)
            {
                //number per second
                double tweetspersecond = (totalNumber - previousTotalNumber) / (float)displayResultsInterval;
                Console.WriteLine(string.Format("Number of Tweets Per Second: {0}", tweetspersecond));

                //number per minute
                Console.WriteLine(string.Format("Number of Tweets Per Minute: {0}", tweetspersecond * 60));

                //number per hour
                Console.WriteLine(string.Format("Number of Tweets Per Hour: {0}", tweetspersecond * 3600));   
            }

            Console.WriteLine("\r\n");

            previousTotalNumber = totalNumber;

            Thread.Sleep(displayResultsInterval * 1000);
            DisplayResults();
        }
    }
}
