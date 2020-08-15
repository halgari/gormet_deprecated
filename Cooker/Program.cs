using System;
using System.Threading.Tasks;
using Cooker.Lib;
using Wabbajack.Common;

namespace Cooker
{
    class Program
    {
        static void Main(string[] args)
        {
            Utils.LogMessages.Subscribe(m => Console.WriteLine(m.ToString()));
            Execute().Wait();

        }

        public static async Task Execute()
        {
            var config = new Config
            {
                SrcModList = (AbsolutePath) @"C:\Wabbajack Installs\Living Skyrim\profiles\Living Skyrim 2\modlist.txt"
            };
            
            var cooker = new Lib.Cooker(config);
            await cooker.Analyze();
            await cooker.CreateBatches();
            await cooker.CreateOutputFolders();
            await cooker.BuildBatches();
        }
    }
}