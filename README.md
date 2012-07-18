Mybox
=====
[https://github.com/jonocodes/mybox](https://github.com/jonocodes/mybox)
version 0.3.0 by Jono


Introduction
------------
Mybox is a centralized file hosting and synchronization system. The goal is for it to be an open source alternative to Dropbox - both the client and server components. One server can host multiple accounts and each account can be used on multiple computers, where all files are automatically kept in sync across those computers.

This version of Mybox makes use of [Owncloud](http://owncloud.org) on the server side.

See the [wiki](https://github.com/jonocodes/mybox/wiki) for more details on [usage](https://github.com/jonocodes/mybox/wiki/Usage), [development](https://github.com/jonocodes/mybox/wiki/Development) and [the motivation](https://github.com/jonocodes/mybox/wiki/Project-Goals) behind this project.


Project Status
--------------
This is a work in progress. The client and server are operational, but unfinished. At this stage, the focus is on the core libraries so the most usable setup would be to use the command line version of Mybox. It should also be noted that socket encryption has been removed in the latest version but will be added back later. In addition the current version assumes that the time on the server and client are the same, so when testing you might want to run the server and client on the same machine.


Requirements (Server)
---------------------
* Linux
* Owncloud 4

Requirements (Client)
---------------------
*  .NET 3.5 runtime or Mono equivalent
*  sqlite3 library installed if in Linux


Quickstart
----------
Here is how get it going in Linux. Start by building the solution with MonoDevelop, Visual Studio or xbuild.

### On the server machine ###

Install and configure [Owncloud](http://owncloud.org/support/install/)

Create a user account in Owncloud and then log into the web interface once as that user so the account gets fully initialized.

Configure Mybox server

      $ mono ServerSetup.exe

Start the Mybox server process as the same user who hosts the Owncloud files

      $ sudo -u http mono Server.exe -c ~/.mybox/mybox_server.ini


### On the client machine ###

Run the setup program to configure the client

      $ mono ClientSetup.exe

Run the client

      $ mono Client.exe

You should now have a ~/Mybox directory which will be automatically synchronized to the server.

### View files on server ###

To see you files in the Owncloud web interface you should be able to visit your server address in a web browser.

