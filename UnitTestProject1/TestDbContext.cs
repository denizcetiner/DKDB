using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DKDB;

namespace UnitTestProject1
{
    class TestDbContext : DbContext
    {
        public DbSet<Teacher> teachers { get; set; }
        public DbSet<Student> students { get; set; }

        public TestDbContext() : base(@"C:\UnitTest1")
        {

        }
    }

    
}
