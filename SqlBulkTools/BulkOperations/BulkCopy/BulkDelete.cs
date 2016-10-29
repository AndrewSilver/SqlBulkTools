﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace SqlBulkTools
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BulkDelete<T> : AbstractOperation<T>, ITransaction
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="list"></param>
        /// <param name="tableName"></param>
        /// <param name="schema"></param>
        /// <param name="columns"></param>
        /// <param name="disableAllIndexes"></param>
        /// <param name="customColumnMappings"></param>
        /// <param name="sqlTimeout"></param>
        /// <param name="bulkCopyTimeout"></param>
        /// <param name="bulkCopyEnableStreaming"></param>
        /// <param name="bulkCopyNotifyAfter"></param>
        /// <param name="bulkCopyBatchSize"></param>
        /// <param name="sqlBulkCopyOptions"></param>
        /// <param name="disableIndexList"></param>
        /// <param name="bulkCopyDelegates"></param>
        public BulkDelete(IEnumerable<T> list, string tableName, string schema, HashSet<string> columns, HashSet<string> disableIndexList,
            bool disableAllIndexes,
            Dictionary<string, string> customColumnMappings, int sqlTimeout, int bulkCopyTimeout,
            bool bulkCopyEnableStreaming, int? bulkCopyNotifyAfter, int? bulkCopyBatchSize, SqlBulkCopyOptions sqlBulkCopyOptions,
            IEnumerable<SqlRowsCopiedEventHandler> bulkCopyDelegates)
            :
            base(list, tableName, schema, columns, disableIndexList, disableAllIndexes, customColumnMappings, sqlTimeout,
                bulkCopyTimeout, bulkCopyEnableStreaming, bulkCopyNotifyAfter, bulkCopyBatchSize, sqlBulkCopyOptions, bulkCopyDelegates)
        {
            _deletePredicates = new List<Condition>();
            _parameters = new List<SqlParameter>();
            _conditionSortOrder = 1;
        }

        /// <summary>
        /// At least one MatchTargetOn is required for correct configuration. MatchTargetOn is the matching clause for evaluating 
        /// each row in table. This is usally set to the unique identifier in the table (e.g. Id). Multiple MatchTargetOn members are allowed 
        /// for matching composite relationships. 
        /// </summary>
        /// <param name="columnName"></param>
        /// <returns></returns>
        public BulkDelete<T> MatchTargetOn(Expression<Func<T, object>> columnName)
        {
            var propertyName = BulkOperationsHelper.GetPropertyName(columnName);
            _matchTargetOn.Add(propertyName);
            return this;
        }

        /// <summary>
        /// Only delete records when the target satisfies a speicific requirement. This is used in conjunction with MatchTargetOn.
        /// See help docs for examples.  
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public BulkDelete<T> DeleteWhen(Expression<Func<T, bool>> predicate)
        {
            BulkOperationsHelper.AddPredicate(predicate, PredicateType.Delete, _deletePredicates, _parameters, _conditionSortOrder, Constants.UniqueParamIdentifier);
            _conditionSortOrder++;

            return this;
        }

        /// <summary>
        /// Sets the identity column for the table. Required if an Identity column exists in table and one of the two 
        /// following conditions is met: (1) MatchTargetOn list contains an identity column (2) AddAllColumns is used in setup. 
        /// </summary>
        /// <param name="columnName"></param>
        /// <returns></returns>
        public BulkDelete<T> SetIdentityColumn(Expression<Func<T, object>> columnName)
        {
            base.SetIdentity(columnName);
            return this;
        }

        /// <summary>
        /// Sets the identity column for the table. Required if an Identity column exists in table and one of the two 
        /// following conditions is met: (1) MatchTargetOn list contains an identity column (2) AddAllColumns is used in setup. 
        /// </summary>
        /// <param name="columnName"></param>
        /// <param name="outputIdentity"></param>
        /// <returns></returns>
        public BulkDelete<T> SetIdentityColumn(Expression<Func<T, object>> columnName, ColumnDirection outputIdentity)
        {
            base.SetIdentity(columnName, outputIdentity);
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
            int affectedRecords = 0;
            if (!_list.Any())
            {
                return affectedRecords;
            }

            base.IndexCheck();
            base.MatchTargetCheck();

            DataTable dt = BulkOperationsHelper.CreateDataTable<T>(_columns, _customColumnMappings, _matchTargetOn, _outputIdentity);
            dt = BulkOperationsHelper.ConvertListToDataTable(dt, _list, _columns, _outputIdentityDic);

            // Must be after ToDataTable is called. 
            BulkOperationsHelper.DoColumnMappings(_customColumnMappings, _columns);
            BulkOperationsHelper.DoColumnMappings(_customColumnMappings, _deletePredicates);

            if (connection.State == ConnectionState.Closed)
                connection.Open();

            var dtCols = BulkOperationsHelper.GetDatabaseSchema(connection, _schema, _tableName);

            SqlCommand command = connection.CreateCommand();
            command.Connection = connection;
            command.CommandTimeout = _sqlTimeout;

            //Creating temp table on database
            command.CommandText = BulkOperationsHelper.BuildCreateTempTable(_columns, dtCols, _outputIdentity);
            command.ExecuteNonQuery();

            BulkOperationsHelper.InsertToTmpTable(connection, dt, _bulkCopyEnableStreaming,
                _bulkCopyBatchSize, _bulkCopyNotifyAfter, _bulkCopyTimeout, _sqlBulkCopyOptions, _bulkCopyDelegates);

            if (_disableIndexList != null && _disableIndexList.Any())
            {
                command.CommandText = BulkOperationsHelper.GetIndexManagementCmd(IndexOperation.Disable, _tableName,
                    _schema, connection, _disableIndexList, _disableAllIndexes);
                command.ExecuteNonQuery();
            }

            string comm = BulkOperationsHelper.GetOutputCreateTableCmd(_outputIdentity, Constants.TempOutputTableName,
            OperationType.InsertOrUpdate, _identityColumn);

            if (!string.IsNullOrWhiteSpace(comm))
            {
                command.CommandText = comm;
                command.ExecuteNonQuery();
            }

            comm = "MERGE INTO " + BulkOperationsHelper.GetFullQualifyingTableName(connection.Database, _schema, _tableName) + " WITH (HOLDLOCK) AS Target " +
                          "USING " + Constants.TempTableName + " AS Source " +
                          BulkOperationsHelper.BuildJoinConditionsForUpdateOrInsert(_matchTargetOn.ToArray(),
                          Constants.SourceAlias, Constants.TargetAlias) +
                          "WHEN MATCHED " + BulkOperationsHelper.BuildPredicateQuery(_matchTargetOn.ToArray(), _deletePredicates, Constants.TargetAlias) +
                          "THEN DELETE " +
                          BulkOperationsHelper.GetOutputIdentityCmd(_identityColumn, _outputIdentity, Constants.TempOutputTableName,
                          OperationType.Delete) + "; " +
                          "DROP TABLE " + Constants.TempTableName + ";";
            command.CommandText = comm;

            if (_parameters.Count > 0)
            {
                command.Parameters.AddRange(_parameters.ToArray());
            }

            affectedRecords = command.ExecuteNonQuery();

            if (_disableIndexList != null && _disableIndexList.Any())
            {
                command.CommandText = BulkOperationsHelper.GetIndexManagementCmd(IndexOperation.Rebuild, _tableName,
                    _schema, connection, _disableIndexList);
                command.ExecuteNonQuery();
            }

            if (_outputIdentity == ColumnDirection.InputOutput)
            {
                BulkOperationsHelper.LoadFromTmpOutputTable(command, _identityColumn, _outputIdentityDic, OperationType.Delete, _list);
            }

            return affectedRecords;


        }

        /// <summary>
        /// Commits a transaction to database asynchronously. A valid setup must exist for the operation to be 
        /// successful.
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        public async Task<int> CommitAsync(SqlConnection connection)
        {
            int affectedRecords = 0;
            if (!_list.Any())
            {
                return affectedRecords;
            }

            base.IndexCheck();
            base.MatchTargetCheck();

            DataTable dt = BulkOperationsHelper.CreateDataTable<T>(_columns, _customColumnMappings, _matchTargetOn, _outputIdentity);
            dt = BulkOperationsHelper.ConvertListToDataTable(dt, _list, _columns, _outputIdentityDic);

            // Must be after ToDataTable is called. 
            BulkOperationsHelper.DoColumnMappings(_customColumnMappings, _columns);
            BulkOperationsHelper.DoColumnMappings(_customColumnMappings, _deletePredicates);

            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            var dtCols = BulkOperationsHelper.GetDatabaseSchema(connection, _schema, _tableName);

            SqlCommand command = connection.CreateCommand();
            command.Connection = connection;
            command.CommandTimeout = _sqlTimeout;

            //Creating temp table on database
            command.CommandText = BulkOperationsHelper.BuildCreateTempTable(_columns, dtCols, _outputIdentity);
            await command.ExecuteNonQueryAsync();

            BulkOperationsHelper.InsertToTmpTable(connection, dt, _bulkCopyEnableStreaming,
                _bulkCopyBatchSize, _bulkCopyNotifyAfter, _bulkCopyTimeout, _sqlBulkCopyOptions, _bulkCopyDelegates);

            if (_disableIndexList != null && _disableIndexList.Any())
            {
                command.CommandText = BulkOperationsHelper.GetIndexManagementCmd(IndexOperation.Disable, _tableName,
                    _schema, connection, _disableIndexList, _disableAllIndexes);
                await command.ExecuteNonQueryAsync();
            }

            string comm = BulkOperationsHelper.GetOutputCreateTableCmd(_outputIdentity, Constants.TempOutputTableName,
            OperationType.InsertOrUpdate, _identityColumn);

            if (!string.IsNullOrWhiteSpace(comm))
            {
                command.CommandText = comm;
                await command.ExecuteNonQueryAsync();
            }

            comm = "MERGE INTO " + BulkOperationsHelper.GetFullQualifyingTableName(connection.Database, _schema, _tableName) + " WITH (HOLDLOCK) AS Target " +
                          "USING " + Constants.TempTableName + " AS Source " +
                          BulkOperationsHelper.BuildJoinConditionsForUpdateOrInsert(_matchTargetOn.ToArray(),
                          Constants.SourceAlias, Constants.TargetAlias) +
                          "WHEN MATCHED " + BulkOperationsHelper.BuildPredicateQuery(_matchTargetOn.ToArray(), _deletePredicates, Constants.TargetAlias) +
                          "THEN DELETE " +
                          BulkOperationsHelper.GetOutputIdentityCmd(_identityColumn, _outputIdentity, Constants.TempOutputTableName,
                          OperationType.Delete) + "; " +
                          "DROP TABLE " + Constants.TempTableName + ";";
            command.CommandText = comm;

            if (_parameters.Count > 0)
            {
                command.Parameters.AddRange(_parameters.ToArray());
            }

            affectedRecords = command.ExecuteNonQuery();

            if (_disableIndexList != null && _disableIndexList.Any())
            {
                command.CommandText = BulkOperationsHelper.GetIndexManagementCmd(IndexOperation.Rebuild, _tableName,
                    _schema, connection, _disableIndexList);
                await command.ExecuteNonQueryAsync();
            }

            if (_outputIdentity == ColumnDirection.InputOutput)
            {
                BulkOperationsHelper.LoadFromTmpOutputTable(command, _identityColumn, _outputIdentityDic, OperationType.Delete, _list);
            }

            return affectedRecords;


        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="connectionName"></param>
        ///// <param name="credentials"></param>
        ///// <param name="connection"></param>
        ///// <returns></returns>
        //async Task<int> ITransaction.CommitTransactionAsync(string connectionName, SqlCredential credentials, SqlConnection connection)
        //{
        //    int affectedRows = 0;
        //    if (!_list.Any())
        //    {
        //        return affectedRows;
        //    }
        //    base.IndexCheck();
        //    base.MatchTargetCheck();

        //    DataTable dt = BulkOperationsHelper.CreateDataTable<T>(_columns, _customColumnMappings, _matchTargetOn, _outputIdentity);
        //    dt = BulkOperationsHelper.ConvertListToDataTable(dt, _list, _columns, _outputIdentityDic);

        //    // Must be after ToDataTable is called. 
        //    BulkOperationsHelper.DoColumnMappings(_customColumnMappings, _columns);
        //    BulkOperationsHelper.DoColumnMappings(_customColumnMappings, _deletePredicates);


        //    using (SqlConnection conn = BulkOperationsHelper.GetSqlConnection(connectionName, credentials, connection))
        //    {
        //        await conn.OpenAsync();
        //        var dtCols = BulkOperationsHelper.GetDatabaseSchema(conn, _schema, _tableName);

        //        using (SqlTransaction transaction = conn.BeginTransaction())
        //        {
        //            try
        //            {
        //                SqlCommand command = conn.CreateCommand();
        //                command.Connection = conn;
        //                command.Transaction = transaction;
        //                command.CommandTimeout = _sqlTimeout;

        //                //Creating temp table on database
        //                command.CommandText = BulkOperationsHelper.BuildCreateTempTable(_columns, dtCols, _outputIdentity);
        //                await command.ExecuteNonQueryAsync();

        //                await BulkOperationsHelper.InsertToTmpTableAsync(conn, transaction, dt, _bulkCopyEnableStreaming, _bulkCopyBatchSize,
        //                    _bulkCopyNotifyAfter, _bulkCopyTimeout, _sqlBulkCopyOptions, _bulkCopyDelegates);

        //                if (_disableIndexList != null && _disableIndexList.Any())
        //                {
        //                    command.CommandText = BulkOperationsHelper.GetIndexManagementCmd(IndexOperation.Disable, _tableName,
        //                        _schema, conn, _disableIndexList, _disableAllIndexes);
        //                    await command.ExecuteNonQueryAsync();
        //                }

        //                string comm = BulkOperationsHelper.GetOutputCreateTableCmd(_outputIdentity, Constants.TempOutputTableName,
        //                OperationType.InsertOrUpdate, _identityColumn);

        //                if (!string.IsNullOrWhiteSpace(comm))
        //                {
        //                    command.CommandText = comm;
        //                    command.ExecuteNonQuery();
        //                }

        //                // Updating destination table, and dropping temp table
        //                comm = "MERGE INTO " + BulkOperationsHelper.GetFullQualifyingTableName(conn.Database, _schema, _tableName) + " WITH (HOLDLOCK) AS Target " +
        //                              "USING " + Constants.TempTableName + " AS Source " +
        //                              BulkOperationsHelper.BuildJoinConditionsForUpdateOrInsert(_matchTargetOn.ToArray(),
        //                              Constants.SourceAlias, Constants.TargetAlias) +
        //                              "WHEN MATCHED " + BulkOperationsHelper.BuildPredicateQuery(_matchTargetOn.ToArray(), _deletePredicates, Constants.TargetAlias) +
        //                              "THEN DELETE " +
        //                              BulkOperationsHelper.GetOutputIdentityCmd(_identityColumn, _outputIdentity, Constants.TempOutputTableName,
        //                              OperationType.Delete) + "; " +
        //                              "DROP TABLE " + Constants.TempTableName + ";";
        //                command.CommandText = comm;

        //                if (_parameters.Count > 0)
        //                {
        //                    command.Parameters.AddRange(_parameters.ToArray());
        //                }

        //                affectedRows = await command.ExecuteNonQueryAsync();

        //                if (_disableIndexList != null && _disableIndexList.Any())
        //                {
        //                    command.CommandText = BulkOperationsHelper.GetIndexManagementCmd(IndexOperation.Rebuild, _tableName,
        //                        _schema, conn, _disableIndexList);
        //                    await command.ExecuteNonQueryAsync();
        //                }

        //                if (_outputIdentity == ColumnDirection.InputOutput)
        //                {
        //                    await
        //                        BulkOperationsHelper.LoadFromTmpOutputTableAsync(command, _identityColumn, _outputIdentityDic,
        //                        OperationType.Delete, _list);
        //                }

        //                transaction.Commit();
        //                return affectedRows;
        //            }
        //            catch (Exception)
        //            {
        //                transaction.Rollback();
        //                throw;
        //            }
        //            finally
        //            {
        //                conn.Close();
        //            }
        //        }
        //    }
        //}
    }
}
