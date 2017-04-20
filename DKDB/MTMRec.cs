using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DKDB
{
    public class MTMRec
    {
        public int id { get; set; }
        public int id_1 { get; set; }
        public int id_2 { get; set; }

        static List<PropertyInfo> primitiveInfos = new List<PropertyInfo>();
        static List<PropertyInfo> customInfos = new List<PropertyInfo>();
        static List<PropertyInfo> orderedInfos = new List<PropertyInfo>(); //order in the file
        static List<PropertyInfo> OneToMany_One = new List<PropertyInfo>();
        public static Tuple<List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, int> piContainer;

        public static void initContainer()
        {
            primitiveInfos = new List<PropertyInfo>();
            foreach(PropertyInfo info in typeof(MTMRec).GetProperties())
            {
                primitiveInfos.Add(info);
            }
            piContainer = new Tuple<List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, List<PropertyInfo>, int>(
                primitiveInfos, customInfos, orderedInfos, OneToMany_One, 12);
        }

        public MTMRec(int id_1, int id_2)
        {
            initContainer();
        }

    }
}
