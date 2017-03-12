using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using DKDB;

namespace DKDB
{
    public class DbContext
    {
        

        public List<Type> dbsetTypes = new List<Type>();
        //public List<TransactionRecord> transactionRecords = new List<TransactionRecord>();

        /// <summary>
        /// Fills the DbSet type list "dbsetTypes" for easier use if needed
        /// </summary>
        public void initSetTypes()
        {
            PropertyInfo[] infos = this.GetType().GetProperties();
            foreach (PropertyInfo info in infos)
            {
                //String propTypeNameString = info.PropertyType.Name.ToString();
                if (info.PropertyType.Name.ToString().Contains("DbSet"))
                {
                    Console.WriteLine(info.PropertyType.GetGenericArguments()[0].Name);
                    dbsetTypes.Add(info.PropertyType.GetGenericArguments()[0]);
                }
            }
        }

        //Returns a specific DbSet for easier access
        public object GetDBSetByType(Type t)
        {
            PropertyInfo[] infos = this.GetType().GetProperties();
            foreach(PropertyInfo info in infos)
            {
                if(info.PropertyType.ToString().Contains("DbSet"))
                {
                    Type t3 = info.PropertyType.GetGenericArguments()[0];
                    if (t == t3)
                    {
                        return info.GetValue(this);
                    }
                }
            }
            return null;
        }

        

        public int SaveChanges()
        {
            PropertyInfo[] infos = this.GetType().GetProperties();
            foreach(PropertyInfo info in infos)
            {
                String type_s = info.PropertyType.ToString();
                if (type_s.Contains("DbSet"))
                {
                    Type d1 = typeof(DbSet<>);
                    Type[] typeArgs = { info.PropertyType };
                    Type constructed = d1.MakeGenericType(typeArgs);

                    info.GetValue(this).GetType().GetMethod("SaveChanges");
                }
            }
            return 0;
        }
    }

    

    //public class TransactionRecord
    //{
    //    public Type t;
    //    public object originalState;
    //    public PropertyInfo changedProperty;
    //    public object value;
    //    public String TransactionType;
    //    public bool complex;
    //    public int complexityOrder;

    //    public TransactionRecord(object originalState, PropertyInfo changedProperty, object value, String TransactionType)
    //    {
    //        originalState = CloneObject(originalState);
    //        t = originalState.GetType();
    //        this.changedProperty = changedProperty;
    //        this.value = value;
    //        this.TransactionType = TransactionType;
    //    }

    //    public void OrderRecords(List<TransactionRecord> records)
    //    {

    //    }

    //    public object CloneObject(object original)
    //    {
    //        object clone = Activator.CreateInstance(original.GetType());
    //        foreach (PropertyInfo info in original.GetType().GetProperties())
    //        {
    //            info.SetValue(clone, info.GetValue(original));
    //        }
    //        return clone;
    //    }
    //}
}
