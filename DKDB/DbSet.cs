using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DKDB
{
    public abstract class DbSet<T>
    {
        private Stream mainFile; //Stream access 
        private Stream metaFile;
        private List<int> removedIndexes = new List<int>();

        public DbContext ctx { get; set; } //To access if needed, like getting the types of other DbSet types in DbContext

        //Dictionary<T, Dictionary<PropertyInfo, object>> Updates = new Dictionary<T, Dictionary<PropertyInfo, object>>();
        public List<T> Updates = new List<T>();

        private List<T> recordsToAddDirectly = new List<T>();

        private List<T> recordsToRemove = new List<T>();

        private List<T> allRecords = new List<T>();

        private List<Tuple<object, object>> recordsToAddAsChild = new List<Tuple<object, object>>();
        //eklenenin sahibi, eklenen obje
        //bunun olma nedeni= bu listede dönülürken, nesne eklenecek, çağıranın fk'si güncellenecek

        List<PropertyInfo> primitiveInfos = new List<PropertyInfo>();
        List<PropertyInfo> customInfos = new List<PropertyInfo>();
        List<PropertyInfo> orderedInfos = new List<PropertyInfo>(); //order in the file

        private List<Tuple<object, PropertyInfo, int>> RecordsToBeFilled = new List<Tuple<object, PropertyInfo, int>>();
        //object=nesne referansı okunup doldurulacak nesne.
        //propertyinfo=doldurulacak property
        //referansın id'si

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
                                object dbset = ctx.GetDBSetByType(childObject.GetType());
                                object[] parameters = { new Tuple<object, object>(record, childObject) };
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

        /// <summary>
        /// Compares the properties of the dbset type with other dbset types, to create a list of custominfos.
        /// </summary>
        public void SetInfos()
        {
            var checklist = DKDBCustomAttributes.GetReferenceChecklist(ctx);
            List<PropertyInfo> infos = DKDBCustomAttributes.GetReferencePropertyList(typeof(T), checklist);

            foreach (PropertyInfo info in infos)
            {
                if (!checklist.Contains(info.PropertyType))
                {
                    primitiveInfos.Add(info);
                }
                else
                {
                    customInfos.Add(info);
                }

            }
        }

        #region CRUD

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
                    //null ise -1 ya da 0 falan bas 
                }
            }


            recordsToAddDirectly.Add(record);
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

        public void UpdateRange(IEnumerable<T> records)
        {

        }

        /// <summary>
        /// Completes the child assignment requests ordered by the parent objects.
        /// </summary>
        /// <returns>Returns value to be used by DbContext wrapper function "FillOthers".</returns>
        private bool FillOtherDbSetRecords()
        {
            bool result = false;
            while (RecordsToBeFilled.Count() != 0)
            {
                result = true;
                //doldur
                Tuple<object, PropertyInfo, int> log = RecordsToBeFilled[0];
                log.Item2.SetValue(log.Item1, Read(log.Item3));
                RecordsToBeFilled.RemoveAt(0);
            }
            return result;
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

        #endregion

        #region Read

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
        /// Completes some of the changes in the dbset chosen by the command.
        /// </summary>
        /// <param name="command">AddDirectly, AddChilds, Update, Remove</param>
        public bool SaveChanges(String command)
        {
            bool result = false;
            if (command == "AddDirectly")
            {
                //0'dakiler ile işlem yapma nedeni:
                //Bir ekleme işlemi yapılırken, içinde çocuk nesne var ise, bir şekilde üzerinde çalışılan
                //listenin sonuna yeni kayıt eklenmesine neden olabilir. mi? düşün
                while (recordsToAddDirectly.Count() != 0)
                {
                    result = true;
                    FileOps.Add(mainFile, removedIndexes, customInfos, primitiveInfos, orderedInfos, recordsToAddDirectly[0]);
                    recordsToAddAsChild.RemoveAt(0);
                }
            }
            if (command == "AddChilds")
            {
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
                    ownerDbSet.GetType().GetMethod("Update").Invoke(ownerDbSet, parameters);
                    recordsToAddAsChild.RemoveAt(0);
                }
            }
            if (command == "Update")
            {
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
                }
            }
            if (command == "Remove")
            {
                while (recordsToRemove.Count() != 0)
                {
                    result = true;
                    FileOps.Remove(mainFile, customInfos, primitiveInfos, orderedInfos, metaFile, recordsToRemove[0]);
                }
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
                mainFile = File.OpenRead("asdf");
            } //else, the stream should be already opened before.
            Tuple<object, Dictionary<PropertyInfo, int>> fillingLog;
            //object = the record that it's childs will be read and assigned to.
            //propertyinfo = propertyinfo of a child of the record
            //int = id of the child to be read.
            fillingLog = FileOps.ReadSingle(mainFile, id, typeof(T), primitiveInfos, customInfos, orderedInfos);
            foreach (KeyValuePair<PropertyInfo, int> kp in fillingLog.Item2)
            {
                object dbset = ctx.GetDBSetByType(kp.Key.PropertyType);
                object[] parameters = { kp.Value };
                Tuple<object, PropertyInfo, int> RecordToBeFilled = new Tuple<object, PropertyInfo, int>(fillingLog.Item1, kp.Key, kp.Value);
                object[] parameters2 = { RecordToBeFilled };
                dbset.GetType().GetProperty("RecordsToBeFilled").GetType().GetMethod("Add").Invoke(dbset.GetType().GetProperty("RecordsToBeFilled"), parameters2);
                
            }
            if (!CameByAllFunction)
            {
                ctx.FillOthers();
                mainFile.Close();
            } //else, the stream will be closed and the FillOthers will be called in the wrapper function.
            return (T)fillingLog.Item1;

        }


    }
}
