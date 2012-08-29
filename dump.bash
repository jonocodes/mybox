#!/bin/bash

echo "              CLIENT 1"
ls -lR ~/Mybox

echo "              CLIENT 2"
ls -lR ~/Mybox2

echo "              SERVER"
ls -lR ~/.mybox/serverData/1

echo "              CLIENT DB 1"
sqlite3 -separator '  ' ~/.mybox/client.db "select path, type, size, checksum from files ORDER BY path"

echo "              CLIENT DB 2"
sqlite3 -separator '  ' ~/.mybox2/client.db "select path, type, size, checksum from files ORDER BY path"

echo "              SERVER DB"
sqlite3 -separator '  ' ~/.mybox/serverData/server.db "select path, parent, type, size, checksum from files ORDER BY path"
