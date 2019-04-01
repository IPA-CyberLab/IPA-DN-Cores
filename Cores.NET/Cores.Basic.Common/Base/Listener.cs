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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.GlobalFunctions.Basic;

namespace IPA.Cores.Basic
{
    enum ListenerStatus
    {
        Trying = 0,
        Listening = 1,
    }

    delegate void AcceptProc(Listener listener, Sock sock, object param);

    class Listener
    {
        public const int ListenRetryTimeDefault = 2 * 1000;

        int listenRetryTime;
        public int ListenRetryTime
        {
            get { return listenRetryTime; }
            set { listenRetryTime = value; }
        }
        object lockObj;
        int port;
        public int Port
        {
            get { return port; }
        }
        ThreadObj thread;
        Sock sock;
        public Sock Sock
        {
            get { return sock; }
        }
        Event eventObj;
        bool halt;
        ListenerStatus status;
        public ListenerStatus Status
        {
            get { return status; }
        }
        AcceptProc acceptProc;
        public AcceptProc AcceptProc
        {
            get { return acceptProc; }
        }
        object acceptParam;
        public object AcceptParam
        {
            get { return acceptParam; }
        }
        bool localOnly;
        public bool LocalOnly
        {
            get { return localOnly; }
        }
        bool getHostName;
        public bool GetHostName
        {
            get { return getHostName; }
            set { getHostName = value; }
        }

        public Listener(int port, AcceptProc acceptProc, object acceptParam)
        {
            init(port, acceptProc, acceptParam, false, false);
        }
        public Listener(int port, AcceptProc acceptProc, object acceptParam, bool localOnly)
        {
            init(port, acceptProc, acceptParam, localOnly, false);
        }
        public Listener(int port, AcceptProc acceptProc, object acceptParam, bool localOnly, bool getHostName)
        {
            init(port, acceptProc, acceptParam, localOnly, getHostName);
        }

        // 初期化
        void init(int port, AcceptProc acceptProc, object acceptParam, bool localOnly, bool getHostName)
        {
            this.lockObj = new object();
            this.port = port;
            this.acceptProc = acceptProc;
            this.acceptParam = acceptParam;
            this.status = ListenerStatus.Trying;
            this.eventObj = new Event();
            this.halt = false;
            this.localOnly = localOnly;
            this.getHostName = getHostName;
            this.listenRetryTime = ListenRetryTimeDefault;

            // スレッドの作成
            ThreadObj thread = new ThreadObj(new ThreadProc(ListenerThread));

            thread.WaitForInit();
        }

        // 停止
        public void Stop()
        {
            Sock s;

            lock (this.lockObj)
            {
                if (this.halt)
                {
                    return;
                }

                this.halt = true;

                s = this.sock;
            }

            if (s != null)
            {
                s.Disconnect();
            }

            this.eventObj.Set();

            this.thread.WaitForEnd();
        }

        // TCP 受付完了
        void tcpAccepted(Sock s)
        {
            ThreadObj t = new ThreadObj(new ThreadProc(tcpAcceptedThread), s);
            t.WaitForInit();
        }

        void tcpAcceptedThread(object param)
        {
            Sock s = (Sock)param;

            ThreadObj.NoticeInited();

            this.acceptProc(this, s, this.acceptParam);
        }

        // スレッド
        public void ListenerThread(object param)
        {
            Sock new_sock, s;
            int num_failed;

            this.thread = ThreadObj.GetCurrentThreadObj();

            this.status = ListenerStatus.Trying;

            ThreadObj.NoticeInited();

            while (true)
            {
                bool firstFailed = true;
                this.status = ListenerStatus.Trying;

                // Listen を試みる
                while (true)
                {
                    if (this.halt)
                    {
                        return;
                    }

                    try
                    {
                        s = Sock.Listen(this.port, this.localOnly);

                        this.sock = s;

                        break;
                    }
                    catch
                    {
                        if (firstFailed)
                        {
                            firstFailed = false;
                        }

                        this.eventObj.Wait(this.listenRetryTime);

                        if (this.halt)
                        {
                            return;
                        }
                    }
                }

                this.status = ListenerStatus.Listening;

                if (this.halt)
                {
                    this.sock.Disconnect();
                    break;
                }

                num_failed = 0;

                // Accept ループ
                while (true)
                {
                    // Accept する
                    new_sock = this.sock.Accept(this.getHostName);
                    if (new_sock != null)
                    {
                        // 成功
                        tcpAccepted(new_sock);
                    }
                    else
                    {
                        // 失敗
                        if (this.halt == false)
                        {
                            if ((++num_failed) <= 5)
                            {
                                continue;
                            }
                        }

                        this.sock.Disconnect();
                        break;
                    }
                }

                if (this.halt)
                {
                    return;
                }
            }
        }
    }
}
