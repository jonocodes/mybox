/**
    Mybox version 0.3.0
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
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace mybox {

  /// <summary>
  /// Command line executable for administering accounts on the server
  /// </summary>
  class ServerAdmin {

    private AccountsDB accountsDb = null;

    private void deleteAccount(){

      accountsDb.ShowAccounts();

      Console.Write("Which account would you like to delete?\naccount number> ");

      String input = null;

      input = Console.ReadLine();

      AccountsDB.Account thisAccount = accountsDb.GetAccountByID(input);

      if (thisAccount == null) {
        Console.WriteLine("account " + input + " does not exist.");
        return;
      }

      Console.Write("Are you sure you want to delete " + thisAccount + "\ny/n> ");

      input = Console.ReadLine();

      if (input == ("y")) {

        // delete the data directory
        String userDir = Server.GetAbsoluteDataDirectory(thisAccount);

        if (!Common.DeleteLocal(userDir))
          Console.WriteLine("There was a problem deleting the user directory " + userDir);
      
        // update the database
        if(accountsDb.DeleteAccount(thisAccount.id)) 
          Console.WriteLine("Account deleted");
        else
          Console.WriteLine("Unable to delete account from database");
      }

    }


    private void addAccount() {

      // gather user input
    
      Console.Write("Add a new account.\nemail> ");
      String email = Console.ReadLine();
    
      Console.Write("password> ");
      String password = Console.ReadLine();
    
      // validate the entered fields
    
      if (!accountsDb.ValidatePotentialAccount(email, password)){
        Console.WriteLine("New account is invalid or conflicts with an existing one.");
        return;
      }
    

      // update the database
      String salt = null, encryptedPassword = null;

      try{
        salt = Common.GenerateSalt(8);
        encryptedPassword = Common.EncryptPassword(password, salt);
      } catch (Exception e) {
        Console.WriteLine("Password encryption error " + e.Message);
      }
    
      AccountsDB.Account account = accountsDb.AddAccount(email, encryptedPassword, salt);
      
      if (account == null) {
        Console.WriteLine("Error: Unable to add account to database");
        return;
      }


      String userDir = Server.GetAbsoluteDataDirectory(account);

      if (!Common.CreateLocalDirectory(userDir)) {
        Console.WriteLine("There was a problem when creating the data directory: " + userDir);
        Common.ExitError();
      }

    }

    public ServerAdmin(String configFile) {

      Server.LoadConfig(configFile);

      accountsDb = new AccountsDB(Server.AccountsDbfile);

      Console.WriteLine("Starting ServerAdmin command line utility...");

      char choice = ' ';
//      String input = null;
    
      // menu
      while (choice != 'q') {
        Console.WriteLine("  l) List accounts");
        //Server.printMessage("  p) Show encyrpted password");
        Console.WriteLine("  a) Add account");
        Console.WriteLine("  d) Delete account");
        Console.WriteLine("  q) Quit");
        Console.Write("  > ");
        
        ConsoleKeyInfo cki = Console.ReadKey(false);

        choice = cki.KeyChar;

        Console.WriteLine();

        switch (choice) {
          case 'l':
            accountsDb.ShowAccounts();
            break;
          case 'd':
            deleteAccount();
            break;
          case 'a':
            addAccount();
            break;
          //case 'p':
          //  showEncryptedPassword();
          //  break;
        }
      }
      //dbConnection.close();

    }

    /// <summary>
    /// Start the command line executable and handle command line arguments
    /// </summary>
    /// <param name="args"></param>
    static void Main(string[] args) {

      OptionSet options = new OptionSet();

      String configFile = Server.DefaultConfigFile;

      options.Add("c|configfile=", "configuration file (default=" + configFile + ")", delegate(string v) {
        configFile = v;
      });

      options.Add("h|help", "show help screen", delegate(string v) {
        Common.ShowCliHelp(options, System.Reflection.Assembly.GetExecutingAssembly());
      });

      options.Add("v|version", "print the Mybox version", delegate(string v) {
        Console.WriteLine(Common.AppVersion);
        System.Diagnostics.Process.GetCurrentProcess().Kill();
      });


      // Note: all additional arguments are invalid since it does not take non-options

      List<string> extra = new List<string>();

      try {
        extra = options.Parse(args);
      } catch (OptionException) {
        Console.WriteLine("Invalid argument entered");

        // print the help screen
        Common.ShowCliHelp(options, System.Reflection.Assembly.GetExecutingAssembly());
      }

      if (extra.Count > 0) {
        Console.WriteLine("Invalid argument entered");

        // print the help screen
        Common.ShowCliHelp(options, System.Reflection.Assembly.GetExecutingAssembly());
      }

      new ServerAdmin(configFile);
    }
  }
}
