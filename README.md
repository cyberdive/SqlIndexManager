﻿# SQL Index Manager

This tool lets you quickly and easily find out the status of your indexes and discover which databases need maintenance.   
You can do maintenance through the UI, or generate a T-SQL script to run in SSMS.

## Key Features

* An incredibly fast describe engine
* Multiple databases scanning
* Advice on rebuilding or reorganizing
* Analysis of fragmentation results
* Configurable fragmentation thresholds
* One-click maintenance
* Command-line automation
* Automatic T-SQL script generation
* Columnstore maintenance support
* Statistics maintenance
* Collecting missing indexes
* Detect duplicate indexes
* Drop or disable unused indexes
* Index rebuild with compression options
* Export of scan results into Excel, CSV, HTML and text files
* Support for any editions of SQL Server 2008+ and Azure
* And a lot of other improvements :)

## Latest Version

You can download .zip file with the latest build of the master branch from [Releases](https://github.com/sergeysyrovatchenko/SQLIndexManager/releases)

## Topics

[SQL Index Manager – Free GUI Tool for Index Maintenance on SQL Server and Azure](https://www.codeproject.com/Articles/5162340/SQL-Index-Manager-Free-GUI-Tool-for-Index-Maintena)

## Screenshots

![SQL Index Manager](https://habrastorage.org/webt/jw/8s/vk/jw8svkqqg0ybvtdgt1cdoiulxsm.png)
![SQL Index Manager](https://habrastorage.org/webt/oq/rf/br/oqrfbrezv3yay64mj4gzpt9cjei.png)
![SQL Index Manager](https://habrastorage.org/webt/3x/iz/4n/3xiz4nf8-wneuchgmovaceynny0.png)

## Command Line

**/connection "Data Source=HOMEPC\SQL_2017;Integrated Security=True;User ID=HOMEPC\PC"**

This switch is used to specify connection string

**/databases "AdventureWorks2017;AdventureWorks2012;msdb"**

This switch is used to specify databases to manage indexes in

**/connectiontimeout 30**

This switch is used to specify connection timeout in seconds. Overrides value specified in the connection string

**/commandtimeout 120**

This switch is used to specify command execution timeout in seconds. Overrides default option (90 seconds)

**/online**

This switch is used to rebuild indexes with online option

**/ignoreheap**

This switch is used to ignore any heaps during maintenance

**/ignorecolumnstore**

This switch is used to ignore any columnstore indexes during maintenance

**/ignorebtree**

This switch is used to ignore any b-tree indexes during maintenance

**/missingindex**

Including missing indexes into regular maintenance

**/maxdop 8**

Configure the max degree of parallelism during maintenance. Overrides server option

**/reorganizethreshold 15**

Specifies reorganize the fragmentation threshold in %. Overrides default option (15%)

**/rebuildthreshold 30**

Specifies rebuild the fragmentation threshold in %. Overrides default option (30%)

**/minindexsize 32**

This switch is used to specify the minimum index size in MB. Overrides global option (6 MB)

**/predescribesize 128**

This switch is used to specify the predescribe index size in MB. Overrides global option (256 MB)

**/maxindexsize 8096**

This switch is used to specify the maximun index size in MB. Overrides global option (8 GB)
