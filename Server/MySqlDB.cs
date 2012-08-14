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
using MySql.Data.MySqlClient;

using DbConnection = System.Data.IDbConnection;
using DbReader = System.Data.IDataReader;
using DbCommand = System.Data.IDbCommand;

namespace mybox
{
  public class MySqlDB : IServerDB {

    private DbConnection dbConnection = null;

    private String baseDataDir = Common.UserHome + "/.mybox/serverData/";
    private readonly String defaultConnectionString = "Server=localhost;Database=mybox;Uid=root;Pwd=root";

    public MySqlDB() {
    }

    public void Connect(String connectionString, String baseDataDir) {
      if (connectionString == null)
        connectionString = defaultConnectionString;

      try {
        dbConnection = new MySqlConnection (connectionString);
        dbConnection.Open ();

        // TODO: "CREATE DATABASE IF NOT EXISTS `mybox`;";

        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = 
@"CREATE TABLE IF NOT EXISTS `files` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `path` varchar(512) NOT NULL,
  `user` int(10) NOT NULL,
  `parent` int(20) NOT NULL,
  `size` int(20) NOT NULL,
  `modtime` int(20) NOT NULL,
  `checksum` varchar(32) NOT NULL,
  `type` varchar(1) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `user` (`user`)
);

CREATE TABLE IF NOT EXISTS `users` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(75) NOT NULL,
  `email` varchar(300) NOT NULL,
  `password` varchar(75) NOT NULL,
  `salt` varchar(75) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `name` (`name`)
);";

        command.ExecuteNonQuery();

#if DEBUG
        // password is 'badpassword'
        dbConnection.CreateCommand();
        command.CommandText = @"INSERT IGNORE INTO `mybox`.`users` (`name`,`password`) VALUES ('test', '3693d93220b28a03d3c70bdc1cab2b890c65a2e6baff3d4a2a651b713c161c5c');";
        command.ExecuteNonQuery();
#endif


      } catch (Exception) {
        throw new Exception("Error connecting to database.");
      }

      if (baseDataDir != null)
        this.baseDataDir = Common.EndDirWithSlash(baseDataDir);

      if (!Directory.Exists(this.baseDataDir)) {
        Directory.CreateDirectory(this.baseDataDir);  // TODO: make this recursive to create parents
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
      return baseDataDir + user.id + "/";
    }

    public bool CheckPassword(String pwordOrig, String pwordHashed) {
      // TODO: salt it!
      return (Common.Sha256Hash(pwordOrig) == pwordHashed);
    }

    public List<List<string>> GetFileListSerializable(ServerUser thisAccount) {
      List<List<string>> fileList = new List<List<string>>();

      int startPath = ("/" + thisAccount.id + "/").Length + 1;

      DbCommand command = dbConnection.CreateCommand ();
      command.CommandText = "SELECT substr(path, " + startPath + ") as path, type, modtime, size, checksum FROM files WHERE user='" + thisAccount.id + "' AND substr(path, " + startPath + ") != ''";
      DbReader reader = command.ExecuteReader ();

      while (reader.Read()) {
        List<string> fileInfo = new List<string>();

        fileInfo.Add(reader["path"].ToString ());
        fileInfo.Add(reader["type"].ToString ());
        fileInfo.Add(reader["modtime"].ToString ());
        fileInfo.Add(reader["size"].ToString());
        fileInfo.Add(reader["checksum"].ToString());

        fileList.Add(fileInfo);
      }

      reader.Close ();

      return fileList;
    }

    public bool UpdateFile(ServerUser user, MyFile thisFile) {

      string path = "/" + user.id + "/" + thisFile.name;

      DbCommand command_checkExists = dbConnection.CreateCommand();
      command_checkExists.CommandText = "SELECT count(id) FROM files WHERE path='"+ path +"'";

      int checkFound = Convert.ToInt32(command_checkExists.ExecuteScalar());

      DbCommand command = dbConnection.CreateCommand();

      if (checkFound > 0) {
        command.CommandText = "UPDATE files SET modtime='" + thisFile.modtime + "' WHERE path='"+ path +"'";
      } else {
        // if the entry does not exist, insert it instead of updating it

        DbCommand command_getParent = dbConnection.CreateCommand ();
        command_getParent.CommandText = "SELECT id FROM files WHERE path='"
          + path.Substring(0, path.LastIndexOf('/')) + "'";

        int parentId = Convert.ToInt32 (command_getParent.ExecuteScalar ());

        command.CommandText = String.Format("INSERT INTO files (parent, path, size, modtime, type, `user`, checksum) "
                                            + "VALUES('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}')",
                                            parentId, path, thisFile.size, thisFile.modtime, thisFile.type, user.id, thisFile.checksum);
      }

      return (command.ExecuteNonQuery() == 1);
    }

    /// <summary>
    /// Removes the file entry from the database.
    /// </summary>
    /// <returns></returns>
    /// <param name='user'></param>
    /// <param name='filePath'></param>
    public bool RemoveFile(ServerUser user, String filePath) {

      DbCommand command = dbConnection.CreateCommand();
      command.CommandText = "DELETE FROM files WHERE path='"+ "/" + user.id + "/" +filePath +"'";

      return (command.ExecuteNonQuery() == 1);
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
    public void ShowUsers () {

      Console.WriteLine ("== users ==");

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

