using EqualityComparer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DirDiff.Cmd
{
    class Program
    {
        static GenericEqualityComparer<DirectoryInfo> dirNameComparer = new GenericEqualityComparer<DirectoryInfo>((d1, d2) => d1.Name == d2.Name, dir => dir.Name.GetHashCode());
        static GenericEqualityComparer<FileInfo> fileNameComparer = new GenericEqualityComparer<FileInfo>((file1, file2) => file1.Name == file2.Name, file => file.Name.GetHashCode());

        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine($"Usage: {Assembly.GetEntryAssembly().GetName().Name} [path1] [path2]");
                return;
            }

            var dir1 = new DirectoryInfo(args[0]);
            var dir2 = new DirectoryInfo(args[1]);

            DiffDirAsync(dir1, dir2).Wait();
        }

        static async Task DiffDirAsync(DirectoryInfo dir1, DirectoryInfo dir2)
        {

            var subDirs1 = dir1.GetDirectories();
            var subDirs2 = dir2.GetDirectories();

            var commonDirs = subDirs1.Intersect(subDirs2, dirNameComparer).ToArray();

            foreach (var extraDir in subDirs1.Except(commonDirs, dirNameComparer))
            {
                LeftOnly(extraDir.FullName);
            }
            foreach (var extraDir in subDirs2.Except(commonDirs, dirNameComparer))
            {
                RightOnly(extraDir.FullName);
            }

            var commonDirsIn1 = commonDirs;
            var commonDirsIn2 = subDirs2.Intersect(commonDirs, dirNameComparer).ToArray();

            for (int i = 0; i < commonDirsIn1.Length; i++)
            {
                await DiffDirAsync(commonDirsIn1[i], commonDirsIn2[i]);
            }

            await DiffFilesAsync(dir1, dir2);
        }

        static async Task DiffFilesAsync(DirectoryInfo dir1, DirectoryInfo dir2)
        {
            var files1 = dir1.GetFiles();
            var files2 = dir2.GetFiles();
            var commonFiles1 = files1.Intersect(files2, fileNameComparer).ToArray();
            var commonFiles2 = files2.Intersect(commonFiles1, fileNameComparer).ToArray();

            foreach (var extraFile in files1.Except(commonFiles1))
            {
                LeftOnly(extraFile.FullName);
            }
            foreach (var extraFile in files2.Except(commonFiles2))
            {
                LeftOnly(extraFile.FullName);
            }

            for (int i = 0; i < commonFiles1.Length; i++)
            {
                if (!await DiffFileAsync(commonFiles1[i], commonFiles2[i]))
                {
                    Different(commonFiles1[i].FullName, commonFiles2[i].FullName);
                }
            }
        }

        private static async Task<bool> DiffFileAsync(FileInfo fileInfo1, FileInfo fileInfo2)
        {
            if (fileInfo1.Length != fileInfo2.Length)
            {
                return false;
            }
            var size = fileInfo1.Length;

            using (var stream1 = fileInfo1.OpenRead())
            using (var stream2 = fileInfo2.OpenRead())
            {
                var bufferSize = 4096;
                var blockSize = 16 * 1024 * 1024;
                var buffer1 = new byte[bufferSize];
                var buffer2 = new byte[bufferSize];
                if (size <= blockSize)
                {
                    return await CompareBlock(blockSize, bufferSize, stream1, stream2, buffer1, buffer2);
                }
                else if (size <= 64 * blockSize)
                {
                    return await CompareWithSkip(blockSize, bufferSize, 3, stream1, stream2, buffer1, buffer2);
                }
                else if (size <= 256 * blockSize)
                {
                    return await CompareWithSkip(blockSize, bufferSize, 7, stream1, stream2, buffer1, buffer2);
                }
                else
                {
                    return await CompareWithSkip(blockSize, bufferSize, 15, stream1, stream2, buffer1, buffer2);
                }
            }
        }

        private static async Task<bool> CompareWithSkip(int blockSize, int bufferSize, int skipRatio, FileStream stream1, FileStream stream2, byte[] buffer1, byte[] buffer2)
        {
            while (stream1.Position < stream1.Length)
            {
                if (!await CompareBlock(blockSize, bufferSize, stream1, stream2, buffer1, buffer2))
                {
                    return false;
                }
                stream1.Seek(blockSize * skipRatio, SeekOrigin.Current);
                stream2.Seek(blockSize * skipRatio, SeekOrigin.Current);
            }

            return await CompareLastBlock(blockSize, bufferSize, stream1, stream2, buffer1, buffer2);
        }

        private static async Task<bool> CompareLastBlock(int blockSize, int bufferSize, FileStream stream1, FileStream stream2, byte[] buffer1, byte[] buffer2)
        {
            stream1.Seek(-blockSize, SeekOrigin.End);
            stream2.Seek(-blockSize, SeekOrigin.End);
            if (!await CompareBlock(blockSize, bufferSize, stream1, stream2, buffer1, buffer2))
            {
                return false;
            }
            return true;
        }

        private static async Task<bool> CompareBlock(
            int blockSize, int bufferSize, FileStream stream1, FileStream stream2, byte[] buffer1, byte[] buffer2)
        {
            for (var i = 0; i < blockSize / bufferSize; i++)
            {
                var results = await Task.WhenAll(
                    stream1.ReadAsync(buffer1, 0, bufferSize),
                    stream2.ReadAsync(buffer2, 0, bufferSize));

                if (results.All(r => r == 0))
                {
                    break;
                }

                if (!AreArraysEqual(bufferSize, buffer1, buffer2))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AreArraysEqual(int bufferSize, byte[] buffer1, byte[] buffer2)
        {
            for (int i = 0; i < bufferSize; i++)
            {
                if (buffer1[i] != buffer2[i])
                {
                    return false;
                }
            }
            return true;
        }

        static void LeftOnly(object value)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("<-- " + value);
            Console.ResetColor();
        }

        static void RightOnly(object value)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("--> " + value);
            Console.ResetColor();
        }
        static void Different(object value1, object value2)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(value1 + " <-> " + value2);
            Console.ResetColor();
        }
    }
}
