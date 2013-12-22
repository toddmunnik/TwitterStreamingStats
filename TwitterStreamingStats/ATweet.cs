using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace TwitterStreamingStats
{
    [DataContract]
    public class ATweet
    {
        [DataMember] 
        public string text;

        [DataMember]
        public entities entities;
    }

    [DataContract]
    public class entities
    {
        [DataMember]
        public List<hashtag> hashtags;

        [DataMember]
        public List<media> media;
    }

    [DataContract]
    public class hashtag
    {
        [DataMember]
        public string text;

        [DataMember]
        public List<int> indices;
    }

    [DataContract]
    public class media
    {
        [DataMember]
        public string id;
        [DataMember]
        public string id_str;
        [DataMember]
        public string media_url;
        [DataMember]
        public string media_url_https;
        [DataMember]
        public string url;
        [DataMember]
        public string display_url;
        [DataMember]
        public string expanded_url;
        [DataMember]
        public string type;
    }
}
