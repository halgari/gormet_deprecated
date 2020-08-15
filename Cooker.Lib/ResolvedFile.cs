using System.IO;
using System.Threading.Tasks;
using Compression.BSA;
using Wabbajack.Common;

namespace Cooker.Lib
{
    public interface IResolvedFile
    {
        RelativePath Path { get; }
        int ModPosition { get; }
        long Size { get; }
        Task<Stream> GetStream();
    }
    
    public class ResolvedDiskFile : IResolvedFile
    {
        public RelativePath Path { get; set; }
        public FullPath PathOnDisk { get; set; }

        private long? _size = null;
        public long Size => (_size ?? (_size = PathOnDisk.Base.Size).Value);
        public async Task<Stream> GetStream()
        {
            return await PathOnDisk.Base.OpenRead();
        }

        public int ModPosition { get; set; }
    }

    
    public class ResolvedBSAFile : IResolvedFile
    {
        public RelativePath Path => BSAFile.Path;
        public int ModPosition { get; set; }
        public IFile BSAFile { get; set; }
        public long Size => BSAFile.Size;
        public async Task<Stream> GetStream()
        {
            var ms = new MemoryStream();
            await BSAFile.CopyDataTo(ms);
            ms.Position = 0;
            return ms;
        }

        public ResolvedDiskFile ResolvedBSA { get; set; }
    }

}