Mybox
=====
[https://github.com/jonocodes/mybox](https://github.com/jonocodes/mybox)
version 0.3.0 by Jono


Introduction
------------
Mybox is a centralized file hosting and synchronization system. The goal is for it to be an open source alternative to Dropbox. The software consists of a server and client component. One server can host multiple accounts and each account can be used on multiple computers, where all files are automatically kept in sync across those computers.

See the [wiki](https://github.com/jonocodes/mybox/wiki) for more details on [usage](https://github.com/jonocodes/mybox/wiki/Usage), [development](https://github.com/jonocodes/mybox/Development) and [the motivation](https://github.com/jonocodes/mybox/wiki/Project-Goals) behind this project.


Project Status
--------------
This is a work in progress. The client and server are operational, but unfinished. At this stage, the focus is on the core libraries so the most usable setup would be to use the command line version of Mybox. It should also be noted that socket encryption has been removed in the latest version but will be added back later. In addition the current version assumes that the time on the server and client are the same.


Requirements (Windows)
----------------------
*  .NET 3.5 runtime


Requirements (Other Systems)
----------------------------
*  Mono runtime
*  sqlite3 library installed


Quickstart
----------
Nothing needs to be installed. Mybox can be run in user mode without administrative privileges. Here is how get it going in Linux.


### Build the project ###

      Fetch the source: git clone git://github.com/jonocodes/mybox.git
      Build it with MonoDevelop, Visual Studio or command line with xbuild.


### On the server machine ###

Configure the server

      $ mono ServerSetup.exe

Create a user

      $ mono ServerAdmin.exe
      
Start the Server process

      $ mono Server.exe


### On the client machine ###

Run the setup program to configure the client

      $ mono ClientSetup.exe

Run the client

      $ mono Client.exe

You should now have a ~/Mybox directory which will be synchronized to the server.


