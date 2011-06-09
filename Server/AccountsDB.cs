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
using System.IO;
using System.Data;
using Mono.Data.SqliteClient;

using DbConnection = System.Data.IDbConnection;
using DbReader = System.Data.IDataReader;
using DbCommand = System.Data.IDbCommand;


namespace mybox {

  /// <summary>
  /// Server side database to store user accounts
  /// </summary>
  public class AccountsDB {

    private String dbLocation = null;

    private DbConnection dbConnection = null;

    public AccountsDB(String absPath) {
      dbLocation = absPath;

      // load the sqlite-JDBC driver
      try {
      
        // TODO: print message if it is not found and creating the database
        if (!File.Exists(dbLocation)) {
          Console.WriteLine("Database not found: " + dbLocation);
          Common.ExitError();
        }

        dbConnection = (IDbConnection) new SqliteConnection("URI=file:" + dbLocation + ",version=3"); // will implicitly create?
        dbConnection.Open();

  //      dbConnection.setAutoCommit(true);
      } catch (Exception e) {
        Console.WriteLine("Unable to load SQLite driver " + e.Message);
        Common.ExitError();
      }

      // check to see that the file can be loaded
      if (!File.Exists(dbLocation)) {
        Console.WriteLine("Accounts database file " + dbLocation + " not found.");
        Common.ExitError();
      }
    }


    /// <summary>
    /// Set up the database. Create it if it does not exist.
    /// </summary>
    /// <param name="location">location of database file</param>
    /// <returns>false if there was an error</returns>
    public static bool Setup(String location) {

      if (File.Exists(location))
        return true;

      try {
        DbConnection dbConnection = new SqliteConnection("URI=file:" + location + ",version=3"); // will implititly create?
//        DbConnection dbConnection = new DbConnection("Version=3,uri=file:" + location); // will implititly create?
        //dbConnection.setAutoCommit(true);

        DbCommand command = dbConnection.CreateCommand();
//        DbCommand command = dbConnection.CreateCommand();
        dbConnection.Open();

        // TODO: turn fields into constants

        command.CommandText = "create table accounts (id integer primary key, email text unique, password text not null, salt text not null, created text not null, quota integer default null)";
        command.ExecuteNonQuery();

      }
      catch (Exception e) {
        Console.WriteLine("AccountsDB error during creation " + e.Message);
        return false;
      }

      return true;
    }


    /// <summary>
    /// Remove the account from the database
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public bool DeleteAccount(String id) {

      try {
        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = "delete from accounts where id='" + id + "';";

        int affectedRows = command.ExecuteNonQuery();

        if (affectedRows != 1) {
          Console.WriteLine("There was an error when removing the account from the database");
          return false;
        }

      }
      catch (Exception) {
        return false;
      }

      return true;
    }

    /// <summary>
    /// Makes sure a new account can be inserted, before it is actually added to the database
    /// </summary>
    /// <param name="email"></param>
    /// <param name="rawPassword"></param>
    /// <returns>true if valid</returns>
    public bool ValidatePotentialAccount(String email, String rawPassword) {

      Account accountByEmail = GetAccountByEmail(email);

      if (accountByEmail != null) // unique in database
        return false;

      if (email.Length < 2 || rawPassword.Length < 2) // TODO: make these constraints harder
        return false;

      return true;
    }


    /// <summary>
    /// Add account to database
    /// </summary>
    /// <param name="email"></param>
    /// <param name="encryptedPassword">The raw password encrypted with the salt</param>
    /// <param name="salt"></param>
    /// <returns>null if the account was not added</returns>
    public Account AddAccount(String email, String encryptedPassword, String salt) {

      Account account = null;

      try {

        DbCommand command = dbConnection.CreateCommand();

        command.CommandText = String.Format("insert or ignore into accounts (email, password, salt, created) values('{0}','{1}','{2}','{3}');", email, encryptedPassword, salt, Common.NowUtcLong());

        int check = command.ExecuteNonQuery();

      }
      catch (Exception e) {
        Console.WriteLine("Unable to add account to database " + e.Message);
        //return null;
      }

      // find the ID
      account = GetAccountByEmail(email);

      return account;
    }

    /// <summary>
    /// Get the number of entries in the accounts table
    /// </summary>
    /// <returns></returns>
    public int AccountsCount() {

      int count = 0;

      try {
        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = "select count(id) from accounts;";
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
    public void ShowAccounts() {

      Console.WriteLine("id\temail");

      try {
        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = "select * from accounts;";
        DbReader reader = command.ExecuteReader();

        while (reader.Read()) {
          Console.WriteLine(reader["id"] + "\t" + reader["email"]);
        }

        reader.Close();
      }
      catch (Exception) {
        //
      }

    }

    /// <summary>
    /// Get an account from the database via a known ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns>null if not found</returns>
    public Account GetAccountByID(String id) {

      Account account = null;

      try {
        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = "select * from accounts where id='" + id + "';";
        DbReader reader = command.ExecuteReader();

        while (reader.Read()) {

          int quota;

          try {
            quota = int.Parse(reader["quota"].ToString());
          }
          catch (Exception) {
            quota = Server.DefaultQuota;
          }
          if (quota == 0)
            quota = Server.DefaultQuota;  // or should this be Server.baseQuota?

          account = new Account(reader["id"].ToString(), reader["email"].ToString()
            , reader["password"].ToString(), reader["salt"].ToString(), quota);

          break;
        }

        reader.Close();
      }
      catch (Exception e) {
        Console.WriteLine("There was an error fetching the account " + e.Message);
      }

      return account;

    }


    /// <summary>
    /// Get an account from the database via a known email
    /// </summary>
    /// <param name="email"></param>
    /// <returns></returns>
    public Account GetAccountByEmail(String email) {

      Account account = null;

      try {

        DbCommand command = dbConnection.CreateCommand();
        command.CommandText = "select * from accounts where email='" + email + "'";//"select * from accounts";
        DbReader reader = command.ExecuteReader();

        while (reader.Read()) {

          int quota = 0;

          if (reader["quota"] == null)
            quota = Server.DefaultQuota;
          else {
            try {
              quota = int.Parse(reader["quota"].ToString());
            }
            catch (FormatException) {
              quota = Server.DefaultQuota;  // or should this be Server.baseQuota?
            }
          }

          account = new Account(reader["id"].ToString(), reader["email"].ToString()
            , reader["password"].ToString(), reader["salt"].ToString(), quota);

          break;
        }

        reader.Close();
      }
      catch (Exception e) {
        Console.WriteLine("There was an error fetching the account: " + e.Message);
      }

      return account;

    }


    /// <summary>
    /// Structure to represent a single account in the database
    /// </summary>
    public class Account {

      // TODO: make all fields readonly properties

      public String id = null;  //unique in DB       // TODO: make this an int to match database
      public String email = null; //unique in DB
      public String password = null;
      public String salt = null;
      public int quota = Server.DefaultQuota;

      public Account(String id, String email, String password, String salt, int quota) {

        if (email == String.Empty || password == String.Empty) {
          Console.WriteLine("Unable to create incomplete user");
          Common.ExitError();
        }

        if (id != null)
          this.id = id;

        this.email = email;
        this.quota = quota;

        this.salt = salt;
        this.password = password;
      }

      public override String ToString() {
        return "(id=" + id + ", email=" + email + ")";
      }
    }
  }

}