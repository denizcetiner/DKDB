using System;
using System.Collections;
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

        private List<Tuple<object, object, PropertyInfo>> recordsToAddAsOTM = new List<Tuple<object, object, PropertyInfo>>();
        //obje1=parent, obje2=child, propertyinfo=child'in foreignkey alanı

        public List<T> allRecords = new List<T>();

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

        Tuple<List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>> piContainer;

        private List<Tuple<object, PropertyInfo, int>> RecordsToBeFilled = new List<Tuple<object, PropertyInfo, int>>();
        //object=nesne referansı okunup doldurulacak nesne.
        //propertyinfo=doldurulacak property
        //referansın id'si

        private List<Tuple<object, PropertyInfo, List<int>>> RecordsToBeFilledMTM = new List<Tuple<object, PropertyInfo, List<int>>>();

        public List<Type> OTORelations = new List<Type>();

        List<PropertyInfo> OneToMany_One = new List<PropertyInfo>();
        //one tarafında olduğumuz infolar
        private List<PropertyInfo> OneToMany_Many = new List<PropertyInfo>();
        //many tarafında olduğumuz infolar (doldurması kompleks, init aşamasında olacak gibi)

        private List<Tuple<object, PropertyInfo>> OTMRequests = new List<Tuple<object, PropertyInfo>>();
        //for reading

        private List<Tuple<String, PropertyInfo, Type>> ManyToMany = new List<Tuple<string, PropertyInfo, Type>>();
        //table adları, ctx'ten bakılacak.
        //dbset construct edilirken bunu ve ctx'in içindekini doldur.


        #endregion

        /// <summary>
        /// Adds a record that is a child of another record.
        /// </summary>
        /// <param name="owner">Owner of the record.</param>
        /// <param name="record">Record.</param>
        public void AddAsChild(object owner, object record)
        {
            //add'in içindeki işlemler. bunun da içinde çocuk nesne olabilir.
            if (CustomAttr.Validator(record)) //Checks the attributes
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
                                object[] parameters = { record, childObject };
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

        public void AddAsOTM(object owner, object child, PropertyInfo info)
        {
            this.recordsToAddAsOTM.Add(new Tuple<object, object, PropertyInfo>(owner, child, info));
        }

        //savechanges çalışırken id'si int olan nesne okunacak, object'in property'sine atanacak.
        public void AddAsFilled(Tuple<object, PropertyInfo, int> RecordToBeFilled)
        {
            this.RecordsToBeFilled.Add(RecordToBeFilled);
        }

        //public void AddAsFilledMTM(Tuple<object, PropertyInfo, int> RecordToBeFilled)
        //{
        //    this.RecordsToBeFilledMTM.Add(RecordToBeFilled);
        //}

        #region constructor related

        public void CreateFilesIfNotExist()
        {
            if (!File.Exists(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + ".dat")))
            {
                mainFile = File.Create(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + ".dat"));
                mainFile.Close();
            }
            if (!File.Exists(Path.Combine(ctx.DatabaseFolder, this.GetType().GetGenericArguments()[0].Name + "_meta.dat")))
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
                else if (info.PropertyType.IsGenericType)
                {
                    if (CustomAttr.GetOTMTarget(info) != null)
                    {
                        OneToMany_One.Add(info);

                    }
                    Tuple<string, string> Target_Table = CustomAttr.GetMTMTargetAndTable(info);
                    if (Target_Table != null)
                    {
                        Type targetType = info.PropertyType.GetGenericArguments()[0].GetType();
                        Type me = typeof(T);
                        PropertyInfo targetInfo = info.PropertyType.GetGenericArguments()[0].GetProperty(Target_Table.Item1);

                        if(!ctx.MTMRelations.Any(e => e.Key == Target_Table.Item2))
                        {
                            ctx.MTMRelations.Add(
                            Target_Table.Item2, new Tuple<Type, Type>(me, targetType));
                        }
                        this.ManyToMany.Add(new Tuple<String, PropertyInfo, Type>(Target_Table.Item2, info, targetType));
                    }
                }
                else
                {
                    customInfos.Add(info);
                }
            }
            OpenMetaRead();
            List<Tuple<String, String>> PropsAndNames = FileOps.ReadMetaFilePropertiesAndNames(metaFile);
            metaFile.Close();

            foreach (Tuple<String, String> pair in PropsAndNames)
            {
                for (int i = 0; i < primitiveInfos.Count; i++)
                {
                    if (pair.Item2 == primitiveInfos[i].Name)
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

        public int WhoAmI(string table)
        {
            KeyValuePair<string, Tuple<Type, Type>> kp = ctx.MTMRelations.FirstOrDefault(r => r.Key == table);
            if (typeof(T) == kp.Value.Item1) return 1;
            else return 2;
        }


        public DbSet(DbContext ctx)
        {
            this.ctx = ctx;
            CreateFilesIfNotExist();
            SetInfos();
            piContainer = new Tuple<List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>>(
                this.primitiveInfos, this.customInfos, this.orderedInfos, OneToMany_One);
        }

        #endregion



        /// <summary>
        /// Adds given record to the buffer.
        /// </summary>
        /// <param name="record">Record to add.</param>
        public void Add(T record)
        {
            if (CustomAttr.Validator(record)) //Checks the attributes
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
                //one to many
                foreach (PropertyInfo OTM in OneToMany_One)
                {
                    IList list = record.GetType().GetProperty(OTM.Name).GetValue(record) as IList;
                    object dbset = ctx.GetDBSetByType(OTM.PropertyType.GetGenericArguments()[0]);
                    foreach(var a in list)
                    {
                        object[] parameters = new object[3];
                        parameters[0] = record;
                        parameters[1] = a;
                        parameters[2] = CustomAttr.GetOTMTarget(OTM);
                        dbset.GetType().GetMethod("AddAsOTM").Invoke(dbset, parameters);
                    }
                }


                //many to many

                WriteCompleteMTM(record);
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
            if (CustomAttr.Validator(record)) //Checks the attributes
            {
                foreach (PropertyInfo info in record.GetType().GetProperties())
                {
                    bool contains = false;
                    foreach(var a in ctx.dbsetTypes)
                    {
                        if(a.Name.Equals(info.PropertyType.Name))
                        {
                            contains = true;
                        }
                    }
                    if (contains)
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
                }
                this.Updates.Add(record);
            }
        }



        /// <summary>
        /// Completes the child assignment requests ordered by the parent objects.
        /// </summary>
        /// <returns>Returns value to be used by DbContext wrapper function "FillOthers".</returns>
        public bool FillOtherDbSetRecords(bool readRemoved = false)
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
                T rec = Read(fkid);
                if(readRemoved || !(bool)rec.GetType().GetProperty("removed").GetValue(rec))
                {
                    log.Item2.SetValue(log.Item1, rec);//critical work completed here
                }
                 

            }
            return result; //returns false if nothing is done, otherwise true.
        }

        public void ReadAllRecords(bool ReadRemoved = false)
        {
            allRecords = new List<T>();
            {
                OpenMainRead();
                int line = Convert.ToInt32(mainFile.Length) / FileOps.CalculateRowByteSize(orderedInfos, customInfos, primitiveInfos);
                for (int i = 0; i < line; i++)
                {
                    object rec = Read(i + 1, true);
                    if(ReadRemoved || !(bool)rec.GetType().GetProperty("removed").GetValue(rec))
                    {
                        allRecords.Add((T)rec);
                    }
                }
                mainFile.Close();
                ctx.FillOthers();
                ctx.CompleteAllOTMRequests();
                ctx.CompleteAllMTMRequests();
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
            if(command == "AddOTM")
            {
                while(recordsToAddAsOTM.Count() != 0)
                {
                    Tuple<object, object, PropertyInfo> rec = recordsToAddAsOTM[0];
                    OpenMainWrite();
                    rec.Item2.GetType().GetProperty(rec.Item3.Name)
                        .SetValue(rec.Item2, rec.Item1.GetType().GetProperty("id").GetValue(rec.Item1));
                    int fk = FileOps.Add(mainFile, removedIndexes, piContainer, rec.Item2);
                    rec.Item2.GetType().GetProperty("id").SetValue(rec.Item2, fk);
                    result = true;
                }
            }
            if (command == "AddDirectly")
            {
                //0. elemanlar ile işlem yapma nedeni:
                //Bir ekleme işlemi yapılırken, içinde çocuk nesne var ise, bir şekilde üzerinde çalışılan
                //listenin sonuna yeni kayıt eklenmesine neden olabilir.
                OpenMainWrite();
                while (recordsToAddDirectly.Count() != 0)
                {
                    result = true;
                    int id = FileOps.Add(mainFile, removedIndexes, piContainer, recordsToAddDirectly[0]);
                    recordsToAddDirectly[0].GetType().GetProperty("id").SetValue(recordsToAddDirectly[0], id);
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
                    int fk = FileOps.Add(mainFile, removedIndexes, piContainer, record);
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
                    FileOps.Overwrite(mainFile, piContainer, record);
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
                    FileOps.Remove(mainFile, piContainer, metaFile, recordsToRemove[0]);
                    if(!ctx.removed.Any(kp=>kp.Key == typeof(T)))
                    {
                        ctx.removed.Add(typeof(T), new List<int>());
                    }
                    ctx.removed.FirstOrDefault(kp => kp.Key == typeof(T)).Value
                        .Add((int)recordsToRemove[0].GetType().GetProperty("id").GetValue(recordsToRemove[0]));
                    recordsToRemove.RemoveAt(0);
                }
                mainFile.Close();
            }
            if(command =="UpdateAfterRemoval") //silinmiş bir kayda referans olanların foreign keylerini -1 yapacak
            {
                List<PropertyInfo> toChangeColumns = new List<PropertyInfo>();
                Dictionary<int, List<int>> toChange = new Dictionary<int, List<int>>();
                foreach(KeyValuePair<Type, List<int>> kp in ctx.removed)
                {
                    foreach(PropertyInfo info in this.GetType().GetProperties())
                    {
                        if (info.PropertyType.Name.Equals(kp.Key.Name))
                        {
                            toChangeColumns.Add(info);
                            foreach(int id in kp.Value) //null yapılacak id
                            {
                                toChange.Add(id, new List<int>());
                            }
                        }
                    }
                }
                ReadAllRecords();
                foreach(T rec in allRecords)
                {
                    foreach(PropertyInfo column in toChangeColumns)
                    {
                        foreach(KeyValuePair<int, List<int>> kp in toChange)
                        {
                            if((int)rec.GetType().GetProperty(column.Name).GetValue(rec) == kp.Key)
                            {
                                kp.Value.Add((int)rec.GetType().GetProperty("id").GetValue(rec));
                            }
                        }
                    }
                }
                foreach(PropertyInfo column in toChangeColumns)
                {
                    OpenMainWrite();
                    FileOps.MakeReferenceNull(mainFile, piContainer, column, toChange);
                }
            }

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
            if (!CameByAllFunction)
            {
                OpenMainRead();
            } //else, the stream should be already opened before.
            Tuple<object, Dictionary<PropertyInfo, int>> fillingLog;
            //object = the record that it's childs will be read and assigned to.
            //propertyinfo = propertyinfo of a child of the record
            //int = id of the child to be read.
            //for one-to-one relation or one-to-many relation's many side
            fillingLog = FileOps.ReadSingleRecord(mainFile, id, typeof(T), piContainer);
            foreach (KeyValuePair<PropertyInfo, int> kp in fillingLog.Item2)
            {
                object dbset = ctx.GetDBSetByType(kp.Key.PropertyType);
                if(kp.Value == -1) //null, okumaya çalışma!
                {
                    continue; 
                }
                object[] parameters = { kp.Value };
                Tuple<object, PropertyInfo, int> RecordToBeFilled = new Tuple<object, PropertyInfo, int>(fillingLog.Item1, kp.Key, kp.Value);
                object[] parameters2 = { RecordToBeFilled };
                dbset.GetType().GetMethod("AddAsFilled").Invoke(dbset, parameters2);

            }

            foreach (PropertyInfo OTM in OneToMany_One)
            {
                object dbset = ctx.GetDBSetByType(OTM.PropertyType.GetGenericArguments()[0]);
                object[] parameters = { new Tuple<object, PropertyInfo>(fillingLog.Item1, OTM) };
                dbset.GetType().GetMethod("AddAsOTMRequest").Invoke(dbset, parameters);
                CompleteOTMRequests();
            }

            //MTM
            ReadMTM(fillingLog.Item1);

            if (!CameByAllFunction)
            {
                mainFile.Close();
                ctx.FillOthers();
                ctx.CompleteAllOTMRequests();
                ctx.CompleteAllMTMRequests();
                
            } //else, the stream will be closed and the FillOthers will be called in the wrapper function.
            return (T)fillingLog.Item1;

        }


        #region OTM

        /// <summary>
        /// For call by reflection from ctx. Adds an OTM (one to many) request to the requests list.
        /// </summary>
        /// <param name="req"></param>
        public void AddAsOTMRequest(Tuple<object, PropertyInfo> req)
        {
            OTMRequests.Add(req);
        }

        /// <summary>
        /// (Read)Fills the 'one' side of OTM relations for each request
        /// </summary>
        public bool CompleteOTMRequests()
        {
            bool result = false;
            while (OTMRequests.Count() > 0)
            {
                result = true;
                ReadAllRecords(); //bunu düzelt sürekli okuyup durmasın. kontrol falan koy savechanges olduktan sonra bir defa 
                //tekrar okusun sadece.
                Tuple<object, PropertyInfo> request = OTMRequests[0];
                int id = (int)request.Item1.GetType().GetProperty("id").GetValue(request.Item1);
                String otm_target = CustomAttr.GetOTMTarget(request.Item2);
                List<T> filtered = allRecords.Where(record => (int)record.GetType().GetProperty(otm_target).GetValue(record) == id).ToList();
            }
            return result;
        }



        #endregion

        #region MTM

        /// <summary>
        /// Reads the middle table of many-to-many relationship. Bad performance currently, because it reads the
        /// middle file everytime a record is read.
        /// </summary>
        /// <param name="rec"></param>
        public void ReadMTM(object rec)
        {
            foreach (Tuple<String, PropertyInfo, Type> tuple in ManyToMany)
            {
                String filepath = Path.Combine(ctx.DatabaseFolder, tuple.Item1) + ".dat";
                Stream mtmStream = File.OpenRead(filepath);
                int line = Convert.ToInt32(mtmStream.Length / sizeof(int) * 3);
                List<MTMRec> mtmRecs = new List<MTMRec>();
                MTMRec.initContainer();
                for (int i = 0; i < line; i++)
                {
                    Tuple<object, Dictionary<PropertyInfo, int>> fillingLog;

                    fillingLog = FileOps.ReadSingleRecord(mtmStream, i + 1, tuple.Item3, MTMRec.piContainer);
                    mtmRecs.Add((MTMRec)fillingLog.Item1);
                }
                mtmStream.Close();
                int my_id_column = WhoAmI(tuple.Item1); //MTMRec içinde id1 mi benim sütunum yoksa id2 mi? ctx'teki sıraya göre kontrol edilir
                List<int> toGetList = new List<int>(); //Getirilecek nesnelerin id listesi
                foreach (MTMRec mtmRec in mtmRecs)
                {
                    if (my_id_column == 1)
                    {
                        if (mtmRec.id_1 == (int)rec.GetType().GetProperty("id").GetValue(rec))
                        {
                            toGetList.Add(mtmRec.id_2);
                        }
                    }
                    else
                    {
                        if (mtmRec.id_2 == (int)rec.GetType().GetProperty("id").GetValue(rec))
                        {
                            toGetList.Add(mtmRec.id_1);
                        }
                    }
                }
                Tuple<object, PropertyInfo, List<int>> request
                    = new Tuple<object, PropertyInfo, List<int>>(rec, tuple.Item2, toGetList);
                  object opposite_dbset = ctx.GetDBSetByType(typeof(T));
                object[] parameters = new object[1];

                parameters[0] = request; //list of ids of the records that we want to read from opposite db
                List<Tuple<object, PropertyInfo, List<int>>> rtbfMTM = opposite_dbset.GetType()
                    .GetProperty("RecordsToBeFilledMTM").GetValue(opposite_dbset) as List<Tuple<object, PropertyInfo, List<int>>>;
                rtbfMTM.GetType().GetMethod("AddRange").Invoke(rtbfMTM, parameters);
                
            }
        }


        /// <summary>
        /// Read requests for performance
        /// </summary>
        public bool CompleteMTMRequests()
        {
            bool result = false;
            while(this.RecordsToBeFilledMTM.Count()>0)
            {
                result = true;
                List<int> toGetList = RecordsToBeFilledMTM[0].Item3;
                OpenMainRead();
                object rec = RecordsToBeFilledMTM[0].Item1;
                IList a = rec.GetType().GetProperty(RecordsToBeFilledMTM[0].Item2.Name).GetValue(rec) as IList;
                if (a == null)
                {
                    Type d1 = typeof(List<>);
                    Type[] typeArgs = { this.GetType().GetGenericArguments()[0] };
                    Type constructed = d1.MakeGenericType(typeArgs);
                    a = (IList)Activator.CreateInstance(constructed);
                }
                foreach (int toGet in toGetList)
                {
                    T toAdd = Read(toGet, true);
                    a.Add(toAdd);
                }
                mainFile.Close();
            }
            return result;
        }

        /// <summary>
        /// Adds the mtm list 
        /// </summary>
        /// <param name="rec"></param>
        public void WriteCompleteMTM(object rec)
        {
            foreach (Tuple<String, PropertyInfo, Type> tuple in ManyToMany)
            {
                object list = rec.GetType().GetProperty(tuple.Item2.Name).GetValue(rec);
                object opposite_dbset = ctx.GetDBSetByType(tuple.Item3);
                foreach (object child_mtm in list as IEnumerable)
                {
                    if (!ctx.MTMToWrite.Any(e => e.Key == tuple.Item1))
                    {
                        ctx.MTMToWrite.Add(tuple.Item1, new List<Tuple<object, object>>());
                    }
                    object[] parameters = new object[1];
                    parameters[0] = child_mtm;
                    if((int)child_mtm.GetType().GetProperty("id").GetValue(child_mtm) == 0)
                    {
                        opposite_dbset.GetType().GetMethod("Add").Invoke(opposite_dbset, parameters); //id kontrol et
                    }

                    int id_column = WhoAmI(tuple.Item1);
                    if (!ctx.MTMToWrite.Any(e => e.Key == tuple.Item1))
                    {
                        ctx.MTMToWrite.Add(tuple.Item1, new List<Tuple<object, object>>());
                    }
                    KeyValuePair<String, List<Tuple<object, object>>> kp
                        = ctx.MTMToWrite.FirstOrDefault(e => e.Key == tuple.Item1);

                    if (id_column == 1)
                    {
                        kp.Value.Add(new Tuple<object, object>(rec, child_mtm));
                    }
                    if (id_column == 2)
                    {
                        kp.Value.Add(new Tuple<object, object>(child_mtm, rec));
                    }
                }
            }
        }


        #endregion

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
