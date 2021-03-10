using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public static class FileUtil
    {
        public static async Task WriteBytesToFileAsync(byte[] bytes, string path, CancellationToken token)
        {
            using (FileStream sourceStream = new FileStream(path,
                FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true))
            {
                await sourceStream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            };
        }
    }
}
