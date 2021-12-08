using eSuite.Utils.Models;
using System;
using System.IO;
using System.Net;
using System.Text;

namespace eSuite.Utils
{
    public class eRest
    {
        public static string GetUserAuthToken(string Username, string Password)
        {
            string Token;
            Token = Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(Username + ":" + Password));
            return $"Basic {Token}";
        }
        private readonly static string ENCODING = "ISO-8859-1";

        public static string SubmitRequest(eRestRequest eRequest)
        {
            // create the request, ensure it is an HttpWebRequest
            WebRequest request = WebRequest.Create(eRequest.Url);
            if (request is HttpWebRequest == false)
            {
                throw new Exception("Incorrect WebRequest type returned.");
            }

            // fill out request
            request.Method = eRequest.RequestType.ToString();
            request.Headers.Add("Authorization", eRequest.AuthToken);
            request.Timeout = 10000;

            // upload any files if applicable
            if (eRequest.FilePathToUpload != null && !string.IsNullOrEmpty(eRequest.FilePathToUpload) && File.Exists(eRequest.FilePathToUpload))
            {
                //boundary
                StringBuilder boundary = new StringBuilder();
                boundary.Append("---------------------------");
                boundary.Append(DateTime.Now.Ticks.ToString("x"));

                request.ContentType = $"multipart/form-data; boundary={boundary.ToString()}";

                //json
                StringBuilder jsonHeader = new StringBuilder();
                jsonHeader.Append("--");
                jsonHeader.Append(boundary);
                jsonHeader.Append("\r\n");
                jsonHeader.Append("Content-Disposition: form-data; name=\"data\"; filename=\"request.json\"\r\n");
                jsonHeader.Append("Content-Type: application/json\r\n\r\n");
                byte[] jsonHeaderBytes = Encoding.GetEncoding(ENCODING).GetBytes(jsonHeader.ToString());
                byte[] jsonBytes = Encoding.GetEncoding(ENCODING).GetBytes(eRequest.Json);

                // file
                StringBuilder fileHeader = new StringBuilder();
                fileHeader.Append("--");
                fileHeader.Append(boundary);
                fileHeader.Append("\r\n");
                fileHeader.Append("Content-Disposition: form-data; name=\"file\"; filename=\"mugshot.png\"\r\n");
                fileHeader.Append("Content-Type: image/png\r\n\r\n");
                byte[] fileHeaderBytes = Encoding.UTF8.GetBytes(fileHeader.ToString());

                // footer
                byte[] footerBytes = Encoding.UTF8.GetBytes(boundary.ToString() + "--");

                using (Stream reqStream = request.GetRequestStream())
                {
                    reqStream.Write(jsonHeaderBytes, 0, jsonHeaderBytes.Length);
                    reqStream.Write(jsonBytes, 0, jsonBytes.Length);
                    reqStream.Write(fileHeaderBytes, 0, fileHeaderBytes.Length);

                    using (FileStream fileStream = new FileStream(eRequest.FilePathToUpload, FileMode.Open, FileAccess.Read))
                    {
                        byte[] fileBuffer = new byte[4096];
                        int bytesRead = 0;

                        while ((bytesRead = fileStream.Read(fileBuffer, 0, fileBuffer.Length)) != 0)
                        {
                            reqStream.Write(fileBuffer, 0, fileBuffer.Length);
                        }

                        fileStream.Close();
                    }

                    reqStream.Write(footerBytes, 0, footerBytes.Length);
                    reqStream.Close();
                }
            }

            // if json is provided without file, stream content
            else if (!string.IsNullOrEmpty(eRequest.Json))
            {
                request.ContentType = "application/json";

                // get bytes of json string
                byte[] jsonBytes = Encoding.GetEncoding(ENCODING).GetBytes(eRequest.Json);

                // update request length
                request.ContentLength = jsonBytes.Length;

                // stream the data
                using (Stream WriteStream = request.GetRequestStream())
                {
                    WriteStream.Write(jsonBytes, 0, jsonBytes.Length);
                }
            }

            // submit the request and get a json response
            // check the response for status code OK
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("API call failed with status code: " + response.StatusCode + ".");
                }

                // read in the response, return a json object with contents
                using (Stream stream = response.GetResponseStream())
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string data = reader.ReadToEnd();
                            return data;
                        }
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }
    }
}