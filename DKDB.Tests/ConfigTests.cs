using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DKDB.Tests
{
    [TestClass]
    public class ConfigTests
    {
        public class Book
        {
            public string name { get; set; }
        }

        public class DataContext : DKDBConfig
        {
            public DataContext() : base(@"C:\Users\Kerem\Desktop\Folder")
            {

            }

            public DbSet<Book> books { get; set; }
        }

        [TestMethod]
        public void DbInitializeTest()
        {
            DataContext db = new DataContext();
            db.Initialize();
        }
    }
}
