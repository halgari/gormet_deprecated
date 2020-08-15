using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Compression.BSA;
using Wabbajack;
using Wabbajack.Common;
using Wabbajack.Common.FileSignatures;

namespace Cooker.Lib
{
    public class Cooker
    {
        private Config _config;
        private List<AbsolutePath> _mods;
        private List<string> _plugins;
        private Dictionary<AbsolutePath, int> _positionForMod;
        private Dictionary<int, AbsolutePath> _modForPosition;
        private List<IResolvedFile> _resolvedPlugins;
        
        private WorkQueue queue = new WorkQueue();
        private Dictionary<RelativePath, IEnumerable<ResolvedDiskFile>> _resolvedByPath;
        private List<IResolvedFile> _resolvedBSAs;
        private Dictionary<ResolvedDiskFile, List<ResolvedBSAFile>> _bsaContentsByResolved;
        private Dictionary<RelativePath, IResolvedFile> _cookedOrder;

        private string[] Endings = new[] {".bsa", " - Textures.bsa", " - Meshes.bsa", " - Misc.bsa", " - Sounds.bsa", " - Music.bsa", " - Animations.bsa"};
        private Dictionary<RelativePath, IResolvedFile> _toPackIntoBSAs;

        
        private HashSet<Extension> _ignoreLoadOrderFiles = new HashSet<Extension>
        {
            new Extension(".bsa"),
            new Extension(".mohidden"),
            new Extension(".psc")
        };

        private Dictionary<RelativePath, IResolvedFile> _looseFiles;
        private List<Dictionary<RelativePath, IResolvedFile>> _cookedBatches;


        public Cooker(Config config)
        {
            _config = config;
        }

        public async Task Analyze()
        {
            _mods = (await _config.SrcModList.ReadAllLinesAsync())
                .Reverse()
                .Where(l => l.StartsWith("+"))
                .Select(l => l.Substring(1))
                .Select(l => _config.ModsFolder.Combine(l))
                .ToList();

            Utils.Log($"Found {_mods.Count} enabled mods");

            _plugins =
                (await _config.PluginsPath.ReadAllLinesAsync())
                .Where(p => p.StartsWith("*"))
                .Select(p => p.Substring(1))
                .ToList();


            Utils.Log($"Found {_plugins.Count} plugins");

            _modForPosition = _mods.Select((mod, idx) => (mod, idx)).ToDictionary(m => m.idx, m => m.mod);
            _positionForMod = _mods.Select((mod, idx) => (mod, idx)).ToDictionary(m => m.mod, m => m.idx);

            Utils.Log("Resolving all disk files");
            var resolved = _mods.Select((mod, idx) => (mod, idx))
                .SelectMany(e => e.mod.EnumerateFiles()
                    .Select(f => new ResolvedDiskFile
                    {
                        ModPosition = e.idx,
                        Path = f.RelativeTo(e.mod),
                        PathOnDisk = new FullPath(f),
                    })).ToList();
            
            Utils.Log($"Resolved {resolved.Count} disk files");

            Utils.Log($"Reading BSA contents");
            
            var bsaExtension = new Extension(".bsa");
            var bsaContents = (await resolved.Where(r => r.Path.Extension == bsaExtension)
                    .PMap(queue, async bsa =>
                    {
                        Utils.Log($"Reading contents of {bsa.Path}");
                        var reader = await BSADispatch.OpenRead(bsa.PathOnDisk.Base);
                        return (bsa, reader.Files);
                    }))
                .SelectMany(t => t.Files.Select(f => new ResolvedBSAFile
                {
                    ModPosition = t.bsa.ModPosition,
                    BSAFile = f,
                    ResolvedBSA = t.bsa
                }))
                .ToList();

            _bsaContentsByResolved = bsaContents.GroupBy(c => c.ResolvedBSA).ToDictionary(f => f.Key, f => f.ToList());

            Utils.Log($"Resolved {bsaContents.Count} files from BSAs");

            Utils.Log($"Creating indexes");

            Utils.Log($"Ordering {resolved.Count} by mod order");
            _resolvedByPath = resolved.OrderByDescending(f => f.ModPosition).GroupBy(f => f.Path)
                .ToDictionary(f => f.Key, f => (IEnumerable<ResolvedDiskFile>)f);

            Utils.Log($"Resolving {_plugins.Count} ESPs");
            _resolvedPlugins = _plugins.Select(p => (IResolvedFile)_resolvedByPath[(RelativePath) p].First()).ToList();

            Utils.Log($"Resolving BSA ordering");

            _resolvedBSAs = _resolvedPlugins.SelectMany(p => FindBSAs(p)).ToList();


            _cookedOrder = new Dictionary<RelativePath, IResolvedFile>();
            
            var dd = _bsaContentsByResolved[_resolvedByPath[(RelativePath) "HearthfireMultiKid.bsa"].First()]
                .Where(f => f.Path.Extension == new Extension(".dlstrings")).ToList();

            var hf = _resolvedBSAs.Where(a => ((string) a.Path).StartsWith("Hearthfire")).ToList();
            
            foreach (var bsa in _resolvedBSAs)
            {
                foreach (var file in _bsaContentsByResolved[(ResolvedDiskFile) bsa].Where(f => !_ignoreLoadOrderFiles.Contains(f.Path.Extension)))
                {
                    _cookedOrder[file.Path] = file;
                }
            }

            foreach (var file in _resolvedByPath.Where(f => !_ignoreLoadOrderFiles.Contains(f.Key.Extension)))
            {
                _cookedOrder[file.Key] = file.Value.First();
            }

            Utils.Log($"Full load order resolved, {_cookedOrder.Count} files in load");
            Utils.Log($"{_cookedOrder.Values.OfType<ResolvedDiskFile>().Count()} files on disk");
            Utils.Log($"{_cookedOrder.Values.OfType<ResolvedBSAFile>().Count()} files in BSAs");
            foreach (var group in _cookedOrder.Values.GroupBy(s => s.Path.Extension).OrderByDescending(e => e.Count()))
            {
                Utils.Log($"{group.Count()} {group.Key} files ({group.OfType<ResolvedDiskFile>().Count()} on disk)");
            }

            
            Utils.Log($"Calculating size");
            var loadOrderSizes = await _cookedOrder.Values.PMap(queue, f => f.Size);
            Utils.Log($"Total Size: {loadOrderSizes.Sum().ToFileSizeString()}");
            

        }


        private IEnumerable<IResolvedFile> FindBSAs(IResolvedFile resolvedFile)
        {
            var baseName = (string)resolvedFile.Path.FileNameWithoutExtension;
            var possibleNames = 
                Endings.Select(n => (RelativePath) (baseName + n));

            foreach (var nm in possibleNames)
            {
                if (_resolvedByPath.TryGetValue(nm, out var bsa))
                {
                    Utils.Log($"Using {bsa.First().Path} for {resolvedFile.Path}");
                    yield return bsa.First();
                }
            }
        }

        public async Task CreateBatches()
        {
            _toPackIntoBSAs = _cookedOrder.Where(f =>
                Definitions.BatchForExtension.TryGetValue(f.Key.Extension, out var setting))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            Utils.Log($"Found {_toPackIntoBSAs.Count} files to pack into BSAs out of {_cookedOrder.Count} files");

            _looseFiles = _cookedOrder.Where(f => !_toPackIntoBSAs.ContainsKey(f.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            Utils.Log($"Found {_looseFiles.Count} loose files");
            foreach (var group in _looseFiles.Values.GroupBy(s => s.Path.Extension).OrderByDescending(e => e.Count()))
            {
                Utils.Log($"{group.Count()} {group.Key} files ({group.OfType<ResolvedDiskFile>().Count()} on disk)");
            }

            Utils.Log($"Generating Batches");
            var batches = new List<Dictionary<RelativePath, IResolvedFile>>();

            long currentSize = 0;
            var currentBatch = new Dictionary<RelativePath, IResolvedFile>();
            batches.Add(currentBatch);
            foreach (var (key, value) in _toPackIntoBSAs)
            {
                if (currentSize + value.Size >= Definitions.MaxBatchSize)
                {
                    Utils.Log($"Batch {batches.Count} defined {currentSize.ToFileSizeString()}");
                    currentBatch = new Dictionary<RelativePath, IResolvedFile>();
                    batches.Add(currentBatch);
                    currentSize = 0;
                }
                
                currentBatch.Add(key, value);
                currentSize += value.Size;
            }

            _cookedBatches = batches;

        }

        public async Task CreateOutputFolders()
        {
           //await _config.CookedPath.DeleteDirectory();
           _config.CookedProfileFolder.CreateDirectory();
            _config.CookedPath.CreateDirectory();
        }

        public async Task BuildBatches()
        {
            Utils.Log($"Bulding {_cookedBatches.Count} BSAs");
            await _cookedBatches.Select((bsa, idx) => (bsa, idx)).PMap(queue, async itm =>
            {

                var (bsa, idx) = itm;
                {
                    var outputPath = _config.CookedPath.Combine($"Cooked{idx}.bsa");
                    if (outputPath.Exists) return;
                    Utils.Log($"Adding {bsa.Count} files to BSA {idx}");
                    var flags = bsa.Keys.Select(f => Definitions.BatchForExtension[f.Extension]).Distinct()
                        .Aggregate((uint) 0, (a, b) => a | (uint) b.FileFlags);
                    await using var builder = await BSABuilder.Create(new BSAStateObject
                    {
                        ArchiveFlags = ((int) (ArchiveFlags.HasFileNames | ArchiveFlags.HasFolderNames
                                              /* | ArchiveFlags.HasFileNameBlobs*/)) | 0x10 | 0x80,
                        FileFlags = flags,
                        Magic = Encoding.ASCII.GetString(new byte[] {0x42, 0x53, 0x41, 0x00}),
                        Version = 0x67
                    }, bsa.Values.Sum(f => f.Size));
                    builder.HeaderType = VersionType.SSE;

                    await bsa.Select((file, idx) => (file, idx)).PMap(queue, async kv =>
                    {
                        var stream = await kv.file.Value.GetStream();
                        //Console.WriteLine($"File {kv.file.Key} {kv.file.Value.Size} {kv.file.Value} {stream.Length}");
                        var state = new BSAFileStateObject
                        {
                            Path = (RelativePath)((string)kv.file.Key).ToLowerInvariant().Replace('/', '\\'),
                            Index = kv.idx,
                            FlipCompression = false
                        };
                        await builder.AddFile(state, stream);
                    });
                    Utils.Log($"Writing {bsa.Count} files to BSA {idx}");
                    await builder.Build(outputPath);
                    //await outputPath.Compact(FileCompaction.Algorithm.XPRESS16K);
                }
            });
            
            for (var idx = 0; idx < _cookedBatches.Count; idx ++)
            {
                var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cooker.Lib.Dummy.esp");
                var outPath = _config.CookedPath.Combine($"Cooked{idx}.esp");
                await outPath.WriteAllAsync(src, true);
            }

            Utils.Log($"Writing {_looseFiles.Count} loose files");
            var parted = _looseFiles.Partition(1000).Select((part, idx) => (part, idx)).ToList();

            await parted.SelectMany(itm => itm.part.Select(f => (f.Key, f.Value, itm.idx))).PMap(queue, async file =>
            {
                var (key, value, idx) = file;
                var outPath = _config.CookedPath.Parent.Combine($"{_config.ProfileName} Cooked_{idx}", (string)key);
                outPath.Parent.CreateDirectory();
                await outPath.WriteAllAsync(await value.GetStream());
                //await outPath.Compact(FileCompaction.Algorithm.XPRESS16K);
            });

            Utils.Log($"Copying Profile");
            foreach (var file in _config.ProfilePath.EnumerateFiles())
            {

                var dest = file.RelativeTo(_config.ProfilePath).RelativeTo(_config.CookedProfileFolder);
                dest.Parent.CreateDirectory();
                await file.CopyToAsync(dest);
            }
            await _config.CookedProfileFolder.Combine("modlist.txt")
                .WriteAllLinesAsync(
                    Enumerable.Range(0, parted.Count).Select(i => $"+{_config.ProfileName} Cooked_{i}").Concat(
                    new[]
                {
                    $"+{_config.CookedPath.FileName}",
                    "*DLC: Dawnguard",
                    "*DLC: Dragonborn",
                    "*DLC: HearthFires",
                }).ToArray());

            var plugins = await _config.CookedProfileFolder.Combine("plugins.txt").ReadAllLinesAsync();
            await _config.CookedProfileFolder.Combine("plugins.txt").WriteAllLinesAsync(
                plugins.Concat(Enumerable.Range(0, _cookedBatches.Count).Select(i => $"*Cooked{i}.esp")).ToArray());

        }
    }
}