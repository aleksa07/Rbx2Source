#pragma warning disable 0649

using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;

using Newtonsoft.Json;

namespace Rbx2Source.Web
{
    public class WebApiError
    {
        public int Code;
        public string Message;
    }

    public class RateLimitException : Exception
    {
        public RateLimitException(string message) : base(message) { }
    }

    public partial class CdnPender
    {
        public Datum[] Data { get; set; }
    }

    public partial class Datum
    {
        public long TargetId { get; set; }

        public string State { get; set; }

        public Uri ImageUrl { get; set; }
    }

    public static class WebUtil
    {
        private static byte[] ReadFullStream(Stream stream, bool close = true)
        {
            byte[] result;

            using (MemoryStream streamBuffer = new MemoryStream())
            {
                stream.CopyTo(streamBuffer);
                result = streamBuffer.ToArray();
            }

            if (close)
            {
                stream.Close();
                stream.Dispose();
            }

            return result;
        }

        private static void wait(float time)
        {
            int ms = (int)(time * 1000);
            Thread.Sleep(ms);
        }

        public static byte[] DownloadData(string address, string method = "GET", string body = "", int maxRetriesOn429 = 0)
        {
            int attempts = 0;

            while (true)
            {
                try
                {
                    HttpWebRequest request = WebRequest.CreateHttp(new Uri(address));
                    request.Headers.Set(HttpRequestHeader.AcceptEncoding, "gzip");

                    request.UserAgent = "Roblox";
                    request.Proxy = null;

                    request.UseDefaultCredentials = true;
                    request.Method = method;

                    if (body != "")
                    {
                        request.ContentType = "application/json";

                        using (var stream = request.GetRequestStream())
                        using (var writer = new StreamWriter(stream))
                        {
                            writer.Write(body);
                        }
                    }

                    var response = request.GetResponse() as HttpWebResponse;
                    var responseStream = response.GetResponseStream();

                    byte[] result;

                    if (response.ContentEncoding == "gzip")
                    {
                        var decompressor = new GZipStream(responseStream, CompressionMode.Decompress);
                        result = ReadFullStream(decompressor);
                        decompressor.Dispose();
                    }
                    else
                    {
                        result = ReadFullStream(responseStream);
                    }

                    return result;
                }
                catch (WebException ex)
                {
                    var errorResponse = ex.Response as HttpWebResponse;
                    bool rateLimited = errorResponse != null && (int)errorResponse.StatusCode == 429;

                    if (rateLimited && attempts < maxRetriesOn429)
                    {
                        attempts++;

                        int retryAfter = 0;
                        if (!int.TryParse(errorResponse.Headers["Retry-After"], out retryAfter))
                            retryAfter = 0;

                        int waitSeconds = retryAfter > 0 ? Math.Min(retryAfter, 60) : 5;
                        wait(waitSeconds);

                        continue;
                    }

                    if (rateLimited)
                        throw new RateLimitException("Roblox is rate-limiting requests. Please wait a moment and try again.");

                    throw;
                }
            }
        }

        public static string DownloadString(string address, string method = "GET", string body = "", int maxRetriesOn429 = 0)
        {
            byte[] data = DownloadData(address, method, body, maxRetriesOn429);
            return Encoding.UTF8.GetString(data);
        }

        public static Bitmap DownloadImage(string address)
        {
            byte[] data = DownloadData(address);
            Bitmap result;

            using (Stream imgStream = new MemoryStream(data))
                result = new Bitmap(imgStream);

            return result;
        }

        public static T DownloadJSON<T>(string address, string method = "GET", string body = "", int maxRetriesOn429 = 0)
        {
            byte[] content = DownloadData(address, method, body, maxRetriesOn429);
            var json = Encoding.UTF8.GetString(content);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public static string PendCdn(string address, bool log = true)
        {
            string result = null;
            bool final = false;
            string dots = "..";

            while (!final && dots.Length <= 13)
            {
                CdnPender pender = DownloadJSON<CdnPender>(address);
                final = pender.Data[0].State == "Final";
                result = pender.Data[0].ImageUrl.ToString();

                if (!final)
                {
                    dots += ".";

                    if (log)
                        Rbx2Source.Print("Waiting for finalization of " + address + dots);

                    wait(1f);
                }
            }

            if (dots.Length > 13)
                throw new Exception("CdnPender timed out after 10 retries! Roblox's servers may be overloaded right now.\nTry again after a few minutes!");

            return result;
        }
    }
}
