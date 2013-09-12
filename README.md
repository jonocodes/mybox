Mybox
=====
[https://github.com/jonocodes/mybox](https://github.com/jonocodes/mybox)
version 0.4.0 by Jono


Introduction
------------
Mybox is a centralized file hosting and synchronization system. The goal is for it to be an open source alternative to Dropbox - both the client and server components. One server can host multiple accounts and each account can be used on multiple computers, where all files are automatically kept in sync across those computers.

See the [wiki](https://github.com/jonocodes/mybox/wiki) for more details on [usage](https://github.com/jonocodes/mybox/wiki/Usage), [development](https://github.com/jonocodes/mybox/wiki/Development) and [the motivation](https://github.com/jonocodes/mybox/wiki/Project-Goals) behind this project.


Project Status
--------------
The client and server are operational, but unfinished. The most stable setup would be to use the command line version of Mybox. It should also be noted that socket encryption has been removed in the latest version but will be added back later. In addition the current version assumes that the time on the server and client are the same, so when testing you might want to run the server and client on the same machine.

In the summer of 2012 I started working on two new features. One was to use Owncloud as a backend server. My intent was to develop a desktop application so the idea of having a community already creating a nice web facing server was great. I went as far as I could take it at that time and found that they were not far along enough in their project to support the API calls I needed. It is certainly possible that later iterations of Owncloud would work, but I have not checked.

I also started the "checksum" feature branch, which I saw as the future of Mybox since it does not require synced system clocks and majorly improves syncing large directories. If I pick up the project again, I will probably continue on that branch.

But for now I have moved on to [other projects](https://github.com/jonocodes). While I would love to bring Mybox to a more polished state I do not have plans to work on it myself in the foreseeable future. I am happy to help anyone pick up where I left off. For those of you who have followed this project and showed interest in its development, I thank you.


Requirements
------------
*  .NET 3.5 runtime or Mono equivalent
*  sqlite3 library installed if in Linux (on the client side)
*  MySQL (on the server side)


Quickstart
----------
Here is how get it going in Linux. Start by building the solution with MonoDevelop, Visual Studio or xbuild.

### On the server machine ###

In MySQL create an 'mybox' database with nothing in it.

Configure Mybox server

      $ mono ServerSetup.exe

Start the Mybox server process

      $ mono Server.exe


### On the client machine ###

Run the setup program to configure the client

      $ mono ClientSetup.exe

Run the client

      $ mono Client.exe

You should now have a ~/Mybox directory which will be automatically synchronized to the server.
