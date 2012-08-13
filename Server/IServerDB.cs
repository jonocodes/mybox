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
    List<List<string>> GetFileListSerializable(ServerUser user);

    /// <summary>
    /// Update or insert a new entry for the file into the database
    /// </summary>
    /// <returns>
    /// Flase if there was a problem during the update
    /// </returns>
    /// <param name='user'></param>
    /// <param name='thisFile'></param>
    bool UpdateFile(ServerUser user, MyFile thisFile);

    /// <summary>
    /// Removes the file entry from the database.
    /// </summary>
    /// <returns></returns>
    /// <param name='user'></param>
    /// <param name='filePath'></param>
    bool RemoveFile(ServerUser user, String filePath);

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
