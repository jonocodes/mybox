/**
    Mybox
    https://github.com/jonocodes/mybox
 
    Copyright (C) 2012  Jono Finger (jono@foodnotblogs.com)

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

    private DbParameter paramPath = new SqliteParameter("path", DbType.String); // should begin with a '/'
    private DbParameter paramType = new SqliteParameter("type", DbType.String); // really just a single char
    private DbParameter paramModtime = new SqliteParameter("modtime", DbType.Int64);
    private DbParameter paramChecksum = new SqliteParameter("checksum", DbType.String);
    private DbParameter paramSize = new SqliteParameter("size", DbType.Int64);
    private DbParameter paramStatus = new SqliteParameter("status", DbType.String); // TODO: set to byte?
    
    private DbCommand commandInsertOrReplace = null;
    private DbCommand commandInsertOrIgnore = null;
    //private DbCommand commandGetFiles = null;
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
        command.CommandText = 
@"CREATE TABLE IF NOT EXISTS files (
  path text primary key,
  type varchar(1),
  modtime bigint,
  size bigint,
  checksum text,
  status varchar(1)
)";

        // TODO: add key and parent to speed up directory treversal
        // TODO: move 'status' field out of database and into a memory hash instead

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
      commandInsertOrReplace.Parameters.Add(paramModtime);
      commandInsertOrReplace.Parameters.Add(paramSize);
      commandInsertOrReplace.Parameters.Add(paramChecksum);
      commandInsertOrReplace.Parameters.Add(paramStatus);
      commandInsertOrReplace.CommandText = "insert or replace into files values(?,?,?,?,?,?)";

      commandInsertOrIgnore = dbConnection.CreateCommand();
      commandInsertOrIgnore.Parameters.Add(paramPath);
      commandInsertOrIgnore.Parameters.Add(paramType);
      commandInsertOrIgnore.Parameters.Add(paramModtime);
      commandInsertOrIgnore.Parameters.Add(paramSize);
      commandInsertOrIgnore.Parameters.Add(paramChecksum);
      commandInsertOrIgnore.Parameters.Add(paramStatus);
      commandInsertOrIgnore.CommandText = "insert or ignore into files values(?,?,?,?,?,?)";

      commandDeleteFile = dbConnection.CreateCommand();
      commandDeleteFile.Parameters.Add(paramPath);
      commandDeleteFile.CommandText = "delete from files where path = ?";


      // TODO: put this somewhere better
      paramPath.Value = "/";
      paramType.Value = FileType.DIR.ToString();
      paramModtime.Value = 0;      
      paramSize.Value = 0;
      paramChecksum.Value = Common.Md5Hash(String.Empty);
      paramStatus.Value = FileSyncStatus.UPTODATE.ToString();
      commandInsertOrIgnore.ExecuteNonQuery();

    }

    /// <summary>
    /// Checks to make sure all files are uptodate. Used for development testing.
    /// </summary>
    /// <returns>
    /// 0 if everything is up to date - as it should be. Else return the number of files which are not.
    /// </returns>
    public int CheckUptodate() {
      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = "SELECT COUNT(path) FROM files WHERE status != '"+ FileSyncStatus.UPTODATE +"'";
      return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Non recursive directory index getter
    /// </summary>
    /// <returns></returns>
    public List<MyFile> GetDirList(String parentPath) {

      // TODO: perhaps keep a running copy in memory so we dont have to fetch from DB?

      List<MyFile> files = new List<MyFile>();

      DbCommand clearCommand = dbConnection.CreateCommand();
      // "SELECT * FROM files WHERE REGEXP '^"+ parentPath +"[^/]+$'"; // cant to reged in native sqlite
      clearCommand.CommandText = "SELECT * FROM files WHERE path LIKE '"+ parentPath +"%'";
      // should we ignore items marked to be DELETEDONSERVER ?
      DbReader reader = clearCommand.ExecuteReader();
      
      while (reader.Read()) {
        // do the regex here since sqlite does not natively support it
        
        String path = reader["path"].ToString();
        
        String localPath = path.Remove(0, parentPath.Length);
        
        if (parentPath != "/" && localPath.Length > 1) // TODO: do this more gracefully
          localPath = localPath.Substring(1, localPath.Length-1);
        
        if (localPath.Length > 0 & !localPath.Contains("/")) {
        
          MyFile myFile = new MyFile(path, (FileType)Enum.Parse(typeof(FileType), reader["type"].ToString(), true),
                     long.Parse(reader["modtime"].ToString()), long.Parse(reader["size"].ToString()),
                     reader["checksum"].ToString(),
                     (FileSyncStatus)Enum.Parse(typeof(FileSyncStatus), reader["status"].ToString(), true) );
        
          files.Add(myFile);
        }
      }
      return files;
    }

    /// <summary>
    /// Insert or update file into index with the current timestamp.
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    public bool Update(MyFile file) {

      Console.WriteLine("FileIndex Update " + file.Path + "  " + file.SyncStatus.ToString());

      if (file.Type == FileType.DIR && file.SyncStatus != FileSyncStatus.DELETEONSERVER ) {
        Console.WriteLine("Updating local index for directory " + file.Path);
        
        DbCommand clearCommand = dbConnection.CreateCommand();
        // make sure Directories come before files
        clearCommand.CommandText = "SELECT * FROM files WHERE path LIKE '"+ file.Path +"_%' AND status != '"+ FileSyncStatus.DELETEONSERVER +"' ORDER BY type, path";
        DbReader reader = clearCommand.ExecuteReader();
        
        String str = string.Empty;
        file.Size = 0;
        
        while (reader.Read()) { 

          String childPath = reader["path"].ToString().Remove(0, file.Path.Length);
        
          if (childPath.LastIndexOf('/') <= 0) {
            str += reader["path"].ToString() + reader["checksum"].ToString() + reader["type"].ToString();
            file.Size += long.Parse(reader["size"].ToString());
          }
        }

        Console.WriteLine("  checksumming dir string: " + str);

        file.Checksum = Common.Md5Hash(str);
        file.SyncStatus = FileSyncStatus.SENDTOSERVER;
        
        reader.Close();
      }

      paramPath.Value = file.Path;
      paramType.Value = file.Type.ToString(); // to ensure it is stored as a char/string instead of a numeric value
      paramModtime.Value = file.Modtime;
      paramSize.Value = file.Size;
      paramChecksum.Value = file.Checksum;
      paramStatus.Value = file.SyncStatus.ToString(); // TODO: does this convert the enum key or value?
      commandInsertOrReplace.ExecuteNonQuery();

      return true;
    }

    /// <summary>
    /// Remove a file from the index
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns>true if 1 row was affected in the database</returns>
    public bool Remove(MyFile file) {
    
      Console.WriteLine("FileIndex Remove " + file);
      
      if (file.Type == FileType.DIR) {
        // remove all children of that directory
        
        DbCommand cmd = dbConnection.CreateCommand();
        cmd.CommandText = "SELECT * FROM files WHERE path LIKE '"+ file.Path +"_%' ORDER BY type, path";
        DbReader reader = cmd.ExecuteReader();
        
        while (reader.Read()) { 

          String thisPath = reader["path"].ToString();
          String childPath = thisPath.Remove(0, file.Path.Length);
        
          if (childPath.LastIndexOf('/') <= 0) {
          
            FileType type = (FileType)Enum.Parse(typeof(FileType), reader["type"].ToString(), true);
            
            if (type == FileType.DIR) 
              Remove(new MyFile(thisPath, type, 0, 0, "0", FileSyncStatus.DELETEONSERVER));
            else {
              paramPath.Value = thisPath;
              commandDeleteFile.ExecuteNonQuery();
            }
          }
        }
      }
    
      paramPath.Value = file.Path;
      return (commandDeleteFile.ExecuteNonQuery() == 1);
    }

  }
}
