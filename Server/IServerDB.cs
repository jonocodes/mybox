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

namespace mybox
{

  /// <summary>
  /// Structure to represent a single account in the database
  /// </summary>
  public class ServerUser {

    // TODO: make all fields readonly properties

    public String id = null;  //unique in DB
    public String password = null;
    public String name = null;
    //  public String salt = null;
    //  public int quota = Server.DefaultQuota;

    /// <summary>
    /// Initializes a new instance
    /// </summary>
    /// <param name='uid'>Uid.</param>
    /// <param name='password'>Password./param>
    public ServerUser(String id, String name, String password) {

      if (id != null)
        this.id = id;

      this.name = name;
      this.password = password;
    }

    public override String ToString() {
      return "(name="+name+" id="+id+")";
    }
  }


  public interface IServerDB {

    // TODO: should this be abstract so we can reuse code

    String GetDataDir(ServerUser user);

    String BaseDataDir { get; }

    String DefaultConnectionString { get; }

    void Connect(String connectionString, String baseDataDir);
    
//    void RebuildFilesTable();
    void RebuildFileEntries(String absParentDir, String userId);
//    KeyValuePair<long, string> rebuildFilesTableDir(string absParentDir, int parentId);

    bool CheckPassword(String pwordOrig, String pwordHashed);

    /// <summary>
    /// Gets the file list in a manner that is easy to serialize and send.
    /// </summary>
    /// <returns>
    /// The file list.
    /// </returns>
    /// <param name='user'>
    /// This account.
    /// </param>
    //List<List<string>> GetFileListSerializable(ServerUser user);
    
    List<List<string>> GetDirListSerializable(ServerUser user, String path);


    void RecalcDirChecksums(HashSet<int> updatedDirectories, int userId);

    /// <summary>
    /// Update or insert a new entry for the file into the database
    /// </summary>
    /// <returns>
    /// Flase if there was a problem during the update
    /// </returns>
    /// <param name='user'></param>
    /// <param name='thisFile'></param>
    int UpdateFile(ServerUser user, MyFile thisFile);

    /// <summary>
    /// Removes the file entry from the database.
    /// </summary>
    /// <returns></returns>
    /// <param name='user'></param>
    /// <param name='filePath'></param>
    int RemoveFile(ServerUser user, String filePath);

    /// <summary>
    /// Get the number of entries in the accounts table
    /// </summary>
    /// <returns></returns>
    int UsersCount();

    /// <summary>
    /// Print a list of the accounts in the database
    /// </summary>
    void ShowUsers();

    /// <summary>
    /// Get an account from the database via a known ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns>null if not found</returns>
    ServerUser GetUserByName(String id);
  }
}
