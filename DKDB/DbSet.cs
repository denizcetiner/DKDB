using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DKDB
{
    public class DbSet<T>
    {
        #region properties and fields

        private Stream mainFile; //Main file's stream access. Assigned automatically while the DbSet is being constructed.
        private Stream metaFile; //Meta file's stream access. Assigned automatically while the DbSet is being constructed.
        private List<int> removedIndexes = new List<int>(); //not yet

        public DbContext ctx { get; set; } //To access if needed, like getting the types of other DbSet types in DbContext

        //Dictionary<T, Dictionary<PropertyInfo, object>> Updates = new Dictionary<T, Dictionary<PropertyInfo, object>>();
        public List<T> Updates = new List<T>();
        //Presumed the primary key(currently only 'id') is never changed:
        //During the SaveChanges operation; out-of-date record is located by the id, and overwritten by the up-to-date record in this list. 

        private List<T> recordsToAddDirectly = new List<T>();
        //Members of this list are added through the "Add" method of this DbSet.
        //Members of this list doesn't have a real id yet. Real ids are assigned during the SaveChanges operation.

        private List<T> recordsToRemove = new List<T>();
        //Members of this list are added through the "Remove" method of this DbSet.
        //During the SaveChanges operation; "removed flag"s of the each member are set to "true", and overwritten in the table file.
        

        private List<T> allRecords = new List<T>();

        private List<Tuple<object, object>> recordsToAddAsChild = new List<Tuple<object, object>>();
        //<owner of the to-be-added record, to-be-added record>
        //explanation: during the SaveChanges operation, this list is iterated.
        //For each _tuple, to-be-added record is inserted into the table file and given an id;
        //and than a message is sent from this DbSet to the owner of the record's DbSet to update the owner of the record.
        //Because both of these objects (owner and the child) are still in the memory,
        //during the OverWrite operation when the "PropertyInfo which is associated with the child record" is checked,
        //the child record will be accessed and its fk_id will be retrieved and overwritten to the file.
        
        List<PropertyInfo> primitiveInfos = new List<PropertyInfo>();
        List<PropertyInfo> customInfos = new List<PropertyInfo>();
        List<PropertyInfo> orderedInfos = new List<PropertyInfo>(); //order in the file

        private List<Tuple<object, PropertyInfo, int>> RecordsToBeFilled = new List<Tuple<object, PropertyInfo, int>>();
        //object=nesne referansı okunup doldurulacak nesne.
        //propertyinfo=doldurulacak property
        //referansın id'si

        #endregion

        /// <summary>
        /// Adds a record that is a child of another record.
        /// </summary>
        /// <param name="owner">Owner of the record.</param>
        /// <param name="record">Record.</param>
        public void AddAsChild(object owner, object record)
        {
            //add'in içindeki işlemler. bunun da içinde çocuk nesne olabilir.
            if (DKDBCustomAttributes.Validator(record)) //Checks the attributes
            {
                foreach (PropertyInfo info in record.GetType().GetProperties())
                {
                    if (ctx.dbsetTypes.Contains(info.GetType()))
                    {
                        object childObject = info.GetValue(record);
                        if (childObject != null)
                        {
                            int childObjectId = (int)childObject.GetType().GetProperty("id").GetValue(childObject);
                            if (childObjectId == 0)
                            {
                                //Çocuk nesne varsa, ilgili dbset'in "Eklenecek çocuklar" listesine 
                                object dbset = ctx.GetDBSetByType(childObject.GetType());
                                object[] parameters = { new Tuple<object, object>(record, childObject) };
                                //^ record=parent, childobject=child
                                dbset.GetType().GetMethod("AddAsChild").Invoke(dbset, parameters);
                            }
                        }

                    }
                    //düz yaz
                    //null ise -1 ya da 0 falan bas 
                }
            }
            recordsToAddAsChild.Add(new Tuple<object, object>(owner, record));
        }

        //savechanges çalışırken id'si int olan nesne okunacak, object'in property'sine atanacak.
        public void AddAsFilled(Tuple<object, PropertyInfo, int> RecordToBeFilled)
        {
            this.RecordsToBeFilled.Add(RecordToBeFilled);
        }

        #region constructor related

        public void CreateFilesIfNotExist()
        {
            if(!File.Exists(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + ".dat")))
            {
                mainFile = File.Create(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + ".dat"));
                mainFile.Close();
            }
            if(!File.Exists(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + "_meta.dat")))
            {
                metaFile = File.Create(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + "_meta.dat"));
                metaFile.Close();
            }
            OpenMetaWrite();
            FileOps.CreateMetaFile(metaFile, this.GetType().GetGenericArguments()[0]);
            metaFile.Close();
        }

        /// <summary>
        /// Compares the properties of the dbset type with other dbset types, to create a list of custominfos.
        /// </summary>
        public void SetInfos()
        {
            List<Type> checklist = ctx.dbsetTypes;
            List<PropertyInfo> infos = typeof(T).GetProperties().ToList();

            foreach (PropertyInfo info in infos)
            {
                //notmapped kontrolü de eklenecek
                if (!checklist.Contains(info.PropertyType))
                {
                    primitiveInfos.Add(info);
                }
                else
                {
                    customInfos.Add(info);
                }
            }
            OpenMetaRead();
            List<Tuple<String,String>> PropsAndNames = FileOps.ReadMetaFilePropertiesAndNames(metaFile);
            metaFile.Close();

            foreach (Tuple<String,String> pair in PropsAndNames)
            {
                for(int i=0;i<primitiveInfos.Count;i++)
                {
                    if(pair.Item2 == primitiveInfos[i].Name)
                    {
                        orderedInfos.Add(primitiveInfos[i]);
                    }
                }
                for (int i = 0; i < customInfos.Count; i++)
                {
                    if (pair.Item2 == customInfos[i].Name)
                    {
                        orderedInfos.Add(customInfos[i]);
                    }
                }
            }
        }

        public DbSet(DbContext ctx)
        {
            this.ctx = ctx;
            CreateFilesIfNotExist();
            SetInfos();
        }

        #endregion



        /// <summary>
        /// Adds given record to the buffer.
        /// </summary>
        /// <param name="record">Record to add.</param>
        public void Add(T record)
        {
            if (DKDBCustomAttributes.Validator(record)) //Checks the attributes
            {
                foreach (PropertyInfo info in record.GetType().GetProperties())
                {
                    if (ctx.dbsetTypes.Contains(info.PropertyType))
                    {
                        object childObject = info.GetValue(record);
                        if (childObject != null)
                        {
                            int childObjectId = (int)childObject.GetType().GetProperty("id").GetValue(childObject);
                            if (childObjectId == 0)
                            {
                                object dbset = ctx.GetDBSetByType(childObject.GetType());
                                object[] parameters = { record, childObject };
                                dbset.GetType().GetMethod("AddAsChild").Invoke(dbset, parameters);
                            }
                        }

                    }
                    //null ise -1 ya da 0 falan bas 
                }
            }

            recordsToAddDirectly.Add(record);
        }

        #region stream openers

        public void OpenMainRead()
        {
            mainFile = File.OpenRead(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + ".dat"));
        }

        public void OpenMainWrite()
        {
            mainFile = File.OpenWrite(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + ".dat"));
        }

        public void OpenMetaRead()
        {
            metaFile = File.OpenRead(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + "_meta.dat"));
        }

        public void OpenMetaWrite()
        {
            metaFile = File.OpenWrite(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + "_meta.dat"));
        }

        #endregion


        #region boş şimdilik, temel şeyler değil

        public void UpdateRange(IEnumerable<T> records)
        {

        }

        public void AddRange(IEnumerable<T> records)
        {

        }

        public void Remove(T record)
        {
            recordsToRemove.Add(record);
        }

        public void RemoveRange(IEnumerable<T> records)
        {

        }

        public long Count()
        {
            ReadAllRecords();
            return allRecords.LongCount();
        }

        public T Last()
        {
            ReadAllRecords();
            return allRecords.Last();
        }

        public T First(Func<T, bool> predicate)
        {
            ReadAllRecords();
            return allRecords.First(predicate);
        }

        public IEnumerable<T> Where(Func<T, bool> predicate)
        {
            ReadAllRecords();
            return allRecords.Where(predicate);
        }

        public List<T> All()
        {
            ReadAllRecords();
            return allRecords;
        }

        #endregion

        /// <summary>
        /// Given record will be updated.
        /// </summary>
        /// <param name="record">Record to be analyzed and updated.</param>
        public void Update(T record)
        {
            if (DKDBCustomAttributes.Validator(record)) //Checks the attributes
            {
                foreach (PropertyInfo info in record.GetType().GetProperties())
                {
                    if (ctx.dbsetTypes.Contains(info.GetType()))
                    {
                        object childObject = info.GetValue(record);
                        if (childObject != null)
                        {
                            int childObjectId = (int)childObject.GetType().GetProperty("id").GetValue(childObject);
                            if (childObjectId == 0)
                            {
                                object dbset = ctx.GetDBSetByType(childObject.GetType());
                                object[] parameters = { new Tuple<object, object>(record, childObject) };
                                dbset.GetType().GetMethod("AddAsChild").Invoke(dbset, parameters);
                            }
                        }
                    }
                }

            }
        }

        

        /// <summary>
        /// Completes the child assignment requests ordered by the parent objects.
        /// </summary>
        /// <returns>Returns value to be used by DbContext wrapper function "FillOthers".</returns>
        public bool FillOtherDbSetRecords()
        {
            bool result = false;
            while (RecordsToBeFilled.Count() != 0)
            {
                result = true;
                //doldur
                Tuple<object, PropertyInfo, int> log = RecordsToBeFilled[0];
                PropertyInfo info = log.Item2;
                object target = log.Item1;
                int fkid = log.Item3;
                RecordsToBeFilled.RemoveAt(0);
                log.Item2.SetValue(log.Item1, Read(log.Item3)); //critical work completed here
                
            }
            return result; //returns false if nothing is done, otherwise true.
        }

        private void ReadAllRecords()
        {
            if (allRecords.Count == 0)
            {
                FileOps.CalculateRowByteSize(orderedInfos, customInfos, primitiveInfos);
                int line = -1;//satır sayısını hesapla
                for (int i = 0; i < line; i++) 
                {
                    allRecords.Add(Read(i, true));
                }
            }
        }
        
        

        /// <summary>
        /// Completes some of the changes in the dbset chosen by the command.
        /// </summary>
        /// <param name="command">AddDirectly, AddChilds, Update, Remove</param>
        public bool SaveChanges(char[] com)
        {
            String command = new string(com);
            bool result = false;
            if (command == "AddDirectly")
            {
                //0. elemanlar ile işlem yapma nedeni:
                //Bir ekleme işlemi yapılırken, içinde çocuk nesne var ise, bir şekilde üzerinde çalışılan
                //listenin sonuna yeni kayıt eklenmesine neden olabilir.
                OpenMainWrite();
                while (recordsToAddDirectly.Count() != 0)
                {
                    result = true;
                    FileOps.Add(mainFile, removedIndexes, customInfos, primitiveInfos, orderedInfos, recordsToAddDirectly[0]);
                    recordsToAddDirectly.RemoveAt(0);
                }
                mainFile.Close();
            }
            if (command == "AddChilds")
            {
                OpenMainWrite();
                while (recordsToAddAsChild.Count() != 0)
                {
                    result = true;
                    //item1=parent, item2=child
                    T record = (T)recordsToAddAsChild[0].Item2;
                    int fk = FileOps.Add(mainFile, removedIndexes, customInfos, primitiveInfos, orderedInfos, record);
                    record.GetType().GetProperty("id").SetValue(record, fk);
                    Type ownerType = recordsToAddAsChild[0].Item1.GetType();
                    Type ownerDbSetType = ctx.dbsetTypes.Where(a => a.ToString().Equals(ownerType.ToString())).ElementAt(0);
                    object ownerDbSet = ctx.GetDBSetByType(ownerDbSetType);
                    object[] parameters = new object[1];
                    parameters[0] = recordsToAddAsChild[0].Item1;
                    //Explained under recordsToAddAsChild declaration.
                    ownerDbSet.GetType().GetMethod("Update").Invoke(ownerDbSet, parameters);
                    recordsToAddAsChild.RemoveAt(0);
                }
                mainFile.Close();
            }
            if (command == "Update")
            {
                OpenMainWrite();
                while (Updates.Count() != 0)
                {
                    result = true;
                    #region Yerelde cacheleme ve güncelleme versiyonu 
                    //KeyValuePair<T, Dictionary<PropertyInfo, object>> UpdatePair = Updates.ElementAt(0);
                    //T objectToUpdate = UpdatePair.Key;
                    //foreach (KeyValuePair<PropertyInfo, object> Update in UpdatePair.Value)
                    //{
                    //    Update.Key.SetValue(objectToUpdate, Update.Key.GetValue(Update.Value));
                    //}
                    #endregion
                    T record = Updates[0];
                    FileOps.Overwrite(mainFile, customInfos, primitiveInfos, orderedInfos, record);
                    Updates.RemoveAt(0);
                }
                mainFile.Close();
            }
            if (command == "Remove")
            {
                OpenMainWrite();
                while (recordsToRemove.Count() != 0)
                {
                    result = true;
                    FileOps.Remove(mainFile, customInfos, primitiveInfos, orderedInfos, metaFile, recordsToRemove[0]);
                    recordsToRemove.RemoveAt(0);
                }
                mainFile.Close();
            }
            //
            return result;
        }


        /// <summary>
        /// Reads a single record from the file.
        /// </summary>
        /// <param name="id">Id of the record to be read.</param>
        /// <param name="CameByAllFunction">Optional parameter, set to true if called by ReadAll function.</param>
        /// <returns></returns>
        public T Read(int id, bool CameByAllFunction = false)
        {
            if(!CameByAllFunction)
            {
                OpenMainRead();
            } //else, the stream should be already opened before.
            Tuple<object, Dictionary<PropertyInfo, int>> fillingLog;
            //object = the record that it's childs will be read and assigned to.
            //propertyinfo = propertyinfo of a child of the record
            //int = id of the child to be read.
            fillingLog = FileOps.ReadSingleRecord(mainFile, id, typeof(T), primitiveInfos, customInfos, orderedInfos);
            foreach (KeyValuePair<PropertyInfo, int> kp in fillingLog.Item2)
            {
                object dbset = ctx.GetDBSetByType(kp.Key.PropertyType);
                object[] parameters = { kp.Value };
                Tuple<object, PropertyInfo, int> RecordToBeFilled = new Tuple<object, PropertyInfo, int>(fillingLog.Item1, kp.Key, kp.Value);
                object[] parameters2 = { RecordToBeFilled };
                dbset.GetType().GetMethod("AddAsFilled").Invoke(dbset, parameters2);
                
            }
            if (!CameByAllFunction)
            {
                ctx.FillOthers();
                mainFile.Close();
            } //else, the stream will be closed and the FillOthers will be called in the wrapper function.
            return (T)fillingLog.Item1;

        }
        

        public List<T> Filter(PropertyInfo info, object value)
        {
            //infosunun değeri, value'ya eşit olanları döndürür. Karşılaştırma fileops'ta gerçekleşecek.
            List<T> filteredResults = new List<T>();
            return filteredResults;
        }

    }
}


//public void Update(T record)
//{
//    ReadAllRecords();
//    PropertyInfo id = record.GetType().GetProperty("id");
//    //Orijinal üzerine daha sonra tüm değişiklikler sırayla uygulanacak.
//    T original = allRecords.FirstOrDefault(u => id.GetValue(u) == id.GetValue(record));

//    //Değişiklikleri tespit et.
//    foreach (PropertyInfo info in record.GetType().GetProperties())
//    {
//        if (primitiveInfos.Contains(info) && info.GetValue(record) != info.GetValue(original))
//        {
//            if (!Updates.ContainsKey(original))
//            {
//                Dictionary<PropertyInfo, object> dict = new Dictionary<PropertyInfo, object>();
//                dict.Add(info, info.GetValue(record));
//                Updates.Add(original, dict);
//            }
//            else
//            {
//                Updates.FirstOrDefault(kp => kp.Key.Equals(original)).Value.Add(info, info.GetValue(record));
//            }
//        }
//        else if (customInfos.Contains(info) && info.GetValue(record) != info.GetValue(original))
//        {
//            //Update listesinde yoksa
//            if (!Updates.ContainsKey(original))
//            {
//                //Anahtar ve değer oluştur
//                Dictionary<PropertyInfo, object> dict = new Dictionary<PropertyInfo, object>();
//                dict.Add(info, info.GetValue(record));
//                Updates.Add(original, dict);
//            }
//            //Update listesinde varsa
//            else
//            {
//                //Değerdeki listeye değişim ekle
//                Updates.FirstOrDefault(kp => kp.Key.Equals(original)).Value.Add(info, info.GetValue(record));
//            }
//        }
//    }
//}
