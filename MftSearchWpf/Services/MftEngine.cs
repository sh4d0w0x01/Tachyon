using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;

namespace MftSearchWpf.Services
{
    public class MftEngine
    {
        // --- P/Invoke Declarations ---
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;
        private const uint FSCTL_ENUM_USN_DATA = 0x000900b3;

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct USN_JOURNAL_DATA_V0
        {
            public ulong UsnJournalID;
            public long FirstUsn;
            public long NextUsn;
            public long LowestValidUsn;
            public long MaxUsn;
            public ulong MaximumSize;
            public ulong AllocationDelta;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MFT_ENUM_DATA_V0
        {
            public ulong StartFileReferenceNumber;
            public long LowUsn;
            public long HighUsn;
        }

        private struct RawFileEntry
        {
            public ulong ParentFrn;
            public string Name;
        }

        public static bool IsAdministrator(ISystemIdentity? identity = null)
        {
            var systemIdentity = identity ?? new SystemIdentity();

            if (!systemIdentity.IsWindowsOS())
                return false;

            return systemIdentity.IsAdministratorRole();
        }

        public static async Task<List<Models.FileRecord>> BuildIndexAsync()
        {
            return await Task.Run(() =>
            {
                var drives = GetNtfsDrives();

                // Using ConcurrentBag to safely collect results from multiple threads
                var allRecords = new ConcurrentBag<List<Models.FileRecord>>();

                Parallel.ForEach(drives, drive =>
                {
                    var driveRecords = ProcessDrive(drive);
                    if (driveRecords != null && driveRecords.Count > 0)
                    {
                        allRecords.Add(driveRecords);
                    }
                });

                // Flatten results and pre-allocate the final list
                int totalCapacity = 0;
                foreach (var list in allRecords)
                    totalCapacity += list.Count;

                // Usually over 1-2 million items
                var finalResults = new List<Models.FileRecord>(totalCapacity);
                foreach (var list in allRecords)
                {
                    finalResults.AddRange(list);
                }

                return finalResults;
            });
        }

        private static List<string> GetNtfsDrives()
        {
            var drivesToIndex = new List<string>();
            DriveInfo[] drives = DriveInfo.GetDrives();
            foreach (DriveInfo drive in drives)
            {
                if (drive.DriveType == DriveType.Fixed && drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    drivesToIndex.Add(drive.Name);
                }
            }
            return drivesToIndex;
        }

        private static List<Models.FileRecord>? ProcessDrive(string driveLetter)
        {
            // Pre-allocate a large dictionary to avoid resizing (e.g. 1M elements per drive)
            var index = new Dictionary<ulong, RawFileEntry>(1000000);

            string volumePath = "\\\\.\\" + driveLetter.TrimEnd('\\');
            IntPtr hVolume = CreateFile(
                volumePath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (hVolume == INVALID_HANDLE_VALUE)
            {
                return null;
            }

            try
            {
                USN_JOURNAL_DATA_V0 journalData = new USN_JOURNAL_DATA_V0();
                int journalDataSize = Marshal.SizeOf(journalData);
                IntPtr pJournalData = Marshal.AllocHGlobal(journalDataSize);

                try
                {
                    if (!DeviceIoControl(hVolume, FSCTL_QUERY_USN_JOURNAL, IntPtr.Zero, 0, pJournalData, (uint)journalDataSize, out _, IntPtr.Zero))
                        return null;

                    journalData = Marshal.PtrToStructure<USN_JOURNAL_DATA_V0>(pJournalData);
                }
                finally
                {
                    Marshal.FreeHGlobal(pJournalData);
                }

                MFT_ENUM_DATA_V0 enumData = new MFT_ENUM_DATA_V0
                {
                    StartFileReferenceNumber = 0,
                    LowUsn = 0,
                    HighUsn = journalData.NextUsn
                };

                int enumDataSize = Marshal.SizeOf(enumData);
                IntPtr pEnumData = Marshal.AllocHGlobal(enumDataSize);
                Marshal.StructureToPtr(enumData, pEnumData, false);

                // Use a larger buffer (e.g., 256KB or 512KB) for fewer IOCTL calls
                int bufferSize = 256 * 1024;
                IntPtr pBuffer = Marshal.AllocHGlobal(bufferSize);

                try
                {
                    while (true)
                    {
                        if (!DeviceIoControl(hVolume, FSCTL_ENUM_USN_DATA, pEnumData, (uint)enumDataSize, pBuffer, (uint)bufferSize, out uint bytesReturned, IntPtr.Zero))
                        {
                            int err = Marshal.GetLastWin32Error();
                            if (err == 38) break; // ERROR_HANDLE_EOF
                            break;
                        }

                        ulong nextStartFrn = (ulong)Marshal.ReadInt64(pBuffer);
                        enumData.StartFileReferenceNumber = nextStartFrn;
                        Marshal.StructureToPtr(enumData, pEnumData, false);

                        unsafe
                        {
                            byte* ptr = (byte*)pBuffer.ToPointer();
                            int offset = 8;

                            while (offset + 4 <= bytesReturned)
                            {
                                byte* recordPtr = ptr + offset;
                                uint recordLength = *(uint*)recordPtr;

                                if (recordLength == 0 || offset + recordLength > bytesReturned) break;

                                if (recordLength >= 6) // Ensure we can safely read MajorVersion
                                {
                                    ushort majorVersion = *(ushort*)(recordPtr + 4);

                                    if (majorVersion == 2 || majorVersion == 3)
                                    {
                                        ulong frn = 0;
                                        ulong parentFrn = 0;
                                        ushort fileNameLength = 0;
                                        ushort fileNameOffset = 0;
                                        bool validRecord = false;

                                        if (majorVersion == 2 && recordLength >= 60)
                                        {
                                            frn = *(ulong*)(recordPtr + 8);
                                            parentFrn = *(ulong*)(recordPtr + 16);
                                            fileNameLength = *(ushort*)(recordPtr + 56);
                                            fileNameOffset = *(ushort*)(recordPtr + 58);
                                            validRecord = true;
                                        }
                                        else if (majorVersion == 3 && recordLength >= 76)
                                        {
                                            frn = *(ulong*)(recordPtr + 8);
                                            parentFrn = *(ulong*)(recordPtr + 24);
                                            fileNameLength = *(ushort*)(recordPtr + 72);
                                            fileNameOffset = *(ushort*)(recordPtr + 74);
                                            validRecord = true;
                                        }

                                        if (validRecord && fileNameOffset + fileNameLength <= recordLength)
                                        {
                                            // Fast string creation using ReadOnlySpan and unsafe code
                                            var span = new ReadOnlySpan<char>(recordPtr + fileNameOffset, fileNameLength / 2);
                                            string fileName = new string(span);

                                            index[frn] = new RawFileEntry
                                            {
                                                Name = fileName,
                                                ParentFrn = parentFrn
                                            };
                                        }
                                    }
                                }
                                offset += (int)recordLength;
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pEnumData);
                    Marshal.FreeHGlobal(pBuffer);
                }

                // Path reconstruction
                return ResolvePaths(index, driveLetter);
            }
            finally
            {
                CloseHandle(hVolume);
            }
        }

        private static List<Models.FileRecord> ResolvePaths(Dictionary<ulong, RawFileEntry> index, string drive)
        {
            var results = new List<Models.FileRecord>(index.Count);

            foreach (var kvp in index)
            {
                ulong frn = kvp.Key;

                // Trace path up to root
                string fullPath = ResolvePathFast(index, frn, drive);

                results.Add(new Models.FileRecord
                {
                    FileName = kvp.Value.Name,
                    FullPath = fullPath
                });
            }

            return results;
        }

        private static string ResolvePathFast(Dictionary<ulong, RawFileEntry> index, ulong frn, string drive)
        {
            // We use a small local list to collect path segments, avoiding heavy allocations
            var parts = new List<string>(8);
            ulong currentFrn = frn;

            // Limit loop to avoid infinite cycles in corrupted MFT trees
            int loopLimit = 100;
            while (currentFrn != 0 && loopLimit > 0 && index.TryGetValue(currentFrn, out RawFileEntry entry))
            {
                parts.Add(entry.Name);
                if (currentFrn == entry.ParentFrn) break;
                currentFrn = entry.ParentFrn;
                loopLimit--;
            }

            // Construct string efficiently
            if (parts.Count == 0) return drive;

            parts.Reverse();
            return Path.Combine(drive, string.Join("\\", parts));
        }
    }
}
