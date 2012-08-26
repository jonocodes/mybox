#!/bin/bash

echo "              CLIENT FILESYSTEM"
ls -lR ~/Mybox

echo "              SERVER FILESYSTEM"
ls -lR ~/.mybox/serverData/1

echo "              CLIENT DB"
sqlite3 -separator '  ' ~/.mybox/client.db "select path, type, size, checksum from files"

echo "              SERVER DB"
sqlite3 -separator '  ' ~/.mybox/serverData/server.db "select path, parent, type, size, checksum from files"
