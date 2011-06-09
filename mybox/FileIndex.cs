/**
    Mybox version 0.3.0
    https://github.com/mybox/myboxSharp
 
    Copyright (C) 2011  Jono Finger (jono@foodnotblogs.com)

    This program is free software; you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation; either version 2 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along
    with this program; if not it can be found here:
    http://www.gnu.org/licenses/gpl-2.0.html
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Data;
using Mono.Data.SqliteClient;

using DbConnection = System.Data.IDbConnection;
using DbReader = System.Data.IDataReader;
using DbCommand = System.Data.IDbCommand;
using DbTransaction = System.Data.IDbTransaction;
using DbParameter = System.Data.IDataParameter;

namespace mybox {

  /// <summary>
  /// The file index is a database of local files and their modification times
  /// </summary>
  public class FileIndex {

    /// <summary>
    /// Absolute path to the sqlite db file
    /// </summary>
    private String dbLocation = null;
    private DbConnection dbConnection = null;

    /// <summary>
    /// Bool to determine if the index was missing when the executable started
    /// </summary>
    private bool foundAtInit = false;

    public long LastUpdate {
      get {
        return Common.GetModTime(dbLocation);
      }
    }

    public bool FoundAtInit {
      get {
        return foundAtInit;
      }
    }

    public FileIndex(String absPath) {

      dbLocation = absPath;

      if (File.Exists(dbLocation))
        foundAtInit = true;

      // load the sqlite-JDBC driver
      try {
        dbConnection = new SqliteConnection("URI=file:" + dbLocation + ",version=3");
        dbConnection.Open();

        //      dbConnection.setAutoCommit(true);

        // TODO: replace field names with constants
        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = "create table if not exists files (path text primary key, type char(1), updatetime bigint, modtime bigint)";

        command.ExecuteNonQuery();

      }
      catch (Exception e) {
        throw new Exception("Unable to load SQLite driver " + e.Message);
        //Common.ExitError();
      }

      // check to see that the file can be loaded
      if (!File.Exists(dbLocation)) {
        throw new Exception("database file " + dbLocation + " not found after init.");
        //Common.ExitError();
      }
    }

    public Dictionary<String, MyFile> GetFiles() {

      // TODO: perhaps keep a running copy in memory so we dont have to fetch from DB?

      Dictionary<String, MyFile> files = new Dictionary<string, MyFile>();

      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = "select * from files";
      DbReader reader = command.ExecuteReader();

      while (reader.Read()) {

        String path = reader["path"].ToString();

        files.Add(path, new MyFile(path, char.Parse(reader["type"].ToString()), Int64.Parse(reader["modtime"].ToString()), Int64.Parse(reader["updatetime"].ToString())));

      }

      return files;
    }

    /// <summary>
    /// Insert or update file into index with the current timestamp.
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    public bool Update(MyFile file) {

      // TODO: sanitize file names as they enter the DB since they might have a single quote or something

      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = "insert or replace into files(path, type, updatetime, modtime) values('"+ file.name +"', '"+
        file.type.ToString() + "', '" +  Common.NowUtcLong() + "', '"+ file.modtime +"')";

      command.ExecuteNonQuery();

      return true;
    }

    public bool Remove(String fileName) {

      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = "delete from files where path = '" + fileName + "'";

      command.ExecuteNonQuery();

      return true;
    }

    public bool RefreshIndex(String baseDir) {

      Console.WriteLine("Refreshing index");

      List<MyFile> fileList = Common.GetFilesRecursive(baseDir);

      //DbTransaction transaction = dbConnection.BeginTransaction();  // need to put this back

      DbCommand clearCommand = dbConnection.CreateCommand();
      clearCommand.CommandText = "DELETE FROM files";
      clearCommand.ExecuteNonQuery();

      DbCommand command = dbConnection.CreateCommand();
      DbParameter paramPath = new SqliteParameter("path", DbType.String);   // new DbParameter("path");
      DbParameter paramType = new SqliteParameter("type", DbType.String); // really a char
      DbParameter paramUpdatetime = new SqliteParameter("updatetime", DbType.Int64);
      DbParameter paramModtime = new SqliteParameter("modtime", DbType.Int64);
      command.Parameters.Add(paramPath);
      command.Parameters.Add(paramType);
      command.Parameters.Add(paramUpdatetime);
      command.Parameters.Add(paramModtime);

      foreach (MyFile file in fileList) {

        //preparedInsert = connection.prepareStatement("insert or ignore into archive values(?,?,?,?);");
        command.CommandText = "insert or ignore into files values(?,?,?,?)";

        paramPath.Value = file.name;
        paramType.Value = file.type.ToString(); // to ensure it is stored as a char/string instead of a numeric value
        paramModtime.Value = file.modtime;
        paramUpdatetime.Value = Common.NowUtcLong();  // is this value needed in the DB?

        command.ExecuteNonQuery();
      }

      //transaction.Commit();

      return true;
    }

  }
}
