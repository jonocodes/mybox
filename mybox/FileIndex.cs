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

    #region members

    /// <summary>
    /// Absolute path to the sqlite db file
    /// </summary>
    private String dbLocation = null;
    private DbConnection dbConnection = null;

    // The following members are stored prepared statements to speed up queries

    private DbParameter paramPath = new SqliteParameter("path", DbType.String);
    private DbParameter paramType = new SqliteParameter("type", DbType.String); // really just a single char
    private DbParameter paramUpdatetime = new SqliteParameter("updatetime", DbType.Int64);
    private DbParameter paramModtime = new SqliteParameter("modtime", DbType.Int64);
    
    private DbCommand commandInsertOrReplace = null;
    private DbCommand commandInsertOrIgnore = null;
    private DbCommand commandGetFiles = null;
    private DbCommand commandDeleteFile = null;

    #endregion

    /// <summary>
    /// Bool to determine if the index was missing when the executable started
    /// </summary>
    private bool foundAtInit = false;

    /// <summary>
    /// Returns the modification time of the index file
    /// </summary>
    public long LastUpdate {
      get {
        return Common.GetModTime(dbLocation);
      }
    }

    /// <summary>
    /// Getter for determining if the index was missing when the executable started
    /// </summary>
    public bool FoundAtInit {
      get {
        return foundAtInit;
      }
    }

    /// <summary>
    /// Get the location of the database file
    /// </summary>
    public String DbLocation {
      get {
        return dbLocation;
      }
    }

    /// <summary>
    /// Open the connection. Be careful with this. It should be used only after CloseDB was called.
    /// </summary>
    public void OpenDB() {
      dbConnection.Open();
    }

    /// <summary>
    /// Close the connection. Be careful with this. It should be used only so the database gets unlocked for transfer or removal. Also note that this function does not return until the connection state is Closed
    /// </summary>
    public void CloseDB() {
      dbConnection.Close();

      // wait until the state is no longer open before returning
      while (dbConnection.State == ConnectionState.Open) 
        System.Threading.Thread.Sleep(500);
    }

    public FileIndex(String absPath) {

      dbLocation = absPath;

      if (File.Exists(dbLocation))
        foundAtInit = true;

      // load the sqlite-JDBC driver
      try {
        dbConnection = new SqliteConnection("URI=file:" + dbLocation + ",version=3");
        dbConnection.Open();

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
      
      
      // prepare the queries so they are nice and fast when the DB is open

      commandInsertOrReplace = dbConnection.CreateCommand();
      commandInsertOrReplace.Parameters.Add(paramPath);
      commandInsertOrReplace.Parameters.Add(paramType);
      commandInsertOrReplace.Parameters.Add(paramUpdatetime);
      commandInsertOrReplace.Parameters.Add(paramModtime);
      commandInsertOrReplace.CommandText = "insert or replace into files values(?,?,?,?)";

      commandInsertOrIgnore = dbConnection.CreateCommand();
      commandInsertOrIgnore.Parameters.Add(paramPath);
      commandInsertOrIgnore.Parameters.Add(paramType);
      commandInsertOrIgnore.Parameters.Add(paramUpdatetime);
      commandInsertOrIgnore.Parameters.Add(paramModtime);
      commandInsertOrIgnore.CommandText = "insert or ignore into files values(?,?,?,?)";

      commandGetFiles = dbConnection.CreateCommand();
      commandGetFiles.CommandText = "select * from files";

      commandDeleteFile = dbConnection.CreateCommand();
      commandDeleteFile.Parameters.Add(paramPath);
      commandDeleteFile.CommandText = "delete from files where path = ?";

    }

    /// <summary>
    /// Reads the index into a dictionary with a filename=>MyFile mapping
    /// </summary>
    /// <returns></returns>
    public Dictionary<String, MyFile> GetFiles() {

      // TODO: perhaps keep a running copy in memory so we dont have to fetch from DB?

      Dictionary<String, MyFile> files = new Dictionary<String, MyFile>();

      DbReader reader = commandGetFiles.ExecuteReader();

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

      paramPath.Value = file.name;
      paramType.Value = file.type.ToString(); // to ensure it is stored as a char/string instead of a numeric value
      paramModtime.Value = file.modtime;
      paramUpdatetime.Value = Common.NowUtcLong();  // is this value needed in the DB?

      commandInsertOrReplace.ExecuteNonQuery();

      return true;
    }

    /// <summary>
    /// Remove a file from the index
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns>true if 1 row was affected in the database</returns>
    public bool Remove(String fileName) {
      paramPath.Value = fileName;
      return (commandDeleteFile.ExecuteNonQuery() == 1);
    }

    /// <summary>
    /// Rebuild the index from scratch
    /// </summary>
    /// <param name="baseDir">the root from which to list files from</param>
    /// <returns></returns>
    public bool RefreshIndex(String baseDir) {

      Console.WriteLine("Refreshing index");

      List<MyFile> fileList = Common.GetFilesRecursive(baseDir);

      DbTransaction transaction = dbConnection.BeginTransaction();

      DbCommand clearCommand = dbConnection.CreateCommand();
      clearCommand.CommandText = "delete from files";
      clearCommand.ExecuteNonQuery();

      foreach (MyFile file in fileList) {

        //preparedInsert = connection.prepareStatement("insert or ignore into archive values(?,?,?,?);");

        paramPath.Value = file.name;
        paramType.Value = file.type.ToString(); // to ensure it is stored as a char/string instead of a numeric value
        paramModtime.Value = file.modtime;
        paramUpdatetime.Value = Common.NowUtcLong();  // is this value needed in the DB?

        commandInsertOrIgnore.ExecuteNonQuery();
      }

      transaction.Commit();

      return true;
    }

  }
}
