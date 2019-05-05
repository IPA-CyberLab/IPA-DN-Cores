// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    abstract class HiveSerializerOptions { }

    abstract class HiveSerializer
    {
        public HiveSerializerOptions Options { get; }

        public HiveSerializer(HiveSerializerOptions options)
        {
            this.Options = options;
        }

        abstract protected Memory<byte> SerializeImpl<T>(T obj);
        abstract protected T DeserializeImpl<T>(ReadOnlyMemory<byte> memory);

        public Memory<byte> Serialize<T>(T obj) => SerializeImpl(obj);
        public T Deserialize<T>(ReadOnlyMemory<byte> memory) => DeserializeImpl<T>(memory);
    }

    class JsonHiveSerializerOptions : HiveSerializerOptions
    {
        public DataContractJsonSerializerSettings JsonSettings { get; }

        public JsonHiveSerializerOptions(DataContractJsonSerializerSettings jsonSettings = null)
        {
            this.JsonSettings = jsonSettings;
        }
    }

    class JsonHiveSerializer : HiveSerializer
    {
        public new JsonHiveSerializerOptions Options => (JsonHiveSerializerOptions)base.Options;

        public JsonHiveSerializer(JsonHiveSerializerOptions options = null) : base(options == null ? new JsonHiveSerializerOptions() : options)
        {
        }

        protected override Memory<byte> SerializeImpl<T>(T obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            return obj.ObjectToRuntimeJson(Options.JsonSettings);
        }

        protected override T DeserializeImpl<T>(ReadOnlyMemory<byte> memory)
        {
            return memory.ToArray().RuntimeJsonToObject<T>(Options.JsonSettings);
        }
    }

    abstract class HiveStorageOptionsBase
    {
        public int MaxDataSize { get; }

        public HiveStorageOptionsBase(int maxDataSize = int.MaxValue)
        {
            this.MaxDataSize = maxDataSize;
        }
    }

    class FileHiveStorageOptions : HiveStorageOptionsBase
    {
        public FileSystem FileSystem { get; }
        public Copenhagen<string> RootDirectoryName { get; }
        public Copenhagen<FileOperationFlags> OperationFlags { get; }
        public Copenhagen<string> FileExt { get; } = ".json";
        public Copenhagen<string> TmpFileExtension { get; } = ".tmp";
        public Copenhagen<string> DefaultContainerName { get; } = "default";

        public FileHiveStorageOptions(FileSystem fileSystem, string rootDirectoryName, FileOperationFlags operationFlags = FileOperationFlags.WriteOnlyIfChanged, int maxDataSize = int.MaxValue)
            : base(maxDataSize)
        {
            this.FileSystem = fileSystem;
            this.RootDirectoryName = rootDirectoryName;
            this.OperationFlags = operationFlags;
        }
    }

    abstract class HiveStorageProvider : AsyncService
    {
        public HiveStorageOptionsBase Options;

        public HiveStorageProvider(HiveStorageOptionsBase options)
        {
            this.Options = options;
        }

        protected abstract Task SaveImplAsync(string dataName, ReadOnlyMemory<byte> data, CancellationToken cancel = default);
        protected abstract Task<Memory<byte>> LoadImplAsync(string dataName, CancellationToken cancel = default);

        public Task SaveAsync(string dataName, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.RunCriticalProcessAsync(true, cancel, c => SaveImplAsync(dataName, data, c));
        public Task<Memory<byte>> LoadAsync(string dataName, CancellationToken cancel = default)
            => this.RunCriticalProcessAsync(true, cancel, c => LoadImplAsync(dataName, c));
    }

    class FileHiveStorageProvider : HiveStorageProvider
    {
        public new FileHiveStorageOptions Options => (FileHiveStorageOptions)base.Options;

        FileSystem FileSystem => Options.FileSystem;
        FileSystemPathParser PathParser => FileSystem.PathParser;
        FileSystemPathParser SafePathParser = FileSystemPathParser.GetInstance(FileSystemStyle.Windows);

        AsyncLock LockObj = new AsyncLock();

        public FileHiveStorageProvider(FileHiveStorageOptions options) : base(options)
        {
        }

        string MakeFileName(string dataName)
        {
            dataName = Hive.NormalizeDataName(dataName);

            if (dataName.IsEmpty())
                dataName = Options.DefaultContainerName;

            string ret = PathParser.Combine(Options.RootDirectoryName, SafePathParser.MakeSafePathName(dataName), true);

            ret = PathParser.RemoveDangerousDirectoryTraversal(ret);

            ret = PathParser.NormalizeDirectorySeparatorIncludeWindowsBackslash(ret);

            return ret;
        }

        protected override async Task SaveImplAsync(string containerName, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            string filename = MakeFileName(containerName);
            string newFilename = filename + Options.TmpFileExtension;
            string directoryName = PathParser.GetDirectoryName(filename);

            if (data.IsEmpty == false)
            {
                try
                {
                    await FileSystem.CreateDirectoryAsync(directoryName, Options.OperationFlags, cancel);

                    try
                    {
                        await FileSystem.DeleteFileAsync(filename, Options.OperationFlags | FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                    }
                    catch { }

                    await FileSystem.MoveFileAsync(newFilename, filename, cancel);
                }
                finally
                {
                    await FileSystem.DeleteFileAsync(newFilename, Options.OperationFlags | FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                }
            }
            else
            {
                try
                {
                    await FileSystem.DeleteFileAsync(filename, FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                }
                catch { }

                try
                {
                    await FileSystem.DeleteFileAsync(newFilename, FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                }
                catch { }

                try
                {
                    await FileSystem.DeleteDirectoryAsync(directoryName, false, cancel);
                }
                catch { }
            }
        }

        protected override async Task<Memory<byte>> LoadImplAsync(string containerName, CancellationToken cancel = default)
        {
            string filename = MakeFileName(containerName);
            string newFilename = filename + Options.TmpFileExtension;

            try
            {
                try
                {
                    await FileSystem.MoveFileAsync(newFilename, filename);
                }
                catch { }

                return await FileSystem.ReadDataFromFileAsync(Options.RootDirectoryName, Options.MaxDataSize, Options.OperationFlags, cancel);
            }
            catch
            {
                throw;
            }
        }
    }

    static partial class CoresConfig
    {
        public static partial class DefaultHiveOptions
        {
            public static readonly Copenhagen<int> ReadPollingIntervalMsec = 2 * 1000;
            public static readonly Copenhagen<int> WritePollingIntervalMsec = 1 * 1000;
        }
    }

    class HiveOptions
    {
        public const int MinReadPollingIntervalMsec = 2 * 1000;
        public const int MinWritePollingIntervalMsec = 1 * 1000;

        public HiveSerializer Serializer { get; }
        public HiveStorageProvider StorageProvider { get; }
        public bool IsPollingEnabled { get; }

        public Copenhagen<int> ReadPollingIntervalMsec { get; } = CoresConfig.DefaultHiveOptions.ReadPollingIntervalMsec;
        public Copenhagen<int> WritePollingIntervalMsec { get; } = CoresConfig.DefaultHiveOptions.WritePollingIntervalMsec;

        public HiveOptions(string rootDirectoryName, HiveSerializer serializer = null)
            : this(new FileHiveStorageProvider(new FileHiveStorageOptions(LfsUtf8, rootDirectoryName))) { }

        public HiveOptions(HiveStorageProvider provider, bool enablePolling = false, int? readPollingInterval = null, int? writePollingInterval = null, HiveSerializer serializer = null)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (serializer == null) serializer = new JsonHiveSerializer();

            this.Serializer = serializer;
            this.StorageProvider = provider;

            if (enablePolling == false)
            {
                if (readPollingInterval.HasValue || writePollingInterval.HasValue)
                {
                    throw new ApplicationException("enablePolling is false while either readPollingInterval or writePollingInterval is specified.");
                }
            }

            if (readPollingInterval != null)
                this.ReadPollingIntervalMsec.SetValue(readPollingInterval.Value);

            if (writePollingInterval != null)
                this.WritePollingIntervalMsec.SetValue(writePollingInterval.Value);

            this.IsPollingEnabled = enablePolling;
        }
    }

    class Hive : AsyncServiceWithMainLoop
    {
        // Static states and methods
        public static readonly StaticModule Module = new StaticModule(InitModule, FreeModule);

        static readonly HashSet<Hive> RunningHivesList = new HashSet<Hive>();
        static readonly CriticalSection RunningHivesListLockObj = new CriticalSection();

        static void InitModule()
        {
        }

        static void FreeModule()
        {
            Hive[] runningHives;
            lock (RunningHivesListLockObj)
            {
                runningHives = RunningHivesList.ToArrayList();
            }
            foreach (Hive hive in runningHives)
            {
                hive.DisposeSafe(new CoresLibraryShutdowningException());
            }
            lock (RunningHivesListLockObj)
            {
                RunningHivesList.Clear();
            }
        }

        public static string NormalizeDataName(string name) => name.NonNullTrim().ToLower();

        // Instance states and methods
        public HiveOptions Options { get; }

        public HiveSerializer Serializer => Options.Serializer;
        public HiveStorageProvider StorageProvider => Options.StorageProvider;

        readonly HashSet<IHiveData> RegisteredHiveData = new HashSet<IHiveData>();
        readonly CriticalSection LockObj = new CriticalSection();

        public Hive(HiveOptions options)
        {
            try
            {
                this.Options = options;

                lock (RunningHivesListLockObj)
                {
                    Module.CheckInitalized();
                    RunningHivesList.Add(this);
                }

                if (this.Options.IsPollingEnabled)
                {
                    this.StartMainLoop(MainLoopProcAsync);
                }
            }
            catch
            {
                this.DisposeSafe();
                throw;
            }
        }

        async Task MainLoopProcAsync(CancellationToken cancel)
        {
            int readPollingInterval = Math.Max(HiveOptions.MinReadPollingIntervalMsec, Options.ReadPollingIntervalMsec);
            int writePollingInterval = Math.Max(HiveOptions.MinWritePollingIntervalMsec, Options.ReadPollingIntervalMsec);

            AsyncLocalTimer timer = new AsyncLocalTimer();

            long nextReadTick = 0;
            long nextWriteTick = 0;

            while (cancel.IsCancellationRequested == false)
            {
                if (timer.RepeatIntervalTimer(readPollingInterval, ref nextReadTick))
                {
                    await PollReadAllManagedHiveDataAsync(cancel);
                }

                if (timer.RepeatIntervalTimer(writePollingInterval, ref nextWriteTick))
                {
                    await PollWriteAllManagedHiveDataAsync(cancel);
                }

                await timer.WaitUntilNextTickAsync(cancel);
            }
        }

        async Task PollReadAllManagedHiveDataAsync(CancellationToken cancel)
        {
        }

        async Task PollWriteAllManagedHiveDataAsync(CancellationToken cancel, bool forceWriteAll = false)
        {
        }

        internal void RegisterInternal(IHiveData hiveData)
        {
            if (hiveData.IsManaged == false)
                throw new ArgumentException("hiveData.IsManaged == false");

            lock (LockObj)
            {
                CheckNotCanceled();

                RegisteredHiveData.Add(hiveData);
            }
        }

        internal void UnregisterInternal(IHiveData hiveData)
        {
            lock (LockObj)
            {
                RegisteredHiveData.Remove(hiveData);
            }
        }

        protected override async Task CleanupImplAsync(Exception ex)
        {
            // Flush all managed hives
            await PollWriteAllManagedHiveDataAsync(default, true);
        }

        protected override void DisposeImpl(Exception ex)
        {
            lock (RunningHivesListLockObj)
            {
                RunningHivesList.Remove(this);
            }

            this.Options.StorageProvider.DisposeSafe();
        }
    }

    [Flags]
    enum HiveDataPolicy
    {
        None = 0,
        AutoReadFromFile = 1,
        AutoWriteToFile = 2,
    }

    interface IHiveData
    {
        HiveDataPolicy Policy { get; }
        string DataName { get; }
        Hive Hive { get; }
        bool IsManaged { get; }
    }

    class HiveData<T> : IHiveData where T: class, new()
    {
        public HiveDataPolicy Policy { get; }
        public string DataName { get; }
        public Hive Hive { get; }
        public bool IsManaged { get; } = false;
        public CriticalSection ReaderWriterLockObj { get; } = new CriticalSection();

        T InternalData = null;

        class HiveDataState
        {
            public T Data;
            public Memory<byte> SerializedData;
            public long Hash;
        }

        public HiveData(Hive hive, string dataName, HiveDataPolicy policy = HiveDataPolicy.None)
        {
            this.Hive = hive;
            this.Policy = policy;
            this.DataName = Hive.NormalizeDataName(dataName);

            if (policy.BitAny(HiveDataPolicy.AutoReadFromFile | HiveDataPolicy.AutoWriteToFile))
            {
                this.IsManaged = true;
            }

            if (this.IsManaged)
            {
                if (hive.Options.IsPollingEnabled == false)
                    throw new ArgumentException($"policy = {policy.ToString()} while the Hive object doesn't support polling.");

                this.Hive.RegisterInternal(this);
            }
        }

        async Task<HiveDataState> LoadDataCoreAsync(CancellationToken cancel)
        {
            HiveDataState ret = new HiveDataState();

            ret.SerializedData = await Hive.StorageProvider.LoadAsync(this.DataName, cancel);
            ret.Hash = Secure.HashSHA1AsLong(ret.SerializedData.Span);
            ret.Data = Hive.Serializer.Deserialize<T>(ret.SerializedData);

            return ret;
        }

        async Task SaveDataCoreAsync(ReadOnlyMemory<byte> serializedData, CancellationToken cancel)
        {
            await Hive.StorageProvider.SaveAsync(this.DataName, serializedData, cancel);
        }
    }
}

