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
        public String EndingChars = "/()=";
        public List<Type> dbsetTypes = new List<Type>();
        public List<object> dbsets = new List<object>();

        public Dictionary<Type, List<int>> removed = new Dictionary<Type, List<int>>();

        public Dictionary<String, Tuple<Type,Type>> MTMRelations
            = new Dictionary<string, Tuple<Type,Type>>();
        //public List<TransactionRecord> transactionRecords = new List<TransactionRecord>();

        public Dictionary<String, List<Tuple<object, object>>> MTMToWrite = new Dictionary<string, List<Tuple<object, object>>>();

        

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
        /// Fills the otm list of the read records
        /// </summary>
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
        /// Fills the child objects of the read records. (one-to-one)
        /// </summary>
        public void FillOthers()
        {
            bool result = true;
            //referansları doldur.
            foreach (object dbset in dbsets)
            {
                object[] parameters = new object[1];
                parameters[0] = false;
                result &= (bool)dbset.GetType().GetMethod("FillOtherDbSetRecords").Invoke(dbset, parameters);
            }
            if (result)
            {
                FillOthers(); //bir tur doldurma işlemi, daha önce kontrol edilmiş dbset'lerde yeni doldurma isteklerini
                //tetiklemiş olabilir. bu blok onu kontrol etmek için var.
            }
        }


        #region constructor related
        
        public DbContext(String DatabaseFolder)
        {
            Directory.CreateDirectory(DatabaseFolder);
            this.DatabaseFolder = DatabaseFolder;
            initSetTypes(); //Creates the list of the DbSet Generic Parameter types
            initDbSetList(); //Creates instances of all DbSets and assigns to proper properties
            InitDbSetProps(); //Assigns this instance of DbContext to the DbSets, for DbSets to be able to send messages to DbContext.
            initMTMTables(); //DbContext will be managing the MTM operations. This will create the MTM tables.
        }

        public DbContext ()
        {
            DatabaseFolder = @"C:\Deneme";
            Directory.CreateDirectory(DatabaseFolder);
            initSetTypes(); //Creates the list of the DbSet Generic Parameter types
            initDbSetList(); //Creates instances of all DbSets and assigns to proper properties
            InitDbSetProps(); //
            initMTMTables();
        }
        
        public void initMTMTables()
        {

            //dosyaları yoksa oluştur
            foreach(var a in MTMRelations)
            {
                String filepath = Path.Combine(this.DatabaseFolder, a.Key) + ".dat";
                Stream mtmStream;
                if (!File.Exists(filepath))
                {
                    mtmStream = File.Create(filepath + ".dat");
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

        public void SetChanged()
        {
            foreach(var a in dbsets)
            {
                a.GetType().GetProperty("Changed").SetValue(a, true);
            }
        }

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
                    SetChanged();
                    MethodInfo method = o.GetType().GetMethod("SaveChanges");
                    result |= (bool)method.Invoke(o, parameter);
                }
            }
            if(result) //if any changes happened, they may have triggered new changes.
            {
                
                SaveChanges();
            }
            //Mtm kayıtlarını hallet.
            foreach(KeyValuePair<String, List<Tuple<object,object>>> kp in MTMToWrite)
            {
                String filepath = Path.Combine(this.DatabaseFolder, kp.Key);
                Stream mtmStream;
                mtmStream = File.OpenWrite(filepath + ".dat"); //Streami ata, aç
                foreach (Tuple<object,object> mtmRecBase in kp.Value)
                {
                    MTMRec mtmRec = new MTMRec(
                        (int)mtmRecBase.Item1.GetType().GetProperty("id").GetValue(mtmRecBase.Item1),
                        (int)mtmRecBase.Item2.GetType().GetProperty("id").GetValue(mtmRecBase.Item2)
                        );
                    FileOps.Add(mtmStream, new List<int>(), MTMRec.piContainer, mtmRec);
                    //Streami kapa, sil listeden.
                }
                mtmStream.Close();
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
