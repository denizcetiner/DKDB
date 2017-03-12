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
        /// <param name="record">Record to add</param>
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

        /// <summary>
        /// Adds given record to the file.
        /// </summary>

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
                    
                    //düz yaz
                    //null ise -1 ya da 0 falan bas 
                }

            }
        }

        public void UpdateRange(IEnumerable<T> records)
        {

        }

        private void ReadAllRecords()
        {
            if (allRecords.Count == 0)
            {

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
        

        public void SaveChanges(String command)
        {
            if(command == "AddDirectly")
            {
                //0'dakiler ile işlem yapma nedeni:
                //Bir ekleme işlemi yapılırken, içinde çocuk nesne var ise, bir şekilde üzerinde çalışılan
                //listenin sonuna yeni kayıt eklenmesine neden olabilir. mi? düşün
                while (recordsToAddDirectly.Count() != 0)
                {
                    FileOps.Add(mainFile, removedIndexes, customInfos, primitiveInfos, recordsToAddDirectly[0]);
                    recordsToAddAsChild.RemoveAt(0);
                }
            }
            if(command == "AddChilds")
            {
                while (recordsToAddAsChild.Count() != 0)
                {
                    //item1=parent, item2=child
                    T record = (T)recordsToAddAsChild[0].Item2;
                    int fk = FileOps.Add(mainFile, removedIndexes, customInfos, primitiveInfos, record);
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
            if(command == "Update")
            {
                while (Updates.Count() != 0)
                {
                    #region Yerelde cacheleme ve güncelleme versiyonu 
                    //KeyValuePair<T, Dictionary<PropertyInfo, object>> UpdatePair = Updates.ElementAt(0);
                    //T objectToUpdate = UpdatePair.Key;
                    //foreach (KeyValuePair<PropertyInfo, object> Update in UpdatePair.Value)
                    //{
                    //    Update.Key.SetValue(objectToUpdate, Update.Key.GetValue(Update.Value));
                    //}
                    #endregion

                    T record = Updates[0];
                    FileOps.Overwrite(mainFile, customInfos, primitiveInfos, record);


                }
            }
            if(command == "Remove")
            {
                while(recordsToRemove.Count() != 0)
                {
                    FileOps.Remove(mainFile, metaFile, recordsToRemove[0]);
                }
            }
            
            
            //

        }

    }
}
