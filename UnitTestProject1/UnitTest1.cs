using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest1
    {
        String filepath = @"C:\UnitTest1";

        [TestMethod]
        public void Destroy()
        {
            IEnumerable<String> files = Directory.EnumerateFiles(filepath);
            foreach(String file in files)
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        public void TestMethod1()
        {
            TestDbContext ctx = new TestDbContext();
            Assert.IsTrue(String.Equals(ctx.DatabaseFolder,filepath));
        }

        [TestMethod]
        public void TestMethod2()
        {
            TestDbContext ctx = new TestDbContext();
            Student st1 = new Student();
            st1.age = 22;
            st1.name = "Deniz C.";
            ctx.students.Add(st1);
            ctx.SaveChanges();
            Assert.IsTrue(st1.id == 1);
        }

        [TestMethod]
        public void TestMethod3()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.students.ReadAllRecords();
            List<Student> studentsList = ctx.students.allRecords;
            Assert.IsTrue(studentsList.Count == 1);
        }

        [TestMethod]
        public void TestMethod4()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.students.ReadAllRecords();
            List<Student> studentsList = ctx.students.allRecords;
            Student deniz = studentsList
                .FirstOrDefault(student => student.name.StartsWith("Deniz"));

            Assert.IsTrue(deniz.age == 22);
        }

        [TestMethod]
        public void TestMethod5()
        {
            TestDbContext ctx = new TestDbContext();
            Student kerem = new Student();
            kerem.name = "Kerem O.";
            kerem.age = 21;
            ctx.students.Add(kerem);
            ctx.SaveChanges();
            Assert.IsTrue(kerem.id == 2);
        }

        [TestMethod]
        public void TestMethod6()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.students.ReadAllRecords();
            Assert.IsTrue(ctx.students.allRecords.Count == 2);
        }

        [TestMethod]
        public void TestMethod7()
        {
            TestDbContext ctx = new TestDbContext();
            Teacher teacher = new Teacher();
            teacher.name = "Bora U.";
            ctx.teachers.Add(teacher);
            ctx.SaveChanges();
            Assert.IsTrue(teacher.id == 1);
        }

        [TestMethod]
        public void TestMethod8()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.teachers.ReadAllRecords();
            Teacher teacher = ctx.teachers.allRecords
                .FirstOrDefault(t => t.name.StartsWith("Bora"));

            Assert.IsTrue(teacher.name.Equals("Bora U."));
        }

        [TestMethod]
        public void TestMethod9()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.teachers.ReadAllRecords();
            Teacher teacher = ctx.teachers.allRecords
                .FirstOrDefault(t => t.name.StartsWith("Bora"));
            ctx.students.ReadAllRecords();
            Student deniz = ctx.students.allRecords.FirstOrDefault(s=>s.name.StartsWith("Deniz"));
            deniz.teacher = teacher;
            ctx.students.Update(deniz);
            ctx.SaveChanges();
        }

        [TestMethod]
        public void TestMethod10()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.teachers.ReadAllRecords();
            Teacher teacher = ctx.teachers.allRecords
                .FirstOrDefault(t => t.name.StartsWith("Bora"));
            ctx.students.ReadAllRecords();
            Student kerem = ctx.students.allRecords.FirstOrDefault(s => s.name.StartsWith("Kerem"));
            kerem.teacher = teacher;
            ctx.students.Update(kerem);
            ctx.SaveChanges();
        }

        [TestMethod]
        public void TestMethod11()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.students.ReadAllRecords();
            Student deniz = ctx.students.allRecords.FirstOrDefault(s => s.name.StartsWith("Deniz"));
            Assert.IsTrue(deniz.teacher.name.Equals("Bora U."));
        }
        [TestMethod]
        public void TestMethod12()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.students.ReadAllRecords();
            Student deniz = ctx.students.allRecords.FirstOrDefault(s => s.name.StartsWith("Deniz"));
            Assert.IsTrue(deniz.teacher.name.Equals("Bora U."));
        }
        [TestMethod]
        public void TestMethod13()
        {
            TestDbContext ctx = new TestDbContext();
            Teacher t = new Teacher();
            t.name = "ismail k.";
            ctx.students.ReadAllRecords();
            Student deniz = ctx.students.allRecords.FirstOrDefault(s => s.name.StartsWith("Deniz"));
            deniz.teacher = t;
            ctx.students.Update(deniz);
            ctx.SaveChanges();
            Assert.IsTrue(deniz.teacher.id == 2);
        }

        [TestMethod]
        public void TestMethod14()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.students.ReadAllRecords();
            Student deniz = ctx.students.allRecords.FirstOrDefault(s => s.name.StartsWith("Deniz"));
            ctx.teachers.Remove(deniz.teacher);
            ctx.SaveChanges();
            ctx.teachers.ReadAllRecords(true);
            Teacher teacher = ctx.teachers.allRecords.FirstOrDefault(t => t.name.StartsWith("Bora"));
            Teacher teacher2 = ctx.teachers.allRecords.FirstOrDefault(t => t.name.StartsWith("ismail"));
            Assert.IsTrue(teacher.removed == false && teacher2.removed == true);
        }

        [TestMethod]
        public void TestMethod15()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.students.ReadAllRecords();
            Student deniz = ctx.students.allRecords.FirstOrDefault(s => s.name.StartsWith("Deniz"));
            Student kerem = ctx.students.allRecords.FirstOrDefault(s => s.name.StartsWith("Kerem"));
            Assert.IsTrue(deniz.teacher == null && kerem.teacher != null);
        }

        [TestMethod]
        public void TestMethod16()
        {
            TestDbContext ctx = new TestDbContext();
            School school = new School();
            school.name = "COMU";
            ctx.schools.Add(school);
            ctx.SaveChanges();
            Assert.IsTrue(school.id == 1);
        }

        [TestMethod]
        public void TestMethod17()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.schools.ReadAllRecords();
            School school = ctx.schools.allRecords.FirstOrDefault(s => s.name.StartsWith("COMU"));
            Assert.IsTrue(school.id == 1);
        }

        [TestMethod]
        public void TestMethod18()
        {
            TestDbContext ctx = new TestDbContext();
            ctx.schools.ReadAllRecords();
            School school = ctx.schools.allRecords.FirstOrDefault(s => s.name.StartsWith("COMU"));
            if(school.studentList == null)
            {
                school.studentList = new List<Student>();
            }
            ctx.students.ReadAllRecords();
            Student deniz = ctx.students.allRecords.FirstOrDefault(s => s.name.StartsWith("Deniz"));
            school.studentList.Add(deniz);
            ctx.schools.Update(school);
            ctx.SaveChanges();
            ctx.students.ReadAllRecords();
            deniz = ctx.students.allRecords.First(s => s.name.StartsWith("Deniz"));
            Assert.IsTrue(deniz.school_id == 1);
        }


    }
}
