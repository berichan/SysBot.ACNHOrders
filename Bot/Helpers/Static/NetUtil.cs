using Discord;
using NHSE.Core;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public static class NetUtil
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public static async Task<byte[]> DownloadFromUrlAsync(string url)
        {
            HttpResponseMessage response = await httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        public static byte[] DownloadFromUrlSync(string url)
        {
            HttpResponseMessage response = httpClient.GetAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsByteArrayAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public static async Task<Download<Item[]>> DownloadNHIAsync(IAttachment att)
        {
            var result = new Download<Item[]> { SanitizedFileName = Format.Sanitize(att.Filename) };
            if ((att.Size > MultiItem.MaxOrder * Item.SIZE) || att.Size < Item.SIZE)
            {
                result.ErrorMessage = $"{result.SanitizedFileName}: Invalid size.";
                return result;
            }

            string url = att.Url;

            // Download the resource and load the bytes into a buffer.
            var buffer = await DownloadFromUrlAsync(url).ConfigureAwait(false);
            var items = Item.GetArray(buffer);
            if (items == null)
            {
                result.ErrorMessage = $"{result.SanitizedFileName}: Invalid nhi attachment.";
                return result;
            }

            result.Data = items;
            result.Success = true;
            return result;
        }

        public static async Task DownloadFileAsync(string url, string destinationFilePath)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                using (HttpResponseMessage response = await httpClient.GetAsync(url))
                {
                    response.EnsureSuccessStatusCode();

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        using (FileStream fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                        {
                            await contentStream.CopyToAsync(fileStream);
                        }
                    }
                }
            }
        }
    }

    public sealed class Download<T> where T : class
    {
        public bool Success;
        public T? Data;
        public string? SanitizedFileName;
        public string? ErrorMessage;
    }
}