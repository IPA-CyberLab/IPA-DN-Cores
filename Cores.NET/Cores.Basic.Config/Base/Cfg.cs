﻿// IPA Cores.NET
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
using System.Threading;

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    class Cfg<T> : IDisposable
        where T : class, new()
    {
        public readonly object ConfigLock = new object();
        public T Config { get; set; }
        public T ConfigSafe
        {
            get
            {
                lock (this.ConfigLock)
                {
                    return (T)this.Config.CloneDeep();
                }
            }
        }
        public ulong ConfigHash
        {
            get
            {
                lock (this.ConfigLock)
                {
                    return this.Config.GetObjectHash();
                }
            }
        }
        public T DefaultConfig { get; }
        public string HeaderStr { get; }
        public bool ReadOnly { get; }
        public string FileName { get; }
        public string DirName { get; }
        public int ReadPollingIntervalSecs { get; }
        public int UpdatePollingIntervalSecs { get; }

        int current_version = 0;
        public int CurrentConfigVersion => this.current_version;

        ThreadObj polling_thread_obj = null;
        Event halt_event = new Event();
        bool halt = false;

        public Cfg(bool read_only = true, int read_polling_interval_secs = 2, int update_polling_interval_secs = 1, T default_config = null, string filename = null, string header_str = null)
        {
            if (default_config == null) default_config = new T();
            this.DefaultConfig = (T)default_config.CloneDeep();
            if (filename.IsEmpty()) filename = "@" + Str.GetLastToken(default_config.GetType().ToString(), '+', '.').MakeSafeFileName() + ".cfg";
            this.FileName = IO.InnerFilePath(filename);
            this.DirName = this.FileName.GetDirectoryName();
            IO.MakeDirIfNotExists(this.DirName);
            if (header_str.IsEmpty())
            {
                header_str = @"# Configuration file
# YAML format";
            }
            this.HeaderStr = header_str;

            this.ReadOnly = read_only;

            if (IO.IsFileExists(this.FileName) == false)
            {
                WriteConfigToFile(this.FileName, this.DefaultConfig, this.HeaderStr);
                this.Config = this.DefaultConfig;
            }

            // 初期状態の読み込み (エラー発生時は例外を出す)
            T t = ReadConfigFromFile(filename, null);
            if (t == null)
            {
                // ファイル内容が空の場合はデフォルト Config を使用
                t = this.DefaultConfig;
                WriteConfigToFile(this.FileName, this.DefaultConfig, this.HeaderStr);
            }

            this.Config = t;
            this.ReadPollingIntervalSecs = read_polling_interval_secs;
            this.UpdatePollingIntervalSecs = update_polling_interval_secs;

            current_version++;

            // スレッドの作成
            halt_event = new Event();
            polling_thread_obj = new ThreadObj(polling_thread);
        }

        void polling_thread(object param)
        {
            Thread.CurrentThread.IsBackground = true;

            long read_interval = this.ReadPollingIntervalSecs * 1000;
            long update_interval = this.UpdatePollingIntervalSecs * 1000;

            long last_read = 0;
            long last_update = 0;

            ulong last_hash = this.ConfigHash;

            while (halt == false)
            {
                halt_event.Wait(200);
                if (halt)
                {
                    break;
                }

                long now = Time.Tick64;

                if (this.ReadOnly == false)
                {
                    if (last_update == 0 || (now >= (last_update + update_interval)))
                    {
                        last_update = now;

                        ulong current_hash = this.ConfigHash;

                        if (last_hash != current_hash)
                        {
                            Dbg.WriteLine($"Memory configuration is updated. Saving to the file '{this.FileName.GetFileName()}'.");

                            try
                            {
                                WriteConfigToFile(this.FileName, this.Config, this.HeaderStr, this.ConfigLock);
                                current_version++;
                                last_hash = current_hash;
                            }
                            catch (Exception ex)
                            {
                                Dbg.WriteLine($"'{this.FileName.GetFileName()}': {ex.Message}");
                            }
                        }
                    }
                }

                if (last_read == 0 || (now >= (last_read + read_interval)))
                {
                    last_read = now;

                    T read_t;

                    try
                    {
                        read_t = ReadConfigFromFile(this.FileName, null);

                        if (read_t != null)
                        {
                            lock (this.ConfigLock)
                            {
                                ulong new_hash = read_t.GetObjectHash();
                                if (last_hash != new_hash)
                                {
                                    last_hash = new_hash;
                                    current_version++;
                                    this.Config = read_t;

                                    Dbg.WriteLine($"File configuration is modified. Loading from the file '{this.FileName.GetFileName()}'.");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Dbg.WriteLine($"'{this.FileName.GetFileName()}': {ex.Message}");
                    }
                }
            }

            if (this.ReadOnly == false)
            {
                ulong current_hash = this.ConfigHash;

                if (last_hash != current_hash)
                {
                    Dbg.WriteLine($"Memory configuration is updated. Saving to the file '{this.FileName.GetFileName()}'.");
                    try
                    {
                        WriteConfigToFile(this.FileName, this.Config, this.HeaderStr, this.ConfigLock);
                        current_version++;
                    }
                    catch (Exception ex)
                    {
                        Dbg.WriteLine(ex.ToString());
                    }
                }
            }
        }

        public static void WriteConfigToFile(string filename, T config, string header_str, object lock_obj = null)
        {
            if (lock_obj == null) lock_obj = new object();

            string str;

            lock (lock_obj)
            {
                str = Yaml.Serialize(config);
            }

            if (header_str.IsEmpty()) header_str = "\n\n";

            str = header_str + "\n\n" + str + "\n";

            str = str.NormalizeCrlfThisPlatform();

            string new_filename = filename + ".new";

            try
            {
                IO.WriteAllTextWithEncoding(new_filename, str, Str.Utf8Encoding, true);
                try
                {
                    IO.FileDelete(filename);
                }
                catch
                {
                }
                IO.FileRename(new_filename, filename);
            }
            finally
            {
                try
                {
                    IO.FileDelete(new_filename);
                }
                catch
                {
                }
            }
        }

        public static T ReadConfigFromFile(string filename, T default_config)
        {
            string body;

            try
            {
                string new_filename = filename + ".new";

                try
                {
                    IO.FileRename(new_filename, filename);
                }
                catch
                {
                }

                body = IO.ReadAllTextWithAutoGetEncoding(filename);
            }
            catch
            {
                if (default_config == null)
                {
                    throw;
                }
                else
                {
                    return (T)default_config.CloneDeep();
                }
            }

            try
            {
                T ret = Yaml.Deserialize<T>(body);

                return ret;
            }
            catch
            {
                if (default_config == null)
                {
                    throw;
                }
                else
                {
                    return (T)default_config.CloneDeep();
                }
            }
        }

        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;

                if (disposing)
                {
                    this.halt = true;

                    this.halt_event.Set();

                    this.polling_thread_obj.WaitForEnd();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}

