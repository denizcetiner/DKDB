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
        public static String filler = "/()=";

        #region reading operations

        /// <summary>
        /// Used for creating ordered property list. And maybe(?) for altering tables. Called from related DbSet
        /// </summary>
        /// <param name="metaFile"></param>
        /// <returns></returns>
        public static List<Tuple<String, String>> ReadMetaFilePropertiesAndNames(Stream metaFile)
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
        public static object ReadSingleProperty(BinaryReader br, List<PropertyInfo> primitiveInfos, List<PropertyInfo> customInfos, PropertyInfo info)
        {
            Type t = info.PropertyType;
            if (customInfos.Contains(info))
            {
                return br.ReadInt32();
            }
            else if (t == typeof(String))
            {
                DKDBCustomAttributes.DKDBMaxLengthAttribute lengthAttr
                    = (DKDBCustomAttributes.DKDBMaxLengthAttribute)
                    DKDBCustomAttributes.GetAttribute(typeof(DKDBCustomAttributes.DKDBMaxLengthAttribute), info);

                return RemoveFiller(new string(br.ReadChars(lengthAttr.MaxLength + filler.Length)), filler);
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
        public static Tuple<object, Dictionary<PropertyInfo, int>> ReadSingleRecord(Stream mainFile, int id, Type t,
            List<PropertyInfo> primitiveInfos, List<PropertyInfo> customInfos, List<PropertyInfo> infos)
        {
            Dictionary<PropertyInfo, int> childObjects = new Dictionary<PropertyInfo, int>();

            object record = Activator.CreateInstance(t);
            BinaryReader br = new BinaryReader(mainFile);
            foreach (PropertyInfo info in infos)
            {
                if (customInfos.Contains(info))
                {
                    childObjects.Add(info, (int)ReadSingleProperty(br, primitiveInfos, customInfos, info));
                }
                else
                {
                    info.SetValue(record, ReadSingleProperty(br, primitiveInfos, customInfos, info));
                }
            }
            Tuple<object, Dictionary<PropertyInfo, int>> fillingLog
                = new Tuple<object, Dictionary<PropertyInfo, int>>(record, childObjects);
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
        public static int Add(Stream mainFile, List<int> removedIndexes, List<PropertyInfo> customInfos, List<PropertyInfo> primitiveInfos, List<PropertyInfo> orderedInfos, object record)
        {
            int indexToBeInserted = -1;
            if (removedIndexes.Count() > 0)
            {
                indexToBeInserted = removedIndexes[0];
                Overwrite(mainFile, customInfos, primitiveInfos, orderedInfos, record);
                return indexToBeInserted;
            }
            int sizeOfRow = CalculateRowByteSize(orderedInfos, customInfos, primitiveInfos); //byte size of a row
            mainFile.Position = (mainFile.Length / sizeOfRow) * ((indexToBeInserted == -1) ? sizeOfRow : indexToBeInserted); //
            indexToBeInserted = Convert.ToInt32(mainFile.Position / sizeOfRow) + 1;
            BinaryWriter bw = new BinaryWriter(mainFile);
            foreach (PropertyInfo info in orderedInfos)
            {
                if (primitiveInfos.Contains(info))
                    if (info.Name == "id")
                    {
                        WriteSingleProperty(bw, info, indexToBeInserted);
                    }
                    else
                    {
                        WriteSingleProperty(bw, info, info.GetValue(record));
                    }
                if (customInfos.Contains(info))
                {
                    int fk_id;
                    object child = info.GetValue(record);
                    if (child == null)
                    {
                        fk_id = -1;
                    }
                    else
                    {
                        fk_id = (int)info.GetValue(record).GetType().GetProperty("id").GetValue(info.GetValue(record));
                    }

                    bw.Write(fk_id);
                }
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
                bw.Write(FillString((String)value, filler, DKDBCustomAttributes.GetLength(info)).ToCharArray());
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

        /// <summary>
        /// Overwrites a record. Used to update records. (or set the removed flag)
        /// </summary>
        /// <param name="mainFile">File stream of the dbset.</param>
        /// <param name="customInfos">To compare.</param>
        /// <param name="primitiveInfos">To compare.</param>
        /// <param name="orderedInfos">The infos in the order of being written to the file.</param>
        /// <param name="record">Record to be overwritten.</param>
        public static void Overwrite(Stream mainFile, List<PropertyInfo> customInfos, List<PropertyInfo> primitiveInfos, List<PropertyInfo> orderedInfos, object record)
        {
            int indexToBeInserted = (int)record.GetType().GetProperty("id").GetValue(record);
            int sizeOfRow = CalculateRowByteSize(orderedInfos, customInfos, primitiveInfos);
            mainFile.Position = sizeOfRow * indexToBeInserted; //gerekirse düzelt
            BinaryWriter bw = new BinaryWriter(mainFile);
            foreach (PropertyInfo info in orderedInfos)
            {
                if (primitiveInfos.Contains(info))
                {
                    WriteSingleProperty(bw, info, info.GetValue(record));
                }
                else
                {
                    int id = (int)info.GetValue(record).GetType().GetProperty("id").GetValue(info.GetValue(record));
                    bw.Write(id);
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
        public static void Remove(Stream mainFile, List<PropertyInfo> customInfos, List<PropertyInfo> primitiveInfos, List<PropertyInfo> orderedInfos, Stream metaFile, object record)
        {
            record.GetType().GetProperty("removedFlag").SetValue(record, true);
            Overwrite(mainFile, customInfos, primitiveInfos, orderedInfos, record);
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
            total += sizeof(int); //id
            foreach (PropertyInfo info in infos)
            {
                if (customInfos.Contains(info))
                {
                    total += sizeof(int);
                }
                else if (info.PropertyType == typeof(String))
                {
                    total += DKDBCustomAttributes.GetLength(info) + filler.Length;
                }
                else
                {
                    total += System.Runtime.InteropServices.Marshal.SizeOf(info.PropertyType);
                }
            }
            total += sizeof(bool); //removed flag
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

