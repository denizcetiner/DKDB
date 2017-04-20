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
        [DKDB.CustomAttr.OneToMany(Target = "teacher")]
        public List<Student> student { get; set; } = new List<Student>();
    }
}
