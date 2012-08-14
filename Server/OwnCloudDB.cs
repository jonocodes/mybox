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


namespace mybox {

  /// <summary>
  /// Server side database to store user accounts
  /// </summary>
  public class OwnCloudDB : IServerDB {

    private DbConnection dbConnection = null;

		// TODO: figure out how to set the owncloud path dynamically. perhaps from http.conf

    // /usr/share/webapps/owncloud for new Arch installs
    private String baseDataDir = "/srv/http/owncloud/data/";  // TODO: set from /srv/httpd/owncloud/config/config.php perhaps?
    private readonly String defaultConnectionString = "Server=localhost;Database=owncloud;Uid=root;Pwd=root";

    // TODO: create prepaired statements for queries

    /// <summary>
    /// Initializes a new instance of the <see cref="mybox.OwnCloudDB"/> class.
    /// </summary>
    /// <param name='connectionString'>
    /// DB connection string.
    /// </param>
    public OwnCloudDB() {
    }

    public void Connect(String connectionString, String baseDataDir) {
      if (connectionString == null)
        connectionString = defaultConnectionString;

      try {
        dbConnection = new MySqlConnection (connectionString);
        dbConnection.Open ();
        // todo: initialize DB structure
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
      return baseDataDir + user.id + "/files/";
    }

    public bool CheckPassword(String pwordOrig, String pwordHashed) {

      // TODO: this depends on an external PHP script. remove this dependency

      string phpPasswordHashLocation = "/srv/http/owncloud/3rdparty/phpass/PasswordHash.php";

      string input = "-r 'require_once \"" + phpPasswordHashLocation +"\"; if (!isset($argv) || count($argv) != 2) { $hasher=new PasswordHash(8,(CRYPT_BLOWFISH!=1));  if ( $hasher->CheckPassword($argv[1], $argv[2]) === true) { print \"password check passed\n\"; } }' "+ pwordOrig +" '"+ pwordHashed +"'";

      Process myProcess = new Process();
      ProcessStartInfo myProcessStartInfo = new ProcessStartInfo("php", input);
      myProcessStartInfo.UseShellExecute = false;
      myProcessStartInfo.RedirectStandardOutput = true;
      myProcess.StartInfo = myProcessStartInfo;

      myProcess.Start();
      StreamReader myStreamReader = myProcess.StandardOutput;

      string line;

      while ((line = myStreamReader.ReadLine()) != null)
        if (line.Contains("password check passed"))
          return true;

      return false;
    }

    /// <summary>
    /// Gets the file list in a manner that is easy to serialize and send.
    /// </summary>
    /// <returns>
    /// The file list.
    /// </returns>
    /// <param name='user'>
    /// This account.
    /// </param>
    public List<List<string>> GetFileListSerializable(ServerUser user) {
      List<List<string>> fileList = new List<List<string>>();

      int startPath = ("/" + user.id + "/files/").Length + 1;

      DbCommand command = dbConnection.CreateCommand ();
      command.CommandText = "SELECT substr(path, " + startPath + ") as path, mtime as modtime, if(mimetype='httpd/unix-directory', 'd','f') as type, size, 0 as checksum FROM oc_fscache WHERE user='" + user.id + "' AND substr(path, " + startPath + ") != ''";
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

    /// <summary>
    /// Update or insert a new entry for the file into the database
    /// </summary>
    /// <returns>
    /// Flase if there was a problem during the update
    /// </returns>
    /// <param name='user'></param>
    /// <param name='thisFile'></param>
    public bool UpdateFile(ServerUser user, MyFile thisFile) {

      // TODO: use OwnCloud API calls if possible perhaps: http://owncloud.org/dev/apps/database/

      string path = "/" + user.id + "/files/" + thisFile.name;
      string absPath = GetDataDir(user) + thisFile.name; //Server.baseDataDir + path;
      FileInfo f = new FileInfo (absPath);
      long mtime = Common.DateTimeToUnixTimestamp(f.LastWriteTimeUtc);

      DbCommand command_checkExists = dbConnection.CreateCommand();
      command_checkExists.CommandText = "SELECT count(id) FROM oc_fscache WHERE path='"+ path +"'";

      int checkFound = Convert.ToInt32(command_checkExists.ExecuteScalar());

      DbCommand command = dbConnection.CreateCommand();

      if (checkFound > 0) {
        command.CommandText = "UPDATE oc_fscache SET mtime='" + mtime + "' WHERE path='"+ path +"'";
      } else {
        // if the entry does not exist, insert it instead of updating it

        long ctime =  Common.DateTimeToUnixTimestamp(f.CreationTimeUtc);
  
        DbCommand command_getParent = dbConnection.CreateCommand ();
        command_getParent.CommandText = "SELECT id FROM oc_fscache WHERE path_hash='"
          + Common.Md5Hash(path.Substring(0, path.LastIndexOf('/'))) + "'";

        int parentId = Convert.ToInt32 (command_getParent.ExecuteScalar ());
  
        string mimetype = MIMEAssistant.GetMIMEType(f.Name);
        string mimepart = mimetype.Substring(0, mimetype.LastIndexOf('/'));

        bool writable = true; //!f.IsReadOnly;
        bool encrypted = false; // ?
        bool versioned = false; // ?
  
        command.CommandText = String.Format("INSERT INTO oc_fscache (parent, name, path, path_hash, size, mtime, ctime, mimetype, mimepart,`user`,writable,encrypted,versioned) "
                                            + "VALUES('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}', '{8}', '{9}', '{10}', '{11}', '{12}')",
                                            parentId, f.Name, path, Common.Md5Hash(path), f.Length, mtime, ctime, mimetype, mimepart, user.id, writable, encrypted, versioned);
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
      command.CommandText = "DELETE FROM oc_fscache WHERE path='"+ "/" + user.id + "/files/" +filePath +"'";

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
        command.CommandText = "select count(uid) from oc_users";
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
        command.CommandText = "SELECT uid FROM oc_users";
        DbReader reader = command.ExecuteReader ();

        while (reader.Read())
          Console.WriteLine (reader ["uid"]);

        reader.Close ();
      } catch (Exception) {
        //
      }

    }

    /// <summary>
    /// Get an account from the database via a known ID
    /// </summary>
    /// <param name="userName"></param>
    /// <returns>null if not found</returns>
    public ServerUser GetUserByName(String userName) {

      ServerUser account = null;

      try {
        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = "select * from oc_users where uid='" + userName + "';";
        DbReader reader = command.ExecuteReader();

        while (reader.Read())
          account = new ServerUser(reader["uid"].ToString(), reader["uid"].ToString(), reader["password"].ToString() );

        reader.Close();
      }
      catch (Exception e) {
        Console.WriteLine("There was an error fetching the account " + e.Message);
      }

      return account;

    }

  }


  /// <summary>
  /// For sloppy MIME detection // TODO: base this on header, not file extension
  /// </summary>
  public static class MIMEAssistant
  {
    private static readonly Dictionary<string, string> MIMETypesDictionary = new Dictionary<string, string>
    {
      {"ai", "application/postscript"},
      {"aif", "audio/x-aiff"},
      {"aifc", "audio/x-aiff"},
      {"aiff", "audio/x-aiff"},
      {"asc", "text/plain"},
      {"atom", "application/atom+xml"},
      {"au", "audio/basic"},
      {"avi", "video/x-msvideo"},
      {"bcpio", "application/x-bcpio"},
      {"bin", "application/octet-stream"},
      {"bmp", "image/bmp"},
      {"cdf", "application/x-netcdf"},
      {"cgm", "image/cgm"},
      {"class", "application/octet-stream"},
      {"cpio", "application/x-cpio"},
      {"cpt", "application/mac-compactpro"},
      {"csh", "application/x-csh"},
      {"css", "text/css"},
      {"dcr", "application/x-director"},
      {"dif", "video/x-dv"},
      {"dir", "application/x-director"},
      {"djv", "image/vnd.djvu"},
      {"djvu", "image/vnd.djvu"},
      {"dll", "application/octet-stream"},
      {"dmg", "application/octet-stream"},
      {"dms", "application/octet-stream"},
      {"doc", "application/msword"},
      {"docx","application/vnd.openxmlformats-officedocument.wordprocessingml.document"},
      {"dotx", "application/vnd.openxmlformats-officedocument.wordprocessingml.template"},
      {"docm","application/vnd.ms-word.document.macroEnabled.12"},
      {"dotm","application/vnd.ms-word.template.macroEnabled.12"},
      {"dtd", "application/xml-dtd"},
      {"dv", "video/x-dv"},
      {"dvi", "application/x-dvi"},
      {"dxr", "application/x-director"},
      {"eps", "application/postscript"},
      {"etx", "text/x-setext"},
      {"exe", "application/octet-stream"},
      {"ez", "application/andrew-inset"},
      {"gif", "image/gif"},
      {"gram", "application/srgs"},
      {"grxml", "application/srgs+xml"},
      {"gtar", "application/x-gtar"},
      {"hdf", "application/x-hdf"},
      {"hqx", "application/mac-binhex40"},
      {"htm", "text/html"},
      {"html", "text/html"},
      {"ice", "x-conference/x-cooltalk"},
      {"ico", "image/x-icon"},
      {"ics", "text/calendar"},
      {"ief", "image/ief"},
      {"ifb", "text/calendar"},
      {"iges", "model/iges"},
      {"igs", "model/iges"},
      {"jnlp", "application/x-java-jnlp-file"},
      {"jp2", "image/jp2"},
      {"jpe", "image/jpeg"},
      {"jpeg", "image/jpeg"},
      {"jpg", "image/jpeg"},
      {"js", "application/x-javascript"},
      {"kar", "audio/midi"},
      {"latex", "application/x-latex"},
      {"lha", "application/octet-stream"},
      {"lzh", "application/octet-stream"},
      {"m3u", "audio/x-mpegurl"},
      {"m4a", "audio/mp4a-latm"},
      {"m4b", "audio/mp4a-latm"},
      {"m4p", "audio/mp4a-latm"},
      {"m4u", "video/vnd.mpegurl"},
      {"m4v", "video/x-m4v"},
      {"mac", "image/x-macpaint"},
      {"man", "application/x-troff-man"},
      {"mathml", "application/mathml+xml"},
      {"me", "application/x-troff-me"},
      {"mesh", "model/mesh"},
      {"mid", "audio/midi"},
      {"midi", "audio/midi"},
      {"mif", "application/vnd.mif"},
      {"mov", "video/quicktime"},
      {"movie", "video/x-sgi-movie"},
      {"mp2", "audio/mpeg"},
      {"mp3", "audio/mpeg"},
      {"mp4", "video/mp4"},
      {"mpe", "video/mpeg"},
      {"mpeg", "video/mpeg"},
      {"mpg", "video/mpeg"},
      {"mpga", "audio/mpeg"},
      {"ms", "application/x-troff-ms"},
      {"msh", "model/mesh"},
      {"mxu", "video/vnd.mpegurl"},
      {"nc", "application/x-netcdf"},
      {"oda", "application/oda"},
      {"ogg", "application/ogg"},
      {"pbm", "image/x-portable-bitmap"},
      {"pct", "image/pict"},
      {"pdb", "chemical/x-pdb"},
      {"pdf", "application/pdf"},
      {"pgm", "image/x-portable-graymap"},
      {"pgn", "application/x-chess-pgn"},
      {"pic", "image/pict"},
      {"pict", "image/pict"},
      {"png", "image/png"},
      {"pnm", "image/x-portable-anymap"},
      {"pnt", "image/x-macpaint"},
      {"pntg", "image/x-macpaint"},
      {"ppm", "image/x-portable-pixmap"},
      {"ppt", "application/vnd.ms-powerpoint"},
      {"pptx","application/vnd.openxmlformats-officedocument.presentationml.presentation"},
      {"potx","application/vnd.openxmlformats-officedocument.presentationml.template"},
      {"ppsx","application/vnd.openxmlformats-officedocument.presentationml.slideshow"},
      {"ppam","application/vnd.ms-powerpoint.addin.macroEnabled.12"},
      {"pptm","application/vnd.ms-powerpoint.presentation.macroEnabled.12"},
      {"potm","application/vnd.ms-powerpoint.template.macroEnabled.12"},
      {"ppsm","application/vnd.ms-powerpoint.slideshow.macroEnabled.12"},
      {"ps", "application/postscript"},
      {"qt", "video/quicktime"},
      {"qti", "image/x-quicktime"},
      {"qtif", "image/x-quicktime"},
      {"ra", "audio/x-pn-realaudio"},
      {"ram", "audio/x-pn-realaudio"},
      {"ras", "image/x-cmu-raster"},
      {"rdf", "application/rdf+xml"},
      {"rgb", "image/x-rgb"},
      {"rm", "application/vnd.rn-realmedia"},
      {"roff", "application/x-troff"},
      {"rtf", "text/rtf"},
      {"rtx", "text/richtext"},
      {"sgm", "text/sgml"},
      {"sgml", "text/sgml"},
      {"sh", "application/x-sh"},
      {"shar", "application/x-shar"},
      {"silo", "model/mesh"},
      {"sit", "application/x-stuffit"},
      {"skd", "application/x-koan"},
      {"skm", "application/x-koan"},
      {"skp", "application/x-koan"},
      {"skt", "application/x-koan"},
      {"smi", "application/smil"},
      {"smil", "application/smil"},
      {"snd", "audio/basic"},
      {"so", "application/octet-stream"},
      {"spl", "application/x-futuresplash"},
      {"src", "application/x-wais-source"},
      {"sv4cpio", "application/x-sv4cpio"},
      {"sv4crc", "application/x-sv4crc"},
      {"svg", "image/svg+xml"},
      {"swf", "application/x-shockwave-flash"},
      {"t", "application/x-troff"},
      {"tar", "application/x-tar"},
      {"tcl", "application/x-tcl"},
      {"tex", "application/x-tex"},
      {"texi", "application/x-texinfo"},
      {"texinfo", "application/x-texinfo"},
      {"tif", "image/tiff"},
      {"tiff", "image/tiff"},
      {"tr", "application/x-troff"},
      {"tsv", "text/tab-separated-values"},
      {"txt", "text/plain"},
      {"ustar", "application/x-ustar"},
      {"vcd", "application/x-cdlink"},
      {"vrml", "model/vrml"},
      {"vxml", "application/voicexml+xml"},
      {"wav", "audio/x-wav"},
      {"wbmp", "image/vnd.wap.wbmp"},
      {"wbmxl", "application/vnd.wap.wbxml"},
      {"wml", "text/vnd.wap.wml"},
      {"wmlc", "application/vnd.wap.wmlc"},
      {"wmls", "text/vnd.wap.wmlscript"},
      {"wmlsc", "application/vnd.wap.wmlscriptc"},
      {"wrl", "model/vrml"},
      {"xbm", "image/x-xbitmap"},
      {"xht", "application/xhtml+xml"},
      {"xhtml", "application/xhtml+xml"},
      {"xls", "application/vnd.ms-excel"},                        
      {"xml", "application/xml"},
      {"xpm", "image/x-xpixmap"},
      {"xsl", "application/xml"},
      {"xlsx","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"},
      {"xltx","application/vnd.openxmlformats-officedocument.spreadsheetml.template"},
      {"xlsm","application/vnd.ms-excel.sheet.macroEnabled.12"},
      {"xltm","application/vnd.ms-excel.template.macroEnabled.12"},
      {"xlam","application/vnd.ms-excel.addin.macroEnabled.12"},
      {"xlsb","application/vnd.ms-excel.sheet.binary.macroEnabled.12"},
      {"xslt", "application/xslt+xml"},
      {"xul", "application/vnd.mozilla.xul+xml"},
      {"xwd", "image/x-xwindowdump"},
      {"xyz", "chemical/x-xyz"},
      {"zip", "application/zip"}
    };

    public static string GetMIMEType(string fileName) {
      try {
        if (MIMETypesDictionary.ContainsKey(Path.GetExtension(fileName).Remove(0, 1))) {
          return MIMETypesDictionary[Path.GetExtension(fileName).Remove(0, 1)];
        }
      } catch (Exception){
        return "unknown/unknown";
      }
      return "unknown/unknown";
    }
  }


  /*
  /// <summary>
  /// Php BB crypto service provider.
  /// Depricated since the hashes are non-portable?
  /// </summary>
  public class phpBBCryptoServiceProvider
  {
    /// <summary>
    /// The encryption string base.
    /// </summary>
    private string itoa64 = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public phpBBCryptoServiceProvider() {
      Console.WriteLine("initializeing phpBBCryptoServiceProvider");
    }

    /// <summary>
    /// Compares the password string given with the hash retrieved from your database.
    /// </summary>
    /// <param name="password">Plaintext password.</param>
    /// <param name="hash">Hash from a SQL database</param>
    /// <returns>True if the password is correct, False otherwise.</returns>
    public bool phpbbCheckHash(string password, string hash)
    {
        if (hash.Length == 34) return (hashCryptPrivate(ASCIIEncoding.ASCII.GetBytes(password), hash, itoa64) == hash);
        return false;
    }

    /// <summary>
    /// This function will return the resulting hash from the password string you specify.
    /// </summary>
    /// <param name="password">String to hash.</param>
    /// <returns>Encrypted hash.</returns>
    /// <remarks>
    /// Although this will return the md5 for an older password, I have not added
    /// support for older passwords, so they will not work with this class unless
    /// I or someone else updates it.
    /// </remarks>
    public string phpbb_hash(string password)
    {
        Console.WriteLine("phpbb_hash called");

        // Generate a random string from a random number with the length of 6.
        // You could use a static string instead, doesn't matter. E.g.
        // byte[] random = ASCIIEncoding.ASCII.GetBytes("abc123");
        byte[] random = ASCIIEncoding.ASCII.GetBytes(new Random().Next(100000, 999999).ToString());

        Console.WriteLine("random " + random);
  
        string hash = hashCryptPrivate(ASCIIEncoding.ASCII.GetBytes(password), hashGensaltPrivate(random, itoa64), itoa64);

        Console.WriteLine("hash length: " + hash.Length);
        Console.WriteLine("hash: " + hash);

        if (hash.Length == 34) return hash;

        return sMD5(password);
    }

    /// <summary>
    /// The workhorse that encrypts your hash.
    /// </summary>
    /// <param name="password">String to be encrypted. Use: ASCIIEncoding.ASCII.GetBytes();</param>
    /// <param name="genSalt">Generated salt.</param>
    /// <param name="itoa64">The itoa64 string.</param>
    /// <returns>The encrypted hash ready to be compared.</returns>
    /// <remarks>
    /// password:  Saves conversion inside the function, lazy coding really.
    /// genSalt:   Returns from hashGensaltPrivate(random, itoa64);
    /// return:    Compare with phpbbCheckHash(password, hash)
    /// </remarks>
    private string hashCryptPrivate(byte[] password, string genSalt, string itoa64)
    {
        string output = "*";
        MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
        if (!genSalt.StartsWith("$H$")) return output;
        //   $count_log2 = strpos($itoa64, $setting[3]);
        int count_log2 = itoa64.IndexOf(genSalt[3]);
        if (count_log2 < 7 || count_log2 > 30) return output;

        int count = 1 << count_log2;
        byte[] salt = ASCIIEncoding.ASCII.GetBytes(genSalt.Substring(4, 8));

        if (salt.Length != 8) return output;

        byte[] hash = md5.ComputeHash(Combine(salt, password));

        do
        {
            hash = md5.ComputeHash(Combine(hash, password));
        } while (count-- > 1);

        output = genSalt.Substring(0, 12);
        output += hashEncode64(hash, 16, itoa64);

        return output;
    }

    /// <summary>
    /// Private function to concat byte arrays.
    /// </summary>
    /// <param name="b1">Source array.</param>
    /// <param name="b2">Array to add to the source array.</param>
    /// <returns>Combined byte array.</returns>
    private byte[] Combine(byte[] b1, byte[] b2)
    {
        byte[] retVal = new byte[b1.Length + b2.Length];
        Array.Copy(b1, 0, retVal, 0, b1.Length);
        Array.Copy(b2, 0, retVal, b1.Length, b2.Length);
        return retVal;
    }

    /// <summary>
    /// Encode the hash.
    /// </summary>
    /// <param name="input">The hash to encode.</param>
    /// <param name="count">[This parameter needs documentation].</param>
    /// <param name="itoa64">The itoa64 string.</param>
    /// <returns>Encoded hash.</returns>
    private string hashEncode64(byte[] input, int count, string itoa64)
    {
        string output = "";
        int i = 0; int value = 0;

        do
        {
            value = input[i++];
            output += itoa64[value & 0x3f];

            if (i < count) value |= input[i] << 8;
            output += itoa64[(value >> 6) & 0x3f];
            if (i++ >= count)
                break;

            if (i < count) value |= input[i] << 16;
            output += itoa64[(value >> 12) & 0x3f];
            if (i++ >= count)
                break;

            output += itoa64[(value >> 18) & 0x3f];

        } while (i < count);

        return output;
    }

    /// <summary>
    /// Generate salt for hash generation.
    /// </summary>
    /// <param name="input">Any random information.</param>
    /// <param name="itoa64">The itoa64 string.</param>
    /// <returns>Generated salt string</returns>
    private string hashGensaltPrivate(byte[] input, string itoa64)
    {
        int iteration_count_log2 = 6;

        string output = "$H$";
        output += itoa64[Math.Min(iteration_count_log2 + 5, 30)];
        output += hashEncode64(input, 6, itoa64);

        return output;
    }

    /// <summary>
    /// Returns a hexadecimal string representation for the encrypted MD5 parameter.
    /// </summary>
    /// <param name="password">String to be encrypted.</param>
    /// <returns>String</returns>
    private string sMD5(string password) { return sMD5(password, false); }

    /// <summary>
    /// Returns a hexadecimal string representation for the encrypted MD5 parameter.
    /// </summary>
    /// <param name="password">String to be encrypted.</param>
    /// <param name="raw">Whether or not to produce a raw string.</param>
    /// <returns>String</returns>
    private string sMD5(string password, bool raw)
    {
        MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
        if (raw) return Encoding.ASCII.GetString(md5.ComputeHash(Encoding.ASCII.GetBytes(password)));
        else return BitConverter.ToString(md5.ComputeHash(Encoding.ASCII.GetBytes(password))).Replace("-", "");
    }
  }
   */
}
