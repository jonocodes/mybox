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
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Data.SqliteClient;

using DbConnection = System.Data.IDbConnection;
using DbReader = System.Data.IDataReader;
using DbCommand = System.Data.IDbCommand;

namespace mybox
{
  public class SqliteDB : IServerDB {

    private DbConnection dbConnection = null;

    private String baseDataDir = Common.UserHome + "/.mybox/serverData/";
    private String defaultConnectionString;

    public SqliteDB() {
      defaultConnectionString = "URI=file:"+ baseDataDir +"server.db,version=3";
    }

    public void Connect(String connectionString, String baseDataDir) {
      if (connectionString == null)
        connectionString = defaultConnectionString;

      if (baseDataDir != null)
        this.baseDataDir = Common.EndDirWithSlash(baseDataDir);

      if (!Directory.Exists(this.baseDataDir)) {
        Directory.CreateDirectory(this.baseDataDir);  // TODO: make this recursive to create parents
      }
      try {
        dbConnection = new SqliteConnection(connectionString);
        dbConnection.Open();

        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = 
@"CREATE TABLE IF NOT EXISTS `files` (
  `id` INTEGER,
  `path` varchar(512) NOT NULL,
  `user` int(10) NOT NULL,
  `parent` int(20) NOT NULL,
  `type` varchar(1) NOT NULL,
  `size` int(20) NOT NULL,
  `checksum` varchar(32) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE (path)
);

CREATE INDEX IF NOT EXISTS files_user ON files(user);

CREATE TABLE IF NOT EXISTS `users` (
  `id` INTEGER,
  `name` varchar(75) NOT NULL,
  `email` varchar(300),
  `password` varchar(75) NOT NULL,
  `salt` varchar(75),
  PRIMARY KEY (`id`),
  UNIQUE (`name`)
);";

        command.ExecuteNonQuery();

#if DEBUG
        // password is 'badpassword'
        dbConnection.CreateCommand();
        command.CommandText = @"INSERT OR IGNORE INTO users (name,password) VALUES ('test', '3693d93220b28a03d3c70bdc1cab2b890c65a2e6baff3d4a2a651b713c161c5c')";
        command.ExecuteNonQuery();
#endif

      } catch (Exception e) {
        throw new Exception("Error connecting to database:" + e.Message + e.StackTrace);
      }
    }

    public String DefaultConnectionString {
      get {
        return defaultConnectionString;
      }
    }

    public String BaseDataDir {
      get {
        return baseDataDir;
      }
    }

    public String GetDataDir(ServerUser user) {
      return baseDataDir + user.id /*+ "/"*/;
    }

    public bool CheckPassword(String pwordOrig, String pwordHashed) {
      // TODO: salt it!
      return (Common.Sha256Hash(pwordOrig) == pwordHashed);
    }
    
    /// <summary>
    /// Recalculate and set checksums for all directories in the list of updated directories.
    ///  Typically used after a sinc is finished.
    /// </summary>
    /// <param name='updatedDirectories'>
    /// Updated directories.
    /// </param>
    /// <param name='userId'>
    /// User identifier.
    /// </param>
    public void RecalcDirChecksums(HashSet<int> updatedDirectories, int userId) {
    
      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = String.Format("SELECT id FROM files WHERE user='{0}' AND parent=-1", userId);
      int parentId = Convert.ToInt32(command.ExecuteScalar());
      
      recalcDirChecksum(updatedDirectories, parentId);
    }
    
    private KeyValuePair<long, string> recalcDirChecksum(HashSet<int> updatedDirectories, int parentId) {
    
      Console.WriteLine("recalcChecksum parentID " + parentId);
    
      long totalSize = 0;
      string toChecksum = string.Empty;
      
      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = String.Format("SELECT * FROM files WHERE parent='{0}' ORDER BY type, path", parentId);
      DbReader reader = command.ExecuteReader();
      
      List<KeyValuePair<int, MyFile>> dirItems = new List<KeyValuePair<int, MyFile>>();
      
      while (reader.Read()) {

        string pathToChecksum = Regex.Replace(reader["path"].ToString(), "^[0-9]+/?", "/");

        dirItems.Add(new KeyValuePair<int, MyFile>(int.Parse(reader["id"].ToString()), new MyFile(pathToChecksum,
          (FileType)Enum.Parse(typeof(FileType),(string)(reader["type"])),
          long.Parse(reader["size"].ToString()), reader["checksum"].ToString())));
      }
      
      reader.Close();
      
      
      foreach (KeyValuePair<int, MyFile> thisItem in dirItems) {
      
        if (thisItem.Value.Type == FileType.DIR && updatedDirectories.Contains(thisItem.Key)) {
        
          // delete item from updateDirectories?
          KeyValuePair<long, string> dirResult = recalcDirChecksum(updatedDirectories, thisItem.Key);
          totalSize += dirResult.Key;
        
          toChecksum += thisItem.Value.Path + dirResult.Value + FileType.DIR.ToString();
        } else {
        
          totalSize += thisItem.Value.Size;
          toChecksum += thisItem.Value.Path + thisItem.Value.Checksum + thisItem.Value.Type.ToString();
        
        }
      
      }

      string cs = Common.Md5Hash(toChecksum);

      Console.WriteLine("  toChecksum: " + toChecksum);

      command = dbConnection.CreateCommand ();
      command.CommandText = String.Format("UPDATE files SET size='{0}', checksum='{1}' WHERE id='{2}'",
                                          totalSize, cs, parentId );
      command.ExecuteNonQuery();
      
      return new KeyValuePair<long, string>(totalSize, cs);
    }
    
    /*
    /// <summary>
    /// Rebuilds the files table by checksumming all the files in the directory
    /// </summary>
    public void RebuildFilesTable() {
    
      // remove all entries
      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = "DELETE FROM files";
      command.ExecuteNonQuery();
      
      // rebuild the table from filesystem entries one user directory at a time
      String[] children = Directory.GetDirectories(baseDataDir);
      foreach (string child in children)
        rebuildFilesTableDir(child, -1);
    }
    */
    
    public void RebuildFileEntries(string absParentDir, string userId) {
    
      // remove old entries
      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = "DELETE FROM files WHERE user=" + userId;
      command.ExecuteNonQuery();
      
      // TODO: make this faster by storing server only timestamps so we can scan for changes instead of deleting everything

      rebuildFilesTableDir(absParentDir, -1);
    }
    
    /// <summary>
    /// Rebuilds the files table for this directory recursively.
    /// </summary>
    /// <returns>
    /// The files table dir.
    /// </returns>
    /// <param name='absParentDir'>
    /// Abs parent dir.
    /// </param>
    /// <param name='parentId'>
    /// Parent identifier.
    /// </param>
    private KeyValuePair<long, string> rebuildFilesTableDir(string absParentDir, int parentId) {

      string relChildPath = absParentDir.Substring(baseDataDir.Length, absParentDir.Length-baseDataDir.Length);

      Console.WriteLine("rebuildFilesTableDir called on " + absParentDir + " " + relChildPath);

      string userId = Regex.Match(relChildPath, @"^[0-9]+").Value;

      DbCommand command = dbConnection.CreateCommand ();
      command.CommandText = String.Format("INSERT INTO files (parent, path, size, type, user, checksum) "
                                            + "VALUES('{0}', '{1}', '{2}', '{3}', '{4}', '{5}')",
                                            parentId, relChildPath, -1, FileType.DIR, 
                                            userId, "-1");
      command.ExecuteNonQuery();
      
      command = dbConnection.CreateCommand();
      command.CommandText = String.Format("SELECT id FROM files WHERE path='{0}'", relChildPath);
      parentId = Convert.ToInt32(command.ExecuteScalar());

      long totalSize = 0;
      string toChecksum = string.Empty;
      
      String[] children = Directory.GetDirectories(absParentDir);
      
      foreach (String absChildPath in children) {
      
        KeyValuePair<long, string> dirResult = rebuildFilesTableDir(absChildPath, parentId);

        string relChildDirPath = absChildPath.Substring(baseDataDir.Length, absChildPath.Length-baseDataDir.Length);
        string pathToChecksum = Regex.Replace(relChildDirPath, "^[0-9]+/?", "/");
        
        totalSize += dirResult.Key;
        toChecksum += pathToChecksum + dirResult.Value + FileType.DIR.ToString();

        Console.WriteLine(relChildPath + "  added directory " + relChildDirPath + " toChecksum: "+ toChecksum);
      }
      
      children = Directory.GetFiles(absParentDir);
      
      foreach (String absChildPath in children) {
        
        string relPath = absChildPath.Substring(baseDataDir.Length, absChildPath.Length-baseDataDir.Length);
        string pathToChecksum = Regex.Replace(relPath, "^[0-9]+/?", "/");

        long fileSize = (new FileInfo(absChildPath)).Length;
        string checksum = Common.FileChecksumToString(absChildPath);
        
        totalSize += fileSize;
        toChecksum += pathToChecksum + checksum + FileType.FILE.ToString();
        
        Console.WriteLine(relChildPath +"  added file "+ relPath +" " + pathToChecksum + checksum + FileType.FILE.ToString());
        
        command = dbConnection.CreateCommand ();
        command.CommandText = String.Format("INSERT INTO files (parent, path, size, type, user, checksum) "
                                            + "VALUES('{0}', '{1}', '{2}', '{3}', '{4}', '{5}')",
                                            parentId, relPath, fileSize, FileType.FILE.ToString(), 
                                            userId, checksum);
        command.ExecuteNonQuery();
      }
      
      String cs = Common.Md5Hash(toChecksum);
      
      Console.WriteLine(relChildPath + "  ended with toChecksum: " + toChecksum);
      Console.WriteLine(relChildPath + "  checksum: " + cs + " sum: " + totalSize);
      
      command = dbConnection.CreateCommand();
      command.CommandText = String.Format("UPDATE files SET size='{0}', checksum='{1}' WHERE id='{2}'",
                                          totalSize, cs, parentId );
      command.ExecuteNonQuery();

      return new KeyValuePair<long, string>(totalSize, cs);
    }
    
    /// <summary>
    ///  Gets the file list in a manner that is easy to serialize and send. 
    /// </summary>
    /// <returns>
    ///  The file list. 
    /// </returns>
    /// <param name='user'>
    ///  This account. 
    /// </param>
    /// <param name='path'>
    /// Path.
    /// </param>
    public List<List<string>> GetDirListSerializable(ServerUser user, String path) {
      List<List<string>> fileList = new List<List<string>>();

      String dbPath = path == "/" ? user.id : user.id + path;

      DbCommand command = dbConnection.CreateCommand ();
      command.CommandText = "SELECT id FROM files WHERE user='" + user.id + "' AND path = '"+ dbPath +"'";

      int parent = Convert.ToInt32(command.ExecuteScalar());
      
      command = dbConnection.CreateCommand();
      command.CommandText = String.Format("SELECT * FROM files WHERE user='{0}' AND parent='{1}'", user.id, parent);
      DbReader reader = command.ExecuteReader ();

      while (reader.Read()) {
        List<string> fileInfo = new List<string>();

        String thisPath = reader["path"].ToString();
        char typeChar = (char)((FileType)Enum.Parse(typeof(FileType), (string)(reader["type"])));
        
        fileInfo.Add(thisPath.Substring(user.id.ToString().Length, thisPath.Length-1));
        fileInfo.Add(typeChar.ToString());
        fileInfo.Add(reader["size"].ToString());
        fileInfo.Add(reader["checksum"].ToString());

        fileList.Add(fileInfo);
      }

      reader.Close ();

      return fileList;
    }

        
    public int UpdateFile(ServerUser user, MyFile thisFile) {

      int parentId = -1;
      string path = user.id + thisFile.Path;

      DbCommand command_checkExists = dbConnection.CreateCommand();
      command_checkExists.CommandText = "SELECT parent FROM files WHERE path='"+ path +"'";
      object fileCheck = command_checkExists.ExecuteScalar();      

      DbCommand command = dbConnection.CreateCommand();

      if (fileCheck != null) {
        parentId = Convert.ToInt32(fileCheck);
        
        command.CommandText = String.Format("UPDATE files SET size='{0}', checksum='{1}' WHERE path='{2}'"
          , thisFile.Size, thisFile.Checksum, path);
      } else {
        // if the entry does not exist, insert it instead of updating it

        DbCommand command_getParent = dbConnection.CreateCommand ();
        command_getParent.CommandText = "SELECT id FROM files WHERE path='"
          + path.Substring(0, path.LastIndexOf('/')) + "'";

        parentId = Convert.ToInt32(command_getParent.ExecuteScalar ());

        command.CommandText = String.Format("INSERT INTO files (parent, path, size, type, user, checksum) "
                                            + "VALUES({0}, '{1}', '{2}', '{3}', '{4}', '{5}')",
                                            parentId, path, thisFile.Size, 
                                            thisFile.Type.ToString(), user.id, thisFile.Checksum);
      }

      if (parentId == 0)
        throw new Exception("UpdateFile with parent of 0");

      if (command.ExecuteNonQuery() != 1) {
        throw new Exception("Error during server DB file update");
      }
      
      return parentId;
    }

    /// <summary>
    /// Removes the file entry from the database.
    /// </summary>
    /// <returns></returns>
    /// <param name='user'></param>
    /// <param name='filePath'></param>
    public int RemoveFile(ServerUser user, String filePath) {
    
      // make sure this handles directories as well as files
      
      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = String.Format("SELECT id, parent, type FROM files WHERE path='{0}'", 
                                          user.id + filePath);
      DbReader reader = command.ExecuteReader();
      reader.Read();
      
      int parentId = int.Parse(reader["parent"].ToString());
      FileType type = (FileType)Enum.Parse(typeof(FileType), (string)(reader["type"]));
      
      if (type == FileType.DIR) {
      
        int fileId = int.Parse(reader["id"].ToString());
      
        command = dbConnection.CreateCommand();
        command.CommandText = String.Format("SELECT * FROM files WHERE parent='{0}' ORDER BY type, path",
                                            fileId);
        
        reader = command.ExecuteReader();
        
        while (reader.Read()) {
        
          String childPath = reader["path"].ToString();
          String childPathWithoutUser = childPath.Remove(0, user.id.ToString().Length);
          String childPathWithoutParent = childPathWithoutUser.Remove(0, filePath.Length+1);
        
          // probably dont need this conditional since we have a parent ID
          if (childPathWithoutParent.LastIndexOf('/') <= 0) {
          
            FileType childType = (FileType)Enum.Parse(typeof(FileType), reader["type"].ToString(), true);
            
            if (childType == FileType.DIR) 
              RemoveFile(user, childPathWithoutUser);  // we dont need to return the parent ID because it will be gone
            else {
              command = dbConnection.CreateCommand();
              command.CommandText = "DELETE FROM files WHERE path='"+ childPath +"'";
              command.ExecuteNonQuery();
            }
            
          }
            
        }
      }
      
      command = dbConnection.CreateCommand();
      command.CommandText = "DELETE FROM files WHERE path='"+ user.id + filePath +"'";
      
      if (command.ExecuteNonQuery() != 1) 
        throw new Exception("Error during server DB file remove");
      
      return parentId;
    }

    /// <summary>
    /// Get the number of entries in the accounts table
    /// </summary>
    /// <returns></returns>
    public int UsersCount() {

      int count = 0;

      try {
        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = "SELECT COUNT(id) FROM users";
        count = Convert.ToInt32(command.ExecuteScalar());
      }
      catch (Exception) {
        return -1;
      }

      return count;
    }

    /// <summary>
    /// Print a list of the accounts in the database
    /// </summary>
    public void ShowUsers() {

      Console.WriteLine("== users ==");

      try {
        DbCommand command = dbConnection.CreateCommand ();
        command.CommandText = "SELECT id, name FROM users";
        DbReader reader = command.ExecuteReader ();

        while (reader.Read())
          Console.WriteLine (reader ["name"]);

        reader.Close ();
      } catch (Exception) {
        //
      }

    }

    /// <summary>
    /// Get an account from the database via a known ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns>null if not found</returns>
    public ServerUser GetUserByName(String userName) {

      ServerUser user = null;

      try {
        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = "SELECT * FROM users WHERE name='" + userName + "';";
        DbReader reader = command.ExecuteReader();

        while (reader.Read())
          user = new ServerUser(reader["id"].ToString(), reader["name"].ToString(), reader["password"].ToString() );

        reader.Close();
      }
      catch (Exception e) {
        Console.WriteLine("There was an error fetching the account " + e.Message);
      }

      return user;

    }

  }
}
