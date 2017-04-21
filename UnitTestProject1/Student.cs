using DKDB;
using System;
using System.Collections.Generic;

namespace UnitTestProject1
{
    public class Student : BaseClass
    {
        [DKDB.CustomAttr.MaxLengthAttr(MaxLength = 10)]
        public String name { get; set; }
        public int age { get; set; }
        public Teacher teacher { get; set; }

        [CustomAttr.ManyToMany(TableName = "students_lessons", Target = "students")]
        public List<Lesson> lessons { get; set; } = new List<Lesson>();
    }
}
