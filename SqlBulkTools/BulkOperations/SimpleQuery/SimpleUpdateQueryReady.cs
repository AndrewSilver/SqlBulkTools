﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace SqlBulkTools
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SimpleUpdateQueryReady<T> : ITransaction
    {
        private readonly T _singleEntity;
        private readonly string _tableName;
        private readonly string _schema;
        private readonly HashSet<string> _columns;
        private readonly Dictionary<string, string> _customColumnMappings;
        private readonly int _sqlTimeout;
        private readonly List<Condition> _whereConditions;
        private readonly List<Condition> _andConditions;
        private readonly List<Condition> _orConditions;
        private readonly List<SqlParameter> _sqlParams;
        private int _conditionSortOrder;
        private string _identityColumn;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="singleEntity"></param>
        /// <param name="tableName"></param>
        /// <param name="schema"></param>
        /// <param name="columns"></param>
        /// <param name="customColumnMappings"></param>
        /// <param name="sqlTimeout"></param>
        /// <param name="conditionSortOrder"></param>
        /// <param name="whereConditions"></param>
        /// <param name="sqlParams"></param>
        public SimpleUpdateQueryReady(T singleEntity, string tableName, string schema, HashSet<string> columns, Dictionary<string, string> customColumnMappings,
            int sqlTimeout, int conditionSortOrder, List<Condition> whereConditions, List<SqlParameter> sqlParams)
        {
            _singleEntity = singleEntity;
            _tableName = tableName;
            _schema = schema;
            _columns = columns;
            _customColumnMappings = customColumnMappings;
            _sqlTimeout = sqlTimeout;
            _conditionSortOrder = conditionSortOrder;
            _whereConditions = whereConditions;
            _andConditions = new List<Condition>();
            _orConditions = new List<Condition>();
            _sqlParams = sqlParams;
            _identityColumn = string.Empty;
        }

        /// <summary>
        /// Sets the identity column for the table. 
        /// </summary>
        /// <param name="columnName"></param>
        /// <returns></returns>
        public SimpleUpdateQueryReady<T> SetIdentityColumn(Expression<Func<T, object>> columnName)
        {
            var propertyName = BulkOperationsHelper.GetPropertyName(columnName);

            if (propertyName == null)
                throw new SqlBulkToolsException("SetIdentityColumn column name can't be null");

            if (_identityColumn == null)
                _identityColumn = propertyName;

            else
            {
                throw new SqlBulkToolsException("Can't have more than one identity column");
            }

            return this;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        /// <exception cref="SqlBulkToolsException"></exception>
        public SimpleUpdateQueryReady<T> And(Expression<Func<T, bool>> expression)
        {
            BulkOperationsHelper.AddPredicate(expression, PredicateType.And, _andConditions, _sqlParams, _conditionSortOrder, appendParam: Constants.UniqueParamIdentifier);
            _conditionSortOrder++;
            return this;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        /// <exception cref="SqlBulkToolsException"></exception>
        public SimpleUpdateQueryReady<T> Or(Expression<Func<T, bool>> expression)
        {
            BulkOperationsHelper.AddPredicate(expression, PredicateType.Or, _orConditions, _sqlParams, _conditionSortOrder, appendParam: Constants.UniqueParamIdentifier);
            _conditionSortOrder++;
            return this;
        }

        /// <summary>
        /// Commits a transaction to database. A valid setup must exist for the operation to be 
        /// successful.
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        public int Commit(SqlConnection connection)
        {
            int affectedRows = 0;
            if (_singleEntity == null)
            {
                return affectedRows;
            }

            if (connection.State == ConnectionState.Closed)
                connection.Open();


            SqlCommand command = connection.CreateCommand();
            command.Connection = connection;
            command.CommandTimeout = _sqlTimeout;

            string fullQualifiedTableName = BulkOperationsHelper.GetFullQualifyingTableName(connection.Database, _schema,
                _tableName);


            BulkOperationsHelper.AddSqlParamsForQuery(_sqlParams, _columns, _singleEntity, customColumns: _customColumnMappings);          
            var concatenatedQuery = _whereConditions.Concat(_andConditions).Concat(_orConditions).OrderBy(x => x.SortOrder);
            BulkOperationsHelper.DoColumnMappings(_customColumnMappings, _columns);

            string comm = $"UPDATE {fullQualifiedTableName} " +
            $"{BulkOperationsHelper.BuildUpdateSet(_columns, _identityColumn)}" +
            $"{BulkOperationsHelper.BuildPredicateQuery(concatenatedQuery)}";

            command.CommandText = comm;

            if (_sqlParams.Count > 0)
            {
                command.Parameters.AddRange(_sqlParams.ToArray());
            }

            affectedRows = command.ExecuteNonQuery();

            return affectedRows;
        }

        /// <summary>
        /// Commits a transaction to database asynchronously. A valid setup must exist for the operation to be 
        /// successful.
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        public async Task<int> CommitAsync(SqlConnection connection)
        {
            int affectedRows = 0;
            if (_singleEntity == null)
            {
                return affectedRows;
            }

            if (connection.State == ConnectionState.Closed)
                await connection.OpenAsync();


            SqlCommand command = connection.CreateCommand();
            command.Connection = connection;
            command.CommandTimeout = _sqlTimeout;

            string fullQualifiedTableName = BulkOperationsHelper.GetFullQualifyingTableName(connection.Database, _schema,
                _tableName);


            BulkOperationsHelper.AddSqlParamsForQuery(_sqlParams, _columns, _singleEntity, customColumns: _customColumnMappings);
            var concatenatedQuery = _whereConditions.Concat(_andConditions).Concat(_orConditions).OrderBy(x => x.SortOrder);
            BulkOperationsHelper.DoColumnMappings(_customColumnMappings, _columns);

            string comm = $"UPDATE {fullQualifiedTableName} " +
            $"{BulkOperationsHelper.BuildUpdateSet(_columns, _identityColumn)}" +
            $"{BulkOperationsHelper.BuildPredicateQuery(concatenatedQuery)}";

            command.CommandText = comm;

            if (_sqlParams.Count > 0)
            {
                command.Parameters.AddRange(_sqlParams.ToArray());
            }

            affectedRows = await command.ExecuteNonQueryAsync();

            return affectedRows;
        }
    }
}
