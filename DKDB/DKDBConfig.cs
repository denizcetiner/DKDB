using System;

namespace DKDB
{
    public abstract class DKDBConfig : IDisposable
    {
        public string databaseFolderPath { get; set; }

        public DKDBConfig(string databaseFolderPath)
        {
            this.databaseFolderPath = databaseFolderPath;
        }

        public void Dispose()
        {
            GC.Collect();
        }
    }
}
