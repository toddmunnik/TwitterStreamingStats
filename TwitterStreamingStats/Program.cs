using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitterStreamingStats
{
    class Program
    {
        private static string trackvalue = String.Empty;
        private static BlockingCollection<string> _queuedTweets;
        private static int totalNumber = 0;
        private static int previousTotalNumber = 0;
        private static int containsUrl = 0;
        private static int containsPhotoUrl = 0;
        private static Dictionary<string, int> topDomains = new Dictionary<string, int>();
        private static Dictionary<string, int> topHashtags = new Dictionary<string, int>();

        static void Main(string[] args)
        {
            try
            {
                //trackvalue provided in command-line arguments
                if (args.Count() == 2)
                {
                    if (args[0] == "/track")
                    {
                        trackvalue = args[1];
                    }
                    else
                    {
                        Console.WriteLine("Bad Command-line argument: Allowed argument = /track");
                    }
                }
                else
                {
                    trackvalue = ConfigurationManager.AppSettings["defaulttrackvalue"];
                }
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
            "&oauth_timestamp={3}&oauth_token={4}&oauth_version={5}&track={6}";

            var baseString = string.Format(baseFormat,
            OAuthConsumerKey,
            OAuthNonce,
            OAuthSignatureMethod,
            OAuthTimestamp,
            OAuthToken,
            OAuthVersion,
            trackvalue
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

            ResourceUrl += "?track=" + trackvalue;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ResourceUrl);
            request.Headers.Add("Authorization", authHeader);
            request.Method = "GET";
            request.ContentType = "application/x-www-form-urlencoded";
            request.KeepAlive = true;

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
                _queuedTweets.TryAdd(streamReader.ReadLine());
            }
            _queuedTweets.CompleteAdding();
        }

        static void ProcessQueue()
        {
            string value;

            while (!_queuedTweets.IsCompleted)
            {
                _queuedTweets.TryTake(out value, Timeout.Infinite);
                totalNumber += 1;

                //serialize Json string into ATweet
                var serializer = new DataContractJsonSerializer(typeof(ATweet));
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(value));
                var rootObject = serializer.ReadObject(stream);
                stream.Close();
                var tweet = ((ATweet)rootObject);

                //contains a url?
                if (tweet.text != null && tweet.text.Contains("http"))
                    containsUrl += 1;

                if (tweet != null && tweet.entities != null && tweet.entities.media != null)
                {
                    if (tweet.entities.media.Count(m => m.url != null || m.display_url != null || m.expanded_url != null || m.media_url != null) > 0)
                    {
                        //is the Url a photoUrl?
                        if (tweet.entities.media.Count(m => m.url.Contains("instagram")
                            || m.display_url.Contains("pic.twitter.com")) > 0)
                        {
                            containsPhotoUrl += 1;
                        }

                        //top domains       
                        foreach (media m in tweet.entities.media)
                        {
                            if (m.url != null)
                            {
                                CheckTopDomains(m.url);
                            }
                            if (m.display_url != null)
                            {
                                CheckTopDomains(m.display_url);
                            }
                            if (m.expanded_url != null)
                            {
                                CheckTopDomains(m.expanded_url);
                            }
                            if (m.media_url != null)
                            {
                                CheckTopDomains(m.media_url);
                            }
                        }
                    }

                    //hashtags
                    if (tweet.entities.hashtags != null)
                    {
                        foreach (hashtag hashtag in tweet.entities.hashtags)
                        {
                            CheckTopHashtags(hashtag.text);
                        }
                    }
                }
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

            //urls
            if (totalNumber > 0)
            {
                Console.WriteLine(string.Format("Contains a Url: {0}%", string.Format("{0:0.00}",(containsUrl / (float)totalNumber) * 100)));
                Console.WriteLine(string.Format("Contains a Photo Url: {0}%", string.Format("{0:0.00}",(containsPhotoUrl / (float)totalNumber) * 100)));                 
            }

            //domains
            if (topDomains.Count > 0)
            {
                List<KeyValuePair<string, int>> listToSort = topDomains.ToList();

                Sort(listToSort);

                //show top 5 domains
                for (int i = 0; i < 5; i++)
                {
                    if (listToSort.Count >= i + 1)
                        Console.WriteLine(string.Format("Top Domains #{0}: {1} ({2})", i + 1, listToSort[i].Key, listToSort[i].Value)); 
                }
            }

            //hashtags
            if (topHashtags.Count > 0)
            {
                List<KeyValuePair<string, int>> listToSort = topHashtags.ToList();

                Sort(listToSort);

                //show top 10 hashtags
                for (int i = 0; i < 10; i++)
                {
                    if (listToSort.Count >= i + 1)
                        Console.WriteLine(string.Format("Top Hashtags #{0}: #{1} ({2})", i + 1, listToSort[i].Key, listToSort[i].Value));
                }
            }

            Console.WriteLine("\r\n");

            previousTotalNumber = totalNumber;

            Thread.Sleep(displayResultsInterval * 1000);
            DisplayResults();
        }

        private static void Sort(List<KeyValuePair<string, int>> list)
        {
            list.Sort((firstPair, nextPair) =>
            {
                return nextPair.Value.CompareTo(firstPair.Value);
            });           
        }

        static void CheckTopDomains(string url)
        {
            int oldValue;

            //some urls are missing the leading protocol
            if (!url.StartsWith("http"))
                url = "http://" + url;

            try
            {
                Uri uri = new Uri(url);
                if (topDomains.ContainsKey(uri.Host))
                {
                    //topDomains already holds this domain, update the count of the entry +1
                    topDomains.TryGetValue(uri.Host, out oldValue);
                    topDomains[uri.Host] = oldValue+1;
                }
                else
                {
                    //new entry
                    topDomains.Add(uri.Host, 1);
                }
            }
            catch (Exception e)
            {
                //ignore badly formatted url
            }
        }

        static void CheckTopHashtags(string hashtag)
        {
            int oldValue;

            try
            {
                if (topHashtags.ContainsKey(hashtag))
                {
                    //topHashtags already holds this hashtag, update the count of the entry +1
                    topHashtags.TryGetValue(hashtag, out oldValue);
                    topHashtags[hashtag] = oldValue+1;
                }
                else
                {
                    //new entry
                    topHashtags.Add(hashtag, 1);
                }
            }
            catch (Exception e)
            {
                //ignore for now
            }
        }
    }
}
