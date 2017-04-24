using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DKDB
{
    public static class FileOps
    {
        public static String delimiter = "/()=";
        

        #region reading operations

        /// <summary>
        /// Used for creating ordered property list. And maybe(?) for altering tables. Called from related DbSet
        /// </summary>
        /// <param name="metaFile"></param>
        /// <returns></returns>
        public static List<Tuple<String, String>> ReadMetaPropsAndNames(Stream metaFile)
        {
            metaFile.Position = 0;
            List<Tuple<String, String>> propsAndNames = new List<Tuple<string, string>>();
            BinaryReader br = new BinaryReader(metaFile);
            String s = br.ReadString();
            while (s != "Removed indexes:")
            {
                String prop = String.Copy(s);
                String name = br.ReadString();
                Tuple<String, String> propAndName = new Tuple<string, string>(prop, name);
                propsAndNames.Add(propAndName);
                s = br.ReadString();
            }
            return propsAndNames;
        }

        /// <summary>
        /// Reads a single property from the file. Used in readsinglerecord method.
        /// </summary>
        /// <param name="br">BinaryReader that has been created before.</param>
        /// <param name="primitiveInfos">To check where the current info belongs</param>
        /// <param name="customInfos">To check where the current info belongs</param>
        /// <param name="info">İnfo to be read</param>
        /// <returns>Returns the read value in object form</returns>
        public static object ReadSingleProperty(BinaryReader br, Type ty, PropertyInfo info)
        {
            BaseClass trash = (BaseClass)Activator.CreateInstance(ty);
            
            Type t = info.PropertyType;
            if (trash.OTORelInfos().Contains(info))
            {
                return br.ReadInt32();
            }
            else if (t == typeof(String))
            {
                CustomAttr.MaxLengthAttr lengthAttr
                    = (CustomAttr.MaxLengthAttr)
                    CustomAttr.GetAttribute(typeof(CustomAttr.MaxLengthAttr), info);

                return RemoveFiller(new string(br.ReadChars(lengthAttr.MaxLength + delimiter.Length)), delimiter);
            }
            else if (t == typeof(bool))
            {
                return br.ReadBoolean();
            }
            else if (t == typeof(int))
            {
                return br.ReadInt32();
            }
            else if (t == typeof(DateTime))
            {
                return DateTime.Parse(new string(br.ReadChars(10)));
            }
            return null; //exception
        }

        public static List<int> ReadMetaFileRemovedIndexes(Stream metaFile)
        {
            metaFile.Position = 0;
            List<int> removedIndexes = new List<int>();
            BinaryReader br = new BinaryReader(metaFile);
            String s = br.ReadString();
            while (s != "Removed indexes:")
            {
                s = br.ReadString();
            }
            while (br.BaseStream.Position != br.BaseStream.Length)
            {
                removedIndexes.Add(br.ReadInt32());
            }
            return removedIndexes;
        }


        /// <summary>
        /// Reads a single record from the file. Uses ReadSingleProperty method.
        /// </summary>
        /// <param name="mainFile">File stream of the dbset of the record</param>
        /// <param name="id">Id of the record to be read</param>
        /// <param name="t">Type of the record</param>
        /// <param name="primitiveInfos"></param>
        /// <param name="customInfos"></param>
        /// <param name="infos">Ordered info list to be read from the file.</param>
        /// <returns>Returns the record, and list of the childs to be read if exists.</returns>
        public static Tuple<BaseClass, Dictionary<PropertyInfo, int>> ReadSingleRecord(Stream mainFile, int id, Type t)
        {
            BaseClass trash = (BaseClass)Activator.CreateInstance(t);

            Dictionary<PropertyInfo, int> childObjects = new Dictionary<PropertyInfo, int>();

            BaseClass record = (BaseClass)Activator.CreateInstance(t);
            BinaryReader br = new BinaryReader(mainFile);

            br.BaseStream.Position = trash.RowSize() * (id - 1);


            foreach (PropertyInfo info in trash.OrderedInfos())
            {
                if (trash.OTORelInfos().Contains(info))
                {
                    childObjects.Add(info, (int)ReadSingleProperty(br, t, info));
                }
                else
                {
                    info.SetValue(record, ReadSingleProperty(br, t, info));
                }
            }
            Tuple<BaseClass, Dictionary<PropertyInfo, int>> fillingLog
                = new Tuple<BaseClass, Dictionary<PropertyInfo, int>>(record, childObjects);
            //^object=record, Dictionary=property,fkid
            return fillingLog;
        }


        #endregion

        #region writing operations

        /// <summary>
        /// Inserts a record for the first time. Returns the id of last inserted record
        /// </summary>
        /// <param name="mainFile">Stream of the file, that holds the data for the DbSet of the record</param>
        /// <param name="removedIndexes">Empty index numbers in the file, can be filled with new records</param>
        /// <param name="infos">Properties that will be stored in database. (To exclude notmapped properties)</param>
        /// <param name="record">Record to be inserted</param>
        /// <returns>Returns the id of last inserted record</returns>
        public static int Add(Stream mainFile, List<int> removedIndexes, BaseClass record)
        {

            int indexToBeInserted = -1;
            if (removedIndexes.Count() > 0)
            {
                indexToBeInserted = removedIndexes[0];
                Overwrite(mainFile, record);
                return indexToBeInserted;
            }
            mainFile.Position = (mainFile.Length / record.RowSize()) * ((indexToBeInserted == -1) ? record.RowSize() : indexToBeInserted); //
            indexToBeInserted = Convert.ToInt32(mainFile.Position / record.RowSize()) + 1; //idler 1den başlasın
            BinaryWriter bw = new BinaryWriter(mainFile);
            foreach (PropertyInfo info in record.OrderedInfos())
            {
                if (record.PrimitiveInfos().Contains(info))
                {
                    if (info.Name == "id")
                    {
                        WriteSingleProperty(bw, info, indexToBeInserted);
                    }
                    else
                    {
                        WriteSingleProperty(bw, info, info.GetValue(record));
                    }
                }
                else if (record.OTORelInfos().Contains(info))
                {
                    BaseClass child = (BaseClass)info.GetValue(record);
                    if (child == null)
                    {
                        bw.Write(-1);
                    }
                    else
                    {
                        bw.Write(child.id);
                    }

                    
                }
                //one to many ise burada iş yok, dbset'te request yapılacak.
            }


            return indexToBeInserted;//id dönecek
        }


        /// <summary>
        /// Writes a single property to the file. Used in Add method, iterating through all properties.
        /// </summary>
        /// <param name="bw">BinaryWriter that has been created before</param>
        /// <param name="info">İnfo to be written</param>
        /// <param name="o">The object the info belongs to.</param>
        public static void WriteSingleProperty(BinaryWriter bw, PropertyInfo info, object o)
        {
            object value = o;
            Type t = info.PropertyType;
            if (t == typeof(String))
            {
                bw.Write(FillString((String)value, delimiter, CustomAttr.GetLength(info)).ToCharArray());
            }
            else if (t == typeof(bool))
            {
                bw.Write((bool)value);
            }
            else if (t == typeof(int))
            {
                bw.Write((int)value);
            }
            else if (t == typeof(DateTime))
            {
                char[] charArray = ((DateTime)value).ToString().ToCharArray();
                bw.Write(charArray);
            }
            else if (t == typeof(long))
            {
                bw.Write((long)value);
            }
            else if (t == typeof(byte[]))
            {
                bw.Write((byte[])value);
            }
            //else if custom info
        }

        public static void MakeReferenceNull(Stream mainFile, PropertyInfo info, Type T, Dictionary<int,List<int>> toChange)
        {
            //ToChange:
            //key = silinecek id
            //list int = key'e eskiden referans eden kayıtlar

            BaseClass trash = (BaseClass)Activator.CreateInstance(T);

            int total = 0;
            foreach(PropertyInfo orderedInfo in trash.OrderedInfos()) //sütun kaçıncı pozisyonda bul
            {
                if(orderedInfo.Name.Equals(info.Name)) //yazacağımız sütunu bulduk.
                {
                    break;
                }
                else
                {
                    if (trash.OTORelInfos().Contains(orderedInfo))
                    {
                        total += sizeof(int); //foreign key
                    }
                    else if (orderedInfo.PropertyType == typeof(Boolean))
                    {
                        total += 1; //marshal 4 döndürüyor, yanlışsın marshal.
                                    //dosyaya yazarken booleanlar 1 yer kaplıyor.
                    }
                    else if (orderedInfo.PropertyType == typeof(String))
                    {
                        total += CustomAttr.GetLength(orderedInfo) + delimiter.Length;
                    }
                    else
                    {
                        total += System.Runtime.InteropServices.Marshal.SizeOf(orderedInfo.PropertyType);
                    }
                }
            }
            BinaryWriter bw = new BinaryWriter(mainFile);
            foreach(KeyValuePair<int, List<int>> kp in toChange)
            {
                foreach(int id in kp.Value)
                {
                    bw.BaseStream.Position = (id - 1) * trash.RowSize() + total;
                    bw.Write(-1);
                }
            }
        }

        /// <summary>
        /// Overwrites a record. Used to update records. (or set the removed flag)
        /// </summary>
        /// <param name="mainFile">File stream of the dbset.</param>
        /// <param name="customInfos">To compare.</param>
        /// <param name="primitiveInfos">To compare.</param>
        /// <param name="orderedInfos">The infos in the order of being written to the file.</param>
        /// <param name="record">Record to be overwritten.</param>
        public static void Overwrite(Stream mainFile, BaseClass record)
        {

            int indexToBeInserted = record.id;
            mainFile.Position = record.RowSize() * (indexToBeInserted-1); //gerekirse düzelt
            BinaryWriter bw = new BinaryWriter(mainFile);
            foreach (PropertyInfo info in record.OrderedInfos())
            {
                if (record.PrimitiveInfos().Contains(info))
                {
                    WriteSingleProperty(bw, info, info.GetValue(record));
                }
                else if(info.PropertyType.IsGenericType)
                {
                    continue;
                }
                else
                {
                    BaseClass child = (BaseClass)info.GetValue(record);
                    
                    if (child == null)
                    {
                        bw.Write(-1);
                    }
                    else
                    {
                        bw.Write(child.id);
                    }
                }
            }
        }

        /// <summary>
        /// Sets the removeFlag of the record to true in the file.
        /// </summary>
        /// <param name="mainFile">File stream of the dbset of the record.</param>
        /// <param name="customInfos"></param>
        /// <param name="primitiveInfos"></param>
        /// <param name="orderedInfos"></param>
        /// <param name="metaFile">MetaFile stream of the dbset. To update the removed indexes. (Burada mı yapılacak? düşün.)</param>
        /// <param name="record"></param>
        public static void Remove(Stream mainFile, Stream metaFile, BaseClass record)
        {
            record.GetType().GetProperty("removed").SetValue(record, true);
            Overwrite(mainFile, record);
            //removed indexes ile ilgili iş nerede yapılacak?
        }


        public static void CreateMetaFile(Stream metaFile, Type t)
        {
            BinaryWriter bw = new BinaryWriter(metaFile);
            foreach (PropertyInfo info in t.GetProperties())
            {
                bw.Write(info.PropertyType.ToString());
                bw.Write(info.Name);
            }
            bw.Write("Removed indexes:");
        }

        
        public static void UpdateRemovedIndexes(Stream metaFile, List<int> removedIndexes)
        {

        }

        #endregion

        #region helper methods

        public static int CalculateRowByteSize(List<PropertyInfo> infos, List<PropertyInfo> customInfos, List<PropertyInfo> primitiveInfos)
        {
            int total = 0;
            foreach (PropertyInfo info in infos)
            {
                if (customInfos.Contains(info))
                {
                    total += sizeof(int); //foreign key
                }
                else if (info.PropertyType == typeof(Boolean))
                {
                    total += 1; //marshal 4 döndürüyor, yanlışsın marshal.
                    //dosyaya yazarken booleanlar 1 yer kaplıyor.
                }
                else if (info.PropertyType == typeof(String))
                {
                    total += CustomAttr.GetLength(info) + delimiter.Length;
                }
                else if(info.PropertyType.IsGenericType)
                {
                    continue;
                }
                else
                {
                    total += System.Runtime.InteropServices.Marshal.SizeOf(info.PropertyType);
                }
            }
            return total;
        }

        public static String RemoveFiller(String filled, String filler)
        {
            return filled.Split(filler.ToCharArray())[0];
        }

        public static String FillString(String original, String filler, int OriginalLengthLimit)
        {
            String result = original + filler;
            while (result.Length < OriginalLengthLimit + filler.Length)
            {
                result += "0";
            }
            return result;
        }

        #endregion



    }
}

