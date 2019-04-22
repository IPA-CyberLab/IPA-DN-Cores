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
using System.Threading;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

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
        public int CurrentConfigVersion { get; private set; } = 0;

        ThreadObj PollingThreadObj = null;
        Event HaltEvent = new Event();
        bool HaltFlag = false;

        public Cfg(bool readOnly = true, int readPollingIntervalSecs = 2, int updatePollingIntervalSecs = 1, T defaultConfig = null, string filename = null, string headerStr = null)
        {
            if (defaultConfig == null) defaultConfig = new T();
            this.DefaultConfig = (T)defaultConfig.CloneDeep();
            if (filename.IsEmpty()) filename = "@" + Str.GetLastToken(defaultConfig.GetType().ToString(), '+', '.').MakeSafeFileName() + ".cfg";
            this.FileName = IO.InnerFilePath(filename);
            this.DirName = this.FileName.GetDirectoryName();
            IO.MakeDirIfNotExists(this.DirName);
            if (headerStr.IsEmpty())
            {
                headerStr = @"# Configuration file
# YAML format";
            }
            this.HeaderStr = headerStr;

            this.ReadOnly = readOnly;

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
            this.ReadPollingIntervalSecs = readPollingIntervalSecs;
            this.UpdatePollingIntervalSecs = updatePollingIntervalSecs;

            CurrentConfigVersion++;

            // スレッドの作成
            HaltEvent = new Event();
            PollingThreadObj = new ThreadObj(PollingThreadProc);
        }

        void PollingThreadProc(object param)
        {
            Thread.CurrentThread.IsBackground = true;

            long readInterval = this.ReadPollingIntervalSecs * 1000;
            long updateInterval = this.UpdatePollingIntervalSecs * 1000;

            long lastRead = 0;
            long lastUpdate = 0;

            ulong lastHash = this.ConfigHash;

            while (HaltFlag == false)
            {
                HaltEvent.Wait(200);
                if (HaltFlag)
                {
                    break;
                }

                long now = Time.Tick64;

                if (this.ReadOnly == false)
                {
                    if (lastUpdate == 0 || (now >= (lastUpdate + updateInterval)))
                    {
                        lastUpdate = now;

                        ulong currentHash = this.ConfigHash;

                        if (lastHash != currentHash)
                        {
                            Dbg.WriteLine($"Memory configuration is updated. Saving to the file '{this.FileName.GetFileName()}'.");

                            try
                            {
                                WriteConfigToFile(this.FileName, this.Config, this.HeaderStr, this.ConfigLock);
                                CurrentConfigVersion++;
                                lastHash = currentHash;
                            }
                            catch (Exception ex)
                            {
                                Dbg.WriteLine($"'{this.FileName.GetFileName()}': {ex.Message}");
                            }
                        }
                    }
                }

                if (lastRead == 0 || (now >= (lastRead + readInterval)))
                {
                    lastRead = now;

                    T read_t;

                    try
                    {
                        read_t = ReadConfigFromFile(this.FileName, null);

                        if (read_t != null)
                        {
                            lock (this.ConfigLock)
                            {
                                ulong newHash = read_t.GetObjectHash();
                                if (lastHash != newHash)
                                {
                                    lastHash = newHash;
                                    CurrentConfigVersion++;
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
                ulong currentHash = this.ConfigHash;

                if (lastHash != currentHash)
                {
                    Dbg.WriteLine($"Memory configuration is updated. Saving to the file '{this.FileName.GetFileName()}'.");
                    try
                    {
                        WriteConfigToFile(this.FileName, this.Config, this.HeaderStr, this.ConfigLock);
                        CurrentConfigVersion++;
                    }
                    catch (Exception ex)
                    {
                        Dbg.WriteLine(ex.ToString());
                    }
                }
            }
        }

        public static void WriteConfigToFile(string filename, T config, string headerStr, object lockObj = null)
        {
            if (lockObj == null) lockObj = new object();

            string str;

            lock (lockObj)
            {
                str = Yaml.Serialize(config);
            }

            if (headerStr.IsEmpty()) headerStr = "\n\n";

            str = headerStr + "\n\n" + str + "\n";

            str = str.NormalizeCrlf(CrlfStyle.LocalPlatform);

            string newFilename = filename + ".new";

            try
            {
                IO.WriteAllTextWithEncoding(newFilename, str, Str.Utf8Encoding, true);
                try
                {
                    IO.FileDelete(filename);
                }
                catch
                {
                }
                IO.FileRename(newFilename, filename);
            }
            finally
            {
                try
                {
                    IO.FileDelete(newFilename);
                }
                catch
                {
                }
            }
        }

        public static T ReadConfigFromFile(string filename, T defaultConfig)
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
                if (defaultConfig == null)
                {
                    throw;
                }
                else
                {
                    return (T)defaultConfig.CloneDeep();
                }
            }

            try
            {
                T ret = Yaml.Deserialize<T>(body);

                return ret;
            }
            catch
            {
                if (defaultConfig == null)
                {
                    throw;
                }
                else
                {
                    return (T)defaultConfig.CloneDeep();
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
                    this.HaltFlag = true;

                    this.HaltEvent.Set();

                    this.PollingThreadObj.WaitForEnd();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}

