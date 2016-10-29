﻿using System.Collections.Generic;
using System.Data.SqlClient;

namespace SqlBulkTools
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class UpsertQueryObject<T>
    {
        private readonly T _singleEntity;
        private List<string> _concatTrans;
        public string _databaseIdentifier;
        private List<SqlParameter> _sqlParams;
        private int _transactionCount;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="singleEntity"></param>
        /// <param name="transactionCount"></param>
        public UpsertQueryObject(T singleEntity, List<SqlParameter> sqlParams)
        {
            _singleEntity = singleEntity;
            _sqlParams = sqlParams;
        }

        /// <summary>
        /// Set the name of table for operation to take place. Registering a table is Required.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <returns></returns>
        public UpsertQueryTable<T> WithTable(string tableName)
        {        
            return new UpsertQueryTable<T>(_singleEntity, tableName, _concatTrans, _databaseIdentifier, _sqlParams, _transactionCount);
        }
    }
}
