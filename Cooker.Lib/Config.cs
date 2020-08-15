using Wabbajack.Common;

namespace Cooker.Lib
{
    public class Config
    {
        private AbsolutePath _mo2Path;
        private AbsolutePath _srcModList;
        private AbsolutePath _pluginsPath;
        private AbsolutePath _modsFolder;
        private string _profileName;
        private AbsolutePath _cookedPath;
        private AbsolutePath _cookedProfileFolder;
        private AbsolutePath _profilePath;

        public AbsolutePath SrcModList
        {
            get => _srcModList;
            set
            {
                _srcModList = value;
                _mo2Path = _srcModList.Parent.Parent.Parent;
                _profilePath = _srcModList.Parent;
                _pluginsPath = _srcModList.Parent.Combine("plugins.txt");
                _profileName = (string)_srcModList.Parent.FileName;
                _modsFolder = _mo2Path.Combine("mods");
                _cookedPath = _modsFolder.Combine(_profileName + " Cooked");
                _cookedProfileFolder = _mo2Path.Combine("profiles", $"{_profileName} Cooked");
            }
        }
        public AbsolutePath MO2Path => _mo2Path;
        public AbsolutePath PluginsPath => _pluginsPath;
        public AbsolutePath ModsFolder => _modsFolder;

        public AbsolutePath CookedPath => _cookedPath;
        public string ProfileName => _profileName;
        public AbsolutePath CookedProfileFolder => _cookedProfileFolder;
        public AbsolutePath ProfilePath => _profilePath;
    }
}