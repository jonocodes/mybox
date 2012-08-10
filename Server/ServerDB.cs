using System;
using System.Collections.Generic;

namespace mybox
{

  /// <summary>
  /// Structure to represent a single account in the database
  /// </summary>
  public class ServerAccount {

    // TODO: make all fields readonly properties

    public String uid = null;  //unique in DB
    public String password = null;
    //  public String salt = null;
    //  public int quota = Server.DefaultQuota;

    /// <summary>
    /// Initializes a new instance
    /// </summary>
    /// <param name='uid'>Uid.</param>
    /// <param name='password'>Password./param>
    public ServerAccount(String uid, String password) {

      if (uid != null)
        this.uid = uid;

      this.password = password;
    }

    public override String ToString() {
      return uid;
    }
  }


  public interface ServerDB {

//    String DefaultServerDbConnectionString { get; }

//    String DefaultBaseDataDir { get; }

    void SetBaseDataDir(String dir);

    String GetDataDir(ServerAccount account);

    bool CheckPassword(String pwordOrig, String pwordHashed);


    /// <summary>
    /// Gets the file list in a manner that is easy to serialize and send.
    /// </summary>
    /// <returns>
    /// The file list.
    /// </returns>
    /// <param name='thisAccount'>
    /// This account.
    /// </param>
    List<List<string>> GetFileListSerializable(ServerAccount thisAccount);

    /// <summary>
    /// Update or insert a new entry for the file into the database
    /// </summary>
    /// <returns>
    /// Flase if there was a problem during the update
    /// </returns>
    /// <param name='thisAccount'></param>
    /// <param name='thisFile'></param>
    bool UpdateFile(ServerAccount thisAccount, MyFile thisFile);

    /// <summary>
    /// Removes the file entry from the database.
    /// </summary>
    /// <returns></returns>
    /// <param name='thisAccount'></param>
    /// <param name='filePath'></param>
    bool RemoveFile(ServerAccount thisAccount, String filePath);

    /// <summary>
    /// Get the number of entries in the accounts table
    /// </summary>
    /// <returns></returns>
    int AccountsCount();

    /// <summary>
    /// Print a list of the accounts in the database
    /// </summary>
    void ShowAccounts();

    /// <summary>
    /// Get an account from the database via a known ID
    /// </summary>
    /// <param name="uid"></param>
    /// <returns>null if not found</returns>
    ServerAccount GetAccountByID(String uid);
  }
}
