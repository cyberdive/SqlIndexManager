﻿using SQLIndexManager.Properties;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace SQLIndexManager {

  public static class QueryEngine {

    public static List<Database> GetDatabases(SqlConnection connection, bool scanUsedSpace) {
      string query = !Settings.ServerInfo.IsAzure && Settings.ServerInfo.IsSysAdmin 
                        ? string.Format(Query.DatabaseList, scanUsedSpace ? Query.DatabaseUsedSpace : string.Empty)
                        : Query.DatabaseListAzure;

      SqlCommand cmd = new SqlCommand(query, connection) { CommandTimeout = Settings.Options.CommandTimeout };

      SqlDataAdapter adapter = new SqlDataAdapter(cmd);
      DataSet data = new DataSet();
      adapter.Fill(data);

      List<Database> dbs = new List<Database>();
      foreach (DataRow _ in data.Tables[0].Rows) {
        long dataSize = _.Field<long?>(Resources.DataSize) ?? 0;
        long logSize = _.Field<long?>(Resources.LogSize) ?? 0;

        dbs.Add(
          new Database {
            DatabaseName  = _.Field<string>(Resources.DatabaseName),
            RecoveryModel = _.Field<string>(Resources.RecoveryModel),
            LogReuseWait  = _.Field<string>(Resources.LogReuseWait),
            DataSize      = dataSize,
            DataFreeSize  = dataSize - (_.Field<long?>(Resources.DataUsedSize) ?? 0),
            LogSize       = logSize,
            LogFreeSize   = logSize - (_.Field<long?>(Resources.LogUsedSize) ?? 0)
          }
        );
      }

      return dbs;
    }

    public static ServerInfo GetServerInfo(SqlConnection connection) {
      DataSet data = new DataSet();

      SqlCommand cmd = new SqlCommand(Query.ServerInfo, connection) { CommandTimeout = Settings.Options.CommandTimeout };

      SqlDataAdapter adapter = new SqlDataAdapter(cmd);
      adapter.Fill(data);
      DataRow row = data.Tables[0].Rows[0];

      string productLevel = row.Field<string>(Resources.ProductLevel);
      string edition = row.Field<string>(Resources.Edition);
      string serverVersion = row.Field<string>(Resources.ServerVersion);
      bool isSysAdmin = row.Field<bool?>(Resources.IsSysAdmin) ?? false;

      return new ServerInfo(productLevel, edition, serverVersion, isSysAdmin);
    }

    public static List<Index> GetIndexes(SqlConnection connection) {
      List<int> it = new List<int>();
      if (Settings.Options.ScanHeap) it.Add((int)IndexType.HEAP);
      if (Settings.Options.ScanClusteredIndex) it.Add((int)IndexType.CLUSTERED);
      if (Settings.Options.ScanNonClusteredIndex) it.Add((int)IndexType.NONCLUSTERED);

      if (Settings.ServerInfo.IsColumnstoreAvailable) {
        if (Settings.Options.ScanClusteredColumnstore) it.Add((int)IndexType.CLUSTERED_COLUMNSTORE);
        if (Settings.Options.ScanNonClusteredColumnstore) it.Add((int)IndexType.NONCLUSTERED_COLUMNSTORE);
      }

      List<Index> indexes = new List<Index>();

      if (it.Count > 0) {

        string lob = string.Empty;
        if (Settings.ServerInfo.IsOnlineRebuildAvailable)
          lob = Settings.ServerInfo.MajorVersion == ServerVersion.Sql2008 ? Query.Lob2008 : Query.Lob2012Plus;

        string indexStats = Settings.ServerInfo.IsAzure && connection.Database == Resources.DatamaseMaster
                              ? Query.IndexStatsAzureMaster
                              : Query.IndexStats;

        string indexQuery = Settings.ServerInfo.MajorVersion == ServerVersion.Sql2008 ? Query.Index2008 : Query.Index2012Plus;

        List<string> excludeObjectMask = Settings.Options.ExcludeObject.Where(_ => _.Contains("%")).ToList();
        List<string> includeObjectMask = Settings.Options.IncludeObject.Where(_ => _.Contains("%")).ToList();
        List<string> excludeObjectId = Settings.Options.ExcludeObject.Where(_ => !_.Contains("%")).ToList();
        List<string> includeObjectId = Settings.Options.IncludeObject.Where(_ => !_.Contains("%")).ToList();

        string excludeList = string.Empty;
        if (Settings.Options.ExcludeSchemas.Count > 0)
          excludeList += "OR [schema_id] = SCHEMA_ID(N'" + string.Join("') OR [schema_id] = SCHEMA_ID(N'", Settings.Options.ExcludeSchemas) + "') ";

        if (excludeObjectMask.Count > 0)
          excludeList += "OR [name] LIKE N'" + string.Join("' OR [name] LIKE N'", excludeObjectMask) + "' ";

        if (excludeObjectId.Count > 0)
          excludeList += "OR [object_id] = OBJECT_ID(N'" + string.Join("') OR [object_id] = OBJECT_ID(N'", excludeObjectId) + "') ";

        string includeListSchemas = Settings.Options.IncludeSchemas.Count > 0
                                      ? "AND ( [schema_id] = SCHEMA_ID(N'" + string.Join("') OR [schema_id] = SCHEMA_ID(N'", Settings.Options.IncludeSchemas) + "') ) "
                                      : string.Empty;

        string includeListObject = string.Empty;
        if (includeObjectMask.Count > 0)
          includeListObject += "OR [name] LIKE N'" + string.Join("' OR [name] LIKE N'", includeObjectMask) + "' ";

        if (includeObjectId.Count > 0)
          includeListObject += "OR [object_id] = OBJECT_ID(N'" + string.Join("') OR [object_id] = OBJECT_ID(N'", includeObjectId) + "') ";

        if (!string.IsNullOrEmpty(includeListObject))
          includeListObject = $"AND ( 1 = 0 {includeListObject})";

        string includeList = string.IsNullOrEmpty(includeListSchemas) && string.IsNullOrEmpty(includeListObject)
                                ? Query.IncludeListEmpty
                                : string.Format(Query.IncludeList, includeListSchemas, includeListObject);

        string ignoreReadOnlyFL = Settings.Options.IgnoreReadOnlyFL ? "" : "AND fg.[is_read_only] = 0";
        string ignorePermissions = Settings.Options.IgnorePermissions ? "" : "AND PERMISSIONS(i.[object_id]) & 2 = 2";

        string query = string.Format(Query.PreDescribeIndexes,
                                    string.Join(", ", it), excludeList, indexQuery, lob,
                                    indexStats, ignoreReadOnlyFL, ignorePermissions, includeList);

        SqlCommand cmd = new SqlCommand(query, connection) { CommandTimeout = Settings.Options.CommandTimeout };

        cmd.Parameters.Add(new SqlParameter("@Fragmentation",   SqlDbType.Float)  { Value = Settings.Options.FirstThreshold });
        cmd.Parameters.Add(new SqlParameter("@MinIndexSize",    SqlDbType.BigInt) { Value = Settings.Options.MinIndexSize.PageSize() });
        cmd.Parameters.Add(new SqlParameter("@MaxIndexSize",    SqlDbType.BigInt) { Value = Settings.Options.MaxIndexSize.PageSize() });
        cmd.Parameters.Add(new SqlParameter("@PreDescribeSize", SqlDbType.BigInt) { Value = Settings.Options.PreDescribeSize.PageSize() });
        cmd.Parameters.Add(new SqlParameter("@ScanMode", SqlDbType.NVarChar, 100) { Value = Settings.Options.ScanMode });

        SqlDataAdapter adapter = new SqlDataAdapter(cmd);
        DataSet data = new DataSet();
        adapter.Fill(data);

        foreach (DataRow _ in data.Tables[0].AsEnumerable()) {

          IndexType indexType = (IndexType)_.Field<byte>(Resources.IndexType);
          bool isOnlineRebuild = Settings.ServerInfo.IsOnlineRebuildAvailable;

          if (isOnlineRebuild) {
            if (
                 _.Field<bool>(Resources.IsLobLegacy)
              ||
                 indexType == IndexType.CLUSTERED_COLUMNSTORE
              ||
                 indexType == IndexType.NONCLUSTERED_COLUMNSTORE
            ) {
              isOnlineRebuild = false;
            }
            else {
              isOnlineRebuild =
                     Settings.ServerInfo.MajorVersion > ServerVersion.Sql2008
                  ||
                     (Settings.ServerInfo.MajorVersion == ServerVersion.Sql2008 && !_.Field<bool>(Resources.IsLob));
            }
          }

          Index index = new Index {
            DatabaseName          = connection.Database,
            ObjectId              = _.Field<int>(Resources.ObjectID),
            IndexId               = _.Field<int>(Resources.IndexID),
            IndexName             = _.Field<string>(Resources.IndexName),
            ObjectName            = _.Field<string>(Resources.ObjectName),
            SchemaName            = _.Field<string>(Resources.SchemaName),
            PagesCount            = _.Field<long>(Resources.PagesCount),
            UnusedPagesCount      = _.Field<long>(Resources.UnusedPagesCount),
            PartitionNumber       = _.Field<int>(Resources.PartitionNumber),
            RowsCount             = _.Field<long>(Resources.RowsCount),
            FileGroupName         = _.Field<string>(Resources.FileGroupName),
            IndexType             = indexType,
            IsPartitioned         = _.Field<bool>(Resources.IsPartitioned),
            IsUnique              = _.Field<bool>(Resources.IsUnique),
            IsPK                  = _.Field<bool>(Resources.IsPK),
            IsFiltered            = _.Field<bool>(Resources.IsFiltered),
            FillFactor            = _.Field<int>(Resources.FillFactor),
            IndexStats            = _.Field<DateTime?>(Resources.IndexStats),
            TotalWrites           = _.Field<long?>(Resources.TotalWrites),
            TotalReads            = _.Field<long?>(Resources.TotalReads),
            TotalSeeks            = _.Field<long?>(Resources.TotalSeeks),
            TotalScans            = _.Field<long?>(Resources.TotalScans),
            TotalLookups          = _.Field<long?>(Resources.TotalLookups),
            LastUsage             = _.Field<DateTime?>(Resources.LastUsage),
            DataCompression       = (DataCompression)_.Field<byte>(Resources.DataCompression),
            Fragmentation         = _.Field<double?>(Resources.Fragmentation),
            PageSpaceUsed         = _.Field<double?>(Resources.PageSpaceUsed),
            IsAllowReorganize     = _.Field<bool>(Resources.IsAllowPageLocks) && indexType != IndexType.HEAP,
            IsAllowOnlineRebuild  = isOnlineRebuild,
            IsAllowCompression    = Settings.ServerInfo.IsCompressionAvailable && !_.Field<bool>(Resources.IsSparse),
            IndexColumns          = _.Field<string>(Resources.IndexColumns),
            IncludedColumns       = _.Field<string>(Resources.IncludedColumns)
          };

          indexes.Add(index);
        }
      }

      if (Settings.Options.ScanMissingIndex && !(Settings.ServerInfo.IsAzure && connection.Database == Resources.DatamaseMaster)) {

        SqlCommand cmd = new SqlCommand(Query.MissingIndex, connection) { CommandTimeout = Settings.Options.CommandTimeout };

        cmd.Parameters.Add(new SqlParameter("@Fragmentation", SqlDbType.Float)  { Value = Settings.Options.FirstThreshold });
        cmd.Parameters.Add(new SqlParameter("@MinIndexSize",  SqlDbType.BigInt) { Value = Settings.Options.MinIndexSize.PageSize() });
        cmd.Parameters.Add(new SqlParameter("@MaxIndexSize",  SqlDbType.BigInt) { Value = Settings.Options.MaxIndexSize.PageSize() });

        SqlDataAdapter adapter = new SqlDataAdapter(cmd);
        DataSet data = new DataSet();
        adapter.Fill(data);

        foreach (DataRow _ in data.Tables[0].AsEnumerable()) {
          string indexCols = _.Field<string>(Resources.IndexColumns);
          string objName = _.Field<string>(Resources.ObjectName);
          string indexName = $"IX_{Guid.NewGuid().ToString().Truncate(5)}_{objName}_{indexCols}"
                                    .Replace(",", "_")
                                    .Replace("[", string.Empty)
                                    .Replace("]", string.Empty)
                                    .Replace(" ", string.Empty).Truncate(240);

          Index index = new Index {
            DatabaseName          = connection.Database,
            ObjectId              = _.Field<int>(Resources.ObjectID),
            IndexName             = indexName,
            ObjectName            = objName,
            SchemaName            = _.Field<string>(Resources.SchemaName),
            PagesCount            = _.Field<long>(Resources.PagesCount),
            RowsCount             = _.Field<long>(Resources.RowsCount),
            FileGroupName         = "PRIMARY",
            IndexType             = IndexType.MISSING_INDEX,
            IndexStats            = _.Field<DateTime?>(Resources.IndexStats),
            TotalReads            = _.Field<long?>(Resources.TotalReads),
            TotalSeeks            = _.Field<long?>(Resources.TotalSeeks),
            TotalScans            = _.Field<long?>(Resources.TotalScans),
            LastUsage             = _.Field<DateTime?>(Resources.LastUsage),
            DataCompression       = DataCompression.NONE,
            Fragmentation         = _.Field<double>(Resources.Fragmentation),
            IsAllowOnlineRebuild  = false,
            IsAllowCompression    = Settings.ServerInfo.IsCompressionAvailable,
            IndexColumns          = indexCols,
            IncludedColumns       = _.Field<string>(Resources.IncludedColumns)
          };

          indexes.Add(index);
        }
      }

      return indexes;
    }

    public static void GetIndexFragmentation(SqlConnection connection, Index index) {
      SqlCommand cmd = new SqlCommand(Query.IndexFragmentation, connection) { CommandTimeout = Settings.Options.CommandTimeout };

      cmd.Parameters.Add(new SqlParameter("@ObjectID",        SqlDbType.Int) { Value = index.ObjectId });
      cmd.Parameters.Add(new SqlParameter("@IndexID",         SqlDbType.Int) { Value = index.IndexId });
      cmd.Parameters.Add(new SqlParameter("@PartitionNumber", SqlDbType.Int) { Value = index.PartitionNumber });
      cmd.Parameters.Add(new SqlParameter("@ScanMode",        SqlDbType.NVarChar, 100) { Value = Settings.Options.ScanMode });

      SqlDataAdapter adapter = new SqlDataAdapter(cmd);
      DataSet data = new DataSet();
      adapter.Fill(data);

      if (data.Tables.Count == 1 && data.Tables[0].Rows.Count == 1) {
        DataRow row = data.Tables[0].Rows[0];

        index.Fragmentation = row.Field<double>(Resources.Fragmentation);
        index.PageSpaceUsed = row.Field<double?>(Resources.PageSpaceUsed);
      }
    }

    public static void GetColumnstoreFragmentation(SqlConnection connection, Index index, List<Index> indexes) {
      SqlCommand cmd = new SqlCommand(Query.ColumnstoreIndexFragmentation, connection) { CommandTimeout = Settings.Options.CommandTimeout };

      cmd.Parameters.Add(new SqlParameter("@ObjectID",      SqlDbType.Int)    { Value = index.ObjectId });
      cmd.Parameters.Add(new SqlParameter("@Fragmentation", SqlDbType.Float)  { Value = Settings.Options.FirstThreshold });
      cmd.Parameters.Add(new SqlParameter("@MinIndexSize",  SqlDbType.BigInt) { Value = Settings.Options.MinIndexSize.PageSize() });
      cmd.Parameters.Add(new SqlParameter("@MaxIndexSize",  SqlDbType.BigInt) { Value = Settings.Options.MaxIndexSize.PageSize() });

      SqlDataAdapter adapter = new SqlDataAdapter(cmd);
      DataSet data = new DataSet();
      adapter.Fill(data);

      foreach(DataRow row in data.Tables[0].Rows) {
        int indexId = row.Field<int>(Resources.IndexID);
        int partitionNumber = row.Field<int>(Resources.PartitionNumber);

        Index idx = indexes.FirstOrDefault(_ => _.ObjectId == index.ObjectId
                                             && _.IndexId == indexId
                                             && _.PartitionNumber == partitionNumber);

        if (idx != null) {
          idx.Fragmentation = row.Field<double>(Resources.Fragmentation);
          idx.PagesCount = row.Field<long>(Resources.PagesCount);
          idx.UnusedPagesCount = row.Field<long>(Resources.UnusedPagesCount);
        }
      }
    }

    public static string FixIndex(SqlConnection connection, Index ix) {
      string sqlInfo = string.Format(ix.IsColumnstore ? Query.AfterFixColumnstoreIndex : Query.AfterFixIndex,
                                     ix.ObjectId, ix.IndexId, ix.PartitionNumber, Settings.Options.ScanMode);

      string query = ix.GetQuery();
      string sql = ix.FixType == IndexOp.DISABLE_INDEX
                || ix.FixType == IndexOp.DROP_INDEX
                || ix.FixType == IndexOp.DROP_TABLE
                || ix.FixType == IndexOp.CREATE_INDEX
                || ix.FixType == IndexOp.UPDATE_STATISTICS_FULL
                || ix.FixType == IndexOp.UPDATE_STATISTICS_RESAMPLE
                || ix.FixType == IndexOp.UPDATE_STATISTICS_SAMPLE
                      ? query
                      : $"{query} \n {sqlInfo}";

      SqlCommand cmd = new SqlCommand(sql, connection) { CommandTimeout = Settings.Options.CommandTimeout };
      SqlDataAdapter adapter = new SqlDataAdapter(cmd);
      DataSet data = new DataSet();

      try {
        adapter.Fill(data);
      }
      catch (Exception ex) {
        ix.Error = ex.Message;
      }

      if (string.IsNullOrEmpty(ix.Error)) {

        if (ix.FixType == IndexOp.UPDATE_STATISTICS_FULL || ix.FixType == IndexOp.UPDATE_STATISTICS_RESAMPLE || ix.FixType == IndexOp.UPDATE_STATISTICS_SAMPLE) {
          ix.IndexStats = DateTime.Now;
        }
        else if (ix.FixType == IndexOp.CREATE_INDEX) {
          ix.IndexStats = DateTime.Now;
          ix.Fragmentation = 0;
        }
        else if (ix.FixType == IndexOp.DISABLE_INDEX || ix.FixType == IndexOp.DROP_INDEX || ix.FixType == IndexOp.DROP_TABLE) {
          ix.PagesCountBefore = ix.PagesCount;
          ix.Fragmentation = 0;
          ix.PagesCount = 0;
          ix.UnusedPagesCount = 0;
          ix.RowsCount = 0;
        }
        else if (data.Tables.Count == 1 && data.Tables[0].Rows.Count == 1) {
          DataRow row = data.Tables[0].Rows[0];

          ix.PagesCountBefore = ix.PagesCount - row.Field<long>(Resources.PagesCount);
          ix.Fragmentation = row.Field<double>(Resources.Fragmentation);
          ix.PageSpaceUsed = row.Field<double?>(Resources.PageSpaceUsed);
          ix.PagesCount = row.Field<long>(Resources.PagesCount);
          ix.UnusedPagesCount = row.Field<long>(Resources.UnusedPagesCount);
          ix.RowsCount = row.Field<long>(Resources.RowsCount);
          ix.DataCompression = ((DataCompression)row.Field<byte>(Resources.DataCompression));
          ix.IndexStats = row.Field<DateTime?>(Resources.IndexStats);
        }

      }

      return query;
    }

    public static void FindUnusedIndexes(List<Index> indexes) {
      foreach (Index ix in indexes.Where(
                  _ => !_.IsPartitioned
                    && _.Warning == null
                    && _.TotalWrites > 50000
                    && (_.TotalReads ?? 0) < _.TotalWrites / 10
                    && (_.IndexType == IndexType.CLUSTERED || _.IndexType == IndexType.NONCLUSTERED || _.IndexType == IndexType.HEAP))) {
        ix.Warning = WarningType.UNUSED;
      }
    }

    public static void FindDublicateIndexes(List<Index> indexes) {
      var data = indexes.Where(_ => !_.IsPartitioned
                                 && _.Warning == null
                                 && (_.IndexType == IndexType.CLUSTERED || _.IndexType == IndexType.NONCLUSTERED))
                        .GroupBy(_ => new { _.DatabaseName, _.ObjectId })
                        .Select(_ => new { _.Key.DatabaseName, _.Key.ObjectId, Indexes = _.ToList() })
                        .Where(_ => _.Indexes.Count > 1);

      foreach (var item in data) {
        foreach (Index a in item.Indexes) {
          if (a.Warning != null) continue;
          foreach (Index b in item.Indexes) {
            if (a != b && b.Warning == null && a.IndexColumns == b.IndexColumns && a.IncludedColumns.Sort() == b.IncludedColumns.Sort())
              a.Warning = b.Warning = WarningType.DUBLICATE;
          }
        }

        foreach (Index a in item.Indexes) {
          foreach (Index b in item.Indexes) {
            if (a != b && b.Warning == null) {
              int len = Math.Min(a.IndexColumns.Length, b.IndexColumns.Length);
              if (a.IndexColumns == b.IndexColumns || a.IndexColumns.Left(len) == b.IndexColumns.Left(len))
                b.Warning = WarningType.OVERLAP;
            }
          }
        }
      }
    }

    private static IndexOp CorrectIndexOp(IndexOp op, Index ix) {
      if (ix.IndexType == IndexType.MISSING_INDEX)
        return IndexOp.CREATE_INDEX;

      if (op == IndexOp.REORGANIZE && (ix.IsAllowReorganize || ix.IsColumnstore))
        return IndexOp.REORGANIZE;

      if (op == IndexOp.REBUILD && !ix.IsColumnstore && ix.IsAllowCompression) {
        if (Settings.Options.DataCompression == DataCompression.NONE && ix.DataCompression != DataCompression.NONE)
          return IndexOp.REBUILD_NONE;

        if (Settings.Options.DataCompression == DataCompression.ROW)
          return IndexOp.REBUILD_ROW;

        if (Settings.Options.DataCompression == DataCompression.PAGE)
          return IndexOp.REBUILD_PAGE;
      }

      if (op == IndexOp.UPDATE_STATISTICS_FULL || op == IndexOp.UPDATE_STATISTICS_RESAMPLE || op == IndexOp.UPDATE_STATISTICS_SAMPLE) {
        if (!ix.IsPartitioned && (ix.IndexType == IndexType.CLUSTERED || ix.IndexType == IndexType.NONCLUSTERED)) {
          return op;
        }
      }

      return Settings.Options.Online && ix.IsAllowOnlineRebuild && (op == IndexOp.REBUILD || op == IndexOp.REORGANIZE)
                ? IndexOp.REBUILD_ONLINE
                : IndexOp.REBUILD;
    }

    public static void UpdateFixType(List<Index> indexes) {
      foreach (Index ix in indexes) {
        ix.FixType = CorrectIndexOp(ix.Fragmentation < Settings.Options.SecondThreshold
                          ? Settings.Options.FirstOperation
                          : Settings.Options.SecondOperation, ix);
      }
    }

  }

}