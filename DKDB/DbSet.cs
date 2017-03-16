using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DKDB
{
    public abstract class DbSet<T>
    {
        private List<T> recordsToAdd = new List<T>();
        private List<T> recordsToRemove = new List<T>();
        private List<T> recordsToUpdate = new List<T>();
        private List<T> allRecords = new List<T>();

        #region CRUD

        /// <summary>
        /// Adds given record to the buffer.
        /// </summary>
        /// <param name="record">Record to add</param>
        public void Add(T record)
        {
            recordsToAdd.Add(record);
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

        public void Update(T record)
        {
            recordsToUpdate.Add(record);
        }

        public void UpdateRange(IEnumerable<T> records)
        {

        }

        private void ReadAllRecords()
        {
            if (allRecords.Count == 0)
            {
                
            }
        }

        #endregion

        #region Read

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

        public int SaveChanges()
        {
            return 0;
        }
    }
}
