using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CommandLine;

namespace MattRuwe.Dedupper
{
    class Program
    {
        static void Main(string[] args)
        {
            CreateDatabaseIfNotExists();

            Parser.Default.ParseArguments<FindDupsOptions, VerifyDupsOptions, ChooseDupsOptions>(args).MapResult(
                (FindDupsOptions o) =>
                {
                    GetFileHashes(o.SourcePath);
                    return 0;
                },
                (VerifyDupsOptions o) =>
                {
                    VerifyDups();
                    return 0;
                },
                (ChooseDupsOptions o) =>
                {
                    ChooseDups(o.SourcePath, o.BackupPath, o.Match);
                    return 0;
                },
                errs => 1);
        }

        private static void GetFileHashes(string currentPath)
        {
            using (var md5 = MD5.Create())
            {
                var files = Directory.GetFiles(currentPath);

                foreach (string file in files)
                {
                    Console.WriteLine($"File: {file}");
                    using (var connection = GetDbConnection())
                    {
                        var command = new SQLiteCommand($"SELECT count(1) FROM dups WHERE LOWER(filePath) = '{FixSql(file).ToLower()}'", connection);
                        var result = command.ExecuteScalar();
                        if ((long)result == 0)
                            using (var stream = File.OpenRead(file))
                            using (var streamMonitor = new StreamMonitor(stream))
                            {
                                var sw = Stopwatch.StartNew();
                                var hash = Convert.ToBase64String(md5.ComputeHash(streamMonitor));
                                sw.Stop();
                                var fileInfo = new FileInfo(file);
                                Console.WriteLine($"  Hash:         {hash}");
                                Console.WriteLine($"  Hash Calc ms: {sw.ElapsedMilliseconds}");
                                Console.WriteLine($"  Size:         {fileInfo.Length}");
                                command.CommandText = $"INSERT INTO dups (filePath, hash) VALUES ('{FixSql(file).ToLower()}', '{hash}')";
                                command.ExecuteNonQuery();
                            }
                        else
                            Console.WriteLine($"  Already been analyzed.");
                    }
                }

                var directories = Directory.GetDirectories(currentPath);
                foreach (string directory in directories)
                {
                    GetFileHashes(directory);
                }

            }
        }

        private static void VerifyDups()
        {
            Console.WriteLine("Verifying files paths stored in the database still exist on the filesystem.");
            using (var connection = GetDbConnection())
            {
                var fileCommand = new SQLiteCommand("SELECT * FROM dups", connection);
                var reader = fileCommand.ExecuteReader();
                var files = new List<string>();
                while (reader.Read())
                {
                    var filePath = (string)reader["filePath"];
                    files.Add(filePath);
                }

                var i = 0;
                foreach (var file in files)
                {
                    i++;
                    if (i%50 == 0)
                    {
                        var status = $"{i}/{files.Count} {((decimal)i / files.Count) * 100:0.00}%";
                        Console.Write(status);
                        Console.CursorLeft = 0;
                    }


                    if (File.Exists(file)) continue;
                    ClearLineAndMoveCursorToStart();
                    Console.WriteLine($"Removing from database: {file}");
                    var deleteCommand = new SQLiteCommand($"DELETE FROM dups WHERE FilePath = '{FixSql(file)}'", connection);
                    deleteCommand.ExecuteNonQuery();
                }
            }
        }

        private static void ChooseDups(string sourceRootFolderPath, string backupFolderPath, string patternMatch)
        {
            if (!Directory.Exists(backupFolderPath))
                Directory.CreateDirectory(backupFolderPath);

            using (var connection = GetDbConnection())
            {
                var hashCommand = new SQLiteCommand("SELECT hash FROM dups GROUP BY hash HAVING COUNT(hash) > 1", connection);
                var hashReader = hashCommand.ExecuteReader();
                while (hashReader.Read())
                {
                    var dupFilePaths = GetDupFilePathsForHash(hashReader, connection);
                    if (dupFilePaths.Count <= 1)
                        continue;

                    var fileToKeep = 0;

                    for (var i = 0; i < dupFilePaths.Count; i++)
                    {
                        var dupFilePath = dupFilePaths[i];
                        if (!string.IsNullOrWhiteSpace(patternMatch) && Regex.IsMatch(dupFilePath, patternMatch))
                        {
                            fileToKeep = i + 1;
                            break;
                        }
                    }

                    if (fileToKeep == 0)
                    {
                        for (var i = 0; i < dupFilePaths.Count; i++)
                        {
                            var dupFilePath = dupFilePaths[i];
                            Console.WriteLine($"  {i + 1}) {dupFilePath}");
                        }

                        Console.Write("Please choose a file to keep: ");
                        fileToKeep = int.Parse(Console.ReadKey(true).KeyChar.ToString());
                        Console.WriteLine();
                    }
                    for (var j = 0; j < dupFilePaths.Count; j++)
                    {
                        if (j != fileToKeep - 1)
                        {
                            var dupFilePath = dupFilePaths[j];
                            if (!File.Exists(dupFilePath))
                                continue;

                            var fileName = Path.GetFileName(dupFilePath);
                            var dupDirectoryName = Path.GetDirectoryName(dupFilePath);
                            var destFilePath = GetDestinationFilePath(sourceRootFolderPath, backupFolderPath, dupDirectoryName, fileName);
                            Console.WriteLine($"  Moving file: {dupFilePath} to {destFilePath}");
                            if (!File.Exists(destFilePath))
                            {
                                File.Move(dupFilePath, destFilePath);
                            }

                        }
                    }
                    Console.WriteLine();
                }
            }
        }

        private static void ClearLineAndMoveCursorToStart()
        {
            Console.Write(String.Empty.PadRight(Console.BufferWidth));
            Console.SetCursorPosition(0, Console.CursorTop - 1);
        }

        private static List<string> GetDupFilePathsForHash(SQLiteDataReader hashReader, SQLiteConnection connection)
        {
            var dupCommand = new SQLiteCommand($"SELECT * FROM dups WHERE hash = '{hashReader["hash"]}'", connection);
            var dupReader = dupCommand.ExecuteReader();
            Console.WriteLine(hashReader["hash"]);
            var i = 0;
            var dupFilePaths = new List<string>();
            while (dupReader.Read())
            {
                i++;
                var dupFilePath = (string)dupReader["filePath"];
                if (File.Exists(dupFilePath))
                {
                    dupFilePaths.Add(dupFilePath);
                }
            }
            return dupFilePaths;
        }

        private static string GetDestinationFilePath(string sourceRootFolderPath, string backupFolderPathRoot, string dupDirectoryName, string fileName)
        {
            string currentDirectoryPath = dupDirectoryName;
            var directories = new List<string>();
            while (currentDirectoryPath?.ToLower() != sourceRootFolderPath)
            {
                directories.Add(new DirectoryInfo(currentDirectoryPath).Name);
                currentDirectoryPath = new DirectoryInfo(currentDirectoryPath).Parent?.FullName;
            }
            directories.Reverse();
            var destinationFolderPath = backupFolderPathRoot;
            for (int k = 0; k <= directories.Count - 1; k++)
            {
                var directory = directories[k];
                destinationFolderPath = Path.Combine(destinationFolderPath, directory);
            }
            if (!Directory.Exists(destinationFolderPath))
                Directory.CreateDirectory(destinationFolderPath);
            var destFilePath = Path.Combine(destinationFolderPath, fileName);
            return destFilePath;
        }

        private static void CreateDatabaseIfNotExists()
        {
            var databaseFileNamePath = GetDatabaseFileNamePath();
            if (!File.Exists(databaseFileNamePath))
            {
                SQLiteConnection.CreateFile(databaseFileNamePath);
                using (var connection = GetDbConnection())
                {
                    var command = new SQLiteCommand("CREATE TABLE dups (filePath varchar(1000), hash varchar(1000))", connection);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static string GetDatabaseFileNamePath()
        {
            var databaseFileNamePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "DupDb.sqlite");
            return databaseFileNamePath;
        }

        private static SQLiteConnection GetDbConnection()
        {
            var databaseFileNamePath = GetDatabaseFileNamePath();
            var connection = new SQLiteConnection($"Data Source={databaseFileNamePath};version=3");
            connection.Open();
            return connection;
        }

        private static string FixSql(string sql)
        {
            return sql.Replace("'", "''");
        }
    }
}
