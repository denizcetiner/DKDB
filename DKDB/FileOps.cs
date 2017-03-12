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
        /// <summary>
        /// 
        /// </summary>
        /// <param name="mainFile">Stream of the file, that holds the data for the DbSet of the record</param>
        /// <param name="removedIndexes">Empty index numbers in the file, can be filled with new records</param>
        /// <param name="infos">Properties that will be stored in database. (To exclude notmapped properties)</param>
        /// <param name="record">Record to be inserted</param>
        /// <returns>Returns the id of last inserted record</returns>
        public static int Add(Stream mainFile, List<int> removedIndexes, List<PropertyInfo> customInfos, List<PropertyInfo> primitiveInfos, object record)
        {
            int indexToBeInserted = -1; //hesapla.
            int sizeOfRow = -1; //hesapla
            mainFile.Position = sizeOfRow * indexToBeInserted; //gerekirse düzelt
            BinaryWriter bw = new BinaryWriter(mainFile);
            foreach(PropertyInfo primitiveInfo in primitiveInfos)
            {
                Write(bw, primitiveInfo, primitiveInfo.GetValue(record));
            }
            foreach(PropertyInfo customInfo in customInfos)
            {
                int id = (int)customInfo.GetValue(record).GetType().GetProperty("id").GetValue(customInfo.GetValue(record));
                bw.Write(id);
            }

            return indexToBeInserted;//id dönecek
        }

        public static void Write(BinaryWriter bw, PropertyInfo info, object o)
        {
            object value = info.GetValue(o);
            Type t = info.PropertyType;
            if(t == typeof(String))
            {
                bw.Write((String)value);
            }
            else if(t == typeof(bool))
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
        }

        public static void Overwrite(Stream mainFile, List<PropertyInfo> customInfos, List<PropertyInfo> primitiveInfos, object record)
        {
            int indexToBeInserted = (int)o.GetType().GetProperty("id").GetValue(record); //hesapla.
            int sizeOfRow = -1; //hesapla
            mainFile.Position = sizeOfRow * indexToBeInserted; //gerekirse düzelt
            BinaryWriter bw = new BinaryWriter(mainFile);
            foreach (PropertyInfo primitiveInfo in primitiveInfos)
            {
                Write(bw, primitiveInfo, primitiveInfo.GetValue(record));
            }
            foreach (PropertyInfo customInfo in customInfos)
            {
                int id = (int)customInfo.GetValue(record).GetType().GetProperty("id").GetValue(customInfo.GetValue(record));
                bw.Write(id);
            }
        }

        public static void Remove(Stream mainFile, Stream metaFile, object o)
        {

        }



        public static List<T> ReadSingle<T>(Stream )
        {
            List<T> records = new List<T>();



            return records;
        }
    }
}
