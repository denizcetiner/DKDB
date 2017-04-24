using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DKDB
{
    public class BaseClass
    {
        public int id { get; set; }
        public bool removed { get; set; }

        private static Dictionary<String, Type> InheritedTypes = new Dictionary<string, Type>();
        private static Dictionary<String, int> RowSizes = new Dictionary<string, int>();

        private static Dictionary<String, List<PropertyInfo>> AllPrimitiveInfos = new Dictionary<String, List<PropertyInfo>>();
        private static Dictionary<String, List<PropertyInfo>> AllOTORelInfos = new Dictionary<String, List<PropertyInfo>>();
        private static Dictionary<String, List<PropertyInfo>> AllOTM_One = new Dictionary<String, List<PropertyInfo>>();
        private static Dictionary<String, List<MTMRelationInfo>> AllMTMInfoList = new Dictionary<String, List<MTMRelationInfo>>();
        private static Dictionary<String, List<PropertyInfo>> AllOrderedInfos = new Dictionary<string, List<PropertyInfo>>();

        public static Dictionary<String, Tuple<Type, Type>> AllMTMRelations = new Dictionary<string, Tuple<Type, Type>>();

        public int RowSize()
        {
            return RowSizes[this.GetType().Name];
        }

        public List<PropertyInfo> PrimitiveInfos()
        {
            return AllPrimitiveInfos[this.GetType().Name];
        }

        public List<PropertyInfo> OTORelInfos()
        {
            return AllOTORelInfos[this.GetType().Name];
        }

        public List<PropertyInfo> OrderedInfos()
        {
            return AllOrderedInfos[this.GetType().Name];
        }

        public List<PropertyInfo> OTM_One()
        {
            return AllOTM_One[this.GetType().Name];
        }

        public List<MTMRelationInfo> MTMInfoList()
        {
            return AllMTMInfoList[this.GetType().Name];
        }



        public BaseClass()
        {
            String name = this.GetType().Name;
            if (!InheritedTypes.ContainsKey(name))
            {
                InheritedTypes.Add(name, this.GetType());

                AllPrimitiveInfos.Add(name, new List<PropertyInfo>());
                AllOTORelInfos.Add(name, new List<PropertyInfo>());
                AllOTM_One.Add(name, new List<PropertyInfo>());
                AllOrderedInfos.Add(name, new List<PropertyInfo>());
                AllMTMInfoList.Add(name, new List<MTMRelationInfo>());

                List<PropertyInfo> infos = this.GetType().GetProperties().ToList();
                foreach (PropertyInfo info in infos)
                {
                    if (info.PropertyType.IsSubclassOf(typeof(BaseClass)))
                    {
                        AllOTORelInfos[name].Add(info);
                        AllOrderedInfos[name].Add(info);
                    }
                    else if (info.PropertyType.IsGenericType)
                    {
                        if (CustomAttr.GetOTMTarget(info) != null)
                        {
                            AllOTM_One[name].Add(info);

                        }
                        else
                        {
                            Tuple<string, string> Target_Table = CustomAttr.GetMTMTargetAndTable(info);
                            if (Target_Table != null)
                            {
                                Type targetType = info.PropertyType.GetGenericArguments()[0];
                                PropertyInfo targetInfo = info.PropertyType.GetGenericArguments()[0].GetProperty(Target_Table.Item1);

                                if (!AllMTMRelations.Any(e => e.Key == Target_Table.Item2)) //tablo adına göre kontrol ediyor
                                {
                                    AllMTMRelations.Add(
                                    Target_Table.Item2, new Tuple<Type, Type>(this.GetType(), targetType)
                                    );
                                }
                                AllMTMInfoList[name].Add(new MTMRelationInfo(Target_Table.Item2, info));

                            }
                        }
                    }
                    else
                    {
                        AllPrimitiveInfos[name].Add(info);
                        AllOrderedInfos[name].Add(info);
                    }
                }
                RowSizes.Add(name, FileOps.CalculateRowByteSize(OrderedInfos(), OTORelInfos(), PrimitiveInfos()));
            }
        }
    }
}
