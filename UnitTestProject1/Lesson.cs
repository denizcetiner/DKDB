using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnitTestProject1
{
    public class Lesson : DKDB.BaseClass
    {
        [DKDB.CustomAttr.MaxLengthAttr(MaxLength = 15)]
        public String name { get; set; }
        [DKDB.CustomAttr.ManyToMany(TableName = "students_lessons", Target = "lessons")]
        public List<Student> students { get; set; } = new List<Student>();
    }
}
