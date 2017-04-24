using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using DKDB;
using System.IO;

namespace DKDB
{
    public class DbContext
    {
        public bool Changed = false;

        public String DatabaseFolder;
        public String delimiter = "/()=";
        public List<Type> dbsetTypes = new List<Type>();
        public List<object> dbsets = new List<object>();

        public Dictionary<Type, List<int>> removed = new Dictionary<Type, List<int>>();
        
        //public List<TransactionRecord> transactionRecords = new List<TransactionRecord>();

        public Dictionary<String, List<Tuple<BaseClass, BaseClass>>> MTMToWrite = new Dictionary<string, List<Tuple<BaseClass, BaseClass>>>();

        

        /// <summary>
        /// Returns a specific DbSet for easier access
        /// </summary>
        /// <param name="t"></param>
        /// <returns>Returns a specific DbSet for easier access</returns>
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

        /// <summary>
        /// Brings the whole database to memory.
        /// </summary>
        /// <param name="ReadRemoved"></param>
        public void ReadAll(bool ReadRemoved=false)
        {
            foreach (object dbset in dbsets)
            {
                object[] parameters = { ReadRemoved };
                dbset.GetType().GetMethod("ReadAll").Invoke(dbset, parameters);
            }
            foreach (object dbset in dbsets)
            {
                object[] parameters = { ReadRemoved };
                dbset.GetType().GetMethod("FillOTO").Invoke(dbset, parameters);
            }
            foreach (object dbset in dbsets)
            {
                dbset.GetType().GetMethod("FillOTM").Invoke(dbset, null);
            }
            foreach (object dbset in dbsets)
            {
                dbset.GetType().GetMethod("FillMTM").Invoke(dbset, null);
            }
        }
        
        #region constructor related
        
        public DbContext(String DatabaseFolder)
        {
            Directory.CreateDirectory(DatabaseFolder);
            this.DatabaseFolder = DatabaseFolder;
            initSetTypes(); //Creates the list of the DbSet Generic Parameter types
            initDbSetList(); //Creates instances of all DbSets and assigns to corresponding properties
            InitDbSetProps(); //Assigns this instance of DbContext to the DbSets, for DbSets to be able to send messages to DbContext.
            initMTMTables(); //DbContext will be managing the MTM write operations. This will create the MTM tables.
        }

        public DbContext ()
        {
            DatabaseFolder = @"C:\Deneme";
            Directory.CreateDirectory(DatabaseFolder);
            initSetTypes(); //Creates the list of the DbSet Generic Parameter types
            initDbSetList(); //Creates instances of all DbSets and assigns to corresponding properties
            InitDbSetProps(); //Assigns this instance of DbContext to the DbSets, for DbSets to be able to send messages to DbContext.
            initMTMTables(); //DbContext will be managing the MTM write operations. This will create the MTM tables.
        }
        
        public void initMTMTables()
        {

            //dosyaları yoksa oluştur
            foreach(KeyValuePair<string, Tuple<Type,Type>> a in BaseClass.AllMTMRelations)
            {
                String filepath = Path.Combine(this.DatabaseFolder, a.Key) + ".dat";
                Stream mtmStream;
                if (!File.Exists(filepath))
                {
                    mtmStream = File.Create(filepath);
                    mtmStream.Close();
                }
            }
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
            object[] params1 = { "AddDirectly".ToCharArray() }; //en başta çünkü child'lara bunun id'si yerleştirilecek
            object[] params2 = { "AddChilds".ToCharArray() };//childlar eklendikten sonra parentlar güncellenecek
            object[] params3 = { "Update".ToCharArray() };//güncelleme
            object[] params4 = { "Remove".ToCharArray() };
            object[] params5 = { "AddOTM".ToCharArray() }; //en sonda çünkü önce parent eklenmeli, id'ye sahip olmalı.
            object[] params6 = { "UpdateAfterRemoval".ToCharArray() };
            object[][] parameters = { params1, params2, params3, params4, params5, params6 };
            foreach (object[] parameter in parameters)
            {
                foreach (object o in dbsets)
                {
                    MethodInfo method = o.GetType().GetMethod("SaveChanges");
                    result |= (bool)method.Invoke(o, parameter);
                }
                
            }
            this.removed = new Dictionary<Type, List<int>>();
            if (result) //if any changes happened, they may have triggered new changes.
            {
                
                SaveChanges();
            }
            //Mtm kayıtlarını hallet.
            String s;
            var kpList = MTMToWrite.ToList();
            while(kpList.Count()>0)
            {
                s = kpList[0].Key;
                String filepath = Path.Combine(this.DatabaseFolder, kpList[0].Key);
                Stream mtmStream;
                mtmStream = File.OpenWrite(filepath + ".dat"); //Streami ata, aç
                foreach (Tuple<BaseClass,BaseClass> mtmRecBase in kpList[0].Value)
                {
                    MTMRec mtmRec = new MTMRec(mtmRecBase.Item1.id, mtmRecBase.Item2.id);
                    int fk = FileOps.Add(mtmStream, new List<int>(), mtmRec);
                    mtmRec.id = fk;
                    //Streami kapa, sil listeden.
                }
                mtmStream.Close();
                kpList.RemoveAt(0);
                MTMToWrite.Remove(s);
            }
            return 0;
        }

        /// <summary>
        /// read.
        /// </summary>
        public void CompleteAllMTMRequests()
        {
            bool result = true;
            //referansları doldur.
            foreach (object dbset in dbsets)
            {
                result &= (bool)dbset.GetType().GetMethod("CompleteMTMRequests").Invoke(dbset, null);
            }
            if (result)
            {
                CompleteAllMTMRequests(); //bir tur doldurma işlemi, daha önce kontrol edilmiş dbset'lerde yeni doldurma isteklerini
                //tetiklemiş olabilir. bu blok onu kontrol etmek için var.
            }
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
