namespace eSuite.Utils.Models
{
    public enum eRestRequestTypes
    {
        Get,
        Post,
        Put,
        Delete
    }

    public class eRestRequest
    {
        public string Url { get; set; }
        public string AuthToken { get; set; }
        public eRestRequestTypes RequestType { get; set; }
        public string Json { get; set; }
        public string FilePathToUpload { get; set; }


        public eRestRequest() { }
        public eRestRequest(string url, string authToken, eRestRequestTypes requestType = eRestRequestTypes.Get, string json = null)
        {
            this.Url = url;
            this.AuthToken = authToken;
            this.RequestType = requestType;
            this.Json = json;
        }
    }
}
