using DKDB;
using System;

namespace UnitTestProject1
{
    public class Student : BaseClass
    {
        [DKDB.CustomAttr.MaxLengthAttr(MaxLength = 10)]
        public String name { get; set; }
        public int age { get; set; }
        public Teacher teacher { get; set; }
    }
}
