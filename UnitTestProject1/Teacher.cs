using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnitTestProject1
{
    public class Teacher : DKDB.BaseClass
    {
        [DKDB.CustomAttr.MaxLengthAttr(MaxLength = 10)]
        public String name { get; set; }
    }
}
