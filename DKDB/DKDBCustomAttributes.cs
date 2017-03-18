using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DKDB
{
    public class DKDBCustomAttributes
    {
        public static List<PropertyInfo> GetReferencePropertyList(Type T, List<Type> ReferenceChecklist)
        {
            List<PropertyInfo> ReferencePropertyList = new List<PropertyInfo>();

            PropertyInfo[] infos = T.GetProperties();
            foreach (PropertyInfo info in infos)
            {
                if (ReferenceChecklist.Contains(info.GetType()))
                {
                    ReferencePropertyList.Add(info);
                }
            }

            return ReferencePropertyList;
        }

        public static List<Type> GetReferenceChecklist(object DbContext)
        {
            List<Type> RefPropTypes = new List<Type>();

            PropertyInfo[] infos = DbContext.GetType().GetProperties();
            foreach (PropertyInfo info in infos)
            {
                //Console.WriteLine(info.PropertyType.Name.ToString());
                if (info.PropertyType.Name.ToString().Contains("DbSet"))
                {
                    Console.WriteLine(info.PropertyType.GetGenericArguments()[0].Name);
                    RefPropTypes.Add(info.PropertyType.GetGenericArguments()[0]);
                }
            }

            return RefPropTypes;
        }

        public static Attribute GetAttribute(Type t, PropertyInfo info)
        {
            return info.GetCustomAttribute(t);
        }

        public static int GetLength(PropertyInfo info)
        {
            DKDBMaxLengthAttribute attr = (DKDBMaxLengthAttribute)info.GetCustomAttribute(typeof(DKDBMaxLengthAttribute));
            return attr.Length;
        }

        public static bool Validator(object o)
        {
            PropertyInfo[] infos = o.GetType().GetProperties();
            foreach (PropertyInfo info in infos)
            {
                DKDBMaxLengthAttribute attr = (DKDBMaxLengthAttribute)info.GetCustomAttribute(typeof(DKDBMaxLengthAttribute));

                Type t = info.PropertyType;
                if (attr.Length < ((String)(info.GetValue(o))).Length)
                {
                    return false;
                }

            }
            return true;
        }

        /// <summary>
        /// max Length attribute for String properties to be validated by DKDB
        /// </summary>
        [AttributeUsage(AttributeTargets.Property)]
        public class DKDBMaxLengthAttribute : Attribute
        {
            public int Length { get; set; }
        }
    }
}