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
        public BaseClass recordToBeFilled { get; }
        //This record's OTOProp will be filled.
        public PropertyInfo OwnOTOProp { get; }

        public int target_id { get; }
        //id of the record that we will fetch, and assign to the otoprop of recordtobefilled.

        public OTOReq(BaseClass recordToBeFilled, PropertyInfo OwnOTOProp, int target_id)
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
        public BaseClass RequesterRecord { get; }
        public PropertyInfo RequesterMTMProp { get; }
        public List<int> idOfRecordsToAssign { get; }
        public IList RequesterMTMList { get; }

        public MTMReq(BaseClass RequesterRecord, PropertyInfo RequesterMTMProp)
        {
            this.RequesterRecord = RequesterRecord;
            this.RequesterMTMProp = RequesterMTMProp;
            this.idOfRecordsToAssign = new List<int>();
            this.RequesterMTMList = GetOwnList();
        }

        public void AddOppId(int oppId)
        {
            idOfRecordsToAssign.Add(oppId);
        }

        public MTMReq(BaseClass RequesterRecord, PropertyInfo RequesterMTMProp, List<int> idOfRecordsToAssign)
        {
            this.RequesterRecord = RequesterRecord;
            this.RequesterMTMProp = RequesterMTMProp;
            this.idOfRecordsToAssign = idOfRecordsToAssign;
            this.RequesterMTMList = GetOwnList();
        }

        private IList GetOwnList()
        {
            return this.RequesterRecord.GetType().GetProperty(RequesterMTMProp.Name).GetValue(this.RequesterRecord) as IList;
        }

        public void Fill(object OppRecord)
        {
            RequesterMTMList.Add(OppRecord);
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

    public class DbSet<T> where T : BaseClass
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
        
        private List<OTOReq> OTOReqList = new List<OTOReq>();
        //object=nesne referansı okunup doldurulacak nesne.
        //propertyinfo=doldurulacak property
        //referansın id'si

        private List<Tuple<object, PropertyInfo>> OTMReqList = new List<Tuple<object, PropertyInfo>>();
        //for reading

        public List<MTMReq> MTMReqList { get; set; } = new List<MTMReq>();
        //fill requests.
        //fills the list of owner record of mtmreq.
        
        private List<PropertyInfo> OTM_Many = new List<PropertyInfo>();
        //PropertyInfos that we are on the "many" side, currently not used.
        //may be implemented in the future for more proper relational data read and write control.
        
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
            Activator.CreateInstance(typeof(T));
        }

        public int WhoAmI(string table)
        {
            KeyValuePair<string, Tuple<Type, Type>> kp = BaseClass.AllMTMRelations.FirstOrDefault(r => r.Key == table);
            if (typeof(T) == kp.Value.Item1) return 1;
            else return 2;
        }

        public DbSet(DbContext ctx)
        {
            this.ctx = ctx;
            CreateFilesIfNotExist();
            SetInfos();
        }

        #endregion


        public void Remove(T record)
        {
            recordsToRemove.Add(record);
        }
        
        public void CheckInside(BaseClass record, String mode)
        {
            foreach (PropertyInfo info in record.GetType().GetProperties())
            {
                if (ctx.dbsetTypes.Any(t => t.Name == info.PropertyType.Name))
                {
                    BaseClass childObject = (BaseClass)info.GetValue(record);
                    if (childObject != null)
                    {
                        if (childObject.id == 0)
                        {
                            object dbset = ctx.GetDBSetByType(childObject.GetType());
                            object[] parameters = { record, childObject };
                            dbset.GetType().GetMethod("AddAsChild").Invoke(dbset, parameters);
                        }
                    }
                }
            }
            //one to many
            foreach (PropertyInfo OTM in record.OTM_One())
            {
                IList list = OTM.GetValue(record) as IList;
                object dbset = ctx.GetDBSetByType(OTM.PropertyType.GetGenericArguments()[0]);
                foreach (BaseClass eleman in list)
                {

                    if (eleman.id != 0)
                    {
                        PropertyInfo targetProp = eleman.GetType().GetProperty(CustomAttr.GetOTMTarget(OTM));
                        //bizde liste var, karşıda ise tek nesneye referans
                        if (targetProp.GetValue(eleman) != record)
                        {
                            targetProp.SetValue(eleman, record);
                            object[] parameters_update = { eleman };
                            object updates = dbset.GetType().GetField("Updates").GetValue(dbset); //içindeki tekrar eklenmesin diye Update fonksiyonunu pas geçtim.
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
        }

        /// <summary>
        /// Adds given record to the buffer.
        /// </summary>
        /// <param name="record">Record to add.</param>
        public void Add(T record)
        {
            if (CustomAttr.Validator(record)) //Checks the attributes
            {
                CheckInside(record, "Add"); //içteki OTO ve OTM ilişkileri işler.
                ProcessMTM(record); //içteki MTM ilişkileri işler.
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
        public void AddAsChild(BaseClass owner, BaseClass record)
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
                    rec.Item3.SetValue(rec.Item2, rec.Item1);
                    int fk = FileOps.Add(mainFile, removedIndexes, (BaseClass)rec.Item2);
                    rec.Item2.GetType().GetProperty(rec.Item3.Name).SetValue(rec.Item2, fk);
                    result = true;
                }
            }
            else if (command == "AddDirectly")
            {
                //0. elemanlar ile işlem yapma nedeni:
                //Bir ekleme işlemi yapılırken, içinde çocuk nesne var ise, bir şekilde üzerinde çalışılan
                //listenin sonuna yeni kayıt eklenmesine neden olabilir.
                OpenMainWrite();
                while (recordsToAddDirectly.Count() != 0)
                {
                    result = true;
                    int id = FileOps.Add(mainFile, removedIndexes, recordsToAddDirectly[0]);
                    recordsToAddDirectly[0].id = id;
                    allRecords.Add(recordsToAddDirectly[0]);
                    recordsToAddDirectly.RemoveAt(0);
                }
                mainFile.Close();
            }
            else if (command == "AddChilds")
            {
                OpenMainWrite();
                while (recordsToAddAsOTO.Count() != 0)
                {
                    result = true;
                    //item1=parent, item2=child
                    T record = (T)recordsToAddAsOTO[0].Item2;
                    int fk = FileOps.Add(mainFile, removedIndexes, record);
                    record.id = fk;
                    Type ownerType = recordsToAddAsOTO[0].Item1.GetType();
                    Type ownerDbSetType = ctx.dbsetTypes.Where(a => a.ToString().Equals(ownerType.ToString())).ElementAt(0);
                    object ownerDbSet = ctx.GetDBSetByType(ownerDbSetType);
                    object[] parameters = new object[1];
                    parameters[0] = (BaseClass)recordsToAddAsOTO[0].Item1;
                    //Explained under recordsToAddAsOTO declaration.
                    ownerDbSet.GetType().GetMethod("Update").Invoke(ownerDbSet, parameters);
                    allRecords.Add(record);
                    recordsToAddAsOTO.RemoveAt(0);
                }
                mainFile.Close();
            }
            else if (command == "Update")
            {
                OpenMainWrite();
                while (Updates.Count() != 0)
                {
                    result = true;
                    T record = Updates[0];
                    FileOps.Overwrite(mainFile, record);
                    Updates.RemoveAt(0);
                }
                mainFile.Close();
            }
            else if (command == "Remove")
            {
                OpenMainWrite();
                while (recordsToRemove.Count() != 0)
                {
                    result = true;
                    recordsToRemove[0].GetType().GetProperty("removed").SetValue(recordsToRemove[0], true);
                    FileOps.Remove(mainFile, metaFile, recordsToRemove[0]);
                    if(!ctx.removed.Any(kp=>kp.Key == typeof(T)))
                    {
                        ctx.removed.Add(typeof(T), new List<int>());
                    }
                    ctx.removed.FirstOrDefault(kp => kp.Key == typeof(T)).Value
                        .Add((int)recordsToRemove[0].id);
                    recordsToRemove.RemoveAt(0);
                }
                mainFile.Close();
            }
            else if(command =="UpdateAfterRemoval") //silinmiş bir kayda referans olanların foreign keylerini -1 yapacak
            {
                List<PropertyInfo> toChangeColumns = new List<PropertyInfo>();
                Dictionary<int, List<int>> toChange = new Dictionary<int, List<int>>();
                foreach(KeyValuePair<Type, List<int>> kp in ctx.removed)
                {
                    List<PropertyInfo> props = this.GetType().GetGenericArguments()[0].GetProperties().ToList();
                    foreach (PropertyInfo info in props)
                    {
                        if (!info.PropertyType.IsGenericType && info.PropertyType.Name.Equals(kp.Key.Name))
                        {
                            toChangeColumns.Add(info);
                            foreach(int id in kp.Value) //null yapılacak id
                            {
                                toChange.Add(id, new List<int>());
                            }
                        }
                        else if(info.PropertyType.IsGenericType && info.PropertyType.GetGenericArguments()[0].Name == kp.Key.Name)
                        {
                            //remove from mtm
                        }
                    }
                }
                foreach(T rec in allRecords)
                {
                    foreach(PropertyInfo column in toChangeColumns)
                    {
                        foreach(KeyValuePair<int, List<int>> kp in toChange)
                        {
                            BaseClass child_rec = (BaseClass)rec.GetType().GetProperty(column.Name).GetValue(rec);
                            if (child_rec.id == kp.Key)
                            {
                                rec.GetType().GetProperty(column.Name).SetValue(rec, null);
                                kp.Value.Add(rec.id);
                            }
                        }
                    }
                }
                foreach(PropertyInfo column in toChangeColumns)
                {
                    OpenMainWrite();
                    FileOps.MakeReferenceNull(mainFile, column, typeof(T), toChange);
                    mainFile.Close();
                }
            }

            return result;
        }

        public void ReadAll(bool ReadRemoved = false)
        {
            allRecords = new List<T>();
            OpenMainRead();
            BaseClass trash = (BaseClass)Activator.CreateInstance(typeof(T));
            int line = Convert.ToInt32(mainFile.Length) / trash.RowSize();
            for (int id = 0; id < line; id++)
            {
                Tuple<BaseClass, Dictionary<PropertyInfo, int>> fillingLog;
                fillingLog = FileOps.ReadSingleRecord(mainFile, id+1, typeof(T));
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
                    foreach (PropertyInfo OTM in trash.OTM_One())
                    {
                        if (OTM.GetValue(fillingLog.Item1) == null)
                        {
                            OTM.SetValue(OTM.GetValue(fillingLog.Item1), Activator.CreateInstance(OTM.PropertyType));
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
            return allRecords.FirstOrDefault(rec => rec.id == id);
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
                    BaseClass target = (BaseClass)record.GetType().GetProperty(otm_target).GetValue(record);
                    if(target == null) { continue; }
                    if (target.id == id)
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
        public void ReadMTM(BaseClass rec)
        {
            foreach (MTMRelationInfo mtmInfo in rec.MTMInfoList())
            {
                String filepath = Path.Combine(ctx.DatabaseFolder, mtmInfo.tableName) + ".dat"; //mtm middle tablo yolu
                Stream mtmStream = File.OpenRead(filepath);
                int line = Convert.ToInt32(mtmStream.Length / (sizeof(int) * 3));
                List<MTMRec> mtmRecs = new List<MTMRec>(); //middle tablo kayıtları
                for (int i = 0; i < line; i++)
                {
                    Tuple<DKDB.BaseClass, Dictionary<PropertyInfo, int>> fillingLog;

                    fillingLog = FileOps.ReadSingleRecord(mtmStream, i + 1, typeof(MTMRec));
                    mtmRecs.Add((MTMRec)fillingLog.Item1);
                }
                mtmStream.Close();
                int my_id_column = WhoAmI(mtmInfo.tableName); //MTMRec içinde id1 mi benim sütunum yoksa id2 mi? ctx'teki sıraya göre kontrol edilir
                MTMReq mtmReq = new MTMReq(rec, mtmInfo.OwnMTMProp);

                foreach (MTMRec mtmRec in mtmRecs) //her bir middle tablo kaydı için:
                {
                    if (my_id_column == 1 && mtmRec.id_1 == rec.id)
                    {
                        mtmReq.AddOppId(mtmRec.id_2);//opposite id
                    }
                    else if (my_id_column == 2 && mtmRec.id_2 == rec.id)
                    {
                        mtmReq.AddOppId(mtmRec.id_1);//opposite id
                    }
                }

                KeyValuePair<string, Tuple<Type, Type>> kp = BaseClass.AllMTMRelations.FirstOrDefault(r => r.Key == mtmInfo.tableName);
                object opposite_dbset = ctx.GetDBSetByType(my_id_column == 1 ? kp.Value.Item2 : kp.Value.Item1);
                object[] parameters = new object[1];

                parameters[0] = mtmReq; //list of ids of the records that we want to read from opposite db
                PropertyInfo fi = opposite_dbset.GetType().GetProperty("MTMReqList");
                List<MTMReq> rtbfMTM = fi.GetValue(opposite_dbset) as List<MTMReq>;
                rtbfMTM.GetType().GetMethod("Add").Invoke(rtbfMTM, parameters);
                
            }
        }

        
        /// <summary>
        /// Processes the mtm list. Adds if they don't exist in the database. 
        /// </summary>
        /// <param name="rec"></param>
        public void ProcessMTM(BaseClass rec)
        {
            foreach (MTMRelationInfo mtmInfo in rec.MTMInfoList())
            {
                IEnumerable ownMTMList = (IEnumerable)mtmInfo.OwnMTMProp.GetValue(rec);
                object opposite_dbset = ctx.GetDBSetByType(mtmInfo.OppType);
                foreach (BaseClass child_mtm in ownMTMList)
                {
                    if (child_mtm.id == 0)
                    {
                        object[] parameters = new object[1];
                        parameters[0] = child_mtm;
                        opposite_dbset.GetType().GetMethod("Add").Invoke(opposite_dbset, parameters); //id kontrol et
                    }
                    IList child_mtm_target_list = mtmInfo.OppMTMProp.GetValue(child_mtm) as IList;
                    if (child_mtm_target_list.Contains(rec)) //listemdeki elemanın, aynı ilişki listesinde ben var mıyım?
                    { //bunun yerine allrecords'tan knotrol et?
                        continue; //varsam zaten db'dedir kurallara göre.
                    }
                    else
                    {
                        child_mtm_target_list.Add(rec);
                        if (!ctx.MTMToWrite.Any((KeyValuePair<string, List<Tuple<DKDB.BaseClass, DKDB.BaseClass>>> e) => e.Key == mtmInfo.tableName))
                        {
                            ctx.MTMToWrite.Add(mtmInfo.tableName, new List<Tuple<DKDB.BaseClass, DKDB.BaseClass>>());
                        }
                        KeyValuePair<string, List<Tuple<DKDB.BaseClass, DKDB.BaseClass>>> kp
                            = ctx.MTMToWrite.FirstOrDefault((KeyValuePair<string, List<Tuple<DKDB.BaseClass, DKDB.BaseClass>>> e) => e.Key == mtmInfo.tableName);
                        
                        int id_column = WhoAmI(mtmInfo.tableName);
                        if (id_column == 1)
                        {
                            kp.Value.Add(new Tuple<DKDB.BaseClass, DKDB.BaseClass>(rec, child_mtm));
                        }
                        else if (id_column == 2)
                        {
                            kp.Value.Add(new Tuple<DKDB.BaseClass, DKDB.BaseClass>(child_mtm, rec));
                        }
                    }                    
                }
            }
        }
    }
}