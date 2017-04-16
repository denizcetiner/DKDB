﻿using System;
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
        public String DatabaseFolder = @"C:\Deneme";
        public String EndingChars = "/()=";
        public List<Type> dbsetTypes = new List<Type>();
        public List<object> dbsets = new List<object>();
        //public List<TransactionRecord> transactionRecords = new List<TransactionRecord>();


        //Returns a specific DbSet for easier access
        public object GetDBSetByType(Type t)
        {
            foreach (object o in dbsets)
            {
                Type t3 = o.GetType().GetGenericArguments()[0];
                if (t == t3)
                {
                    return o;
                }
            }
            return null;
        }


        public void CompleteAllOTMRequests()
        {
            bool result = true;
            //referansları doldur.
            foreach (object dbset in dbsets)
            {
                result &= (bool)dbset.GetType().GetMethod("CompleteOTMRequests").Invoke(dbset, null);
            }
            if (result)
            {
                CompleteAllOTMRequests(); //bir tur doldurma işlemi, daha önce kontrol edilmiş dbset'lerde yeni doldurma isteklerini
                //tetiklemiş olabilir. bu blok onu kontrol etmek için var.
            }
        }

        /// <summary>
        /// Fills the child objects of the read records.
        /// </summary>
        public void FillOthers()
        {
            bool result = true;
            //referansları doldur.
            foreach (object dbset in dbsets)
            {
                result &= (bool)dbset.GetType().GetMethod("FillOtherDbSetRecords").Invoke(dbset, null);
            }
            if (result)
            {
                FillOthers(); //bir tur doldurma işlemi, daha önce kontrol edilmiş dbset'lerde yeni doldurma isteklerini
                //tetiklemiş olabilir. bu blok onu kontrol etmek için var.
            }
        }


        #region constructor related

        public DbContext(String DatabaseFolder) : this()
        {
            this.DatabaseFolder = DatabaseFolder;

        }

        public DbContext ()
        {
            initSetTypes(); //Creates the list of the DbSet Generic Parameter types
            initDbSetList(); //Creates instances of all DbSets and assigns to proper properties
            InitDbSetProps(); //
            
        }

        

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

        //Creates instances for null DbSet properties
        public void initDbSetList()
        {
            PropertyInfo[] infos = this.GetType().GetProperties();
            foreach (PropertyInfo info in infos)
            {
                //String propTypeNameString = info.PropertyType.Name.ToString();
                if (info.PropertyType.Name.ToString().Contains("DbSet"))
                {
                    Type d1 = typeof(DbSet<>);
                    Type[] typeArgs = { info.PropertyType.GetGenericArguments()[0] };
                    Type constructed = d1.MakeGenericType(typeArgs);
                    object[] parameters = { this };
                    object dbset = Activator.CreateInstance(constructed, parameters);
                    info.SetValue(this, dbset);
                    dbsets.Add(info.GetValue(this));
                }
            }
        }

        /// <summary>
        /// Assigns this DbContext to the ctx property of all dbsets, for easier access and message sending from dbset to dbcontext
        /// </summary>
        public void InitDbSetProps()
        {
            foreach (object dbset in dbsets)
            {
                dbset.GetType().GetProperty("ctx").SetValue(dbset, this);
            }
        }

        #endregion

        public int SaveChanges()
        {
            bool result = false;
            object[] params1 = { "AddDirectly".ToCharArray() };
            object[] params2 = { "AddChilds".ToCharArray() };
            object[] params3 = { "Update".ToCharArray() };
            object[] params4 = { "Remove".ToCharArray() };
            object[][] parameters = { params1, params2, params3, params4 };
            foreach (object[] parameter in parameters)
            {
                foreach (object o in dbsets)
                {
                    MethodInfo method = o.GetType().GetMethod("SaveChanges");
                    result = (bool)method.Invoke(o, parameter);
                }
            }
            if(result) //if any changes happened, they may have triggered new changes.
            {
                SaveChanges();
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
