using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DKDB
{
    public class OTOReq
    {
        public object recordToBeFilled { get; }
        public PropertyInfo OwnOTOProp { get; }
        public int target_id { get; }

        public OTOReq(object recordToBeFilled, PropertyInfo OwnOTOProp, int target_id)
        {
            this.recordToBeFilled = recordToBeFilled;
            this.OwnOTOProp = OwnOTOProp;
            this.target_id = target_id;
        }

        public void Fill(object target)
        {
            recordToBeFilled.GetType().GetProperty(OwnOTOProp.Name).SetValue(recordToBeFilled,target);
        }
    }

    public class MTMReq
    {
        public object OwnRecord { get; }
        public PropertyInfo OwnMTMProp { get; }
        public List<int> idOfRecordsToAssign { get; }
        public IList OwnMTMList { get; }

        public MTMReq(object OwnRecord, PropertyInfo OwnMTMProp)
        {
            this.OwnRecord = OwnRecord;
            this.OwnMTMProp = OwnMTMProp;
            this.idOfRecordsToAssign = new List<int>();
            this.OwnMTMList = GetOwnList();
        }

        public void AddOppId(int oppId)
        {
            idOfRecordsToAssign.Add(oppId);
        }

        public MTMReq(object OwnRecord, PropertyInfo OwnMTMProp, List<int> idOfRecordsToAssign)
        {
            this.OwnRecord = OwnRecord;
            this.OwnMTMProp = OwnMTMProp;
            this.idOfRecordsToAssign = idOfRecordsToAssign;
            this.OwnMTMList = GetOwnList();
        }

        private IList GetOwnList()
        {
            return this.OwnRecord.GetType().GetProperty(OwnMTMProp.Name).GetValue(this.OwnRecord) as IList;
        }

        public void Fill(object OppRecord)
        {
            OwnMTMList.Add(OppRecord);
        }

    }

    public class MTMRelationInfo
    {
        public String tableName { get; }
        public PropertyInfo OwnMTMProp { get; }

        //opposite type
        public Type OppType { get; set; }

        //opposite mtm property (list)
        public PropertyInfo OppMTMProp { get; }

        public MTMRelationInfo(String tableName, PropertyInfo OwnMTMProp)
        {
            this.tableName = tableName;
            this.OwnMTMProp = OwnMTMProp;
            this.OppType = OwnMTMProp.PropertyType.GetGenericArguments()[0];
            this.OppMTMProp = this.OppType.GetProperty(CustomAttr.GetMTMTargetAndTable(OwnMTMProp).Item1);
        }
    }

    public class DbSet<T>
    {
        #region properties and fields

        //public bool Changed { get; set; } = true;
        public int RowSize;

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

        private List<Tuple<object, object>> recordsToAddAsOTO = new List<Tuple<object, object>>();
        //<owner of the to-be-added record, to-be-added record>
        //explanation: during the SaveChanges operation, this list is iterated.
        //For each _tuple, to-be-added record is inserted into the table file and given an id;
        //and than a message is sent from this DbSet to the owner of the record's DbSet to update the owner of the record.
        //Because both of these objects (owner and the child) are still in the memory,
        //during the OverWrite operation when the "PropertyInfo which is associated with the child record" is checked,
        //the child record will be accessed and its fk_id will be retrieved and overwritten to the file.
        
        private List<Tuple<object, object, PropertyInfo>> recordsToAddAsOTM = new List<Tuple<object, object, PropertyInfo>>();
        //obje1=parent, obje2=child, propertyinfo=child'in foreignkey alanı
        
        private List<T> recordsToRemove = new List<T>();
        //Members of this list are added through the "Remove" method of this DbSet.
        //During the SaveChanges operation; "removed flag"s of the each member are set to "true", and overwritten in the table file.


        public List<T> allRecords = new List<T>();

        

        List<PropertyInfo> primitiveInfos = new List<PropertyInfo>();
        List<PropertyInfo> customInfos = new List<PropertyInfo>();
        List<PropertyInfo> orderedInfos = new List<PropertyInfo>(); //order in the file

        Tuple<List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, int> piContainer;

        private List<OTOReq> OTOReqList = new List<OTOReq>();
        //object=nesne referansı okunup doldurulacak nesne.
        //propertyinfo=doldurulacak property
        //referansın id'si

        private List<Tuple<object, PropertyInfo>> OTMReqList = new List<Tuple<object, PropertyInfo>>();
        //for reading

        public List<MTMReq> MTMReqList { get; set; } = new List<MTMReq>();
        //read

        public List<Type> OTORelations = new List<Type>();
        //to keep one to one relations, currently not used.
        //may be implemented in the future for more proper relational data read and write control.

        List<PropertyInfo> OneToMany_One = new List<PropertyInfo>();
        //PropertyInfos that we are on the "one" side.
        private List<PropertyInfo> OneToMany_Many = new List<PropertyInfo>();
        //PropertyInfos that we are on the "many" side, currently not used.
        //may be implemented in the future for more proper relational data read and write control.

        private List<MTMRelationInfo> MTMInfoList = new List<MTMRelationInfo>();
        //table adları, ctx'ten bakılacak.
        //String : Table name of the MTM relation.
        //PropertyInfo : List property of type T.
        //Type

        


        #endregion


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
                if (info.PropertyType.IsGenericType)
                {
                    if (CustomAttr.GetOTMTarget(info) != null)
                    {
                        OneToMany_One.Add(info);

                    }
                    else
                    {
                        Tuple<string, string> Target_Table = CustomAttr.GetMTMTargetAndTable(info);
                        if (Target_Table != null)
                        {
                            Type targetType = info.PropertyType.GetGenericArguments()[0];
                            Type me = typeof(T);
                            PropertyInfo targetInfo = info.PropertyType.GetGenericArguments()[0].GetProperty(Target_Table.Item1);

                            if (!ctx.MTMRelations.Any(e => e.Key == Target_Table.Item2))
                            {
                                ctx.MTMRelations.Add(
                                Target_Table.Item2, new Tuple<Type, Type>(me, targetType));
                            }
                            this.MTMInfoList.Add(new MTMRelationInfo(Target_Table.Item2, info));
                            
                        }
                    }
                }
                else if (!checklist.Contains(info.PropertyType))
                {
                    primitiveInfos.Add(info);
                }
                else
                {
                    customInfos.Add(info);
                }
            }
            OpenMetaRead();
            #region orderedInfos oluşturumu
            //dosyaya yazılış ve dosyadan okunuş sırası
            List<Tuple<String, String>> PropsAndNames = FileOps.ReadMetaPropsAndNames(metaFile);
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
            #endregion
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
            this.RowSize = FileOps.CalculateRowByteSize(orderedInfos, customInfos, primitiveInfos);
            piContainer = new Tuple<List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, int>(
                this.primitiveInfos, this.customInfos, this.orderedInfos, OneToMany_One, RowSize);
            
        }

        #endregion


        public void Remove(T record)
        {
            recordsToRemove.Add(record);
        }


        public void CheckInside(T record, String mode)
        {
            foreach (PropertyInfo info in record.GetType().GetProperties())
            {
                bool contains = false;
                foreach (var a in ctx.dbsetTypes)
                {
                    if (a.Name.Equals(info.PropertyType.Name))
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
                //null ise -1 ya da 0 falan bas 
            }
            //one to many
            foreach (PropertyInfo OTM in OneToMany_One)
            {
                IList list = record.GetType().GetProperty(OTM.Name).GetValue(record) as IList;
                object dbset = ctx.GetDBSetByType(OTM.PropertyType.GetGenericArguments()[0]);
                foreach (var eleman in list)
                {

                    if ((int)eleman.GetType().GetProperty("id").GetValue(eleman) != 0)
                    {
                        if (eleman.GetType().GetProperty(CustomAttr.GetOTMTarget(OTM)).GetValue(eleman) != (object)record)
                        {
                            eleman.GetType().GetProperty(CustomAttr.GetOTMTarget(OTM)).SetValue(eleman, record);
                            object[] parameters_update = { eleman };
                            object updates = dbset.GetType().GetField("Updates").GetValue(dbset); //içindeki tekrar eklenmesin diye.
                            updates.GetType().GetMethod("Add").Invoke(updates, parameters_update);
                        }
                        continue;
                    }
                    object[] parameters = new object[3];
                    parameters[0] = record;
                    parameters[1] = eleman;
                    parameters[2] = eleman.GetType().GetProperty(CustomAttr.GetOTMTarget(OTM));
                    dbset.GetType().GetMethod("AddAsOTM").Invoke(dbset, parameters);
                }
            }


            //many to many

            //WriteCompleteMTM(record);
        }


        /// <summary>
        /// Adds given record to the buffer.
        /// </summary>
        /// <param name="record">Record to add.</param>
        public void Add(T record)
        {
            if (CustomAttr.Validator(record)) //Checks the attributes
            {
                CheckInside(record, "Add");
                ProcessMTM(record);
            }
            if (allRecords.Contains(record)) return;
            recordsToAddDirectly.Add(record); 
        }

        /// <summary>
        /// Given record will be updated.
        /// </summary>
        /// <param name="record">Record to be analyzed and updated.</param>
        public void Update(T record)
        {
            if (CustomAttr.Validator(record)) //Checks the attributes
            {
                CheckInside(record, "Update");
                ProcessMTM(record);
                this.Updates.Add(record);
            }
        }


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
                CheckInside((T)record, "AddAsChild");
                ProcessMTM((T)record);
            }
            if (allRecords.Contains((T)record)) return;
            recordsToAddAsOTO.Add(new Tuple<object, object>(owner, record));
        }

        public void AddAsOTM(object owner, object child, PropertyInfo info)
        {
            this.recordsToAddAsOTM.Add(new Tuple<object, object, PropertyInfo>(owner, child, info));
        }

        //savechanges çalışırken id'si int olan nesne okunacak, object'in property'sine atanacak.
        public void AddOTOReq(OTOReq otoReq)
        {
            this.OTOReqList.Add(otoReq);
        }

        /// <summary>
        /// For call by reflection from ctx. Adds an OTM (one to many) request to the requests list.
        /// </summary>
        /// <param name="req"></param>
        public void AddOTMReq(Tuple<object, PropertyInfo> req)
        {
            OTMReqList.Add(req);
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
                        .SetValue(rec.Item2, rec.Item1);
                    int fk = FileOps.Add(mainFile, removedIndexes, piContainer, rec.Item2);
                    rec.Item2.GetType().GetProperty(rec.Item3.Name).SetValue(rec.Item2, fk);
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
                    allRecords.Add(recordsToAddDirectly[0]);
                    recordsToAddDirectly.RemoveAt(0);
                }
                mainFile.Close();
            }
            if (command == "AddChilds")
            {
                OpenMainWrite();
                while (recordsToAddAsOTO.Count() != 0)
                {
                    result = true;
                    //item1=parent, item2=child
                    T record = (T)recordsToAddAsOTO[0].Item2;
                    int fk = FileOps.Add(mainFile, removedIndexes, piContainer, record);
                    record.GetType().GetProperty("id").SetValue(record, fk);
                    Type ownerType = recordsToAddAsOTO[0].Item1.GetType();
                    Type ownerDbSetType = ctx.dbsetTypes.Where(a => a.ToString().Equals(ownerType.ToString())).ElementAt(0);
                    object ownerDbSet = ctx.GetDBSetByType(ownerDbSetType);
                    object[] parameters = new object[1];
                    parameters[0] = recordsToAddAsOTO[0].Item1;
                    //Explained under recordsToAddAsOTO declaration.
                    ownerDbSet.GetType().GetMethod("Update").Invoke(ownerDbSet, parameters);
                    allRecords.Add(record);
                    recordsToAddAsOTO.RemoveAt(0);
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
                    recordsToRemove[0].GetType().GetProperty("removed").SetValue(recordsToRemove[0], true);
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
                    List<PropertyInfo> props = this.GetType().GetGenericArguments()[0].GetProperties().ToList();
                    foreach (PropertyInfo info in props)
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
                foreach(T rec in allRecords)
                {
                    foreach(PropertyInfo column in toChangeColumns)
                    {
                        foreach(KeyValuePair<int, List<int>> kp in toChange)
                        {
                            object child_rec = rec.GetType().GetProperty(column.Name).GetValue(rec);
                            if ((int)child_rec.GetType().GetProperty("id").GetValue(child_rec) == kp.Key)
                            {
                                int id = (int)rec.GetType().GetProperty("id").GetValue(rec);
                                rec.GetType().GetProperty(column.Name).SetValue(rec, null);
                                kp.Value.Add(id);
                            }
                        }
                    }
                }
                foreach(PropertyInfo column in toChangeColumns)
                {
                    OpenMainWrite();
                    FileOps.MakeReferenceNull(mainFile, piContainer, column, toChange);
                    mainFile.Close();
                }
            }

            return result;
        }

        

        public void ReadAllPlain(bool ReadRemoved = false)
        {
            allRecords = new List<T>();
            OpenMainRead();
            int line = Convert.ToInt32(mainFile.Length) / RowSize;
            for (int id = 0; id < line; id++)
            {
                Tuple<object, Dictionary<PropertyInfo, int>> fillingLog;
                fillingLog = FileOps.ReadSingleRecord(mainFile, id+1, typeof(T), piContainer);
                object rec = fillingLog.Item1;
                if (ReadRemoved || !(bool)rec.GetType().GetProperty("removed").GetValue(rec))
                {
                    allRecords.Add((T)rec);
                    foreach (KeyValuePair<PropertyInfo, int> kp in fillingLog.Item2) //OTOReq'ler
                    {
                        object dbset = ctx.GetDBSetByType(kp.Key.PropertyType);
                        if (kp.Value == -1) //null, okumaya çalışma!
                        {
                            continue;
                        }
                        Tuple<object, PropertyInfo, int> RecordToBeFilled = new Tuple<object, PropertyInfo, int>(fillingLog.Item1, kp.Key, kp.Value);
                        
                        object[] parameters = { new OTOReq(fillingLog.Item1, kp.Key, kp.Value) };
                        dbset.GetType().GetMethod("AddOTOReq").Invoke(dbset, parameters);

                    }

                    foreach (PropertyInfo OTM in OneToMany_One)
                    {
                        if (fillingLog.Item1.GetType().GetProperty(OTM.Name).GetValue(fillingLog.Item1) == null)
                        {
                            fillingLog.Item1.GetType().GetProperty(OTM.Name).SetValue(fillingLog.Item1.GetType().GetProperty(OTM.Name), Activator.CreateInstance(OTM.PropertyType));
                        }
                        object dbset = ctx.GetDBSetByType(OTM.PropertyType.GetGenericArguments()[0]);
                        object[] parameters = { new Tuple<object, PropertyInfo>(fillingLog.Item1, OTM) };
                        dbset.GetType().GetMethod("AddOTMReq").Invoke(dbset, parameters);

                    }

                    //MTM
                    ReadMTM(fillingLog.Item1);
                }
            }
            mainFile.Close();
        }

        public T Get(int id)
        {
            return allRecords.FirstOrDefault(rec => (int)rec.GetType().GetProperty("id").GetValue(rec) == id);
        }

        #region fill

        /// <summary>
        /// Completes the child assignment requests ordered by the parent objects.
        /// </summary>
        /// <returns>Returns value to be used by DbContext wrapper function "FillOthers".</returns>
        public bool FillOTO(bool readRemoved = false)
        {
            bool result = false;
            while (OTOReqList.Count() != 0)
            {
                result = true;
                //doldur
                OTOReq otoReq = OTOReqList[0];
                OTOReqList.RemoveAt(0);
                object found = Get(otoReq.target_id);
                otoReq.Fill(found);
            }
            return result; //returns false if nothing is done, otherwise true.
        }


        /// <summary>
        /// (Read)Fills the 'one' side of OTM relations for each request
        /// </summary>
        public bool FillOTM()
        {
            bool result = false;
            while (OTMReqList.Count() > 0)
            {
                result = true;
                Tuple<object, PropertyInfo> request = OTMReqList[0];
                int id = (int)request.Item1.GetType().GetProperty("id").GetValue(request.Item1);
                String otm_target = CustomAttr.GetOTMTarget(request.Item2);
                List<T> filtered = new List<T>();
                foreach(T record in allRecords)
                {
                    object target = record.GetType().GetProperty(otm_target).GetValue(record);
                    if(target == null) { continue; }
                    int opposite_id = (int)target.GetType().GetProperty("id").GetValue(target);
                    if (opposite_id == id)
                    {
                        IList list = request.Item1.GetType().GetProperty(request.Item2.Name).GetValue(request.Item1) as IList;
                        list.Add(record);
                    }
                }
                OTMReqList.RemoveAt(0);
            }
            return result;
        }
        

        /// <summary>
        /// Read requests for performance
        /// </summary>
        public bool FillMTM()
        {
            bool result = false;
            while (this.MTMReqList.Count() > 0)
            {
                result = true;
                OpenMainRead();
                
                foreach (int toGet in MTMReqList[0].idOfRecordsToAssign)
                {
                    object found = Get(toGet);
                    if (found == null) continue;
                    MTMReqList[0].Fill(found);
                }
                mainFile.Close();
                MTMReqList.RemoveAt(0);
            }
            
            return result;
        }

        #endregion
        
        

        /// <summary>
        /// Reads the middle table of many-to-many relationship. Bad performance currently, because it reads the
        /// middle file everytime a record is read.
        /// </summary>
        /// <param name="rec"></param>
        public void ReadMTM(object rec)
        {
            foreach (MTMRelationInfo mtmInfo in MTMInfoList)
            {
                String filepath = Path.Combine(ctx.DatabaseFolder, mtmInfo.tableName) + ".dat"; //mtm middle tablo yolu
                Stream mtmStream = File.OpenRead(filepath);
                int line = Convert.ToInt32(mtmStream.Length / (sizeof(int) * 3));
                List<MTMRec> mtmRecs = new List<MTMRec>(); //middle tablo kayıtları
                MTMRec.initContainer();
                for (int i = 0; i < line; i++)
                {
                    Tuple<object, Dictionary<PropertyInfo, int>> fillingLog;

                    fillingLog = FileOps.ReadSingleRecord(mtmStream, i + 1, typeof(MTMRec), MTMRec.piContainer);
                    mtmRecs.Add((MTMRec)fillingLog.Item1);
                }
                mtmStream.Close();
                int my_id_column = WhoAmI(mtmInfo.tableName); //MTMRec içinde id1 mi benim sütunum yoksa id2 mi? ctx'teki sıraya göre kontrol edilir
                MTMReq mtmReq = new MTMReq(rec, mtmInfo.OwnMTMProp);

                foreach (MTMRec mtmRec in mtmRecs) //her bir middle tablo kaydı için:
                {
                    if (my_id_column == 1 && mtmRec.id_1 == (int)rec.GetType().GetProperty("id").GetValue(rec))
                    {
                        mtmReq.AddOppId(mtmRec.id_2);//opposite id
                    }
                    else if (mtmRec.id_2 == (int)rec.GetType().GetProperty("id").GetValue(rec))
                    {
                        mtmReq.AddOppId(mtmRec.id_1);//opposite id
                    }
                }

                KeyValuePair<string, Tuple<Type, Type>> kp = ctx.MTMRelations.FirstOrDefault(r => r.Key == mtmInfo.tableName);
                object opposite_dbset = ctx.GetDBSetByType(my_id_column == 1 ? kp.Value.Item2 : kp.Value.Item1);
                object[] parameters = new object[1];

                parameters[0] = mtmReq; //list of ids of the records that we want to read from opposite db
                PropertyInfo fi = opposite_dbset.GetType().GetProperty("MTMReqList");
                List<MTMReq> rtbfMTM = fi.GetValue(opposite_dbset) as List<MTMReq>;
                rtbfMTM.GetType().GetMethod("Add").Invoke(rtbfMTM, parameters);
                
            }
        }

        
        /// <summary>
        /// Adds the mtm list 
        /// </summary>
        /// <param name="rec"></param>
        public void ProcessMTM(object rec)
        {
            foreach (MTMRelationInfo mtmInfo in MTMInfoList)
            {
                object list = rec.GetType().GetProperty(mtmInfo.OwnMTMProp.Name).GetValue(rec);
                object opposite_dbset = ctx.GetDBSetByType(mtmInfo.OppType);
                foreach (object child_mtm in list as IEnumerable)
                {
                    int child_mtm_id = (int)child_mtm.GetType().GetProperty("id").GetValue(child_mtm);
                    if (child_mtm_id == 0)
                    {
                        object[] parameters = new object[1];
                        parameters[0] = child_mtm;
                        opposite_dbset.GetType().GetMethod("Add").Invoke(opposite_dbset, parameters); //id kontrol et
                    }
                    IList child_mtm_target_list = mtmInfo.OppMTMProp.GetValue(child_mtm) as IList;
                    if (child_mtm_target_list.Contains(rec))
                    {
                        continue;
                    }
                    else
                    {
                        child_mtm_target_list.Add(rec);
                    }

                    int id_column = WhoAmI(mtmInfo.tableName);
                    if (!ctx.MTMToWrite.Any(e => e.Key == mtmInfo.tableName))
                    {
                        ctx.MTMToWrite.Add(mtmInfo.tableName, new List<Tuple<object, object>>());
                    }
                    KeyValuePair<String, List<Tuple<object, object>>> kp
                        = ctx.MTMToWrite.FirstOrDefault(e => e.Key == mtmInfo.tableName);

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
        

    }
}