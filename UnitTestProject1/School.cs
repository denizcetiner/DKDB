using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DKDB;

namespace UnitTestProject1
{
    public class School : BaseClass
    {
        [CustomAttr.MaxLengthAttr(MaxLength =15)]
        public String name { get; set; }
        [CustomAttr.OneToMany(Target = "school_id")]
        public List<Teacher> teacherList { get; set; } = new List<Teacher>();
        [CustomAttr.OneToMany(Target = "school_id")]
        public List<Student> studentList { get; set; } = new List<Student>();
    }
}
