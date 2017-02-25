using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DKDB
{
    public abstract class DKDBConfig : IDisposable
    {
        public void Dispose()
        {
            GC.Collect();
        }
    }
}
