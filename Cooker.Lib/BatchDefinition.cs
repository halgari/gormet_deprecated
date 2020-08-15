using System.Collections.Generic;
using Wabbajack.Common;

namespace Cooker.Lib
{
    public class BatchDefinition
    {
        public int Index { get; set; }
        public Dictionary<RelativePath, IResolvedFile> Files { get; set; }
    }
}