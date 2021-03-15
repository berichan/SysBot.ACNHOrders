using System.IO;
using System.Reflection;
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

        public static string GetEmbeddedResource(string namespacename, string filename)
        {
            var assembly = Assembly.GetExecutingAssembly();
            if (assembly == null)
                return string.Empty;
            var resourceName = namespacename + "." + filename;
#pragma warning disable CS8600, CS8604
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                string result = reader.ReadToEnd();
                return result;
            }
#pragma warning restore CS8600, CS8604
        }
    }
}
