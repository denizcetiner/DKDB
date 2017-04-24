using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DKDB
{
    /// <summary>
    /// Model of the middle table records of Many-To-Many relations.
    /// </summary>
    public class MTMRec : BaseClass
    {
        public int id_1 { get; set; }
        public int id_2 { get; set; }


        public MTMRec(int id_1, int id_2)
        {
            this.id_1 = id_1;
            this.id_2 = id_2;
        }

        public MTMRec()
        {

        }

    }
}
