# Dedupper

## Usage
There are 3 main operating modes for the dedupper:  
1) **Find** - Examines each file and stores a hash value for comparison
2) **Choose** - Once the `find` process is complete, you need to choose which of the duplicate files you wish to keep.  This process will enumerate all of the duplicates and interactive allow you to choose which of the files you wish to keep.  It will move the remaining files to a backup location so you can manually remove them later.  *NOTE* The Dedupper will **NEVER** delete your files!
3) **Verify** - Once the files have been moved from the original location, you need to cleanup the database of file hashes.  Verify will look through all of the files in the database and verify that they still exist on disk.  If they do not, this command will remove the files from the application database where it stores the hashes.

## Find Options
- **--source (-s)** - the root file path to search for files.  The application will recursively look through all of the files in this location, read their contents, calculate a hash and store the results in the application database

**Example**  
`dotnet Dedupper.dll find --source "c:\my files"`

## Choose Options
- **--source (-s)** - The root file path to search for files.  This is needed so the folder structure can be acurately recreated in the backup location location.
- **--backup (-b)** - The path where files should be moved to if they are not selected as the file to retain.
- **--match (-m)** - A regular expression used to accelerate the process of choosing a file.  The first file that the application encounters that matches the regular expression will be selected as the file to keep.  The other files will automatically be moved to the backup folder.

**Example**  - Selects files that contains numbers in their path as the file to keep:  
`dotnet Dedupper.dll choose --source "c:\my files" --backup "c:\my backup files" --match "[0-9]"`

## Verify Options
- **None**

**Example** - Verifies that all files contained within the application database still exist on disk.  If the database contains a file that doesn't exist on disk, the application will remove it from the database.

`dotnet Dedupper.dll verify`